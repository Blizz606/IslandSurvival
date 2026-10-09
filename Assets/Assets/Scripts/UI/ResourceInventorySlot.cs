using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One visual slot in the bottom inventory bar.</summary>
[DisallowMultipleComponent]
public sealed class ResourceInventorySlot : MonoBehaviour
{
    [SerializeField] private string itemId;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI amountText;

    public string ItemId => itemId;

    public void Refresh(ResourceInventory inventory)
    {
        int amount = inventory == null ? 0 : inventory.GetAmount(itemId);

        if (amountText != null)
            amountText.text = amount > 0 ? amount.ToString() : string.Empty;

        if (itemIcon != null)
            itemIcon.enabled = itemIcon.sprite != null && amount > 0;
    }
}
