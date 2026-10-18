using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TestTreeGen : MonoBehaviour
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

    private void Start()
    {
        GenerateTrees();
    }

    private void OnValidate()
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

/// <summary>Legacy dynamic-tree implementation kept only for old scene data.</summary>
internal sealed class LegacyHarvestableTree : MonoBehaviour
{
    private Transform trunk;
    private Transform crown;
    private Transform stump;
    private int remainingHits;

    public bool IsFelled { get; private set; }
    public Vector2 InteractionPosition => trunk != null ? trunk.position : transform.position;

    public void Configure(
        Sprite trunkSprite,
        Sprite crownSprite,
        Sprite stumpSprite,
        Vector2 trunkOffset,
        Vector2 stumpOffset,
        float treeScale,
        float stumpScaleMultiplier,
        int hitsToFell,
        bool stumpBlocksPlayer,
        string sortingLayer,
        int trunkSortingOrder,
        int crownSortingOrder,
        float crownOccludedAlpha,
        float crownFadeSpeed,
        float crownFadeDistance
    )
    {
        remainingHits = Mathf.Max(1, hitsToFell);
        trunk = CreateVisual("Trunk", trunkSprite, trunkOffset, treeScale, sortingLayer, trunkSortingOrder);
        crown = CreateVisual("Crown", crownSprite, Vector2.zero, treeScale, sortingLayer, crownSortingOrder);
        stump = CreateVisual(
            "Stump",
            stumpSprite != null ? stumpSprite : trunkSprite,
            Vector2.zero,
            treeScale * stumpScaleMultiplier,
            sortingLayer,
            trunkSortingOrder
        );

        AlignStumpToTrunkBase(stumpOffset);
        AddCollider(trunk, trunkSprite, true);
        AddCollider(stump, stumpSprite != null ? stumpSprite : trunkSprite, stumpBlocksPlayer);
        ConfigureCrownFade(crown, sortingLayer, crownSortingOrder, crownOccludedAlpha, crownFadeSpeed, crownFadeDistance);

        if (stump != null)
            stump.gameObject.SetActive(false);
    }

    public void ApplyDamage(int damage)
    {
        if (IsFelled)
            return;

        remainingHits -= Mathf.Max(1, damage);
        if (remainingHits <= 0)
            Fell();
    }

    public void SetPreview(bool enabled)
    {
        if (!IsFelled && stump != null)
            stump.gameObject.SetActive(enabled);
    }

    private void Fell()
    {
        IsFelled = true;
        if (trunk != null)
            trunk.gameObject.SetActive(false);
        if (crown != null)
            crown.gameObject.SetActive(false);
        if (stump != null)
            stump.gameObject.SetActive(true);
    }

    private Transform CreateVisual(
        string objectName,
        Sprite sprite,
        Vector2 localOffset,
        float scale,
        string sortingLayer,
        int sortingOrder
    )
    {
        if (sprite == null)
            return null;

        GameObject visual = new GameObject(objectName);
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = localOffset;
        visual.transform.localScale = Vector3.one * scale;

        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = sortingLayer;
        renderer.sortingOrder = sortingOrder;
        return visual.transform;
    }

    private void AlignStumpToTrunkBase(Vector2 offset)
    {
        if (trunk == null || stump == null)
            return;

        SpriteRenderer trunkRenderer = trunk.GetComponent<SpriteRenderer>();
        SpriteRenderer stumpRenderer = stump.GetComponent<SpriteRenderer>();
        if (trunkRenderer == null || stumpRenderer == null
            || trunkRenderer.sprite == null || stumpRenderer.sprite == null)
            return;

        // The sprite pivots and scales can differ. Aligning their lower bounds
        // makes every stump appear exactly at the base of its own visible trunk.
        float trunkBottom = trunk.localPosition.y
            + trunkRenderer.sprite.bounds.min.y * trunk.localScale.y;
        float stumpBottomOffset = stumpRenderer.sprite.bounds.min.y * stump.localScale.y;

        stump.localPosition = new Vector3(
            trunk.localPosition.x + offset.x,
            trunkBottom - stumpBottomOffset + offset.y,
            trunk.localPosition.z
        );
    }

    private static void AddCollider(Transform target, Sprite sprite, bool enabled)
    {
        if (target == null || sprite == null || !enabled)
            return;

        // A BoxCollider would discard the custom Physics Shape authored in
        // Unity's Sprite Editor. Copy every shape path onto a PolygonCollider
        // instead, so the visible tree and its collision area match exactly.
        PolygonCollider2D collider = target.gameObject.AddComponent<PolygonCollider2D>();
        int shapeCount = sprite.GetPhysicsShapeCount();

        if (shapeCount <= 0)
        {
            Vector2 extents = sprite.bounds.extents;
            collider.SetPath(0, new[]
            {
                new Vector2(-extents.x, -extents.y),
                new Vector2(-extents.x, extents.y),
                new Vector2(extents.x, extents.y),
                new Vector2(extents.x, -extents.y),
            });
            return;
        }

        collider.pathCount = shapeCount;
        List<Vector2> path = new List<Vector2>();
        for (int pathIndex = 0; pathIndex < shapeCount; pathIndex++)
        {
            path.Clear();
            sprite.GetPhysicsShape(pathIndex, path);
            collider.SetPath(pathIndex, path);
        }
    }

    private static void ConfigureCrownFade(
        Transform target,
        string sortingLayer,
        int sortingOrder,
        float occludedAlpha,
        float fadeSpeed,
        float fadeDistance
    )
    {
        if (target == null)
            return;

        TreeOcclusion occlusion = target.gameObject.AddComponent<TreeOcclusion>();
        occlusion.renderSortingLayer = sortingLayer;
        occlusion.treeSortingOrder = sortingOrder;
        occlusion.occludedAlpha = occludedAlpha;
        occlusion.fadeSpeed = fadeSpeed;
        occlusion.fadeDistance = fadeDistance;
        occlusion.logFadeChanges = false;
    }
}
