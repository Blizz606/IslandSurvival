using UnityEngine;
using UnityEngine.Tilemaps;

public class GrassIslandGenerator : MonoBehaviour
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

    private void Start()
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
