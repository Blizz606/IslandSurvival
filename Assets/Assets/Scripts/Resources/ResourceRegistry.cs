using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fast spatial lookup for harvestable resources. Queries inspect only nearby grid buckets,
/// instead of scanning every tree, rock or plant in the world.
/// </summary>
public static class ResourceRegistry
{
    private const float CellSize = 4f;
    private static readonly Dictionary<Vector2Int, HashSet<HarvestableResource>> ResourcesByCell = new();
    private static readonly Dictionary<HarvestableResource, Vector2Int> CellByResource = new();

    public static void Register(HarvestableResource resource)
    {
        if (resource == null)
            return;

        Unregister(resource);
        Vector2Int cell = ToCell(resource.InteractionPosition);
        if (!ResourcesByCell.TryGetValue(cell, out HashSet<HarvestableResource> resources))
        {
            resources = new HashSet<HarvestableResource>();
            ResourcesByCell.Add(cell, resources);
        }

        resources.Add(resource);
        CellByResource[resource] = cell;
    }

    public static void Unregister(HarvestableResource resource)
    {
        if (resource == null || !CellByResource.TryGetValue(resource, out Vector2Int cell))
            return;

        if (ResourcesByCell.TryGetValue(cell, out HashSet<HarvestableResource> resources))
        {
            resources.Remove(resource);
            if (resources.Count == 0)
                ResourcesByCell.Remove(cell);
        }

        CellByResource.Remove(resource);
    }

    public static void RefreshPosition(HarvestableResource resource)
    {
        if (resource == null)
            return;

        Vector2Int newCell = ToCell(resource.InteractionPosition);
        if (CellByResource.TryGetValue(resource, out Vector2Int oldCell) && oldCell == newCell)
            return;

        Register(resource);
    }

    public static bool TryFindClosest(
        Vector2 origin,
        float range,
        ResourceToolType tool,
        out HarvestableResource closestResource
    )
    {
        closestResource = null;
        if (range <= 0f)
            return false;

        float closestDistanceSqr = range * range;
        Vector2Int minCell = ToCell(origin - Vector2.one * range);
        Vector2Int maxCell = ToCell(origin + Vector2.one * range);

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                if (!ResourcesByCell.TryGetValue(new Vector2Int(x, y), out HashSet<HarvestableResource> resources))
                    continue;

                foreach (HarvestableResource resource in resources)
                {
                    if (resource == null || !resource.isActiveAndEnabled || resource.IsDepleted)
                        continue;

                    ResourceDefinition definition = resource.Definition;
                    if (definition != null && !definition.CanHarvestWith(tool))
                        continue;

                    float distanceSqr = (resource.InteractionPosition - origin).sqrMagnitude;
                    if (distanceSqr > closestDistanceSqr)
                        continue;

                    closestDistanceSqr = distanceSqr;
                    closestResource = resource;
                }
            }
        }

        return closestResource != null;
    }

    public static bool TryHarvestClosest(Vector2 origin, float range, ResourceToolType tool, int power)
    {
        return TryFindClosest(origin, range, tool, out HarvestableResource resource)
            && resource.TryHarvest(tool, power);
    }

    private static Vector2Int ToCell(Vector2 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x / CellSize),
            Mathf.FloorToInt(position.y / CellSize)
        );
    }
}
