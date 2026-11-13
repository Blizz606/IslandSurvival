using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// Shared implementation and controls for all world-generation systems.
/// Original components are retained as data adapters so scene, prefab and
/// gameplay references keep pointing at the same objects and serialized fields.
/// </summary>
[DisallowMultipleComponent]
public sealed class UltimateWorldGenerator : MonoBehaviour
{
    [SerializeField, HideInInspector] private bool showIndividualComponents;

    public bool ShowIndividualComponents => showIndividualComponents;

    public static bool IsGeneratorModule(MonoBehaviour component)
    {
        return component is IslandModule || component is LayeredTreeModule
            || component is PlantModule || component is WaterModule || component is BeachModule
            || component is GrassIslandModule || component is AlternatingIslandModule
            || component is GrassOverlayModule || component is SpriteTreeModule;
    }

    public MonoBehaviour[] GetModules()
    {
        List<MonoBehaviour> modules = new List<MonoBehaviour>();
        foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
        {
            if (IsGeneratorModule(component))
                modules.Add(component);
        }
        return modules.ToArray();
    }

    // Startup remains on the existing adapters, preserving their execution
    // order, enabled states and coroutine timing without a second generation.
    [ContextMenu("Generate All (New Seed)")]
    public void GenerateAll()
    {
        IslandModule island = GetComponent<IslandModule>();
        if (island != null)
        {
            island.GenerateAll();
            return;
        }

        GrassIslandModule grass = GetComponent<GrassIslandModule>();
        if (grass != null && grass.generatorEnabled)
        {
            grass.GenerateIsland();
        }
        else
        {
            GetComponent<AlternatingIslandModule>()?.GenerateWorld();
            GetComponent<GrassOverlayModule>()?.GenerateOverlay();
            GetComponent<BeachModule>()?.GenerateBeach();
            GetComponent<SpriteTreeModule>()?.GenerateTrees();
        }

        WaterModule water = GetComponent<WaterModule>();
        if (water != null)
        {
            water.GenerateWater();
            water.RandomizeFishSeed();
        }
        GetComponent<LayeredTreeModule>()?.RandomizeSeed();
        GetComponent<PlantModule>()?.RandomizeSeed();
    }

    #region Shared tile utilities
    private static bool HasTiles(TileBase[] tiles)
    {
        if (tiles == null) return false;
        foreach (TileBase tile in tiles)
        {
            if (tile != null) return true;
        }
        return false;
    }

    private static TileBase SelectAnimatedTile(TileBase[] tiles, bool synchronize,
        System.Random random, List<AnimatedTile> generated)
    {
        int validCount = 0;
        foreach (TileBase tile in tiles)
        {
            if (tile != null) validCount++;
        }
        int selectedIndex = random.Next(validCount);
        foreach (TileBase tile in tiles)
        {
            if (tile == null || selectedIndex-- != 0) continue;
            if (synchronize || tile is not AnimatedTile source) return tile;

            AnimatedTile variant = ScriptableObject.CreateInstance<AnimatedTile>();
            variant.m_AnimatedSprites = source.m_AnimatedSprites;
            variant.m_MinSpeed = source.m_MinSpeed;
            variant.m_MaxSpeed = source.m_MaxSpeed;
            variant.m_TileColliderType = source.m_TileColliderType;
            variant.m_TileAnimationFlags = source.m_TileAnimationFlags;
            float speed = Mathf.Max(0.01f, source.m_MaxSpeed);
            float cycleDuration = source.m_AnimatedSprites.Length / speed;
            variant.m_AnimationStartTime = (float)random.NextDouble() * cycleDuration;
            variant.m_AnimationStartFrame = 0;
            generated.Add(variant);
            return variant;
        }
        return null;
    }

    private static void ClearAnimationVariants(List<AnimatedTile> generated)
    {
        foreach (AnimatedTile tile in generated)
        {
            if (tile == null) continue;
            if (Application.isPlaying) Destroy(tile);
            else DestroyImmediate(tile);
        }
        generated.Clear();
    }

