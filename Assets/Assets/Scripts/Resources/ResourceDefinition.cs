using System;
using System.Collections.Generic;
using UnityEngine;

public enum ResourceCategory
{
    Tree,
    Rock,
    Ore,
    Plant,
    Fish,
    Custom,
}

public enum ResourceToolType
{
    None,
    Hands,
    Axe,
    Pickaxe,
    FishingRod,
}

[Serializable]
public struct ResourceYield
{
    [Tooltip("Stabile ID für ein späteres Inventar- oder Loot-System, z. B. wood_log.")]
    [SerializeField] private string itemId;
    [SerializeField, Min(0)] private int minimumAmount;
    [SerializeField, Min(0)] private int maximumAmount;

    public string ItemId => itemId;
    public int MinimumAmount => minimumAmount;
    public int MaximumAmount => Mathf.Max(minimumAmount, maximumAmount);
}

/// <summary>Shared, data-driven configuration for any harvestable world resource.</summary>
[CreateAssetMenu(fileName = "ResourceDefinition", menuName = "Island Survival/Resources/Resource Definition")]
public sealed class ResourceDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string resourceId = "resource_id";
    [SerializeField] private string displayName = "Resource";
    [SerializeField] private ResourceCategory category = ResourceCategory.Custom;

    [Header("Harvesting")]
    [SerializeField] private ResourceToolType requiredTool = ResourceToolType.Hands;
    [SerializeField, Min(1)] private int hitsToDeplete = 3;
    [Tooltip("0 deaktiviert Respawn. Positive Werte sind Sekunden bis zum Wiedererscheinen.")]
    [SerializeField, Min(0f)] private float respawnSeconds;
    [SerializeField] private bool blocksPlayerAfterDepletion;

    [Header("Yield")]
    [SerializeField] private ResourceYield[] yields = Array.Empty<ResourceYield>();

    public string ResourceId => resourceId;
    public string DisplayName => displayName;
    public ResourceCategory Category => category;
    public int HitsToDeplete => hitsToDeplete;
    public float RespawnSeconds => respawnSeconds;
    public bool BlocksPlayerAfterDepletion => blocksPlayerAfterDepletion;
    public IReadOnlyList<ResourceYield> Yields => yields;

    public bool CanHarvestWith(ResourceToolType tool)
    {
        return requiredTool == ResourceToolType.None || requiredTool == tool;
    }
}
