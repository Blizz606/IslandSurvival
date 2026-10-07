using TMPro;
using UnityEngine;

/// <summary>Displays one resource amount in the existing top HUD.</summary>
[DisallowMultipleComponent]
public sealed class ResourceHud : MonoBehaviour
{
    [SerializeField] private ResourceInventory inventory;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private string itemId = "wood";
    [Tooltip("Use 00 to keep the current two-digit HUD style.")]
    [SerializeField] private string numberFormat = "00";

    private void Start()
    {
        if (inventory == null)
            inventory = FindAnyObjectByType<ResourceInventory>();

        if (amountText == null)
            amountText = GetComponentInChildren<TextMeshProUGUI>(true);

        if (inventory != null)
            inventory.Changed += OnInventoryChanged;

        Refresh();
    }

    private void OnDestroy()
    {
        if (inventory != null)
            inventory.Changed -= OnInventoryChanged;
    }

    private void OnInventoryChanged(string changedItemId, int amount)
    {
        if (changedItemId == itemId)
            Refresh();
    }

    private void Refresh()
    {
        if (amountText == null)
            return;

        int amount = inventory == null ? 0 : inventory.GetAmount(itemId);
        amountText.text = string.IsNullOrEmpty(numberFormat)
            ? amount.ToString()
            : amount.ToString(numberFormat);
    }
}
