using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TestWaterGen : MonoBehaviour
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

    private bool HasFishTiles()
    {
        if (fishTiles == null || fishTiles.Length == 0)
            return false;

        foreach (TileBase tile in fishTiles)
        {
            if (tile != null)
                return true;
        }

        return false;
    }

    private TileBase GetFishTile(System.Random random)
    {
        int validCount = 0;
        foreach (TileBase tile in fishTiles)
        {
            if (tile != null)
                validCount++;
        }

        int selectedIndex = random.Next(validCount);
        foreach (TileBase tile in fishTiles)
        {
            if (tile == null)
                continue;

            if (selectedIndex-- == 0)
                return CreateFishAnimationVariant(tile, random);
        }

        return null;
    }

    private TileBase CreateFishAnimationVariant(TileBase sourceTile, System.Random random)
    {
        if (synchronizeFishAnimations || sourceTile is not AnimatedTile sourceAnimation)
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

        generatedFishAnimatedTiles.Add(variant);
        return variant;
    }

    private void ClearGeneratedFishAnimatedTiles()
    {
        foreach (AnimatedTile tile in generatedFishAnimatedTiles)
        {
            if (tile == null)
                continue;

            if (Application.isPlaying)
                Destroy(tile);
            else
                DestroyImmediate(tile);
        }

        generatedFishAnimatedTiles.Clear();
    }

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
        if (beachTilemap != null)
            return;

        Tilemap[] tilemaps = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        foreach (Tilemap tilemap in tilemaps)
        {
            if (tilemap.name == "Beach")
            {
                beachTilemap = tilemap;
                return;
            }
        }
    }

    private void ApplySortingOrder()
    {
        TilemapRenderer renderer = waterTilemap.GetComponent<TilemapRenderer>();
        if (renderer != null)
            renderer.sortingOrder = waterSortingOrder;
    }
}
