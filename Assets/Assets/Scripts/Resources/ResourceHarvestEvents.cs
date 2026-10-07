using System;

/// <summary>Single extension point for inventory, loot, sound and quest systems.</summary>
public static class ResourceHarvestEvents
{
    public static event Action<HarvestableResource, int> Hit;
    public static event Action<HarvestableResource, ResourceDefinition> Depleted;
    public static event Action<HarvestableResource> Respawned;

    internal static void RaiseHit(HarvestableResource resource, int power)
    {
        Hit?.Invoke(resource, power);
    }

    internal static void RaiseDepleted(HarvestableResource resource)
    {
        Depleted?.Invoke(resource, resource.Definition);
    }

    internal static void RaiseRespawned(HarvestableResource resource)
    {
        Respawned?.Invoke(resource);
    }
}
