using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class GrassEdgeOverlay : MonoBehaviour
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
