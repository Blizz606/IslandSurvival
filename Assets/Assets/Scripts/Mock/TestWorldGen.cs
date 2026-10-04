using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(-100)]
public class IslandGenerator : MonoBehaviour
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

    private void OnValidate()
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

    private void Start()
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

        if (treeGenerator != null)
            treeGenerator.ClearTreesNear(spawnPosition, spawnClearance);

        if (plantGenerator != null)
            plantGenerator.ClearPlantsNear(spawnPosition, spawnClearance);
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
