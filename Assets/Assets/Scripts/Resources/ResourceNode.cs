using UnityEngine;

/// <summary>
/// Generic prefab component for rocks, ore veins, plants and any future resource
/// that only needs an intact and depleted state.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResourceNode : HarvestableResource
{
    [SerializeField] private ResourceDefinition definition;
    [SerializeField] private Transform interactionPoint;
    [SerializeField] private GameObject intactVisual;
    [SerializeField] private GameObject depletedVisual;
    [SerializeField] private Collider2D[] intactColliders;
    [SerializeField] private Collider2D[] depletedColliders;
    [SerializeField] private bool startDepleted;

    private int remainingHits;
    private bool isDepleted;
    private float respawnAt = -1f;

    public override ResourceDefinition Definition => definition;
    public override Vector2 InteractionPosition => interactionPoint != null ? interactionPoint.position : transform.position;
    public override bool IsDepleted => isDepleted;

    private void Awake()
    {
        ResetResource(startDepleted);
    }

    private void Update()
    {
        if (!isDepleted || definition == null || definition.RespawnSeconds <= 0f || Time.time < respawnAt)
            return;

        ResetResource(false);
        ResourceHarvestEvents.RaiseRespawned(this);
    }

    public override bool TryHarvest(ResourceToolType tool, int power)
    {
        if (isDepleted || definition == null || !definition.CanHarvestWith(tool))
            return false;

        int appliedPower = Mathf.Max(1, power);
        remainingHits -= appliedPower;
        ResourceHarvestEvents.RaiseHit(this, appliedPower);
        if (remainingHits <= 0)
            Deplete();

        return true;
    }

    [ContextMenu("Reset Resource")]
    public void ResetResource()
    {
        ResetResource(false);
    }

    private void ResetResource(bool depleted)
    {
        isDepleted = depleted;
        remainingHits = definition != null ? definition.HitsToDeplete : 1;
        respawnAt = -1f;
        ApplyState();
    }

    private void Deplete()
    {
        isDepleted = true;
        if (definition.RespawnSeconds > 0f)
            respawnAt = Time.time + definition.RespawnSeconds;

        ApplyState();
        ResourceHarvestEvents.RaiseDepleted(this);
    }

    private void ApplyState()
    {
        if (intactVisual != null)
            intactVisual.SetActive(!isDepleted);
        if (depletedVisual != null)
            depletedVisual.SetActive(isDepleted);

        SetCollidersEnabled(intactColliders, !isDepleted);
        SetCollidersEnabled(depletedColliders, isDepleted && definition != null && definition.BlocksPlayerAfterDepletion);
    }

    private static void SetCollidersEnabled(Collider2D[] colliders, bool enabled)
    {
        if (colliders == null)
            return;

        foreach (Collider2D collider in colliders)
        {
            if (collider != null)
                collider.enabled = enabled;
        }
    }
}
