using UnityEngine;
using UnityEngine.Tilemaps;

public class BeachGenerator : MonoBehaviour
{
    [Header("Generator")]
    [Tooltip("Wenn deaktiviert, werden Sand, Übergang und Wasser nicht erzeugt.")]
    public bool generatorEnabled = true;

    [Header("Tilemaps")]
    public Tilemap grassTilemap;
    public Tilemap grassOverlayTilemap;
    public Tilemap sandTilemap;
    public Tilemap sandTransitionTilemap;
    public Tilemap waterTilemap;

    [Header("Sand")]
    public TileBase sandTile;

    [Header("Water")]
    public TileBase waterTile;

    [Range(1, 50)]
    public int waterBorder = 15;

    public TileBase sandVariant1;
    public TileBase sandVariant2;
    public TileBase sandVariant3;

    [Header("Sand Varianten")]
    [Range(0f, 1f)]
    public float variant1Chance = 0.02f;

    [Range(0f, 1f)]
    public float variant2Chance = 0.01f;

    [Range(0f, 1f)]
    public float variant3Chance = 0.005f;

    [Header("Sand-Wasser Übergang (1 Tile)")]
    [Tooltip("Wasserfeld direkt oberhalb des Sands.")]
    public TileBase edgeTop;
    [Tooltip("Wasserfeld direkt unterhalb des Sands.")]
    public TileBase edgeBottom;
    [Tooltip("Wasserfeld direkt links vom Sand.")]
    public TileBase edgeLeft;
    [Tooltip("Wasserfeld direkt rechts vom Sand.")]
    public TileBase edgeRight;
    [Tooltip("Wasserfeld an der äußeren oberen linken Ecke des Sands.")]
    public TileBase outerTopLeft;
    [Tooltip("Wasserfeld an der äußeren oberen rechten Ecke des Sands.")]
    public TileBase outerTopRight;
    [Tooltip("Wasserfeld an der äußeren unteren linken Ecke des Sands.")]
    public TileBase outerBottomLeft;
    [Tooltip("Wasserfeld an der äußeren unteren rechten Ecke des Sands.")]
    public TileBase outerBottomRight;
    [Tooltip("Wasserfeld in einer inneren oberen linken Sand-Einbuchtung.")]
    public TileBase innerTopLeft;
    [Tooltip("Wasserfeld in einer inneren oberen rechten Sand-Einbuchtung.")]
    public TileBase innerTopRight;
    [Tooltip("Wasserfeld in einer inneren unteren linken Sand-Einbuchtung.")]
    public TileBase innerBottomLeft;
    [Tooltip("Wasserfeld in einer inneren unteren rechten Sand-Einbuchtung.")]
    public TileBase innerBottomRight;

    [Header("Beach Settings")]
    [Range(1, 10)]
    public int beachWidth = 3;

