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

    [Header("Layered Tree (optional)")]
    [Tooltip("Nur der Stamm: liegt hinter dem Player und erhält die Collider.")]
    [SerializeField] private Tilemap treeTrunksTilemap;
    [Tooltip("Nur die Krone: liegt vor dem Player und braucht keinen Collider.")]
    [SerializeField] private Tilemap treeCrownsTilemap;
    [SerializeField] private Sprite treeTrunkSprite;
    [SerializeField] private Sprite treeCrownSprite;
    [Tooltip("Verschiebt nur den Stamm innerhalb seiner Grid-Zelle. Negative Y-Werte bewegen ihn nach unten.")]
    [SerializeField] private Vector2 treeTrunkOffset = Vector2.zero;

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

    [Header("Editor")]
    [SerializeField] private bool generateOnValidate = true;

    private const string GeneratedParentName = "Generated Trees";
    private const string GeneratedCrownsParentName = "Generated Crowns";
    private bool isGenerating;
    private Transform generatedCrownsRoot;

    private void Start()
    {
        GenerateTrees();
    }

    private void OnValidate()
    {
        if (!generateOnValidate || Application.isPlaying || isGenerating)
            return;

        if (!HasTreeAssets())
            return;

        GenerateTrees();
    }

    [ContextMenu("Generate Trees")]
    public void GenerateTrees()
    {
        FindLayeredTreeTilemaps();
        bool useLayeredTrees = HasLayeredTreeSprites();

        if (useLayeredTrees && (treeTrunksTilemap == null || treeCrownsTilemap == null))
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

        System.Random random = new System.Random(seed);
        bool useTreeTilemap = !useLayeredTrees && treesTilemap != null;
        Transform parent = null;
        if (useLayeredTrees)
        {
            treeTrunksTilemap.ClearAllTiles();
            treeCrownsTilemap.ClearAllTiles();
            ClearGeneratedCrowns();

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

        Transform crownsRoot = generatedCrownsRoot;
        if (crownsRoot == null && treeCrownsTilemap != null)
            crownsRoot = treeCrownsTilemap.transform.Find(GeneratedCrownsParentName);

        if (crownsRoot == null)
            return;

        float clearanceSqr = clearance * clearance;
        for (int i = crownsRoot.childCount - 1; i >= 0; i--)
        {
            Transform crown = crownsRoot.GetChild(i);
            if (((Vector2)(crown.position - worldPosition)).sqrMagnitude > clearanceSqr)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(crown.gameObject);
            else
#endif
                Destroy(crown.gameObject);
        }
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

    private static void AddSpriteWidth(Sprite sprite, ref float widestSprite)
    {
        if (sprite == null)
            return;

        widestSprite = Mathf.Max(widestSprite, sprite.bounds.size.magnitude);
    }

    private void ClearTrees(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(child);
            else
#endif
                Destroy(child);
        }
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

        SetTile(treeTrunksTilemap, worldPosition,
            CreateTreeTile(treeTrunkSprite, Tile.ColliderType.Sprite), treeTrunkOffset);
        CreateFadingCrown(worldPosition);
    }

    private void CreateFadingCrown(Vector3 worldPosition)
    {
        GameObject crown = new GameObject("Tree Crown");
        crown.transform.SetParent(GetOrCreateCrownsRoot(), false);
        crown.transform.position = worldPosition;
        crown.transform.localScale = Vector3.one * treeScale;

        SpriteRenderer crownRenderer = crown.AddComponent<SpriteRenderer>();
        crownRenderer.sprite = treeCrownSprite;

        TilemapRenderer crownTilemapRenderer = treeCrownsTilemap.GetComponent<TilemapRenderer>();
        if (crownTilemapRenderer != null)
        {
            crownRenderer.sortingLayerName = crownTilemapRenderer.sortingLayerName;
            crownRenderer.sortingOrder = crownTilemapRenderer.sortingOrder;
        }

        TreeOcclusion occlusion = crown.AddComponent<TreeOcclusion>();
        occlusion.renderSortingLayer = crownRenderer.sortingLayerName;
        occlusion.treeSortingOrder = crownRenderer.sortingOrder;
        occlusion.occludedAlpha = crownOccludedAlpha;
        occlusion.fadeSpeed = crownFadeSpeed;
        occlusion.fadeDistance = crownFadeDistance;
        occlusion.logFadeChanges = false;
    }

    private Transform GetOrCreateCrownsRoot()
    {
        if (generatedCrownsRoot != null)
            return generatedCrownsRoot;

        Transform existingRoot = treeCrownsTilemap.transform.Find(GeneratedCrownsParentName);
        if (existingRoot != null)
        {
            generatedCrownsRoot = existingRoot;
            return generatedCrownsRoot;
        }

        GameObject root = new GameObject(GeneratedCrownsParentName);
        root.transform.SetParent(treeCrownsTilemap.transform, false);
        generatedCrownsRoot = root.transform;
        return generatedCrownsRoot;
    }

    private void ClearGeneratedCrowns()
    {
        Transform root = generatedCrownsRoot;
        if (root == null && treeCrownsTilemap != null)
            root = treeCrownsTilemap.transform.Find(GeneratedCrownsParentName);

        if (root == null)
            return;

        ClearTrees(root);
        generatedCrownsRoot = root;
    }

    private void SetTile(
        Tilemap tilemap,
        Vector3 worldPosition,
        Tile tile,
        Vector2 localOffset
    )
    {
        Vector3Int cell = tilemap.WorldToCell(worldPosition);
        tilemap.SetTile(cell, tile);
        tilemap.SetTileFlags(cell, TileFlags.None);
        tilemap.SetTransformMatrix(
            cell,
            Matrix4x4.TRS(
                (Vector3)localOffset,
                Quaternion.identity,
                Vector3.one * treeScale
            )
        );
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
