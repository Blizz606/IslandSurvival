using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

public class TreeGenerator : MonoBehaviour
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

    private IEnumerator Start()
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