    public void GenerateBeach()
    {
        if (!generatorEnabled)
        {
            Debug.Log("BeachGenerator: Generator ist im Inspector deaktiviert.", this);
            return;
        }

        if (
            grassTilemap == null ||
            grassOverlayTilemap == null ||
            sandTilemap == null
        )
        {
            Debug.LogError("BeachGenerator: Eine Tilemap fehlt!");
            return;
        }

        if (sandTile == null)
        {
            Debug.LogError("BeachGenerator: Sand Tile fehlt!");
            return;
        }

        EnsureTransitionTilemap();
        EnsureWaterTilemap();

        // Alten Strand und alten Wasserübergang löschen
        sandTilemap.ClearAllTiles();
        if (sandTransitionTilemap != null)
        {
            sandTransitionTilemap.ClearAllTiles();
        }

        // Bounds von Gras + Overlay
        BoundsInt grassBounds = grassTilemap.cellBounds;
        BoundsInt overlayBounds = grassOverlayTilemap.cellBounds;

        int minX = Mathf.Min(
            grassBounds.xMin,
            overlayBounds.xMin
        ) - beachWidth;

        int maxX = Mathf.Max(
            grassBounds.xMax,
            overlayBounds.xMax
        ) + beachWidth;

        int minY = Mathf.Min(
            grassBounds.yMin,
            overlayBounds.yMin
        ) - beachWidth;

        int maxY = Mathf.Max(
            grassBounds.yMax,
            overlayBounds.yMax
        ) + beachWidth;


        // =====================================
        // 1. SAND UNTER GRASS-OVERLAY
        // =====================================

        foreach (
            Vector3Int pos
            in overlayBounds.allPositionsWithin
        )
        {
            if (grassOverlayTilemap.HasTile(pos))
            {
                sandTilemap.SetTile(
                    pos,
                    GetSandTile()
                );
            }
        }


        // =====================================
        // 2. STRAND AUSSEN HERUM
        // =====================================

        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                Vector3Int pos =
                    new Vector3Int(x, y, 0);

                // Hier ist schon Insel
                if (IsIsland(pos))
                {
                    continue;
                }

                // Liegt dieses Feld nah genug
                // an der Insel?
                if (IsNearIsland(pos))
                {
                    sandTilemap.SetTile(
                        pos,
                        GetSandTile()
                    );
                }
            }
        }

        if (sandTransitionTilemap != null)
        {
            GenerateSandWaterTransition();
        }

        GenerateWater();

        Debug.Log("Beach generated!");
    }

    private void GenerateSandWaterTransition()
    {
        BoundsInt sourceBounds = sandTilemap.cellBounds;
        BoundsInt bounds = new BoundsInt(
            sourceBounds.xMin - 1,
            sourceBounds.yMin - 1,
            0,
            sourceBounds.size.x + 2,
            sourceBounds.size.y + 2,
            1);

        foreach (Vector3Int pos in bounds.allPositionsWithin)
        {
            if (sandTilemap.HasTile(pos) || IsIsland(pos))
            {
                continue;
            }

            bool up = HasSand(pos + Vector3Int.up);
            bool down = HasSand(pos + Vector3Int.down);
            bool left = HasSand(pos + Vector3Int.left);
            bool right = HasSand(pos + Vector3Int.right);
            bool upLeft = HasSand(pos + new Vector3Int(-1, 1, 0));
            bool upRight = HasSand(pos + new Vector3Int(1, 1, 0));
            bool downLeft = HasSand(pos + new Vector3Int(-1, -1, 0));
            bool downRight = HasSand(pos + new Vector3Int(1, -1, 0));

            TileBase selected = null;

            // Innere Ecken: das Wasserfeld liegt in einer Sand-Einbuchtung.
            if (down && right && downRight) selected = innerTopLeft;
            else if (down && left && downLeft) selected = innerTopRight;
            else if (up && right && upRight) selected = innerBottomLeft;
            else if (up && left && upLeft) selected = innerBottomRight;
            // Äußere Ecken: das Wasserfeld berührt Sand nur diagonal.
            else if (downLeft && !down && !left) selected = outerTopRight;
            else if (downRight && !down && !right) selected = outerTopLeft;
            else if (upLeft && !up && !left) selected = outerBottomRight;
            else if (upRight && !up && !right) selected = outerBottomLeft;
            // Gerade Kanten: Der Feldname beschreibt die Position im Wasser-Ring.
            else if (up) selected = edgeBottom;
            else if (down) selected = edgeTop;
            else if (left) selected = edgeRight;
            else if (right) selected = edgeLeft;

            if (selected != null)
            {
                sandTransitionTilemap.SetTile(pos, selected);
            }
        }
    }

    private bool HasSand(Vector3Int position)
    {
        return sandTilemap.HasTile(position);
    }

    private void GenerateWater()
    {
        if (waterTilemap == null || waterTile == null)
        {
            Debug.LogWarning("BeachGenerator: Water Tilemap oder Water Tile fehlt!");
            return;
        }

        BoundsInt sandBounds = sandTilemap.cellBounds;
        BoundsInt waterBounds = new BoundsInt(
            sandBounds.xMin - waterBorder,
            sandBounds.yMin - waterBorder,
            0,
            sandBounds.size.x + waterBorder * 2,
            sandBounds.size.y + waterBorder * 2,
            1);

        waterTilemap.ClearAllTiles();

        foreach (Vector3Int pos in waterBounds.allPositionsWithin)
        {
            if (sandTilemap.HasTile(pos) || IsIsland(pos))
            {
                continue;
            }

            waterTilemap.SetTile(pos, waterTile);
        }
    }

    private TileBase GetSandTile()
    {
        float value = Random.value;

        if (sandVariant3 != null && value < variant3Chance)
        {
            return sandVariant3;
        }

        if (sandVariant2 != null && value < variant3Chance + variant2Chance)
        {
            return sandVariant2;
        }

        if (sandVariant1 != null && value < variant3Chance + variant2Chance + variant1Chance)
        {
            return sandVariant1;
        }

        return sandTile;
    }

    private void EnsureTransitionTilemap()
    {
        if (sandTransitionTilemap != null)
        {
            return;
        }

        Transform parent = sandTilemap.transform.parent != null
            ? sandTilemap.transform.parent
            : transform;

        GameObject transitionObject = new GameObject("Sand Water Transition");
        transitionObject.transform.SetParent(parent, false);
        sandTransitionTilemap = transitionObject.AddComponent<Tilemap>();

        TilemapRenderer transitionRenderer = transitionObject.AddComponent<TilemapRenderer>();
        TilemapRenderer sandRenderer = sandTilemap.GetComponent<TilemapRenderer>();
        if (sandRenderer != null)
        {
            transitionRenderer.sortingLayerID = sandRenderer.sortingLayerID;
            transitionRenderer.sortingOrder = sandRenderer.sortingOrder - 1;
        }
    }

    private void EnsureWaterTilemap()
    {
        if (waterTilemap != null)
        {
            return;
        }

        Transform parent = sandTilemap.transform.parent != null
            ? sandTilemap.transform.parent
            : transform;

        GameObject waterObject = new GameObject("Water");
        waterObject.transform.SetParent(parent, false);
        waterTilemap = waterObject.AddComponent<Tilemap>();

        TilemapRenderer waterRenderer = waterObject.AddComponent<TilemapRenderer>();
        TilemapRenderer sandRenderer = sandTilemap.GetComponent<TilemapRenderer>();
        if (sandRenderer != null)
        {
            waterRenderer.sortingLayerID = sandRenderer.sortingLayerID;
            waterRenderer.sortingOrder = sandRenderer.sortingOrder - 2;
        }
    }


    // =========================================
    // IST DIE POSITION TEIL DER INSEL?
    // =========================================

    private bool IsIsland(Vector3Int pos)
    {
        return
            grassTilemap.HasTile(pos) ||
            grassOverlayTilemap.HasTile(pos);
    }


    // =========================================
    // IST INSEL IN DER NÄHE?
    // =========================================

    private bool IsNearIsland(Vector3Int position)
    {
        for (
            int offsetX = -beachWidth;
            offsetX <= beachWidth;
            offsetX++
        )
        {
            for (
                int offsetY = -beachWidth;
                offsetY <= beachWidth;
                offsetY++
            )
            {
                // Kreisförmige Distanz
                // statt quadratischem Strand
                float distance =
                    Mathf.Sqrt(
                        offsetX * offsetX +
                        offsetY * offsetY
                    );

                if (distance > beachWidth)
                {
                    continue;
                }

                Vector3Int checkPosition =
                    position +
                    new Vector3Int(
                        offsetX,
                        offsetY,
                        0
                    );

                if (IsIsland(checkPosition))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