    private static Tilemap FindNamedTilemap(string tilemapName)
    {
        foreach (Tilemap tilemap in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tilemap.name == tilemapName) return tilemap;
        }
        return null;
    }
    #endregion

    #region IslandModule
    // Inspector field names and types stay on the original component instances.
    public abstract class IslandModule : MonoBehaviour
    {
        private enum EdgeSide
        {
            Bottom,
            Right,
            Top,
            Left
        }

        private enum Corner
        {
            TopRight,
            BottomRight,
            BottomLeft,
            TopLeft
        }

        [Header("Start Campfire")]
        [SerializeField] private Tilemap placedObjectsTilemap;
        [SerializeField] private TileBase campfireTile;
        [SerializeField, Min(1)] private int campfireDistanceFromPlayer = 2;
        [SerializeField, Min(0f)] private float minimumCampfireTreeDistance = 4f;
        [SerializeField, Min(0f)] private float maximumCampfireTreeDistance = 5f;
        
        [Header("Tilemap")]
        [SerializeField] private Tilemap tilemap;
        [SerializeField] private TestTreeGen treeGenerator;
        [SerializeField] private TestPlantGen plantGenerator;
        [SerializeField] private TestWaterGen waterGenerator;
        [SerializeField] private TestBeachGen beachGenerator;

        [Header("Player Spawn")]
        [SerializeField] private PlayerMovement player;
        [Tooltip("Freier Bereich um den Player-Start in Tile-Zellen.")]
        [SerializeField, Min(1)] private int spawnClearance = 5;

        [Header("Automatisch gedrehte Rand-Tiles")]
        [Tooltip("Ein gerades Rand-Tile. Der Generator dreht es fuer alle vier Seiten.")]
        [SerializeField] private TileBase edgeTile;
        [Tooltip("Welche Seite dieses Rand-Tiles zeigt im Original ins Leere?")]
        [SerializeField] private EdgeSide edgeTileEmptySide = EdgeSide.Bottom;

        [Tooltip("Optional: ein Aussen-Ecken-Tile. Der Generator dreht es fuer alle vier Aussen-Ecken.")]
        [SerializeField] private TileBase outerCornerTile;
        [Tooltip("Welche Ecke dieses Aussen-Ecken-Tiles zeigt im Original ins Leere?")]
        [SerializeField] private Corner outerCornerEmptyCorner = Corner.TopRight;

        [Header("Normales Land")]
        [SerializeField] private TileBase landTile;

        [Header("Innere Ecke")]
        [Tooltip("Ein Innen-Ecken-Tile fuer Buchten. Der Generator dreht es automatisch.")]
        [SerializeField] private TileBase innerCornerTile;
        [Tooltip("Welche Ecke dieses Innen-Ecken-Tiles zeigt im Original ins Leere?")]
        [SerializeField] private Corner innerCornerEmptyCorner = Corner.BottomRight;

        // =========================================================
        // ISLAND SETTINGS
        // =========================================================

        [Header("Island Size")]
        [SerializeField] private int width = 80;
        [SerializeField] private int height = 80;

        [Header("Island Shape")]
        [SerializeField] private float noiseScale = 18f;

        [Range(1f, 4f)]
        [SerializeField] private float roundness = 2.2f;

        [Range(0f, 0.4f)]
        [SerializeField] private float coastVariation = 0.16f;

        [Range(0f, 1f)]
        [SerializeField] private float threshold = 0.42f;

        [Header("Seed")]
        [SerializeField] private int seed = 12345;

        private bool[,] land;

        private bool isGenerating;
        private Vector3Int spawnedCampfireCell;
        private bool hasSpawnedCampfire;
        private Vector3 queuedSpawnPosition;
        private bool finishSpawnAreaQueued;

        protected virtual void OnValidate()
        {
            // Im Inspector direkt eine Testinsel anzeigen, sobald die wichtigsten
            // Tile-Referenzen gesetzt wurden.
            if (Application.isPlaying || isGenerating)
                return;

            if (tilemap == null || edgeTile == null || landTile == null)
                return;

            isGenerating = true;
            GenerateIsland();
            isGenerating = false;
        }

        // =========================================================
        // START
        // =========================================================

        protected virtual void Start()
        {
            GenerateAll();
        }

        [ContextMenu("Generate Island")]
        public void GenerateIsland()
        {
            if (tilemap == null)
            {
                Debug.LogError("Keine Tilemap zugewiesen.");
                return;
            }

            tilemap.ClearAllTiles();

            GenerateLand();

            KeepLargestIsland();
            RemoveThinExtensions();
            FillNarrowWaterInlets();

            DrawIsland();
        }

        [ContextMenu("Generate All")]
        public void GenerateAll()
        {
            // Generate All is the deliberate "new world" action. Inspector
            // changes use GenerateIsland instead and therefore keep this seed.
            seed = Random.Range(-100000, 100000);
            GenerateIsland();

            if (treeGenerator == null)
                treeGenerator = GetComponent<TestTreeGen>();

            if (plantGenerator == null)
                plantGenerator = GetComponent<TestPlantGen>();

            if (waterGenerator == null)
                waterGenerator = GetComponent<TestWaterGen>();

            if (beachGenerator == null)
                beachGenerator = GetComponent<TestBeachGen>();

            if (waterGenerator != null)
                waterGenerator.GenerateWater();

            if (beachGenerator != null)
                beachGenerator.GenerateBeach();

            if (waterGenerator != null)
                waterGenerator.RandomizeFishSeed();

            if (treeGenerator != null)
                treeGenerator.RandomizeSeed();
            else
                Debug.LogWarning("Kein TestTreeGen für Generate All zugewiesen.", this);

            if (plantGenerator != null)
                plantGenerator.RandomizeSeed();

            PlacePlayerAtSpawn();
        }

        [ContextMenu("New Random World Seed")]
        public void RandomizeSeed()
        {
            GenerateAll();
        }

        private void PlacePlayerAtSpawn()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerMovement>();

            if (player == null || tilemap == null)
            {
                Debug.LogWarning("IslandGenerator: PlayerMovement oder Grass-Tilemap für die Spawn-Zone fehlt.", this);
                return;
            }

            if (!TryFindSpawnCell(out Vector3Int spawnCell))
            {
                Debug.LogWarning("IslandGenerator: Keine freie Spawn-Zone auf der Insel gefunden.", this);
                return;
            }

            Vector3 spawnPosition = tilemap.GetCellCenterWorld(spawnCell);
            player.transform.position = spawnPosition;

            Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
            if (playerBody != null)
                playerBody.position = spawnPosition;

            queuedSpawnPosition = spawnPosition;
            finishSpawnAreaQueued = true;
        }

        protected virtual void LateUpdate()
        {
            if (!finishSpawnAreaQueued)
                return;

            finishSpawnAreaQueued = false;

            if (treeGenerator != null)
                treeGenerator.ClearTreesNear(queuedSpawnPosition, spawnClearance);

            if (plantGenerator != null)
                plantGenerator.ClearPlantsNear(queuedSpawnPosition, spawnClearance);

            PlaceStartingCampfire(queuedSpawnPosition);
        }

        private void PlaceStartingCampfire(Vector3 spawnPosition)
        {
            if (placedObjectsTilemap == null || campfireTile == null)
                return;

            if (hasSpawnedCampfire && placedObjectsTilemap.GetTile(spawnedCampfireCell) == campfireTile)
                placedObjectsTilemap.SetTile(spawnedCampfireCell, null);

            if (!TryFindCampfireCell(spawnPosition, out Vector3Int campfireCell))
            {
                // The world can still start even if a custom world has no tree in the
                // requested range. Normal generated worlds always find a valid cell.
                campfireCell = placedObjectsTilemap.WorldToCell(spawnPosition);
                campfireCell += Vector3Int.right * campfireDistanceFromPlayer;
                Debug.LogWarning("IslandGenerator: No campfire cell with a 4-5 tile tree distance was found. Using the safe spawn fallback.", this);
            }

            placedObjectsTilemap.SetTile(campfireCell, campfireTile);
            spawnedCampfireCell = campfireCell;
            hasSpawnedCampfire = true;
        }

        private bool TryFindCampfireCell(Vector3 spawnPosition, out Vector3Int campfireCell)
        {
            campfireCell = Vector3Int.zero;
            if (treeGenerator == null || tilemap == null)
                return false;

            float cellSize = Mathf.Max(
                Mathf.Abs(tilemap.layoutGrid.cellSize.x),
                Mathf.Abs(tilemap.layoutGrid.cellSize.y)
            );
            float minimumDistance = minimumCampfireTreeDistance * cellSize;
            float maximumDistance = Mathf.Max(minimumDistance, maximumCampfireTreeDistance * cellSize);
            float idealDistance = (minimumDistance + maximumDistance) * 0.5f;
            float bestScore = float.MaxValue;

            foreach (Vector3Int groundCell in tilemap.cellBounds.allPositionsWithin)
            {
                if (!tilemap.HasTile(groundCell))
                    continue;

                Vector3 worldPosition = tilemap.GetCellCenterWorld(groundCell);
                Vector3Int candidateCell = placedObjectsTilemap.WorldToCell(worldPosition);
                if (placedObjectsTilemap.HasTile(candidateCell))
                    continue;

                float nearestTreeDistance = treeGenerator.GetNearestTreeDistance(worldPosition);
                if (nearestTreeDistance < minimumDistance || nearestTreeDistance > maximumDistance)
                    continue;

                float score = Mathf.Abs(nearestTreeDistance - idealDistance) * 1000f
                    + ((Vector2)worldPosition - (Vector2)spawnPosition).sqrMagnitude;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                campfireCell = candidateCell;
            }

            return bestScore < float.MaxValue;
        }

        private bool TryFindSpawnCell(out Vector3Int spawnCell)
        {
            Vector3Int closestCell = Vector3Int.zero;
            float closestDistance = float.MaxValue;
            bool foundAnyLand = false;
            Vector3Int closestClearCell = Vector3Int.zero;
            float closestClearDistance = float.MaxValue;
            bool foundClearArea = false;

            Vector3 center = tilemap.cellBounds.center;
            foreach (Vector3Int candidate in tilemap.cellBounds.allPositionsWithin)
            {
                if (!tilemap.HasTile(candidate))
                    continue;

                float distanceToCenter = ((Vector3)candidate - center).sqrMagnitude;
                if (distanceToCenter < closestDistance)
                {
                    closestCell = candidate;
                    closestDistance = distanceToCenter;
                    foundAnyLand = true;
                }

                if (IsClearSpawnArea(candidate) && distanceToCenter < closestClearDistance)
                {
                    closestClearCell = candidate;
                    closestClearDistance = distanceToCenter;
                    foundClearArea = true;
                }
            }

            if (foundClearArea)
            {
                spawnCell = closestClearCell;
                return true;
            }

            spawnCell = closestCell;
            return foundAnyLand;
        }

        private bool IsClearSpawnArea(Vector3Int centerCell)
        {
            for (int x = -spawnClearance; x <= spawnClearance; x++)
            {
                for (int y = -spawnClearance; y <= spawnClearance; y++)
                {
                    if (!tilemap.HasTile(centerCell + new Vector3Int(x, y, 0)))
                        return false;
                }
            }

            return true;
        }

        // =========================================================
        // GENERATE LAND
        // =========================================================

        private void GenerateLand()
        {
            land = new bool[width, height];

            System.Random random = new System.Random(seed);

            float offsetX = random.Next(-10000, 10000);
            float offsetY = random.Next(-10000, 10000);

            float centerX = (width - 1) / 2f;
            float centerY = (height - 1) / 2f;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float broadNoise = Mathf.PerlinNoise(
                        (x + offsetX) / noiseScale,
                        (y + offsetY) / noiseScale
                    );

                    // Feineres Noise erzeugt Buchten und unregelmäßige Vorsprünge.
                    float detailNoise = Mathf.PerlinNoise(
                        (x + offsetX * 0.37f) / (noiseScale * 0.75f),
                        (y + offsetY * 0.37f) / (noiseScale * 0.75f)
                    );

                    float noise =
                        broadNoise * (1f - coastVariation) +
                        detailNoise * coastVariation;

                    float nx =
                        (x - centerX) /
                        (width / 2f);

                    float ny =
                        (y - centerY) /
                        (height / 2f);

                    float distance =
                        Mathf.Sqrt(
                            nx * nx +
                            ny * ny
                        );

                    if (distance > 1f)
                    {
                        land[x, y] = false;
                        continue;
                    }

                    float falloff =
                        1f -
                        Mathf.Pow(
                            Mathf.Clamp01(distance),
                            roundness
                        );

                    float value =
                        noise * 0.48f +
                        falloff * 0.72f;

                    land[x, y] =
                        value > threshold;
                }
            }
        }

        // =========================================================
        // KEEP ONLY BIGGEST CONNECTED ISLAND
        // =========================================================

        private void KeepLargestIsland()
        {
            bool[,] visited =
                new bool[width, height];

            List<Vector2Int> largestRegion =
                new List<Vector2Int>();

            Vector2Int[] directions =
            {
                Vector2Int.up,
                Vector2Int.down,
                Vector2Int.left,
                Vector2Int.right
            };

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!land[x, y])
                        continue;

                    if (visited[x, y])
                        continue;

                    List<Vector2Int> region =
                        new List<Vector2Int>();

                    Queue<Vector2Int> queue =
                        new Queue<Vector2Int>();

                    Vector2Int start =
                        new Vector2Int(x, y);

                    queue.Enqueue(start);
                    visited[x, y] = true;

                    while (queue.Count > 0)
                    {
                        Vector2Int current =
                            queue.Dequeue();

                        region.Add(current);

                        foreach (
                            Vector2Int direction
                            in directions
                        )
                        {
                            Vector2Int next =
                                current + direction;

                            if (
                                next.x < 0 ||
                                next.y < 0 ||
                                next.x >= width ||
                                next.y >= height
                            )
                                continue;

                            if (!land[next.x, next.y])
                                continue;

                            if (visited[next.x, next.y])
                                continue;

                            visited[
                                next.x,
                                next.y
                            ] = true;

                            queue.Enqueue(next);
                        }
                    }

                    if (
                        region.Count >
                        largestRegion.Count
                    )
                    {
                        largestRegion =
                            region;
                    }
                }
            }

            // Alles löschen
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    land[x, y] = false;
                }
            }

            // Nur größte Insel wieder setzen
            foreach (
                Vector2Int position
                in largestRegion
            )
            {
                land[
                    position.x,
                    position.y
                ] = true;
            }
        }

        // =========================================================
        // DRAW
        // =========================================================

        private void DrawIsland()
        {
            Vector3Int centerCell =
                tilemap.WorldToCell(
                    transform.position
                );

            int halfWidth =
                width / 2;

            int halfHeight =
                height / 2;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!land[x, y])
                        continue;

                    TileBase selectedTile = GetTileForCell(x, y, out float rotation);

                    if (selectedTile == null)
                        continue;

                    Vector3Int cellPosition =
                        new Vector3Int(
                            centerCell.x +
                            x -
                            halfWidth,

                            centerCell.y +
                            y -
                            halfHeight,

                            centerCell.z
                        );

                    tilemap.SetTile(
                        cellPosition,
                        selectedTile
                    );

                    // Tile-Assets sperren ihre Transformation oft. Ohne dies wuerde
                    // Unity die vom Generator berechnete Drehung teilweise ignorieren.
                    tilemap.SetTileFlags(cellPosition, TileFlags.None);
                    tilemap.SetTransformMatrix(
                        cellPosition,
                        Matrix4x4.TRS(
                            Vector3.zero,
                            Quaternion.Euler(0f, 0f, rotation),
                            Vector3.one
                        )
                    );
                }
            }
        }

        // =========================================================
        // TILE AUS DEN ACHT NACHBARRICHTUNGEN
        // =========================================================

        private TileBase GetTileForCell(int x, int y, out float rotation)
        {
            rotation = 0f;

            bool n = IsLand(x, y + 1);
            bool e = IsLand(x + 1, y);
            bool s = IsLand(x, y - 1);
            bool w = IsLand(x - 1, y);
            bool ne = IsLand(x + 1, y + 1);
            bool se = IsLand(x + 1, y - 1);
            bool sw = IsLand(x - 1, y - 1);
            bool nw = IsLand(x - 1, y + 1);

            // Zwei aneinanderliegende fehlende Nachbarn bilden eine Aussen-Ecke.
            if (!n && !e)
                return SelectOuterCornerTile(Corner.TopRight, out rotation);
            if (!s && !e)
                return SelectOuterCornerTile(Corner.BottomRight, out rotation);
            if (!s && !w)
                return SelectOuterCornerTile(Corner.BottomLeft, out rotation);
            if (!n && !w)
                return SelectOuterCornerTile(Corner.TopLeft, out rotation);

            // Eine offene Seite ist eine gerade Kante.
            if (!n)
                return SelectEdgeTile(EdgeSide.Top, out rotation);
            if (!e)
                return SelectEdgeTile(EdgeSide.Right, out rotation);
            if (!s)
                return SelectEdgeTile(EdgeSide.Bottom, out rotation);
            if (!w)
                return SelectEdgeTile(EdgeSide.Left, out rotation);

            // Alle geraden Nachbarn vorhanden, aber eine Diagonale offen = Innen-Ecke.
            if (!ne)
                return SelectCornerTile(innerCornerTile, innerCornerEmptyCorner, Corner.TopRight, out rotation);
            if (!se)
                return SelectCornerTile(innerCornerTile, innerCornerEmptyCorner, Corner.BottomRight, out rotation);
            if (!sw)
                return SelectCornerTile(innerCornerTile, innerCornerEmptyCorner, Corner.BottomLeft, out rotation);
            if (!nw)
                return SelectCornerTile(innerCornerTile, innerCornerEmptyCorner, Corner.TopLeft, out rotation);

            return landTile;
        }

        private void RemoveThinExtensions()
        {
            // Mehrfach wiederholen, damit lange ein Tile breite Ausläufer komplett verschwinden.
            for (int pass = 0; pass < 3; pass++)
            {
                List<Vector2Int> toRemove = new List<Vector2Int>();

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        if (!land[x, y])
                            continue;

                        bool north = IsLand(x, y + 1);
                        bool east = IsLand(x + 1, y);
                        bool south = IsLand(x, y - 1);
                        bool west = IsLand(x - 1, y);

                        int neighbours = 0;
                        if (north) neighbours++;
                        if (east) neighbours++;
                        if (south) neighbours++;
                        if (west) neighbours++;

                        bool isStraightOneTileArm =
                            neighbours == 2 && ((north && south) || (east && west));

                        if (neighbours <= 1 || isStraightOneTileArm)
                            toRemove.Add(new Vector2Int(x, y));
                    }
                }

                foreach (Vector2Int position in toRemove)
                    land[position.x, position.y] = false;
            }
        }

        // Verhindert einen Tile breiten Wasser-Schlauch zwischen Landfeldern.
        // Ohne diese Glättung entstehen am Strand spitze blaue Einbuchtungen
        // wie im Screenshot, die mit den Corner-Tiles nicht sauber aussehen.
        private void FillNarrowWaterInlets()
        {
            for (int pass = 0; pass < 3; pass++)
            {
                List<Vector2Int> toFill = new List<Vector2Int>();

                for (int x = 1; x < width - 1; x++)
                {
                    for (int y = 1; y < height - 1; y++)
                    {
                        if (land[x, y])
                            continue;

                        bool enclosedHorizontally = land[x - 1, y] && land[x + 1, y];
                        bool enclosedVertically = land[x, y - 1] && land[x, y + 1];

                        if (enclosedHorizontally || enclosedVertically)
                            toFill.Add(new Vector2Int(x, y));
                    }
                }

                foreach (Vector2Int cell in toFill)
                    land[cell.x, cell.y] = true;
            }
        }

        private TileBase SelectEdgeTile(EdgeSide targetEmptySide, out float rotation)
        {
            if (edgeTile == null)
            {
                rotation = 0f;
                return landTile;
            }

            rotation = RotationFromEdgeSide(edgeTileEmptySide, targetEmptySide);
            return edgeTile;
        }

        private TileBase SelectOuterCornerTile(Corner targetEmptyCorner, out float rotation)
        {
            TileBase tile = SelectCornerTile(
                outerCornerTile,
                outerCornerEmptyCorner,
                targetEmptyCorner,
                out rotation
            );

            // In Unity a negative Z rotation turns a 2D tile clockwise/right.
            rotation -= 90f;
            return tile;
        }

        private TileBase SelectCornerTile(TileBase tile, Corner sourceEmptyCorner, Corner targetEmptyCorner, out float rotation)
        {
            if (tile == null)
            {
                rotation = 0f;
                return landTile;
            }

            rotation = RotationFromCorner(sourceEmptyCorner, targetEmptyCorner);
            return tile;
        }

        private static float RotationFromEdgeSide(EdgeSide source, EdgeSide target)
        {
            return Mathf.DeltaAngle(EdgeSideAngle(source), EdgeSideAngle(target));
        }

        private static float RotationFromCorner(Corner source, Corner target)
        {
            return Mathf.DeltaAngle(CornerAngle(source), CornerAngle(target));
        }

        private static float EdgeSideAngle(EdgeSide side)
        {
            switch (side)
            {
                case EdgeSide.Right: return 90f;
                case EdgeSide.Top: return 180f;
                case EdgeSide.Left: return -90f;
                default: return 0f; // Bottom
            }
        }

        private static float CornerAngle(Corner corner)
        {
            switch (corner)
            {
                case Corner.BottomRight: return -90f;
                case Corner.BottomLeft: return 180f;
                case Corner.TopLeft: return 90f;
                default: return 0f; // TopRight
            }
        }

        // =========================================================
        // LAND CHECK
        // =========================================================

        private bool IsLand(
            int x,
            int y
        )
        {
            if (
                x < 0 ||
                y < 0 ||
                x >= width ||
                y >= height
            )
            {
                return false;
            }

            return land[x, y];
        }
    }
    #endregion

    #region LayeredTreeModule
    // Inspector field names and types stay on the original component instances.
    public abstract class LayeredTreeModule : MonoBehaviour
    {
        [Header("Ground")]
        [SerializeField] private Tilemap groundTilemap;

        [Header("Trees Tilemap")]
        [SerializeField] private Tilemap treesTilemap;

        [Header("Tree Sprites")]
        [SerializeField] private Sprite[] treeSprites;
        [SerializeField] private TileBase[] treeTiles;

        [Header("Tree Prefab")]
        [Tooltip("Instanz dieses Prefabs für jeden Baum. Es muss die Kinder Trunk, Crown und Stump enthalten.")]
        [SerializeField] private HarvestableTree treePrefab;

        [Header("Layered Tree (optional)")]
        [Tooltip("Nur der Stamm: liegt hinter dem Player und erhält die Collider.")]
        [SerializeField] private Tilemap treeTrunksTilemap;
        [Tooltip("Nur die Krone: liegt vor dem Player und braucht keinen Collider.")]
        [SerializeField] private Tilemap treeCrownsTilemap;
        [SerializeField] private Sprite treeTrunkSprite;
        [SerializeField] private Sprite treeCrownSprite;
        [Tooltip("Verschiebt nur den Stamm innerhalb seiner Grid-Zelle. Negative Y-Werte bewegen ihn nach unten.")]
        [SerializeField] private Vector2 treeTrunkOffset = Vector2.zero;

        [Header("Holzfällen")]
        [Tooltip("Wie viele Axttreffer ein Baum benötigt, bevor er zum Stumpf wird.")]
        [SerializeField, Min(1)] private int hitsToFell = 3;
        [Tooltip("Wird nach dem Fällen statt des Baumstamms angezeigt.")]
        [SerializeField] private Sprite stumpSprite;
        [Tooltip("Feinverschiebung des Stumpfs relativ zur Unterkante des sichtbaren Stamms.")]
        [SerializeField] private Vector2 stumpOffset = Vector2.zero;
        [Tooltip("Zusätzliche Skalierung des Stumpfs relativ zur Baumskalierung.")]
        [SerializeField, Min(0.01f)] private float stumpScaleMultiplier = 1f;
        [Tooltip("Soll der Stumpf den Player weiterhin blockieren?")]
        [SerializeField] private bool stumpBlocksPlayer;
        [Tooltip("Zeigt bei einem generierten Baum bereits im Edit-Modus den Stumpf an. So lassen sich Sprite, Offset und Skalierung direkt einstellen.")]
        [SerializeField] private bool previewStumpInEditor;

        [Header("Crown Fade")]
        [Tooltip("Deckkraft der Krone, wenn der Player darunter steht.")]
        [SerializeField, Range(0.05f, 1f)] private float crownOccludedAlpha = 0.55f;
        [SerializeField, Min(0.01f)] private float crownFadeSpeed = 8f;
        [SerializeField, Min(0.1f)] private float crownFadeDistance = 0.55f;

        [Header("Tree Settings")]
        [SerializeField, Min(0)] private int treeCount = 40;
        [SerializeField, Range(0f, 1f)] private float spawnChance = 0.35f;
        [SerializeField, Min(0.1f)] private float treeScale = 10f;
        [SerializeField, Min(0)] private int edgeClearance = 2;
        [Tooltip("Zusätzlicher freier Abstand zwischen Baumkronen in Welt-Einheiten.")]
        [SerializeField, Min(0f)] private float treeSpacing = 1f;
        [SerializeField] private int seed = 12345;

        [Header("Kleine Wälder")]
        [SerializeField, Min(0)] private int forestCount = 3;
        [SerializeField, Min(1)] private int treesPerForest = 6;
        [SerializeField, Min(1f)] private float forestRadius = 25f;

        private const string GeneratedParentName = "Generated Trees";
        private readonly List<HarvestableTree> spawnedTrees = new();
        private bool isGenerating;

        protected virtual void Start()
        {
            GenerateTrees();
        }

        protected virtual void OnValidate()
        {
            // Bäume werden nur per Inspector-Button oder beim Spielstart erzeugt.
            // So kann eine Offset-Änderung niemals Objekte duplizieren.
        }

        [ContextMenu("Generate Trees")]
        public void GenerateTrees()
        {
            FindLayeredTreeTilemaps();
            bool useLayeredTrees = treePrefab != null || HasLayeredTreeSprites();

            if (useLayeredTrees && treePrefab == null
                && (treeTrunksTilemap == null || treeCrownsTilemap == null))
            {
                Debug.LogWarning("TestTreeGen: TreesTrunk und TreesCrown im Inspector zuweisen oder als Kinder des Grid benennen.", this);
                return;
            }

            if (!HasTreeAssets())
            {
                Debug.LogWarning("TestTreeGen: Ground Tilemap oder Tree-Sprites/Tree-Tiles fehlen.", this);
                return;
            }

            isGenerating = true;
            spawnedTrees.Clear();

            System.Random random = new System.Random(seed);
            bool useTreeTilemap = !useLayeredTrees && treesTilemap != null;
            Transform parent = null;
            if (useLayeredTrees)
            {
                if (treeTrunksTilemap != null)
                    treeTrunksTilemap.ClearAllTiles();
                if (treeCrownsTilemap != null)
                    treeCrownsTilemap.ClearAllTiles();
                ClearLegacyTreeVisuals();

                // Jeder neu erzeugte Baum hat genau ein Root-Objekt. Dieses wird
                // vor jeder Generierung komplett geleert, damit nichts dupliziert.
                Transform legacyFullTrees = transform.Find(GeneratedParentName);
                if (legacyFullTrees != null)
                    ClearTrees(legacyFullTrees);

                // Remove the old full-tree visuals so they cannot overlap the
                // separated trunk/crown version.
                if (treesTilemap != null
                    && treesTilemap != treeTrunksTilemap
                    && treesTilemap != treeCrownsTilemap)
                {
                    treesTilemap.ClearAllTiles();
                }
            }
            else if (!useTreeTilemap)
            {
                parent = treesTilemap != null ? treesTilemap.transform : GetOrCreateParent();
                if (treesTilemap == null)
                    ClearTrees(parent);
            }
            else
            {
                treesTilemap.ClearAllTiles();
            }

            float minimumSpacing = GetMinimumTreeSpacing();
            int treeRadiusInCells = Mathf.CeilToInt(minimumSpacing * 0.5f);
            int minimumEdgeClearance = Mathf.Max(edgeClearance, treeRadiusInCells);
            int usedEdgeClearance = minimumEdgeClearance;
            List<Vector3Int> validCells = CollectValidCells(usedEdgeClearance, random, false);

            Shuffle(validCells, random);

            List<Vector3Int> selectedCells = new List<Vector3Int>();
            int desiredAmount = spawnChance <= 0f
                ? 0
                : Mathf.Clamp(
                    Mathf.CeilToInt(validCells.Count * spawnChance),
                    validCells.Count > 0 ? 1 : 0,
                    treeCount
                );
            GenerateForests(validCells, selectedCells, desiredAmount, minimumSpacing, random);

            // Der Rest verteilt sich locker über die Insel. So gibt es kleine
            // Wälder, aber auch einzelne Bäume zwischen den Gruppen.
            foreach (Vector3Int candidate in validCells)
            {
                if (selectedCells.Count >= desiredAmount)
                    break;

                TryAddTreeCell(candidate, selectedCells, minimumSpacing);
            }

            int amount = selectedCells.Count;
            if (amount == 0)
            {
                Debug.LogWarning("TestTreeGen: Keine gültigen Landfelder gefunden. Prüfe, ob die Ground Tilemap bereits generiert wurde.", this);
                isGenerating = false;
                return;
            }

            for (int i = 0; i < amount; i++)
            {
                Vector3Int cell = selectedCells[i];

                if (useLayeredTrees)
                {
                    PlaceLayeredTree(cell);
                    continue;
                }

                if (useTreeTilemap)
                {
                    Vector3 tileWorldPosition = groundTilemap.GetCellCenterWorld(cell);
                    Vector3Int treeCell = treesTilemap.WorldToCell(tileWorldPosition);
                    treesTilemap.SetTile(treeCell, GetRandomTreeTile(random));
                    treesTilemap.SetTileFlags(treeCell, TileFlags.None);
                    treesTilemap.SetTransformMatrix(
                        treeCell,
                        Matrix4x4.TRS(
                            Vector3.zero,
                            Quaternion.identity,
                            Vector3.one * treeScale
                        )
                    );
                    continue;
                }

                Vector3 spriteWorldPosition = groundTilemap.GetCellCenterWorld(cell);
                GameObject tree = new GameObject("Tree");
                tree.transform.SetParent(parent);
                tree.transform.position = spriteWorldPosition;
                tree.transform.localScale = Vector3.one * treeScale;

                SpriteRenderer renderer = tree.AddComponent<SpriteRenderer>();
                renderer.sprite = treeSprites[random.Next(treeSprites.Length)];
                renderer.sortingOrder = 1;
            }

            ShowStumpPreviewInEditor();

            Debug.Log($"TestTreeGen: {amount} Bäume auf {validCells.Count} möglichen Landfeldern erzeugt.", this);

            isGenerating = false;
        }

        [ContextMenu("New Random Tree Seed")]
        public void RandomizeSeed()
        {
            seed = Random.Range(int.MinValue, int.MaxValue);
            GenerateTrees();
        }

        public void ClearTreesNear(Vector3 worldPosition, int clearance)
        {
            FindLayeredTreeTilemaps();
            if (treeTrunksTilemap != null)
            {
                Vector3Int centerCell = treeTrunksTilemap.WorldToCell(worldPosition);
                for (int x = -clearance; x <= clearance; x++)
                {
                    for (int y = -clearance; y <= clearance; y++)
                        treeTrunksTilemap.SetTile(centerCell + new Vector3Int(x, y, 0), null);
                }
            }

            float clearanceSqr = clearance * clearance;
            List<HarvestableTree> trees = new List<HarvestableTree>(FindSpawnedTrees());
            foreach (HarvestableTree tree in trees)
            {
                if (tree == null
                    || ((Vector2)(tree.transform.position - worldPosition)).sqrMagnitude > clearanceSqr)
                    continue;

    #if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(tree.gameObject);
                else
    #endif
                    Destroy(tree.gameObject);

                spawnedTrees.Remove(tree);
            }
        }

        /// <summary>Applies axe damage to the closest intact tree in range.</summary>
        public bool TryChopTree(Vector2 playerPosition, float range, int damage = 1)
        {
            if (range <= 0f)
                return false;

            float closestDistanceSqr = range * range;
            HarvestableTree closestTree = null;

            foreach (HarvestableTree tree in FindSpawnedTrees())
            {
                if (tree == null || tree.IsFelled)
                    continue;

                Vector2 treePosition = tree.InteractionPosition;
                float distanceSqr = (treePosition - playerPosition).sqrMagnitude;
                if (distanceSqr > closestDistanceSqr)
                    continue;

                closestDistanceSqr = distanceSqr;
                closestTree = tree;
            }

            if (closestTree == null)
                return false;

            closestTree.ApplyDamage(Mathf.Max(1, damage));
            return true;
        }

        public bool TryGetClosestTreeDirection(Vector2 playerPosition, float range, out Vector2 direction)
        {
            float closestDistanceSqr = range * range;
            direction = Vector2.zero;

            foreach (HarvestableTree tree in FindSpawnedTrees())
            {
                if (tree == null || tree.IsFelled)
                    continue;

                Vector2 candidate = tree.InteractionPosition - playerPosition;
                float distanceSqr = candidate.sqrMagnitude;
                if (distanceSqr <= 0.0001f || distanceSqr > closestDistanceSqr)
                    continue;

                closestDistanceSqr = distanceSqr;
                direction = candidate;
            }

            return direction.sqrMagnitude > 0f;
        }

        /// <summary>Returns true when a generated tree root is within the supplied world-space radius.</summary>
        public bool HasTreeNear(Vector2 worldPosition, float radius)
        {
            float radiusSqr = Mathf.Max(0f, radius) * Mathf.Max(0f, radius);
            foreach (HarvestableTree tree in FindSpawnedTrees())
            {
                if (tree == null)
                    continue;

                if (((Vector2)tree.transform.position - worldPosition).sqrMagnitude <= radiusSqr)
                    return true;
            }

            return false;
        }

        /// <summary>Returns the world-space distance to the closest generated tree.</summary>
        public float GetNearestTreeDistance(Vector2 worldPosition)
        {
            float nearestDistanceSqr = float.MaxValue;
            foreach (HarvestableTree tree in FindSpawnedTrees())
            {
                if (tree == null)
                    continue;

                float distanceSqr = ((Vector2)tree.transform.position - worldPosition).sqrMagnitude;
                if (distanceSqr < nearestDistanceSqr)
                    nearestDistanceSqr = distanceSqr;
            }

            return nearestDistanceSqr == float.MaxValue
                ? float.PositiveInfinity
                : Mathf.Sqrt(nearestDistanceSqr);
        }

        private IEnumerable<HarvestableTree> FindSpawnedTrees()
        {
            if (spawnedTrees.Count > 0)
                return spawnedTrees;

            return FindObjectsByType<HarvestableTree>(FindObjectsInactive.Exclude);
        }

        private Transform GetOrCreateParent()
        {
            Transform parent = transform.Find(GeneratedParentName);
            if (parent != null)
                return parent;

            GameObject container = new GameObject(GeneratedParentName);
            container.transform.SetParent(transform);
            container.transform.localPosition = Vector3.zero;
            return container.transform;
        }

        private void GenerateForests(
            List<Vector3Int> validCells,
            List<Vector3Int> selectedCells,
            int desiredAmount,
            float minimumSpacing,
            System.Random random
        )
        {
            if (forestCount <= 0 || desiredAmount <= 0)
                return;

            for (int forestIndex = 0;
                 forestIndex < forestCount && selectedCells.Count < desiredAmount;
                 forestIndex++)
            {
                if (!TryFindForestCenter(
                        validCells,
                        selectedCells,
                        minimumSpacing,
                        out Vector3Int forestCenter
                    ))
                    break;

                if (!TryAddTreeCell(forestCenter, selectedCells, minimumSpacing))
                    continue;

                int treesInThisForest = 1;
                List<Vector3Int> forestCandidates = new List<Vector3Int>();
                Vector3 centerWorld = groundTilemap.GetCellCenterWorld(forestCenter);
                float radiusSqr = forestRadius * forestRadius;

                foreach (Vector3Int candidate in validCells)
                {
                    Vector3 delta = groundTilemap.GetCellCenterWorld(candidate) - centerWorld;
                    if (delta.sqrMagnitude <= radiusSqr)
                        forestCandidates.Add(candidate);
                }

                Shuffle(forestCandidates, random);
                foreach (Vector3Int candidate in forestCandidates)
                {
                    if (treesInThisForest >= treesPerForest || selectedCells.Count >= desiredAmount)
                        break;

                    if (TryAddTreeCell(candidate, selectedCells, minimumSpacing))
                        treesInThisForest++;
                }
            }
        }

        private bool TryFindForestCenter(
            List<Vector3Int> validCells,
            List<Vector3Int> selectedCells,
            float minimumSpacing,
            out Vector3Int forestCenter
        )
        {
            foreach (Vector3Int candidate in validCells)
            {
                if (CanPlaceTreeCell(candidate, selectedCells, minimumSpacing))
                {
                    forestCenter = candidate;
                    return true;
                }
            }

            forestCenter = Vector3Int.zero;
            return false;
        }

        private bool TryAddTreeCell(
            Vector3Int candidate,
            List<Vector3Int> selectedCells,
            float minimumSpacing
        )
        {
            if (!CanPlaceTreeCell(candidate, selectedCells, minimumSpacing))
                return false;

            selectedCells.Add(candidate);
            return true;
        }

        private bool CanPlaceTreeCell(
            Vector3Int candidate,
            List<Vector3Int> selectedCells,
            float minimumSpacing
        )
        {
            Vector3 candidateWorld = groundTilemap.GetCellCenterWorld(candidate);
            float minimumSpacingSqr = minimumSpacing * minimumSpacing;

            foreach (Vector3Int selected in selectedCells)
            {
                Vector3 delta = candidateWorld - groundTilemap.GetCellCenterWorld(selected);
                if (delta.sqrMagnitude < minimumSpacingSqr)
                    return false;
            }

            return true;
        }

        private float GetMinimumTreeSpacing()
        {
            float widestSprite = 0f;
            AddPrefabWidth(ref widestSprite);
            AddSpriteWidth(treeTrunkSprite, ref widestSprite);
            AddSpriteWidth(treeCrownSprite, ref widestSprite);

            if (treeSprites != null)
            {
                foreach (Sprite sprite in treeSprites)
                    AddSpriteWidth(sprite, ref widestSprite);
            }

            if (treeTiles != null)
            {
                foreach (TileBase tileBase in treeTiles)
                {
                    if (tileBase is Tile tile)
                        AddSpriteWidth(tile.sprite, ref widestSprite);
                }
            }

            // A circle with the sprite's widest side as diameter prevents overlap
            // even when two crowns are placed diagonally next to one another.
            float visualDiameter = widestSprite * treeScale;
            return Mathf.Max(0.1f, visualDiameter + Mathf.Max(0f, treeSpacing));
        }

        private void AddPrefabWidth(ref float widestSprite)
        {
            if (treePrefab == null)
                return;

            foreach (SpriteRenderer renderer in treePrefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite == null)
                    continue;

                float scaledWidth = renderer.sprite.bounds.size.magnitude
                    * Mathf.Max(Mathf.Abs(renderer.transform.localScale.x), Mathf.Abs(renderer.transform.localScale.y));
                widestSprite = Mathf.Max(widestSprite, scaledWidth / treeScale);
            }
        }

        private static void AddSpriteWidth(Sprite sprite, ref float widestSprite)
        {
            if (sprite == null)
                return;

            widestSprite = Mathf.Max(widestSprite, sprite.bounds.size.magnitude);
        }

        private void ClearTrees(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                DestroyGeneratedObject(parent.GetChild(i).gameObject);
        }

        private static void DestroyGeneratedObject(GameObject target)
        {
    #if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(target);
            else
    #endif
                Destroy(target);
        }

        private List<Vector3Int> CollectValidCells(int clearance, System.Random random, bool applySpawnChance = true)
        {
            BoundsInt bounds = groundTilemap.cellBounds;
            List<Vector3Int> cells = new List<Vector3Int>();

            foreach (Vector3Int cell in bounds.allPositionsWithin)
            {
                if (!IsValidTreeCell(cell, clearance))
                    continue;

                if (applySpawnChance && random.NextDouble() > spawnChance)
                    continue;

                cells.Add(cell);
            }

            return cells;
        }

        private bool IsValidTreeCell(Vector3Int cell, int clearance)
        {
            if (!groundTilemap.HasTile(cell))
                return false;

            for (int x = -clearance; x <= clearance; x++)
            {
                for (int y = -clearance; y <= clearance; y++)
                {
                    if (!groundTilemap.HasTile(cell + new Vector3Int(x, y, 0)))
                        return false;
                }
            }

            return true;
        }

        private bool HasTreeAssets()
        {
            if (treePrefab != null)
                return groundTilemap != null;

            if (HasLayeredTreeSprites())
                return groundTilemap != null;

            bool hasTiles = treeTiles != null && treeTiles.Length > 0;
            bool hasSprites = treeSprites != null && treeSprites.Length > 0;
            return groundTilemap != null && (hasTiles || hasSprites);
        }

        private bool HasLayeredTreeSprites()
        {
            return treeTrunkSprite != null && treeCrownSprite != null;
        }

        private void FindLayeredTreeTilemaps()
        {
            if (groundTilemap == null)
                return;

            Transform grid = groundTilemap.transform.parent;
            if (grid == null)
                return;

            if (treeTrunksTilemap == null)
            {
                Transform trunks = grid.Find("TreesTrunk");
                if (trunks != null)
                    treeTrunksTilemap = trunks.GetComponent<Tilemap>();
            }

            if (treeCrownsTilemap == null)
            {
                Transform crowns = grid.Find("TreesCrown");
                if (crowns != null)
                    treeCrownsTilemap = crowns.GetComponent<Tilemap>();
            }
        }

        private void PlaceLayeredTree(Vector3Int groundCell)
        {
            Vector3 worldPosition = groundTilemap.GetCellCenterWorld(groundCell);

            if (treePrefab != null)
            {
                HarvestableTree prefabInstance = InstantiateTreePrefab(worldPosition);
                prefabInstance.name = treePrefab.name;
                prefabInstance.ConfigureFromPrefab(hitsToFell, stumpBlocksPlayer);
                spawnedTrees.Add(prefabInstance);
                return;
            }

            GameObject treeObject = new GameObject("Tree");
            treeObject.transform.SetParent(GetOrCreateParent(), false);
            treeObject.transform.position = worldPosition;

            TilemapRenderer trunkRenderer = treeTrunksTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer crownRenderer = treeCrownsTilemap.GetComponent<TilemapRenderer>();
            HarvestableTree tree = treeObject.AddComponent<HarvestableTree>();
            tree.Configure(
                treeTrunkSprite,
                treeCrownSprite,
                stumpSprite,
                treeTrunkOffset,
                stumpOffset,
                treeScale,
                stumpScaleMultiplier,
                hitsToFell,
                stumpBlocksPlayer,
                trunkRenderer != null ? trunkRenderer.sortingLayerName : "Default",
                trunkRenderer != null ? trunkRenderer.sortingOrder : 9,
                crownRenderer != null ? crownRenderer.sortingOrder : 11,
                crownOccludedAlpha,
                crownFadeSpeed,
                crownFadeDistance
            );
            spawnedTrees.Add(tree);
        }

        private HarvestableTree InstantiateTreePrefab(Vector3 worldPosition)
        {
    #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                GameObject instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(
                    treePrefab.gameObject,
                    GetOrCreateParent()
                );
                instance.transform.position = worldPosition;
                return instance.GetComponent<HarvestableTree>();
            }
    #endif

            return Instantiate(treePrefab, worldPosition, Quaternion.identity, GetOrCreateParent());
        }

        private void ShowStumpPreviewInEditor()
        {
            if (Application.isPlaying || !previewStumpInEditor || spawnedTrees.Count == 0)
                return;

            spawnedTrees[0].SetPreview(true);
        }

        private void ClearLegacyTreeVisuals()
        {
            ClearLegacyVisualRoot(treeCrownsTilemap, "Generated Crowns");
            ClearLegacyVisualRoot(treeTrunksTilemap, "Generated Stumps");
            ClearLegacyVisualRoot(treeTrunksTilemap, "Stump Preview");
        }

        private void ClearLegacyVisualRoot(Tilemap tilemap, string objectName)
        {
            if (tilemap == null)
                return;

            for (int i = tilemap.transform.childCount - 1; i >= 0; i--)
            {
                Transform legacyVisual = tilemap.transform.GetChild(i);
                if (legacyVisual.name == objectName)
                    DestroyGeneratedObject(legacyVisual.gameObject);
            }
        }

        private TileBase GetRandomTreeTile(System.Random random)
        {
            if (treeTiles != null && treeTiles.Length > 0)
                return treeTiles[random.Next(treeTiles.Length)];

            return CreateTreeTile(
                treeSprites[random.Next(treeSprites.Length)],
                Tile.ColliderType.Sprite
            );
        }

        private static Tile CreateTreeTile(Sprite sprite, Tile.ColliderType colliderType)
        {
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.colliderType = colliderType;
            return tile;
        }

        private void Shuffle(List<Vector3Int> cells, System.Random random)
        {
            for (int i = cells.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                Vector3Int temporary = cells[i];
                cells[i] = cells[swapIndex];
                cells[swapIndex] = temporary;
            }
        }
    }
    #endregion

    #region PlantModule
    // Inspector field names and types stay on the original component instances.
    public abstract class PlantModule : MonoBehaviour
    {
        [Header("Ground")]
        [SerializeField] private Tilemap groundTilemap;
        [SerializeField] private Tilemap treeTrunksTilemap;
        [SerializeField] private TestTreeGen treeGenerator;

        [Header("Plant Layer")]
        [Tooltip("Wird bei Bedarf als Kind des Grid mit dem Namen Plants erstellt.")]
        [SerializeField] private Tilemap plantsTilemap;
        [SerializeField] private string plantSortingLayer = "Default";
        [SerializeField] private int plantSortingOrder = 5;

        [Header("Animated Plant Tiles")]
        [SerializeField] private TileBase[] plantTiles;
        [Tooltip("Aktiviert: alle Pflanzen starten im selben Animations-Frame. Deaktiviert: jede Pflanze startet versetzt.")]
        [SerializeField] private bool synchronizeAnimations = true;

        [Header("Generation")]
        [SerializeField, Range(0f, 1f)] private float density = 0.06f;
        [SerializeField, Min(0)] private int maxPlants = 160;
        [SerializeField, Range(0.1f, 5f)] private float plantScale = 1f;
        [Tooltip("Pflanzen werden innerhalb dieses Abstands zur Küstenkante nicht gesetzt.")]
        [SerializeField, Min(0)] private int beachClearance = 2;
        [Tooltip("Freier Bereich um jeden Baumstamm. Deckt bei großen Bäumen auch die Krone ab.")]
        [SerializeField, Min(0)] private int treeClearance = 5;
        [SerializeField] private int seed = 24680;

        private const string PlantsTilemapName = "Plants";
        private bool isGenerating;
        private readonly List<AnimatedTile> generatedAnimatedTiles = new List<AnimatedTile>();

        protected virtual void OnValidate()
        {
            if (Application.isPlaying || isGenerating || !HasPlantTiles())
                return;

            GeneratePlants();
        }

        [ContextMenu("Generate Plants")]
        public void GeneratePlants()
        {
            FindGroundTilemap();
            FindTreeTrunksTilemap();
            EnsurePlantsTilemap();

            if (groundTilemap == null || plantsTilemap == null || !HasPlantTiles())
            {
                Debug.LogWarning("TestPlantGen: Grass-Tilemap und mindestens ein Animated Tile zuweisen.", this);
                return;
            }

            isGenerating = true;
            plantsTilemap.ClearAllTiles();
            ClearGeneratedAnimatedTiles();

            System.Random random = new System.Random(seed);
            int spawned = 0;

            foreach (Vector3Int groundCell in groundTilemap.cellBounds.allPositionsWithin)
            {
                if (spawned >= maxPlants)
                    break;

                if (!groundTilemap.HasTile(groundCell)
                    || IsTooCloseToBeach(groundCell)
                    || IsTooCloseToTree(groundCell)
                    || random.NextDouble() > density)
                    continue;

                Vector3 worldPosition = groundTilemap.GetCellCenterWorld(groundCell);
                Vector3Int plantCell = plantsTilemap.WorldToCell(worldPosition);
                plantsTilemap.SetTile(plantCell, GetPlantTile(random));
                plantsTilemap.SetTileFlags(plantCell, TileFlags.None);
                plantsTilemap.SetTransformMatrix(
                    plantCell,
                    Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * plantScale)
                );
                spawned++;
            }

            isGenerating = false;
            Debug.Log($"TestPlantGen: {spawned} Pflanzen erzeugt.", this);
        }

        [ContextMenu("New Random Plant Seed")]
        public void RandomizeSeed()
        {
            seed = Random.Range(int.MinValue, int.MaxValue);
            GeneratePlants();
        }

        public void ClearPlantsNear(Vector3 worldPosition, int clearance)
        {
            if (plantsTilemap == null)
                return;

            Vector3Int centerCell = plantsTilemap.WorldToCell(worldPosition);
            for (int x = -clearance; x <= clearance; x++)
            {
                for (int y = -clearance; y <= clearance; y++)
                    plantsTilemap.SetTile(centerCell + new Vector3Int(x, y, 0), null);
            }
        }

        private bool HasPlantTiles() => HasTiles(plantTiles);

        private TileBase GetPlantTile(System.Random random)
        {
            return SelectAnimatedTile(plantTiles, synchronizeAnimations, random, generatedAnimatedTiles);
        }

        private void ClearGeneratedAnimatedTiles() => ClearAnimationVariants(generatedAnimatedTiles);

        private bool IsTooCloseToBeach(Vector3Int groundCell)
        {
            if (beachClearance <= 0)
                return false;

            for (int x = -beachClearance; x <= beachClearance; x++)
            {
                for (int y = -beachClearance; y <= beachClearance; y++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) > beachClearance)
                        continue;

                    Vector3Int nearbyGroundCell = groundCell + new Vector3Int(x, y, 0);
                    if (!groundTilemap.HasTile(nearbyGroundCell))
                        return true;
                }
            }

            return false;
        }

        private bool IsTooCloseToTree(Vector3Int groundCell)
        {
            if (treeClearance <= 0)
                return false;

            Vector3 worldPosition = groundTilemap.GetCellCenterWorld(groundCell);
            if (treeGenerator == null)
                treeGenerator = FindAnyObjectByType<TestTreeGen>();

            if (treeGenerator != null)
            {
                float cellSize = Mathf.Max(
                    Mathf.Abs(groundTilemap.layoutGrid.cellSize.x),
                    Mathf.Abs(groundTilemap.layoutGrid.cellSize.y)
                );
                return treeGenerator.HasTreeNear(worldPosition, treeClearance * cellSize);
            }

            // Compatibility for scenes that still use the old trunk tilemap setup.
            if (treeTrunksTilemap == null)
                return false;

            Vector3Int treeCell = treeTrunksTilemap.WorldToCell(worldPosition);

            for (int x = -treeClearance; x <= treeClearance; x++)
            {
                for (int y = -treeClearance; y <= treeClearance; y++)
                {
                    if (treeTrunksTilemap.HasTile(treeCell + new Vector3Int(x, y, 0)))
                        return true;
                }
            }

            return false;
        }

        private void FindGroundTilemap()
        {
            if (groundTilemap == null) groundTilemap = FindNamedTilemap("Grass");
        }

        private void FindTreeTrunksTilemap()
        {
            if (treeTrunksTilemap == null) treeTrunksTilemap = FindNamedTilemap("TreesTrunk");
        }

        private void EnsurePlantsTilemap()
        {
            if (plantsTilemap != null)
            {
                ConfigurePlantsRenderer();
                return;
            }

            if (groundTilemap == null || groundTilemap.transform.parent == null)
                return;

            Transform grid = groundTilemap.transform.parent;
            Transform existingPlants = grid.Find(PlantsTilemapName);
            if (existingPlants != null)
            {
                plantsTilemap = existingPlants.GetComponent<Tilemap>();
                ConfigurePlantsRenderer();
                return;
            }

            GameObject plants = new GameObject(PlantsTilemapName);
            plants.transform.SetParent(grid, false);
            plantsTilemap = plants.AddComponent<Tilemap>();
            plants.AddComponent<TilemapRenderer>();
            ConfigurePlantsRenderer();
        }

        private void ConfigurePlantsRenderer()
        {
            if (plantsTilemap == null)
                return;

            TilemapRenderer renderer = plantsTilemap.GetComponent<TilemapRenderer>();
            if (renderer == null)
                renderer = plantsTilemap.gameObject.AddComponent<TilemapRenderer>();

            renderer.sortingLayerName = plantSortingLayer;
            renderer.sortingOrder = plantSortingOrder;
        }
    }
    #endregion

    #region WaterModule
    // Inspector field names and types stay on the original component instances.
    public abstract class WaterModule : MonoBehaviour
    {
        [Header("Insel")]
        [SerializeField] private Tilemap groundTilemap;

        [Header("Water Tilemap")]
        [SerializeField] private Tilemap waterTilemap;
        [SerializeField] private TileBase waterTile;
        [SerializeField, Min(1)] private int waterPadding = 4;
        [SerializeField] private int waterSortingOrder = -20;

        [Header("Fish Tiles in Water")]
        [SerializeField] private Tilemap beachTilemap;
        [SerializeField] private TileBase[] fishTiles;
        [Tooltip("Aktiviert: alle Fische starten im selben Animations-Frame. Deaktiviert: jeder Fisch startet versetzt.")]
        [SerializeField] private bool synchronizeFishAnimations = true;

        [Header("Fish Generation")]
        [SerializeField, Range(0f, 1f)] private float fishDensity = 0.025f;
        [SerializeField, Min(0)] private int maxFish = 40;
        [SerializeField, Range(0.1f, 5f)] private float fishScale = 1f;
        [SerializeField, Min(0)] private int shoreClearance = 2;
        [Tooltip("Mindestabstand zwischen Fischen in Tile-Zellen. 2 verhindert direkt benachbarte Fische.")]
        [SerializeField, Min(1)] private int fishSpacing = 2;
        [SerializeField] private int fishSeed = 97531;

        private readonly List<AnimatedTile> generatedFishAnimatedTiles = new List<AnimatedTile>();

        [ContextMenu("Generate Water")]
        public void GenerateWater()
        {
            if (groundTilemap == null || waterTilemap == null || waterTile == null)
            {
                Debug.LogWarning("TestWaterGen: Ground Tilemap, Water Tilemap oder Water Tile fehlt.", this);
                return;
            }

            bool foundLand = false;
            Vector3Int minCell = Vector3Int.zero;
            Vector3Int maxCell = Vector3Int.zero;

            foreach (Vector3Int groundCell in groundTilemap.cellBounds.allPositionsWithin)
            {
                if (!groundTilemap.HasTile(groundCell))
                    continue;

                Vector3 worldPosition = groundTilemap.GetCellCenterWorld(groundCell);
                Vector3Int waterCell = waterTilemap.WorldToCell(worldPosition);

                if (!foundLand)
                {
                    minCell = waterCell;
                    maxCell = waterCell;
                    foundLand = true;
                    continue;
                }

                minCell = Vector3Int.Min(minCell, waterCell);
                maxCell = Vector3Int.Max(maxCell, waterCell);
            }

            if (!foundLand)
            {
                Debug.LogWarning("TestWaterGen: Keine Land-Tiles gefunden. Generiere zuerst die Insel.", this);
                return;
            }

            waterTilemap.ClearAllTiles();
            ApplySortingOrder();

            for (int x = minCell.x - waterPadding; x <= maxCell.x + waterPadding; x++)
            {
                for (int y = minCell.y - waterPadding; y <= maxCell.y + waterPadding; y++)
                {
                    waterTilemap.SetTile(new Vector3Int(x, y, minCell.z), waterTile);
                }
            }
        }

        [ContextMenu("Generate Fish")]
        public void GenerateFish()
        {
            FindBeachTilemap();

            if (groundTilemap == null || waterTilemap == null || waterTile == null || !HasFishTiles())
            {
                Debug.LogWarning("TestWaterGen: Water Tilemap und mindestens ein Animated Fish Tile zuweisen.", this);
                return;
            }

            ResetWaterTiles();
            ClearGeneratedFishAnimatedTiles();
            System.Random random = new System.Random(fishSeed);
            int spawned = 0;
            List<Vector3Int> spawnedFishCells = new List<Vector3Int>();

            foreach (Vector3Int waterCell in waterTilemap.cellBounds.allPositionsWithin)
            {
                if (spawned >= maxFish)
                    break;

                if (!waterTilemap.HasTile(waterCell)
                    || IsLandOrBeach(waterCell)
                    || IsTooCloseToShore(waterCell)
                    || IsTooCloseToOtherFish(waterCell, spawnedFishCells)
                    || random.NextDouble() > fishDensity)
                {
                    continue;
                }

                waterTilemap.SetTile(waterCell, GetFishTile(random));
                waterTilemap.SetTileFlags(waterCell, TileFlags.None);
                waterTilemap.SetTransformMatrix(
                    waterCell,
                    Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * fishScale)
                );
                spawnedFishCells.Add(waterCell);
                spawned++;
            }

            Debug.Log($"TestWaterGen: {spawned} Fische erzeugt.", this);
        }

        private void ResetWaterTiles()
        {
            foreach (Vector3Int waterCell in waterTilemap.cellBounds.allPositionsWithin)
            {
                if (!waterTilemap.HasTile(waterCell))
                    continue;

                waterTilemap.SetTile(waterCell, waterTile);
                waterTilemap.SetTileFlags(waterCell, TileFlags.None);
                waterTilemap.SetTransformMatrix(waterCell, Matrix4x4.identity);
            }
        }

        [ContextMenu("New Random Fish Seed")]
        public void RandomizeFishSeed()
        {
            fishSeed = Random.Range(int.MinValue, int.MaxValue);
            GenerateFish();
        }

        private bool HasFishTiles() => HasTiles(fishTiles);

        private TileBase GetFishTile(System.Random random)
        {
            return SelectAnimatedTile(fishTiles, synchronizeFishAnimations, random, generatedFishAnimatedTiles);
        }

        private void ClearGeneratedFishAnimatedTiles() => ClearAnimationVariants(generatedFishAnimatedTiles);

        private bool IsLandOrBeach(Vector3Int waterCell)
        {
            Vector3 worldPosition = waterTilemap.GetCellCenterWorld(waterCell);
            if (groundTilemap.HasTile(groundTilemap.WorldToCell(worldPosition)))
                return true;

            return beachTilemap != null && beachTilemap.HasTile(beachTilemap.WorldToCell(worldPosition));
        }

        private bool IsTooCloseToShore(Vector3Int waterCell)
        {
            if (shoreClearance <= 0)
                return false;

            Vector3 worldPosition = waterTilemap.GetCellCenterWorld(waterCell);
            Vector3Int groundCell = groundTilemap.WorldToCell(worldPosition);

            for (int x = -shoreClearance; x <= shoreClearance; x++)
            {
                for (int y = -shoreClearance; y <= shoreClearance; y++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) > shoreClearance)
                        continue;

                    if (groundTilemap.HasTile(groundCell + new Vector3Int(x, y, 0)))
                        return true;
                }
            }

            return false;
        }

        private bool IsTooCloseToOtherFish(
            Vector3Int candidateCell,
            List<Vector3Int> spawnedFishCells
        )
        {
            foreach (Vector3Int fishCell in spawnedFishCells)
            {
                int distanceX = Mathf.Abs(candidateCell.x - fishCell.x);
                int distanceY = Mathf.Abs(candidateCell.y - fishCell.y);

                if (Mathf.Max(distanceX, distanceY) < fishSpacing)
                    return true;
            }

            return false;
        }

        private void FindBeachTilemap()
        {
            if (beachTilemap == null) beachTilemap = FindNamedTilemap("Beach");
        }

        private void ApplySortingOrder()
        {
            TilemapRenderer renderer = waterTilemap.GetComponent<TilemapRenderer>();
            if (renderer != null)
                renderer.sortingOrder = waterSortingOrder;
        }
    }
    #endregion

    #region BeachModule
    // Inspector field names and types stay on the original component instances.
    public abstract class BeachModule : MonoBehaviour
    {
        [Header("Generator")]
        public bool generatorEnabled = true;

        [Header("Tilemaps")]
        [FormerlySerializedAs("groundTilemap")]
        public Tilemap grassTilemap;
        public Tilemap grassOverlayTilemap;
        [FormerlySerializedAs("beachTilemap")]
        public Tilemap sandTilemap;
        public Tilemap sandTransitionTilemap;
        public Tilemap waterTilemap;

        [Header("Sand")]
        [FormerlySerializedAs("beachTile")]
        public TileBase sandTile;
        [Min(1)] public int beachWidth = 3;
        public TileBase sandVariant1;
        public TileBase sandVariant2;
        public TileBase sandVariant3;
        [Range(0f, 1f)] public float variant1Chance = 0.02f;
        [Range(0f, 1f)] public float variant2Chance = 0.01f;
        [Range(0f, 1f)] public float variant3Chance = 0.005f;

        [Header("Water")]
        public TileBase waterTile;
        [Range(1, 50)] public int waterBorder = 15;

        [Header("Einzeln zugewiesene Rand-Tiles")]
        public TileBase edgeTop;
        public TileBase edgeBottom;
        public TileBase edgeLeft;
        public TileBase edgeRight;
        public TileBase outerTopLeft;
        public TileBase outerTopRight;
        public TileBase outerBottomLeft;
        public TileBase outerBottomRight;
        public TileBase innerTopLeft;
        public TileBase innerTopRight;
        public TileBase innerBottomLeft;
        public TileBase innerBottomRight;

        [Header("Automatisch gedrehte Rand-Tiles")]
        [SerializeField] private int beachSortingOrder = -10;
        [SerializeField] private TileBase edgeTile;
        [SerializeField] private TileBase outerCornerTile;
        [SerializeField] private TileBase innerCornerTile;

        private bool isGenerating;

        // The compatibility component selects the existing Main-scene tile layout.
        protected virtual bool UsesRotatedTiles => false;

        protected virtual void OnValidate()
        {
            if (UsesRotatedTiles && !Application.isPlaying && !isGenerating
                && grassTilemap != null && sandTilemap != null && sandTile != null)
                GenerateBeach();
        }

        [ContextMenu("Generate Beach")]
        public void GenerateBeach()
        {
            if (!generatorEnabled || isGenerating)
                return;

            if (grassTilemap == null || sandTilemap == null || sandTile == null
                || (!UsesRotatedTiles && grassOverlayTilemap == null))
            {
                Debug.LogWarning("BeachGenerator: Ground-/Sand-Tilemap oder Sand-Tile fehlt; die Variante mit einzelnen Rand-Tiles benötigt zusätzlich die Grass Overlay Tilemap.", this);
                return;
            }

            isGenerating = true;
            try
            {
                if (UsesRotatedTiles)
                    GenerateRotatedBeach();
                else
                    GenerateOverlayBeach();
            }
            finally
            {
                isGenerating = false;
            }
        }

        private void GenerateOverlayBeach()
        {
            if (sandTransitionTilemap == null)
                sandTransitionTilemap = CreateTilemap("Sand Water Transition", -1);
            if (waterTilemap == null)
                waterTilemap = CreateTilemap("Water", -2);

            sandTilemap.ClearAllTiles();
            sandTransitionTilemap.ClearAllTiles();

            BoundsInt grassBounds = grassTilemap.cellBounds;
            BoundsInt overlayBounds = grassOverlayTilemap.cellBounds;
            int minX = Mathf.Min(grassBounds.xMin, overlayBounds.xMin) - beachWidth;
            int maxX = Mathf.Max(grassBounds.xMax, overlayBounds.xMax) + beachWidth;
            int minY = Mathf.Min(grassBounds.yMin, overlayBounds.yMin) - beachWidth;
            int maxY = Mathf.Max(grassBounds.yMax, overlayBounds.yMax) + beachWidth;

            foreach (Vector3Int cell in overlayBounds.allPositionsWithin)
            {
                if (grassOverlayTilemap.HasTile(cell))
                    sandTilemap.SetTile(cell, GetSandTile());
            }

            for (int x = minX; x < maxX; x++)
            {
                for (int y = minY; y < maxY; y++)
                {
                    Vector3Int cell = new Vector3Int(x, y, 0);
                    if (!IsIsland(cell) && IsNearIsland(cell))
                        sandTilemap.SetTile(cell, GetSandTile());
                }
            }

            GenerateSandWaterTransition();
            GenerateWater();
            Debug.Log("Beach generated!", this);
        }

        private void GenerateSandWaterTransition()
        {
            foreach (Vector3Int cell in ExpandedBounds(sandTilemap.cellBounds, 1).allPositionsWithin)
            {
                if (sandTilemap.HasTile(cell) || IsIsland(cell))
                    continue;

                bool up = sandTilemap.HasTile(cell + Vector3Int.up);
                bool down = sandTilemap.HasTile(cell + Vector3Int.down);
                bool left = sandTilemap.HasTile(cell + Vector3Int.left);
                bool right = sandTilemap.HasTile(cell + Vector3Int.right);
                bool upLeft = sandTilemap.HasTile(cell + new Vector3Int(-1, 1, 0));
                bool upRight = sandTilemap.HasTile(cell + new Vector3Int(1, 1, 0));
                bool downLeft = sandTilemap.HasTile(cell + new Vector3Int(-1, -1, 0));
                bool downRight = sandTilemap.HasTile(cell + new Vector3Int(1, -1, 0));

                TileBase selected = null;
                if (down && right && downRight) selected = innerTopLeft;
                else if (down && left && downLeft) selected = innerTopRight;
                else if (up && right && upRight) selected = innerBottomLeft;
                else if (up && left && upLeft) selected = innerBottomRight;
                else if (downLeft && !down && !left) selected = outerTopRight;
                else if (downRight && !down && !right) selected = outerTopLeft;
                else if (upLeft && !up && !left) selected = outerBottomRight;
                else if (upRight && !up && !right) selected = outerBottomLeft;
                else if (up) selected = edgeBottom;
                else if (down) selected = edgeTop;
                else if (left) selected = edgeRight;
                else if (right) selected = edgeLeft;

                if (selected != null)
                    sandTransitionTilemap.SetTile(cell, selected);
            }
        }

        private void GenerateWater()
        {
            if (waterTilemap == null || waterTile == null)
            {
                Debug.LogWarning("BeachGenerator: Water Tilemap oder Water Tile fehlt!", this);
                return;
            }

            BoundsInt bounds = ExpandedBounds(sandTilemap.cellBounds, waterBorder);
            waterTilemap.ClearAllTiles();
            foreach (Vector3Int cell in bounds.allPositionsWithin)
            {
                if (!sandTilemap.HasTile(cell) && !IsIsland(cell))
                    waterTilemap.SetTile(cell, waterTile);
            }
        }

        private TileBase GetSandTile()
        {
            float value = Random.value;
            if (sandVariant3 != null && value < variant3Chance)
                return sandVariant3;
            if (sandVariant2 != null && value < variant3Chance + variant2Chance)
                return sandVariant2;
            if (sandVariant1 != null && value < variant3Chance + variant2Chance + variant1Chance)
                return sandVariant1;
            return sandTile;
        }

        private Tilemap CreateTilemap(string tilemapName, int sortingOffset)
        {
            GameObject tilemapObject = new GameObject(tilemapName);
            tilemapObject.transform.SetParent(sandTilemap.transform.parent != null
                ? sandTilemap.transform.parent : transform, false);
            Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
            TilemapRenderer renderer = tilemapObject.AddComponent<TilemapRenderer>();
            TilemapRenderer sandRenderer = sandTilemap.GetComponent<TilemapRenderer>();
            if (sandRenderer != null)
            {
                renderer.sortingLayerID = sandRenderer.sortingLayerID;
                renderer.sortingOrder = sandRenderer.sortingOrder + sortingOffset;
            }
            return tilemap;
        }

        private static BoundsInt ExpandedBounds(BoundsInt bounds, int border)
        {
            return new BoundsInt(bounds.xMin - border, bounds.yMin - border, 0,
                bounds.size.x + border * 2, bounds.size.y + border * 2, 1);
        }

        private bool IsIsland(Vector3Int cell)
        {
            return grassTilemap.HasTile(cell)
                || (grassOverlayTilemap != null && grassOverlayTilemap.HasTile(cell));
        }

        private bool IsNearIsland(Vector3Int cell)
        {
            for (int x = -beachWidth; x <= beachWidth; x++)
            {
                for (int y = -beachWidth; y <= beachWidth; y++)
                {
                    if (Mathf.Sqrt(x * x + y * y) <= beachWidth
                        && IsIsland(cell + new Vector3Int(x, y, 0)))
                        return true;
                }
            }
            return false;
        }

        private void GenerateRotatedBeach()
        {
            TilemapRenderer renderer = sandTilemap.GetComponent<TilemapRenderer>();
            if (renderer != null)
                renderer.sortingOrder = beachSortingOrder;
            sandTilemap.ClearAllTiles();

            HashSet<Vector3Int> beachCells = new HashSet<Vector3Int>();
            HashSet<Vector3Int> underlayCells = new HashSet<Vector3Int>();
            foreach (Vector3Int landCell in grassTilemap.cellBounds.allPositionsWithin)
            {
                if (!grassTilemap.HasTile(landCell))
                    continue;

                for (int x = -beachWidth; x <= beachWidth; x++)
                {
                    for (int y = -beachWidth; y <= beachWidth; y++)
                    {
                        Vector3Int groundCell = landCell + new Vector3Int(x, y, 0);
                        Vector3Int beachCell = sandTilemap.WorldToCell(grassTilemap.GetCellCenterWorld(groundCell));
                        if (grassTilemap.HasTile(groundCell))
                            underlayCells.Add(beachCell);
                        else
                            beachCells.Add(beachCell);
                    }
                }
            }

            // Ground and beach tilemaps may use different cell sizes.
            beachCells.ExceptWith(underlayCells);
            FillNarrowBeachGaps(beachCells);
            foreach (Vector3Int cell in underlayCells)
                sandTilemap.SetTile(cell, sandTile);
            foreach (Vector3Int cell in beachCells)
                sandTilemap.SetTile(cell, sandTile);

            GenerateOuterEdge(beachCells);
            Debug.Log($"BeachGenerator: {beachCells.Count} Beach-Tiles erzeugt.", this);
        }

        private void GenerateOuterEdge(HashSet<Vector3Int> beachCells)
        {
            HashSet<Vector3Int> candidates = new HashSet<Vector3Int>();
            HashSet<Vector3Int> edgeCells = new HashSet<Vector3Int>();
            HashSet<Vector3Int> outerCornerCells = new HashSet<Vector3Int>();
            foreach (Vector3Int beachCell in beachCells)
            {
                foreach (Vector3Int direction in CardinalDirections)
                {
                    Vector3Int candidate = beachCell + direction;
                    if (!beachCells.Contains(candidate))
                        candidates.Add(candidate);
                }
            }

            foreach (Vector3Int candidate in candidates)
            {
                List<Vector3Int> directions = GetBeachDirections(candidate, beachCells);
                if (directions.Count == 0 || IsGroundAtBeachCell(candidate))
                    continue;

                if (directions.Count == 2
                    && TryGetCornerDirection(candidate, beachCells, out Vector3Int cornerDirection)
                    && outerCornerTile != null)
                {
                    SetTileTransform(candidate, outerCornerTile, GetCornerRotation(cornerDirection));
                    outerCornerCells.Add(candidate);
                }
                else if (directions.Count == 1 && edgeTile != null)
                {
                    SetTileTransform(candidate, edgeTile, GetEdgeRotation(directions[0]));
                    edgeCells.Add(candidate);
                }
            }
            GenerateInnerCorners(beachCells, edgeCells, outerCornerCells);
        }

        private void FillNarrowBeachGaps(HashSet<Vector3Int> beachCells)
        {
            if (beachCells.Count == 0)
                return;

            for (int pass = 0; pass < 3; pass++)
            {
                int minX = int.MaxValue, maxX = int.MinValue;
                int minY = int.MaxValue, maxY = int.MinValue;
                foreach (Vector3Int cell in beachCells)
                {
                    minX = Mathf.Min(minX, cell.x);
                    maxX = Mathf.Max(maxX, cell.x);
                    minY = Mathf.Min(minY, cell.y);
                    maxY = Mathf.Max(maxY, cell.y);
                }

                List<Vector3Int> toFill = new List<Vector3Int>();
                for (int x = minX + 1; x < maxX; x++)
                {
                    for (int y = minY + 1; y < maxY; y++)
                    {
                        Vector3Int cell = new Vector3Int(x, y, 0);
                        if (beachCells.Contains(cell) || IsGroundAtBeachCell(cell))
                            continue;
                        bool horizontal = beachCells.Contains(cell + Vector3Int.left)
                            && beachCells.Contains(cell + Vector3Int.right);
                        bool vertical = beachCells.Contains(cell + Vector3Int.up)
                            && beachCells.Contains(cell + Vector3Int.down);
                        if (horizontal || vertical)
                            toFill.Add(cell);
                    }
                }
                if (toFill.Count == 0)
                    break;
                foreach (Vector3Int cell in toFill)
                    beachCells.Add(cell);
            }
        }

        private void GenerateInnerCorners(HashSet<Vector3Int> beachCells,
            HashSet<Vector3Int> edgeCells, HashSet<Vector3Int> outerCornerCells)
        {
            if (innerCornerTile == null)
                return;

            Dictionary<Vector3Int, float> rotations = new Dictionary<Vector3Int, float>();
            HashSet<Vector3Int> ambiguousCells = new HashSet<Vector3Int>();
            foreach (Vector3Int beachCell in beachCells)
            {
                foreach (Vector3Int diagonal in DiagonalDirections)
                {
                    Vector3Int cornerCell = beachCell + diagonal;
                    Vector3Int sideA = beachCell + new Vector3Int(diagonal.x, 0, 0);
                    Vector3Int sideB = beachCell + new Vector3Int(0, diagonal.y, 0);
                    if (beachCells.Contains(cornerCell) || edgeCells.Contains(cornerCell)
                        || outerCornerCells.Contains(cornerCell) || IsGroundAtBeachCell(cornerCell)
                        || IsGroundAtBeachCell(sideA) || IsGroundAtBeachCell(sideB)
                        || GetBeachDirections(cornerCell, beachCells).Count != 0)
                        continue;

                    Vector3Int inward = new Vector3Int(-diagonal.x, -diagonal.y, 0);
                    float rotation = GetCornerRotation(inward) + 180f;
                    if (rotations.TryGetValue(cornerCell, out float previous) && previous != rotation)
                        ambiguousCells.Add(cornerCell);
                    else
                        rotations[cornerCell] = rotation;
                }
            }
            foreach (KeyValuePair<Vector3Int, float> corner in rotations)
            {
                if (!ambiguousCells.Contains(corner.Key))
                    SetTileTransform(corner.Key, innerCornerTile, corner.Value);
            }
        }

        private static readonly Vector3Int[] CardinalDirections =
        {
            Vector3Int.up, Vector3Int.right, Vector3Int.down, Vector3Int.left
        };

        private static readonly Vector3Int[] DiagonalDirections =
        {
            new Vector3Int(-1, -1, 0), new Vector3Int(1, -1, 0),
            new Vector3Int(1, 1, 0), new Vector3Int(-1, 1, 0)
        };

        private static List<Vector3Int> GetBeachDirections(Vector3Int cell, HashSet<Vector3Int> beachCells)
        {
            List<Vector3Int> directions = new List<Vector3Int>();
            foreach (Vector3Int direction in CardinalDirections)
            {
                if (beachCells.Contains(cell + direction))
                    directions.Add(direction);
            }
            return directions;
        }

        private static bool TryGetCornerDirection(Vector3Int cell,
            HashSet<Vector3Int> beachCells, out Vector3Int cornerDirection)
        {
            // Keep the original priority when several corners fit the same cell.
            Vector3Int[] corners = { DiagonalDirections[0], DiagonalDirections[3],
                DiagonalDirections[2], DiagonalDirections[1] };
            foreach (Vector3Int corner in corners)
            {
                if (beachCells.Contains(cell + new Vector3Int(corner.x, 0, 0))
                    && beachCells.Contains(cell + new Vector3Int(0, corner.y, 0)))
                {
                    cornerDirection = corner;
                    return true;
                }
            }
            cornerDirection = Vector3Int.zero;
            return false;
        }

        private static float GetEdgeRotation(Vector3Int direction)
        {
            if (direction == Vector3Int.down) return 90f;
            if (direction == Vector3Int.left) return 0f;
            if (direction == Vector3Int.up) return -90f;
            return 180f;
        }

        private static float GetCornerRotation(Vector3Int direction)
        {
            if (direction == new Vector3Int(-1, -1, 0)) return -90f;
            if (direction == new Vector3Int(-1, 1, 0)) return 180f;
            if (direction == new Vector3Int(1, 1, 0)) return 90f;
            return 0f;
        }

        private bool IsGroundAtBeachCell(Vector3Int cell)
        {
            return grassTilemap.HasTile(grassTilemap.WorldToCell(sandTilemap.GetCellCenterWorld(cell)));
        }

        private void SetTileTransform(Vector3Int cell, TileBase tile, float rotation)
        {
            sandTilemap.SetTile(cell, tile);
            sandTilemap.SetTileFlags(cell, TileFlags.None);
            sandTilemap.SetTransformMatrix(cell,
                Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotation), Vector3.one));
        }
    }
    #endregion

    #region GrassIslandModule
    // Inspector field names and types stay on the original component instances.
    public abstract class GrassIslandModule : MonoBehaviour
    {
        [Header("Tilemap")]
        public Tilemap grassTilemap;

        [Header("Grass Tiles")]
        public TileBase normalGrass;
        public TileBase grassVariant1;
        public TileBase grassVariant2;
        public TileBase grassVariant3;

        [Header("Island Size")]
        public int width = 80;
        public int height = 80;

        [Header("Generation")]
        [Tooltip("Wenn deaktiviert, wird diese Welt beim Start und bei manueller Ausfuehrung nicht generiert.")]
        public bool generatorEnabled = true;

        public int seed;

        public bool randomSeed = true;

        [Range(0.005f, 0.15f)]
        public float noiseScale = 0.035f;

        [Range(0f, 1f)]
        public float noiseStrength = 0.22f;

        [Header("Organic Island Shape")]
        [Range(0f, 0.5f)]
        public float shapeWarpStrength = 0.22f;

        [Range(0.005f, 0.12f)]
        public float edgeNoiseScale = 0.045f;

        [Range(0f, 1f)]
        public float edgeNoiseStrength = 0.35f;

        [Range(0f, 1f)]
        public float islandThreshold = 0.38f;

        [Header("Island Smoothing")]
        [Range(0, 10)]
        public int smoothIterations = 4;

        [Header("Grass Variants")]
        [Range(0f, 1f)]
        public float variant1Chance = 0.02f;

        [Range(0f, 1f)]
        public float variant2Chance = 0.01f;

        [Range(0f, 1f)]
        public float variant3Chance = 0.005f;

        private bool[,] islandMap;

        protected virtual void Start()
        {
            if (generatorEnabled)
            {
                GenerateIsland();
            }
        }

        public void GenerateIsland()
        {
            if (!generatorEnabled)
            {
                Debug.Log("GrassIslandGenerator: Generator ist im Inspector deaktiviert.", this);
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogError("Island size must be greater than zero.");
                return;
            }

            if (randomSeed)
            {
                seed = Random.Range(-999999999, 999999999);
            }
            
            if (grassTilemap == null)
            {
                Debug.LogError("Grass Tilemap fehlt!");
                return;
            }

            if (normalGrass == null)
            {
                Debug.LogError("Normal Grass fehlt!");
                return;
            }

            grassTilemap.ClearAllTiles();

            islandMap = new bool[width, height];

            System.Random random =
                new System.Random(seed);

            float offsetX =
                random.Next(-100000, 100000);

            float offsetY =
                random.Next(-100000, 100000);

            float aspectX = Mathf.Lerp(0.88f, 1.18f, (float)random.NextDouble());
            float aspectY = Mathf.Lerp(0.88f, 1.18f, (float)random.NextDouble());
            float angle = random.Next(-45, 46) * Mathf.Deg2Rad;
            float sinAngle = Mathf.Sin(angle);
            float cosAngle = Mathf.Cos(angle);

            // ========================================
            // 1. ROHE INSEL FORM GENERIEREN
            // ========================================

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float rawX = (x - width / 2f) / (width / 2f);
                    float rawY = (y - height / 2f) / (height / 2f);

                    float warpX = (Mathf.PerlinNoise(
                        (x + offsetX) * noiseScale * 0.45f,
                        (y + offsetY) * noiseScale * 0.45f) - 0.5f) * 2f;
                    float warpY = (Mathf.PerlinNoise(
                        (x + offsetX + 1731f) * noiseScale * 0.45f,
                        (y + offsetY + 941f) * noiseScale * 0.45f) - 0.5f) * 2f;

                    rawX += warpX * shapeWarpStrength;
                    rawY += warpY * shapeWarpStrength;

                    float rotatedX = rawX * cosAngle - rawY * sinAngle;
                    float rotatedY = rawX * sinAngle + rawY * cosAngle;
                    float nx = rotatedX / aspectX;
                    float ny = rotatedY / aspectY;

                    float distance =
                        Mathf.Sqrt(
                            nx * nx +
                            ny * ny
                        );

                    // Mitte = 1
                    // Außen = 0
                    float islandMask =
                        1f - Mathf.Clamp01(distance);

                    float noise =
                        Mathf.PerlinNoise(
                            (x + offsetX) * noiseScale,
                            (y + offsetY) * noiseScale
                        );

                    float centeredNoise =
                        noise - 0.5f;

                    float edgeNoise =
                        Mathf.PerlinNoise(
                            (x + offsetX + 341f) * edgeNoiseScale,
                            (y + offsetY + 719f) * edgeNoiseScale) - 0.5f;

                    float islandValue =
                        islandMask +
                        centeredNoise * noiseStrength +
                        edgeNoise * 2f * edgeNoiseStrength;

                    islandMap[x, y] =
                        islandValue >= islandThreshold;
                }
            }

            // ========================================
            // 2. KÜSTE GLÄTTEN
            // ========================================

            for (int i = 0; i < smoothIterations; i++)
            {
                SmoothIsland();
            }

            // ========================================
            // 3. GRASS TILES SETZEN
            // ========================================

            int startX = -width / 2;
            int startY = -height / 2;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!islandMap[x, y])
                        continue;

                    Vector3Int position =
                        new Vector3Int(
                            startX + x,
                            startY + y,
                            0
                        );

                    grassTilemap.SetTile(
                        position,
                        GetGrassTile(random)
                    );
                }
            }

            // ========================================
            // 4. OVERLAY DANACH GENERIEREN
            // ========================================

            GrassEdgeOverlay overlay =
                FindFirstObjectByType<GrassEdgeOverlay>();

            if (overlay != null)
            {
                overlay.GenerateOverlay();
            }

            BeachGenerator beach =
                FindFirstObjectByType<BeachGenerator>();

            if (beach != null)
            {
                beach.GenerateBeach();
            }

            TreeGenerator trees =
                FindFirstObjectByType<TreeGenerator>();

            if (trees != null)
            {
                trees.GenerateTrees();
            }
        }

        // ============================================
        // INSEL GLÄTTEN
        // ============================================

        private void SmoothIsland()
        {
            bool[,] smoothed =
                new bool[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int landNeighbors =
                        CountLandNeighbors(x, y);

                    // Viele Land-Nachbarn:
                    // dieses Feld wird Land.
                    if (landNeighbors >= 5)
                    {
                        smoothed[x, y] = true;
                    }

                    // Wenige Land-Nachbarn:
                    // dieses Feld wird leer.
                    else if (landNeighbors <= 3)
                    {
                        smoothed[x, y] = false;
                    }

                    // Bei 4 behalten wir den alten Zustand.
                    else
                    {
                        smoothed[x, y] =
                            islandMap[x, y];
                    }
                }
            }

            islandMap = smoothed;
        }

        // ============================================
        // 8 NACHBARN ZÄHLEN
        // ============================================

        private int CountLandNeighbors(int x, int y)
        {
            int count = 0;

            for (int offsetX = -1;
                 offsetX <= 1;
                 offsetX++)
            {
                for (int offsetY = -1;
                     offsetY <= 1;
                     offsetY++)
                {
                    if (
                        offsetX == 0 &&
                        offsetY == 0
                    )
                    {
                        continue;
                    }

                    int checkX = x + offsetX;
                    int checkY = y + offsetY;

                    // Außerhalb der Map = kein Land
                    if (
                        checkX < 0 ||
                        checkX >= width ||
                        checkY < 0 ||
                        checkY >= height
                    )
                    {
                        continue;
                    }

                    if (islandMap[checkX, checkY])
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        // ============================================
        // GRASS VARIANTE
        // ============================================

        private TileBase GetGrassTile(
            System.Random random
        )
        {
            double value =
                random.NextDouble();

            if (
                grassVariant3 != null &&
                value < variant3Chance
            )
            {
                return grassVariant3;
            }

            if (
                grassVariant2 != null &&
                value <
                variant3Chance +
                variant2Chance
            )
            {
                return grassVariant2;
            }

            if (
                grassVariant1 != null &&
                value <
                variant3Chance +
                variant2Chance +
                variant1Chance
            )
            {
                return grassVariant1;
            }

            return normalGrass;
        }
    }
    #endregion

    #region AlternatingIslandModule
    // Inspector field names and types stay on the original component instances.
    public abstract class AlternatingIslandModule : MonoBehaviour
    {
        [Header("Ziel-Tilemap")]
        [Tooltip("Tilemap, auf der die neue Welt gezeichnet wird.")]
        [SerializeField] private Tilemap targetTilemap;

        [Header("Tile")]
        [FormerlySerializedAs("firstTile")]
        [SerializeField] private TileBase worldTile;

        [Header("Automatisch gedrehte Kanten-Tiles")]
        [Tooltip("Das obere Kanten-Tile. Der Generator dreht es fuer die anderen drei Richtungen.")]
        [SerializeField] private TileBase edgeTile;
        [Tooltip("Die obere linke Aussen-Ecke. Der Generator dreht sie fuer die anderen Ecken.")]
        [SerializeField] private TileBase outerCornerTile;

        [Header("Weltgroesse")]
        [Min(1)] [SerializeField] private int width = 32;
        [Min(1)] [SerializeField] private int height = 24;
        [Tooltip("Mittelpunkt der Insel in der Tilemap.")]
        [SerializeField] private Vector3Int origin = Vector3Int.zero;

        [Header("Insel-Form")]
        [Tooltip("Bestimmt die Groesse der Perlin-Noise-Strukturen. Kleinere Werte erzeugen groessere Landmassen.")]
        [Min(0.001f)] [SerializeField] private float noiseScale = 0.08f;
        [Range(0f, 1f)] [Tooltip("Hoehere Werte erzeugen weniger Land.")]
        [SerializeField] private float landThreshold = 0.38f;
        [Range(0f, 1f)] [Tooltip("Zieht die Insel zu ihrer Mitte und sorgt fuer Wasser am Rand.")]
        [SerializeField] private float edgeFalloff = 0.55f;
        [SerializeField] private int seed = 12345;

        [Header("Tile-Groesse")]
        [Tooltip("Passt die separate Tilemap automatisch an die Spritegroesse an, damit jedes Tile eine volle Grid-Zelle ausfuellt.")]
        [SerializeField] private bool fitSpritesToGridCell = true;
        [Min(0.01f)] [SerializeField] private float desiredTileWorldSize = 1f;
        [Tooltip("Fallback fuer RuleTiles oder TileAssets ohne direkt auslesbares Sprite. Bei 16px-Sprites mit 100 PPU ist 6.25 korrekt.")]
        [Min(0.01f)] [SerializeField] private float fallbackTilemapScale = 6.25f;

        [Header("Generierung")]
        [Tooltip("Wenn deaktiviert, wird diese Welt beim Start und bei manueller Ausfuehrung nicht generiert.")]
        [SerializeField] private bool generatorEnabled = true;
        [SerializeField] private bool generateOnStart = true;
        [SerializeField] private bool clearTilemapBeforeGenerate = true;

        protected virtual void Start()
        {
            if (generateOnStart)
                GenerateWorld();
        }

        /// <summary>Kann auch ueber den Inspector als Context-Menue ausgefuehrt werden.</summary>
        [ContextMenu("Generate island")]
        public void GenerateWorld()
        {
            if (!generatorEnabled)
            {
                Debug.Log("AlternatingTileWorldGenerator: Generator ist im Inspector deaktiviert.", this);
                return;
            }

            if (!ValidateSetup())
                return;

            FitSpritesToGridCell();

            if (clearTilemapBeforeGenerate)
                targetTilemap.ClearAllTiles();

            int placedTiles = 0;
            Vector2 noiseOffset = GetNoiseOffset();
            int minX = width;
            int maxX = -1;
            int minY = height;
            int maxY = -1;

            bool[,] landTiles = new bool[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    landTiles[x, y] = IsLand(x, y, noiseOffset);
                    if (landTiles[x, y])
                    {
                        placedTiles++;
                        minX = Mathf.Min(minX, x);
                        maxX = Mathf.Max(maxX, x);
                        minY = Mathf.Min(minY, y);
                        maxY = Mathf.Max(maxY, y);
                    }
                }
            }

            int startX = placedTiles > 0 ? -Mathf.RoundToInt((minX + maxX) * 0.5f) : 0;
            int startY = placedTiles > 0 ? -Mathf.RoundToInt((minY + maxY) * 0.5f) : 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!landTiles[x, y])
                        continue;

                    TileBase tile = GetTileForPosition(x, y, landTiles, out float rotationDegrees);
                    Vector3Int position = origin + new Vector3Int(startX + x, startY + y, 0);
                    targetTilemap.SetTile(position, tile);

                    if (!Mathf.Approximately(rotationDegrees, 0f))
                    {
                        targetTilemap.SetTileFlags(position, TileFlags.None);
                        targetTilemap.SetTransformMatrix(
                            position,
                            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotationDegrees), Vector3.one));
                    }
                }
            }

            targetTilemap.RefreshAllTiles();
            Debug.Log($"AlternatingTileWorldGenerator: Insel mit {placedTiles} Tiles generiert.", this);
        }

        private bool ValidateSetup()
        {
            if (targetTilemap == null)
            {
                Debug.LogError("AlternatingTileWorldGenerator: Bitte targetTilemap im Inspector zuweisen.", this);
                return false;
            }

            if (worldTile == null)
            {
                Debug.LogError("AlternatingTileWorldGenerator: Bitte worldTile im Inspector zuweisen.", this);
                return false;
            }

            if (width < 1 || height < 1)
            {
                Debug.LogError("AlternatingTileWorldGenerator: width und height muessen mindestens 1 sein.", this);
                return false;
            }

            return true;
        }

        private bool IsLand(int x, int y, Vector2 noiseOffset)
        {
            float noise = Mathf.PerlinNoise(
                (x + noiseOffset.x) * noiseScale,
                (y + noiseOffset.y) * noiseScale);

            float normalizedX = width > 1 ? x / (float)(width - 1) * 2f - 1f : 0f;
            float normalizedY = height > 1 ? y / (float)(height - 1) * 2f - 1f : 0f;
            float distanceFromCenter = Mathf.Clamp01(new Vector2(normalizedX, normalizedY).magnitude);
            float islandValue = noise - distanceFromCenter * edgeFalloff;

            return islandValue >= landThreshold;
        }

        private TileBase GetTileForPosition(int x, int y, bool[,] landTiles, out float rotationDegrees)
        {
            rotationDegrees = 0f;
            bool up = HasLand(x, y + 1, landTiles);
            bool down = HasLand(x, y - 1, landTiles);
            bool left = HasLand(x - 1, y, landTiles);
            bool right = HasLand(x + 1, y, landTiles);
            if (!up && !left)
                return SelectTile(outerCornerTile, 90f, out rotationDegrees);
            if (!up && !right)
                return SelectTile(outerCornerTile, 0f, out rotationDegrees);
            if (!down && !left)
                return SelectTile(outerCornerTile, 180f, out rotationDegrees);
            if (!down && !right)
                return SelectTile(outerCornerTile, -90f, out rotationDegrees);

            if (!up)
                return SelectTile(edgeTile, 180f, out rotationDegrees);
            if (!down)
                return SelectTile(edgeTile, 0f, out rotationDegrees);
            if (!left)
                return SelectTile(edgeTile, -90f, out rotationDegrees);
            if (!right)
                return SelectTile(edgeTile, 90f, out rotationDegrees);

            return worldTile;
        }

        private TileBase SelectTile(TileBase configuredTile, float rotation, out float rotationDegrees)
        {
            if (configuredTile != null)
            {
                rotationDegrees = rotation;
                return configuredTile;
            }

            rotationDegrees = 0f;
            return worldTile;
        }

        private bool HasLand(int x, int y, bool[,] landTiles)
        {
            return x >= 0 && x < width && y >= 0 && y < height && landTiles[x, y];
        }

        private Vector2 GetNoiseOffset()
        {
            float seedValue = seed;
            return new Vector2(
                Mathf.Repeat(seedValue * 0.1031f, 10000f),
                Mathf.Repeat(seedValue * 0.11369f, 10000f));
        }

        private void FitSpritesToGridCell()
        {
            if (!fitSpritesToGridCell)
                return;

            float scale = fallbackTilemapScale;
            if (worldTile is Tile tile && tile.sprite != null &&
                tile.sprite.pixelsPerUnit > 0f && tile.sprite.rect.width > 0f)
            {
                float sourceWorldWidth = tile.sprite.rect.width / tile.sprite.pixelsPerUnit;
                scale = desiredTileWorldSize / sourceWorldWidth;
            }

            targetTilemap.transform.localScale = Vector3.one * scale;
        }
    }
    #endregion

    #region GrassOverlayModule
    // Inspector field names and types stay on the original component instances.
    public abstract class GrassOverlayModule : MonoBehaviour
    {
        [Header("Generator")]
        [Tooltip("Wenn deaktiviert, wird das Gras-Overlay nicht erzeugt.")]
        public bool generatorEnabled = true;

        [Header("Tilemaps")]
        public Tilemap grassTilemap;
        public Tilemap overlayTilemap;

        [Header("Straight Edges")]
        public TileBase edgeTop;
        public TileBase edgeBottom;
        public TileBase edgeLeft;
        public TileBase edgeRight;

        [Header("Outer Corners")]
        public TileBase outerTopLeft;
        public TileBase outerTopRight;
        public TileBase outerBottomLeft;
        public TileBase outerBottomRight;

        [Header("Inner Corners")]
        public TileBase innerTopLeft;
        public TileBase innerTopRight;
        public TileBase innerBottomLeft;
        public TileBase innerBottomRight;

        private struct OverlayData
        {
            public Vector3Int position;
            public TileBase tile;
            public float rotationDegrees;

            public OverlayData(Vector3Int position, TileBase tile, float rotationDegrees)
            {
                this.position = position;
                this.tile = tile;
                this.rotationDegrees = rotationDegrees;
            }
        }

        public void GenerateOverlay()
        {
            if (!generatorEnabled)
            {
                Debug.Log("GrassEdgeOverlay: Generator ist im Inspector deaktiviert.", this);
                return;
            }

            if (grassTilemap == null || overlayTilemap == null)
            {
                Debug.LogError("Grass Tilemap oder Overlay Tilemap fehlt!");
                return;
            }

            overlayTilemap.ClearAllTiles();

            BoundsInt bounds = grassTilemap.cellBounds;

            // Erst speichern wir alle Rand-Tiles.
            // Das Gras wird NOCH NICHT gelöscht.
            List<OverlayData> overlays = new List<OverlayData>();

            foreach (Vector3Int pos in bounds.allPositionsWithin)
            {
                if (!HasGrass(pos))
                    continue;

                bool up =
                    HasGrass(pos + Vector3Int.up);

                bool down =
                    HasGrass(pos + Vector3Int.down);

                bool left =
                    HasGrass(pos + Vector3Int.left);

                bool right =
                    HasGrass(pos + Vector3Int.right);

                bool upLeft =
                    HasGrass(pos + new Vector3Int(-1, 1, 0));

                bool upRight =
                    HasGrass(pos + new Vector3Int(1, 1, 0));

                bool downLeft =
                    HasGrass(pos + new Vector3Int(-1, -1, 0));

                bool downRight =
                    HasGrass(pos + new Vector3Int(1, -1, 0));

                TileBase selected = null;
                float rotationDegrees = 0f;

                // ============================
                // OUTER CORNERS
                // ============================

                if (!up && !left)
                {
                    selected = outerTopLeft;
                    rotationDegrees = -90f;
                }
                else if (!up && !right)
                {
                    selected = outerTopRight;
                    rotationDegrees = -90f;
                }
                else if (!down && !left)
                {
                    selected = outerBottomLeft;
                    rotationDegrees = -90f;
                }
                else if (!down && !right)
                {
                    selected = outerBottomRight;
                    rotationDegrees = -90f;
                }

                // ============================
                // STRAIGHT EDGES
                // ============================

                else if (!up)
                {
                    selected = edgeTop;
                }
                else if (!down)
                {
                    selected = edgeBottom;
                }
                else if (!left)
                {
                    selected = edgeLeft;
                }
                else if (!right)
                {
                    selected = edgeRight;
                }

                // ============================
                // INNER CORNERS
                // ============================

                else if (!upLeft)
                {
                    selected = innerTopLeft;
                }
                else if (!upRight)
                {
                    selected = innerTopRight;
                }
                else if (!downLeft)
                {
                    selected = innerBottomLeft;
                }
                else if (!downRight)
                {
                    selected = innerBottomRight;
                }

                if (selected != null)
                {
                    overlays.Add(
                        new OverlayData(
                            pos,
                            selected,
                            rotationDegrees
                        )
                    );
                }
            }

            // =====================================
            // JETZT Gras löschen + Overlay setzen
            // =====================================

            foreach (OverlayData data in overlays)
            {
                // Gras unter dem Overlay entfernen
                grassTilemap.SetTile(
                    data.position,
                    null
                );

                // Overlay setzen
                overlayTilemap.SetTile(
                    data.position,
                    data.tile
                );

                // Unity uses positive Z for counter-clockwise rotation.
                // -90 degrees therefore turns only the outer corner tiles right/clockwise.
                if (!Mathf.Approximately(data.rotationDegrees, 0f))
                {
                    overlayTilemap.SetTileFlags(data.position, TileFlags.None);
                    overlayTilemap.SetTransformMatrix(
                        data.position,
                        Matrix4x4.TRS(
                            Vector3.zero,
                            Quaternion.Euler(0f, 0f, data.rotationDegrees),
                            Vector3.one
                        )
                    );
                }
            }

            Debug.Log(
                "Grass Edge Tiles ersetzt: " +
                overlays.Count
            );
        }

        private bool HasGrass(Vector3Int position)
        {
            return grassTilemap.HasTile(position);
        }
    }
    #endregion

    #region SpriteTreeModule
    // Inspector field names and types stay on the original component instances.
    public abstract class SpriteTreeModule : MonoBehaviour
    {
        [Header("Tilemap")]
        public Tilemap grassTilemap;
        public Tilemap detailsTilemap;

        [Header("Tree Prefabs")]
        public GameObject[] treePrefabs;

        [Header("Oder einzelne Baum-Sprites")]
        public Sprite treeSprite;
        public Sprite[] treeSprites;

        [Min(11)]
        [Tooltip("Baeume liegen mindestens auf Order 11, vor dem Spieler auf Order 10.")]
        public int treeSortingOrder = 11;
        public string renderSortingLayer = "Default";

        public bool fadeWhenPlayerBehind = true;

        [Header("Tree Collision")]
        public bool addTreeCollider = true;
        public Vector2 colliderSize = new Vector2(0.5f, 0.45f);
        public Vector2 colliderOffset = new Vector2(0f, -0.25f);

        [Range(0f, 1f)]
        public float treeDensity = 0.08f;

        [Min(0)]
        public int maxTrees = 80;

        [Min(0f)]
        public float minimumDistance = 2f;

        [Header("Generation")]
        [Tooltip("Wenn deaktiviert, werden keine Bäume erzeugt.")]
        public bool generatorEnabled = true;
        public int seed;
        public bool randomSeed = true;

        private Transform generatedTreesRoot;
        private bool hasGeneratedTrees;

        protected virtual IEnumerator Start()
        {
            // Island generation also calls GenerateTrees. Wait for all Start methods
            // so their execution order cannot generate a second set of trees.
            yield return null;
            if (generatorEnabled && !hasGeneratedTrees)
            {
                GenerateTrees();
            }
        }

        [ContextMenu("Generate Trees")]
        public void GenerateTrees()
        {
            if (!generatorEnabled)
            {
                Debug.Log("TreeGenerator: Generator ist im Inspector deaktiviert.", this);
                return;
            }

            // Existing scenes can still serialize the old value 2 even after the
            // field default was changed. Keep trees in front of the player (10).
            treeSortingOrder = Mathf.Max(11, treeSortingOrder);

            List<GameObject> validPrefabs = GetValidTreePrefabs();
            List<Sprite> validSprites = GetValidTreeSprites();

            if (validPrefabs.Count == 0 && validSprites.Count == 0)
            {
                SpriteRenderer sourceRenderer = GetComponent<SpriteRenderer>();
                if (sourceRenderer != null && sourceRenderer.sprite != null)
                {
                    validSprites.Add(sourceRenderer.sprite);
                }
            }

            if (grassTilemap == null)
            {
                GrassIslandGenerator island = FindFirstObjectByType<GrassIslandGenerator>();
                if (island != null) grassTilemap = island.grassTilemap;
            }

            FindDetailsTilemap();

            bool hasPrefabs = validPrefabs.Count > 0;
            bool hasSprites = validSprites.Count > 0;

            if (grassTilemap == null)
            {
                Debug.LogWarning("TreeGenerator: Grass Tilemap im Inspector zuweisen!", this);
                return;
            }

            if (maxTrees <= 0 || treeDensity <= 0f)
            {
                ClearGeneratedTrees();
                hasGeneratedTrees = true;
                return;
            }

            if (!hasPrefabs && !hasSprites)
            {
                Debug.LogWarning("TreeGenerator: Mindestens ein Tree Prefab oder Tree Sprite im Inspector zuweisen!", this);
                return;
            }

            if (randomSeed)
            {
                seed = Random.Range(-999999999, 999999999);
            }

            ClearGeneratedTrees();
            EnsureGeneratedTreesRoot();
            hasGeneratedTrees = true;

            System.Random random = new System.Random(seed);
            List<Vector3> positions = new List<Vector3>();
            BoundsInt bounds = grassTilemap.cellBounds;

            foreach (Vector3Int cell in bounds.allPositionsWithin)
            {
                if (!grassTilemap.HasTile(cell) || random.NextDouble() > treeDensity)
                {
                    continue;
                }

                Vector3 position = grassTilemap.GetCellCenterWorld(cell);
                bool tooClose = false;

                foreach (Vector3 otherPosition in positions)
                {
                    if (Vector3.Distance(position, otherPosition) < minimumDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    positions.Add(position);
                }

                if (positions.Count >= maxTrees)
                {
                    break;
                }
            }

            int spawnedTrees = 0;

            foreach (Vector3 position in positions)
            {
                if (hasPrefabs)
                {
                    GameObject prefab = validPrefabs[random.Next(validPrefabs.Count)];
                    GameObject tree = Instantiate(
                        prefab,
                        position,
                        Quaternion.identity,
                        generatedTreesRoot);
                    ConfigureTreeRenderers(tree);
                    EnsureTreeCollider(tree);
                    AddTreeOcclusion(tree);
                    spawnedTrees++;
                }
                else
                {
                    Sprite sprite = validSprites[random.Next(validSprites.Count)];
                    CreateTreeFromSprite(sprite, position);
                    spawnedTrees++;
                }
            }

            if (positions.Count == 0)
            {
                Debug.LogWarning("TreeGenerator: Keine gültigen Grasfelder gefunden. Insel zuerst generieren oder Tree Density erhöhen.");
            }

            Debug.Log("Trees generated: " + spawnedTrees + " objects (" + positions.Count + " positions).", this);
        }

        private List<Sprite> GetValidTreeSprites()
        {
            List<Sprite> sprites = new List<Sprite>();
            if (treeSprite != null)
            {
                sprites.Add(treeSprite);
            }
            else if (treeSprites != null)
            {
                foreach (Sprite sprite in treeSprites)
                {
                    if (sprite != null) sprites.Add(sprite);
                }
            }

            return sprites;
        }

        private List<GameObject> GetValidTreePrefabs()
        {
            List<GameObject> prefabs = new List<GameObject>();
            if (treePrefabs != null)
            {
                foreach (GameObject prefab in treePrefabs)
                {
                    if (prefab != null) prefabs.Add(prefab);
                }
            }

            return prefabs;
        }

        private void CreateTreeFromSprite(Sprite sprite, Vector3 position)
        {
            if (sprite == null)
            {
                return;
            }

            GameObject tree = new GameObject("Generated Tree");
            tree.transform.SetParent(generatedTreesRoot, false);
            tree.transform.position = position;

            SpriteRenderer renderer = tree.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            ConfigureTreeRenderer(renderer);
            EnsureTreeCollider(tree);
            AddTreeOcclusion(tree);
        }

        private void ConfigureTreeRenderers(GameObject tree)
        {
            SpriteRenderer[] renderers = tree.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer renderer in renderers)
            {
                ConfigureTreeRenderer(renderer);
            }

            foreach (SortingGroup group in tree.GetComponentsInChildren<SortingGroup>(true))
            {
                if (!string.IsNullOrEmpty(renderSortingLayer))
                {
                    group.sortingLayerName = renderSortingLayer;
                }

                group.sortingOrder = treeSortingOrder;
            }
        }

        private void EnsureTreeCollider(GameObject tree)
        {
            if (!addTreeCollider || tree.GetComponentInChildren<Collider2D>(true) != null)
            {
                return;
            }

            BoxCollider2D collider = tree.AddComponent<BoxCollider2D>();
            collider.size = colliderSize;
            collider.offset = colliderOffset;
        }

        private void AddTreeOcclusion(GameObject tree)
        {
            TreeOcclusion occlusion = tree.GetComponent<TreeOcclusion>();
            if (!fadeWhenPlayerBehind)
            {
                if (occlusion != null) occlusion.enabled = false;
                return;
            }

            if (occlusion == null)
            {
                occlusion = tree.AddComponent<TreeOcclusion>();
            }

            occlusion.renderSortingLayer = renderSortingLayer;
            occlusion.treeSortingOrder = treeSortingOrder;
            occlusion.enabled = true;
        }

        private void ConfigureTreeRenderer(SpriteRenderer renderer)
        {
            if (!string.IsNullOrEmpty(renderSortingLayer))
            {
                renderer.sortingLayerName = renderSortingLayer;
            }

            renderer.sortingOrder = treeSortingOrder;
        }

        private void FindDetailsTilemap()
        {
            if (detailsTilemap != null || grassTilemap == null)
            {
                return;
            }

            Transform grid = grassTilemap.transform.parent;
            if (grid == null)
            {
                return;
            }

            Transform details = grid.Find("Details");
            if (details != null)
            {
                detailsTilemap = details.GetComponent<Tilemap>();
            }
        }

        private void EnsureGeneratedTreesRoot()
        {
            if (generatedTreesRoot != null)
            {
                return;
            }

            GameObject root = new GameObject("Generated Trees");
            Transform parent = grassTilemap != null && grassTilemap.transform.parent != null
                ? grassTilemap.transform.parent
                : null;
            root.transform.SetParent(parent, false);
            generatedTreesRoot = root.transform;
        }

        private void ClearGeneratedTrees()
        {
            Transform parent = grassTilemap != null && grassTilemap.transform.parent != null
                ? grassTilemap.transform.parent
                : transform;
            Transform root = generatedTreesRoot != null ? generatedTreesRoot : parent.Find("Generated Trees");
            if (root == null)
            {
                return;
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                GameObject tree = root.GetChild(i).gameObject;
                // Destroy runs at the end of the frame. Hide the old set immediately
                // to prevent overlapping renderers and colliders during regeneration.
                tree.SetActive(false);
                if (Application.isPlaying) Destroy(tree);
                else DestroyImmediate(tree);
            }

            generatedTreesRoot = root;
        }
    }
    #endregion

}
