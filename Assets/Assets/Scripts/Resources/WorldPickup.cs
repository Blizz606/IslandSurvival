using UnityEngine;

/// <summary>A visible item picked up when the player walks nearby.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public sealed class WorldPickup : MonoBehaviour
{
    private string itemId;
    private int amount;
    private PlayerMovement player;
    private ResourceInventory inventory;
    private float collectAfter;
    private Vector3 origin;
    private float phase;

    public void Configure(string id, int quantity, Sprite sprite, PlayerMovement target, ResourceInventory bag)
    {
        itemId = id;
        amount = quantity;
        player = target;
        inventory = bag;
        origin = transform.position;
        phase = Random.Range(0f, 6.28f);
        collectAfter = Time.time + 0.35f;
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = 10;
        transform.localScale = Vector3.one * 3f;
    }

    private void Update()
    {
        transform.position = origin + Vector3.up * (Mathf.Sin(Time.time * 2.5f + phase) * 0.035f);
        if (player == null || inventory == null || Time.time < collectAfter || GameOverScreen.IsGameOver) return;
        if (((Vector2)(player.transform.position - origin)).sqrMagnitude > 0.7f * 0.7f) return;
        if (!inventory.TryAdd(itemId, amount)) return;
        if (MiniGameController.Instance != null) MiniGameController.Instance.NotifyPickup(itemId, amount);
        Destroy(gameObject);
    }
}
