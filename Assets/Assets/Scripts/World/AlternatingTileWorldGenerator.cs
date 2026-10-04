using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// Erstellt eine separate Insel aus einem Tile mit Perlin Noise.
/// Der bestehende World-/Island-Generator wird von diesem Script nicht verwendet.
/// </summary>
public sealed class AlternatingTileWorldGenerator : MonoBehaviour
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

    private void Start()
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
