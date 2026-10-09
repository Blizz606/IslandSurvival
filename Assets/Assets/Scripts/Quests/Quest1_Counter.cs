using TMPro;
using UnityEngine;

public sealed class QuestWoodCounter : MonoBehaviour
{
    [SerializeField] private ResourceInventory inventory;
    [SerializeField] private TextMeshProUGUI woodCountText;
    [SerializeField] private int targetWoodAmount = 3;

    private void Start()
    {
        if (inventory == null)
            inventory = FindAnyObjectByType<ResourceInventory>();

        if (inventory != null)
            inventory.Changed += UpdateWoodCount;

        Refresh();
    }

    private void OnDestroy()
    {
        if (inventory != null)
            inventory.Changed -= UpdateWoodCount;
    }

    private void UpdateWoodCount(string itemId, int amount)
    {
        if (itemId == "wood")
            Refresh();
    }

    private void Refresh()
    {
        int woodAmount = inventory == null ? 0 : inventory.GetAmount("wood");
        woodCountText.text = $"{woodAmount}/{targetWoodAmount}";
    }
}