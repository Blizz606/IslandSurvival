using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TestBeachGen : MonoBehaviour
{
    [Header("Insel")]
    [SerializeField] private Tilemap groundTilemap;

    [Header("Beach Tilemap")]
    [SerializeField] private Tilemap beachTilemap;
    [SerializeField] private TileBase beachTile;
    [SerializeField, Min(1)] private int beachWidth = 1;
    [SerializeField] private int beachSortingOrder = -10;

    [Header("Äußerer Beach-Ring")]
    [SerializeField] private TileBase edgeTile;
    [SerializeField] private TileBase outerCornerTile;

    [Header("Ecke zwischen zwei Beach-Kanten")]
    [SerializeField] private TileBase innerCornerTile;

    private bool isGenerating;

    private void OnValidate()
    {
        if (Application.isPlaying || isGenerating)
            return;

        if (groundTilemap != null && beachTilemap != null && beachTile != null)
            GenerateBeach();
    }

    [ContextMenu("Generate Beach")]
    public void GenerateBeach()
    {
        if (groundTilemap == null || beachTilemap == null || beachTile == null)
        {
            Debug.LogWarning("TestBeachGen: Ground Tilemap, Beach Tilemap oder Beach Tile fehlt.", this);
            return;
        }

        isGenerating = true;
        ApplySortingOrder();
        beachTilemap.ClearAllTiles();

        HashSet<Vector3Int> beachCells = new HashSet<Vector3Int>();
        HashSet<Vector3Int> underlayCells = new HashSet<Vector3Int>();
        BoundsInt bounds = groundTilemap.cellBounds;

        foreach (Vector3Int landCell in bounds.allPositionsWithin)
        {
            if (!groundTilemap.HasTile(landCell))
                continue;

            for (int x = -beachWidth; x <= beachWidth; x++)
            {
                for (int y = -beachWidth; y <= beachWidth; y++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) > beachWidth)
                        continue;

                    Vector3Int groundCell = landCell + new Vector3Int(x, y, 0);

                    Vector3 worldPosition = groundTilemap.GetCellCenterWorld(groundCell);
                    Vector3Int beachCell = beachTilemap.WorldToCell(worldPosition);

                    if (groundTilemap.HasTile(groundCell))
                        underlayCells.Add(beachCell);
                    else
                        beachCells.Add(beachCell);
                }
            }
        }

        // Eine Zelle unter Gras ist kein sichtbarer Strand, auch wenn mehrere
        // Ground-Zellen auf dieselbe Beach-Zelle abgebildet werden.
        beachCells.ExceptWith(underlayCells);
        FillNarrowBeachGaps(beachCells);

        foreach (Vector3Int cell in underlayCells)
            beachTilemap.SetTile(cell, beachTile);

        foreach (Vector3Int cell in beachCells)
            beachTilemap.SetTile(cell, beachTile);

        GenerateOuterEdge(beachCells);

        isGenerating = false;
        Debug.Log($"TestBeachGen: {beachCells.Count} Beach-Tiles erzeugt.", this);
    }

    private void GenerateOuterEdge(HashSet<Vector3Int> beachCells)
    {
        HashSet<Vector3Int> candidates = new HashSet<Vector3Int>();
        HashSet<Vector3Int> edgeCells = new HashSet<Vector3Int>();
        HashSet<Vector3Int> outerCornerCells = new HashSet<Vector3Int>();

        foreach (Vector3Int beachCell in beachCells)
        {
            AddCandidate(beachCell + Vector3Int.up, beachCells, candidates);
            AddCandidate(beachCell + Vector3Int.right, beachCells, candidates);
            AddCandidate(beachCell + Vector3Int.down, beachCells, candidates);
            AddCandidate(beachCell + Vector3Int.left, beachCells, candidates);
        }

        foreach (Vector3Int candidate in candidates)
        {
            List<Vector3Int> directions = GetBeachDirections(candidate, beachCells);
            if (directions.Count == 0)
                continue;

            if (IsGroundAtBeachCell(candidate))
                continue;

            if (directions.Count == 2
                && TryGetCornerDirection(candidate, beachCells, out Vector3Int cornerDirection)
                && outerCornerTile != null)
            {
                float rotation = GetCornerRotation(cornerDirection);
                SetTileTransform(candidate, outerCornerTile, rotation);
                outerCornerCells.Add(candidate);
            }
            else if (directions.Count == 1 && edgeTile != null)
            {
                SetTileTransform(candidate, edgeTile, GetEdgeRotation(directions[0]));
                edgeCells.Add(candidate);
            }
        }

        GenerateInnerCorners(beachCells, edgeCells, outerCornerCells);
    }

    // Füllt ein Tile breite Wasser-Schlitze im Strandring. Ohne diesen Schritt
    // kann ein Outer-Corner-Tile eine ungewollte blaue Wasser-Nase in den Strand
    // zeichnen, obwohl der Strand links/rechts bzw. oben/unten durchgehend ist.
    private void FillNarrowBeachGaps(HashSet<Vector3Int> beachCells)
    {
        if (beachCells.Count == 0)
            return;

        for (int pass = 0; pass < 3; pass++)
        {
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;

            foreach (Vector3Int cell in beachCells)
            {
                minX = Mathf.Min(minX, cell.x);
                maxX = Mathf.Max(maxX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxY = Mathf.Max(maxY, cell.y);
            }

            List<Vector3Int> toFill = new List<Vector3Int>();
            for (int x = minX + 1; x < maxX; x++)
            {
                for (int y = minY + 1; y < maxY; y++)
                {
                    Vector3Int cell = new Vector3Int(x, y, 0);
                    if (beachCells.Contains(cell) || IsGroundAtBeachCell(cell))
                        continue;

                    bool enclosedHorizontally = beachCells.Contains(cell + Vector3Int.left)
                        && beachCells.Contains(cell + Vector3Int.right);
                    bool enclosedVertically = beachCells.Contains(cell + Vector3Int.up)
                        && beachCells.Contains(cell + Vector3Int.down);

                    if (enclosedHorizontally || enclosedVertically)
                        toFill.Add(cell);
                }
            }

            if (toFill.Count == 0)
                break;

            foreach (Vector3Int cell in toFill)
                beachCells.Add(cell);
        }
    }

    private void GenerateInnerCorners(
        HashSet<Vector3Int> beachCells,
        HashSet<Vector3Int> edgeCells,
        HashSet<Vector3Int> outerCornerCells
    )
    {
        if (innerCornerTile == null)
            return;

        // Diese Ecke füllt eine freie diagonale Lücke am äußeren Ring.
        // Die seitlichen Übergangszellen können bei gestuften Küsten auch
        // als Outer Corner statt als gerades Edge-Tile klassifiziert sein.
        Vector3Int[] diagonals =
        {
            new Vector3Int(-1, -1, 0),
            new Vector3Int(1, -1, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(-1, 1, 0)
        };

        Dictionary<Vector3Int, float> rotations = new Dictionary<Vector3Int, float>();
        HashSet<Vector3Int> ambiguousCells = new HashSet<Vector3Int>();

        foreach (Vector3Int beachCell in beachCells)
        {
            foreach (Vector3Int diagonal in diagonals)
            {
                Vector3Int cornerCell = beachCell + diagonal;
                Vector3Int sideA = beachCell + new Vector3Int(diagonal.x, 0, 0);
                Vector3Int sideB = beachCell + new Vector3Int(0, diagonal.y, 0);

                if (beachCells.Contains(cornerCell)
                    || edgeCells.Contains(cornerCell)
                    || outerCornerCells.Contains(cornerCell)
                    || IsGroundAtBeachCell(cornerCell)
                    || IsGroundAtBeachCell(sideA)
                    || IsGroundAtBeachCell(sideB)
                    || GetBeachDirections(cornerCell, beachCells).Count != 0)
                    continue;

                Vector3Int inwardDirection = new Vector3Int(-diagonal.x, -diagonal.y, 0);
                float rotation = GetInnerCornerRotation(inwardDirection) + 180f;
                if (rotations.TryGetValue(cornerCell, out float previousRotation)
                    && previousRotation != rotation)
                    ambiguousCells.Add(cornerCell);
                else
                    rotations[cornerCell] = rotation;
            }
        }

        foreach (KeyValuePair<Vector3Int, float> corner in rotations)
        {
            if (!ambiguousCells.Contains(corner.Key))
                SetTileTransform(corner.Key, innerCornerTile, corner.Value);
        }
    }

    private float GetInnerCornerRotation(Vector3Int inwardDirection)
    {
        if (inwardDirection == new Vector3Int(1, 1, 0)) return 90f;
        if (inwardDirection == new Vector3Int(-1, 1, 0)) return 180f;
        if (inwardDirection == new Vector3Int(-1, -1, 0)) return -90f;
        return 0f;
    }

    private void AddCandidate(
        Vector3Int cell,
        HashSet<Vector3Int> beachCells,
        HashSet<Vector3Int> candidates
    )
    {
        if (beachCells.Contains(cell) || candidates.Contains(cell))
            return;

        candidates.Add(cell);
    }

    private List<Vector3Int> GetBeachDirections(
        Vector3Int candidate,
        HashSet<Vector3Int> beachCells
    )
    {
        List<Vector3Int> directions = new List<Vector3Int>();

        Vector3Int[] cardinalDirections =
        {
            Vector3Int.up,
            Vector3Int.right,
            Vector3Int.down,
            Vector3Int.left
        };

        foreach (Vector3Int direction in cardinalDirections)
        {
            if (beachCells.Contains(candidate + direction))
                directions.Add(direction);
        }

        return directions;
    }

    private bool TryGetCornerDirection(
        Vector3Int candidate,
        HashSet<Vector3Int> beachCells,
        out Vector3Int cornerDirection
    )
    {
        Vector3Int[] corners =
        {
            new Vector3Int(-1, -1, 0),
            new Vector3Int(-1, 1, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(1, -1, 0)
        };

        foreach (Vector3Int corner in corners)
        {
            Vector3Int sideA = candidate + new Vector3Int(corner.x, 0, 0);
            Vector3Int sideB = candidate + new Vector3Int(0, corner.y, 0);
            if (beachCells.Contains(sideA)
                && beachCells.Contains(sideB))
            {
                cornerDirection = corner;
                return true;
            }
        }

        cornerDirection = Vector3Int.zero;
        return false;
    }

    private float GetEdgeRotation(Vector3Int direction)
    {
        if (direction == Vector3Int.down) return 90f;
        if (direction == Vector3Int.left) return 0f;
        if (direction == Vector3Int.up) return -90f;
        return 180f;
    }

    private float GetCornerRotation(Vector3Int direction)
    {
        if (direction == new Vector3Int(-1, -1, 0)) return -90f;
        if (direction == new Vector3Int(-1, 1, 0)) return 180f;
        if (direction == new Vector3Int(1, 1, 0)) return 90f;
        if (direction == new Vector3Int(1, -1, 0)) return 0f;

        Debug.LogError($"TestBeachGen: Unbekannte Corner-Richtung {direction}.", this);
        return 0f;
    }

    private bool IsGroundAtBeachCell(Vector3Int beachCell)
    {
        Vector3 worldPosition = beachTilemap.GetCellCenterWorld(beachCell);
        Vector3Int groundCell = groundTilemap.WorldToCell(worldPosition);
        return groundTilemap.HasTile(groundCell);
    }

    private void SetTileTransform(Vector3Int cell, TileBase tile, float rotation)
    {
        beachTilemap.SetTile(cell, tile);
        beachTilemap.SetTileFlags(cell, TileFlags.None);
        beachTilemap.SetTransformMatrix(
            cell,
            Matrix4x4.TRS(
                Vector3.zero,
                Quaternion.Euler(0f, 0f, rotation),
                Vector3.one
            )
        );
    }

    private void ApplySortingOrder()
    {
        TilemapRenderer renderer = beachTilemap.GetComponent<TilemapRenderer>();
        if (renderer != null)
            renderer.sortingOrder = beachSortingOrder;
    }
}
