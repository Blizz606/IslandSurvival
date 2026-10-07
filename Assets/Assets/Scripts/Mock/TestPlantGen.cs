using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public class TestPlantGen : MonoBehaviour
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

    private void OnValidate()
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

    private bool HasPlantTiles()
    {
        if (plantTiles == null || plantTiles.Length == 0)
            return false;

        foreach (TileBase tile in plantTiles)
        {
            if (tile != null)
                return true;
        }

        return false;
    }

    private TileBase GetPlantTile(System.Random random)
    {
        int validCount = 0;
        foreach (TileBase tile in plantTiles)
        {
            if (tile != null)
                validCount++;
        }

        int selectedIndex = random.Next(validCount);
        foreach (TileBase tile in plantTiles)
        {
            if (tile == null)
                continue;

            if (selectedIndex-- == 0)
                return CreateAnimationVariant(tile, random);
        }

        return null;
    }

    private TileBase CreateAnimationVariant(TileBase sourceTile, System.Random random)
    {
        if (synchronizeAnimations || sourceTile is not AnimatedTile sourceAnimation)
            return sourceTile;

        AnimatedTile variant = ScriptableObject.CreateInstance<AnimatedTile>();
        variant.m_AnimatedSprites = sourceAnimation.m_AnimatedSprites;
        variant.m_MinSpeed = sourceAnimation.m_MinSpeed;
        variant.m_MaxSpeed = sourceAnimation.m_MaxSpeed;
        variant.m_TileColliderType = sourceAnimation.m_TileColliderType;
        variant.m_TileAnimationFlags = sourceAnimation.m_TileAnimationFlags;

        float speed = Mathf.Max(0.01f, sourceAnimation.m_MaxSpeed);
        float cycleDuration = sourceAnimation.m_AnimatedSprites.Length / speed;
        variant.m_AnimationStartTime = (float)random.NextDouble() * cycleDuration;
        variant.m_AnimationStartFrame = 0;

        generatedAnimatedTiles.Add(variant);
        return variant;
    }

    private void ClearGeneratedAnimatedTiles()
    {
        foreach (AnimatedTile tile in generatedAnimatedTiles)
        {
            if (tile == null)
                continue;

            if (Application.isPlaying)
                Destroy(tile);
            else
                DestroyImmediate(tile);
        }

        generatedAnimatedTiles.Clear();
    }

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
        if (groundTilemap != null)
            return;

        Tilemap[] tilemaps = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        foreach (Tilemap tilemap in tilemaps)
        {
            if (tilemap.name == "Grass")
            {
                groundTilemap = tilemap;
                return;
            }
        }
    }

    private void FindTreeTrunksTilemap()
    {
        if (treeTrunksTilemap != null)
            return;

        Tilemap[] tilemaps = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        foreach (Tilemap tilemap in tilemaps)
        {
            if (tilemap.name == "TreesTrunk")
            {
                treeTrunksTilemap = tilemap;
                return;
            }
        }
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
