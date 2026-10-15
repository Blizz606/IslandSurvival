using TMPro;
using UnityEngine;
using System.Collections;

public sealed class Quest1_Counter : MonoBehaviour
{
    [Header("Inventory")]
    [SerializeField] private ResourceInventory inventory;

    [Header("Quest UI")]
    [SerializeField] private TextMeshProUGUI woodCountText;
    [SerializeField] private TextMeshProUGUI stoneCountText;

    [Header("Required resources")]
    [SerializeField, Min(1)] private int targetWoodAmount = 3;
    [SerializeField, Min(1)] private int targetStoneAmount = 2;
    
    [SerializeField] private RectTransform questCard;
    [SerializeField] private float exitDistance = 650f;
    [SerializeField] private float exitDuration = 0.45f;

    private bool questCompleted;

    private void Start()
    {
        if (inventory == null)
            inventory = FindAnyObjectByType<ResourceInventory>();

        if (inventory != null)
            inventory.Changed += OnInventoryChanged;
        
        if (questCard == null)
            questCard = GetComponent<RectTransform>();

        Refresh();
    }

    private void OnDestroy()
    {
        if (inventory != null)
            inventory.Changed -= OnInventoryChanged;
    }

    private void OnInventoryChanged(string itemId, int amount)
    {
        if (itemId == "wood" || itemId == "stone")
            Refresh();
    }

    private void Refresh()
    {
        int woodAmount = inventory == null ? 0 : inventory.GetAmount("wood");
        int stoneAmount = inventory == null ? 0 : inventory.GetAmount("stone");

        if (woodCountText != null)
            woodCountText.text = $"{woodAmount}/{targetWoodAmount}";

        if (stoneCountText != null)
            stoneCountText.text = $"{stoneAmount}/{targetStoneAmount}";
        
        if (!questCompleted &&
            woodAmount >= targetWoodAmount &&
            stoneAmount >= targetStoneAmount)
        {
            questCompleted = true;
            StartCoroutine(SlideQuestOut());
        }
    }
    
    private IEnumerator SlideQuestOut()
    {
        Vector2 startPosition = questCard.anchoredPosition;
        Vector2 endPosition = startPosition + Vector2.left * exitDistance;

        float elapsed = 0f;

        while (elapsed < exitDuration)
        {
            elapsed += Time.deltaTime;

            float time = elapsed / exitDuration;
            float easedTime = Mathf.SmoothStep(0f, 1f, time);

            questCard.anchoredPosition =
                Vector2.Lerp(startPosition, endPosition, easedTime);

            yield return null;
        }

        questCard.gameObject.SetActive(false);
    }
}
