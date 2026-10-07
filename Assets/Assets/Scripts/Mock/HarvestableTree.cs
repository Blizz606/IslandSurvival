using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns the trunk, crown and stump parts of one tree prefab instance.</summary>
[DisallowMultipleComponent]
public sealed class HarvestableTree : HarvestableResource
{
    [Header("Resource")]
    [SerializeField] private ResourceDefinition resourceDefinition;
    [SerializeField] private Transform trunk;
    [SerializeField] private Transform crown;
    [SerializeField] private Transform stump;

    private int remainingHits;
    private float respawnAt = -1f;

    public bool IsFelled { get; private set; }
    public override ResourceDefinition Definition => resourceDefinition;
    public override Vector2 InteractionPosition => trunk != null ? trunk.position : transform.position;
    public override bool IsDepleted => IsFelled;

    private void Update()
    {
        if (!IsFelled || resourceDefinition == null || resourceDefinition.RespawnSeconds <= 0f
            || Time.time < respawnAt)
            return;

        Restore();
        ResourceHarvestEvents.RaiseRespawned(this);
    }

    /// <summary>Keeps all visual settings from the prefab and only configures gameplay state.</summary>
    public void ConfigureFromPrefab(int hitsToFell, bool stumpBlocksPlayer)
    {
        ResolveParts();
        remainingHits = resourceDefinition != null
            ? resourceDefinition.HitsToDeplete
            : Mathf.Max(1, hitsToFell);
        IsFelled = false;
        respawnAt = -1f;

        if (trunk != null)
            trunk.gameObject.SetActive(true);
        if (crown != null)
            crown.gameObject.SetActive(true);
        if (stump != null)
            stump.gameObject.SetActive(false);

        EnsurePolygonCollider(trunk, true);
        EnsurePolygonCollider(
            stump,
            resourceDefinition != null ? resourceDefinition.BlocksPlayerAfterDepletion : stumpBlocksPlayer
        );
        RefreshRegistryPosition();
    }

    public void Configure(
        Sprite trunkSprite,
        Sprite crownSprite,
        Sprite stumpSprite,
        Vector2 trunkOffset,
        Vector2 stumpOffset,
        float treeScale,
        float stumpScaleMultiplier,
        int hitsToFell,
        bool stumpBlocksPlayer,
        string sortingLayer,
        int trunkSortingOrder,
        int crownSortingOrder,
        float crownOccludedAlpha,
        float crownFadeSpeed,
        float crownFadeDistance
    )
    {
        remainingHits = resourceDefinition != null
            ? resourceDefinition.HitsToDeplete
            : Mathf.Max(1, hitsToFell);
        trunk = CreateVisual("Trunk", trunkSprite, trunkOffset, treeScale, sortingLayer, trunkSortingOrder);
        crown = CreateVisual("Crown", crownSprite, Vector2.zero, treeScale, sortingLayer, crownSortingOrder);
        stump = CreateVisual(
            "Stump",
            stumpSprite != null ? stumpSprite : trunkSprite,
            Vector2.zero,
            treeScale * stumpScaleMultiplier,
            sortingLayer,
            trunkSortingOrder
        );

        AlignStumpToTrunkBase(stumpOffset);
        EnsurePolygonCollider(trunk, true);
        EnsurePolygonCollider(
            stump,
            resourceDefinition != null ? resourceDefinition.BlocksPlayerAfterDepletion : stumpBlocksPlayer
        );
        ConfigureCrownFade(crown, sortingLayer, crownSortingOrder, crownOccludedAlpha, crownFadeSpeed, crownFadeDistance);

        if (stump != null)
            stump.gameObject.SetActive(false);

        RefreshRegistryPosition();
    }

    public bool ApplyDamage(int damage)
    {
        if (IsFelled)
            return false;

        int appliedDamage = Mathf.Max(1, damage);
        remainingHits -= appliedDamage;
        ResourceHarvestEvents.RaiseHit(this, appliedDamage);
        if (remainingHits <= 0)
            Fell();

        return true;
    }

    public override bool TryHarvest(ResourceToolType tool, int power)
    {
        if (resourceDefinition != null && !resourceDefinition.CanHarvestWith(tool))
            return false;

        return ApplyDamage(power);
    }

    public void SetPreview(bool enabled)
    {
        ResolveParts();
        if (!IsFelled && stump != null)
            stump.gameObject.SetActive(enabled);
    }

