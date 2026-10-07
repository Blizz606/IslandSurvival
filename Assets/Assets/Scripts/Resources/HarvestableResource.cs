using UnityEngine;

/// <summary>Common runtime contract for every gatherable resource in the world.</summary>
public abstract class HarvestableResource : MonoBehaviour
{
    public abstract ResourceDefinition Definition { get; }
    public abstract Vector2 InteractionPosition { get; }
    public abstract bool IsDepleted { get; }

    public abstract bool TryHarvest(ResourceToolType tool, int power);

    protected virtual void OnEnable()
    {
        ResourceRegistry.Register(this);
    }

    protected virtual void OnDisable()
    {
        ResourceRegistry.Unregister(this);
    }

    /// <summary>Call only when a resource is moved after it has been spawned.</summary>
    public void RefreshRegistryPosition()
    {
        ResourceRegistry.RefreshPosition(this);
    }
}