    private void Fell()
    {
        IsFelled = true;
        if (resourceDefinition != null && resourceDefinition.RespawnSeconds > 0f)
            respawnAt = Time.time + resourceDefinition.RespawnSeconds;

        if (trunk != null)
            trunk.gameObject.SetActive(false);
        if (crown != null)
            crown.gameObject.SetActive(false);
        if (stump != null)
            stump.gameObject.SetActive(true);

        ResourceHarvestEvents.RaiseDepleted(this);
    }

    private void Restore()
    {
        IsFelled = false;
        remainingHits = resourceDefinition.HitsToDeplete;
        respawnAt = -1f;
        if (trunk != null)
            trunk.gameObject.SetActive(true);
        if (crown != null)
            crown.gameObject.SetActive(true);
        if (stump != null)
            stump.gameObject.SetActive(false);
    }

    private void ResolveParts()
    {
        if (trunk == null)
            trunk = transform.Find("Trunk");
        if (crown == null)
            crown = transform.Find("Crown");
        if (stump == null)
            stump = transform.Find("Stump");
    }

    private Transform CreateVisual(
        string objectName,
        Sprite sprite,
        Vector2 localOffset,
        float scale,
        string sortingLayer,
        int sortingOrder
    )
    {
        if (sprite == null)
            return null;

        GameObject visual = new GameObject(objectName);
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = localOffset;
        visual.transform.localScale = Vector3.one * scale;

        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = sortingLayer;
        renderer.sortingOrder = sortingOrder;
        return visual.transform;
    }

    private void AlignStumpToTrunkBase(Vector2 offset)
    {
        if (trunk == null || stump == null)
            return;

        SpriteRenderer trunkRenderer = trunk.GetComponent<SpriteRenderer>();
        SpriteRenderer stumpRenderer = stump.GetComponent<SpriteRenderer>();
        if (trunkRenderer == null || stumpRenderer == null
            || trunkRenderer.sprite == null || stumpRenderer.sprite == null)
            return;

        float trunkBottom = trunk.localPosition.y
            + trunkRenderer.sprite.bounds.min.y * trunk.localScale.y;
        float stumpBottomOffset = stumpRenderer.sprite.bounds.min.y * stump.localScale.y;
        stump.localPosition = new Vector3(
            trunk.localPosition.x + offset.x,
            trunkBottom - stumpBottomOffset + offset.y,
            trunk.localPosition.z
        );
    }

    private static void EnsurePolygonCollider(Transform target, bool shouldBlockPlayer)
    {
        if (target == null)
            return;

        PolygonCollider2D collider = target.GetComponent<PolygonCollider2D>();
        if (!shouldBlockPlayer)
        {
            if (collider != null)
                collider.enabled = false;
            return;
        }

        if (collider != null)
        {
            collider.enabled = true;
            return;
        }

        SpriteRenderer renderer = target.GetComponent<SpriteRenderer>();
        if (renderer == null || renderer.sprite == null)
            return;

        collider = target.gameObject.AddComponent<PolygonCollider2D>();
        Sprite sprite = renderer.sprite;
        int shapeCount = sprite.GetPhysicsShapeCount();
        if (shapeCount <= 0)
        {
            Vector2 extents = sprite.bounds.extents;
            collider.SetPath(0, new[]
            {
                new Vector2(-extents.x, -extents.y),
                new Vector2(-extents.x, extents.y),
                new Vector2(extents.x, extents.y),
                new Vector2(extents.x, -extents.y),
            });
            return;
        }

        collider.pathCount = shapeCount;
        List<Vector2> path = new List<Vector2>();
        for (int pathIndex = 0; pathIndex < shapeCount; pathIndex++)
        {
            path.Clear();
            sprite.GetPhysicsShape(pathIndex, path);
            collider.SetPath(pathIndex, path);
        }
    }

    private static void ConfigureCrownFade(
        Transform target,
        string sortingLayer,
        int sortingOrder,
        float occludedAlpha,
        float fadeSpeed,
        float fadeDistance
    )
    {
        if (target == null)
            return;

        TreeOcclusion occlusion = target.GetComponent<TreeOcclusion>();
        if (occlusion == null)
            occlusion = target.gameObject.AddComponent<TreeOcclusion>();

        occlusion.renderSortingLayer = sortingLayer;
        occlusion.treeSortingOrder = sortingOrder;
        occlusion.occludedAlpha = occludedAlpha;
        occlusion.fadeSpeed = fadeSpeed;
        occlusion.fadeDistance = fadeDistance;
        occlusion.logFadeChanges = false;
    }
}
