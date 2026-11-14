using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a health bar made from full, half and empty heart images.
/// One heart image represents two health points.
/// </summary>
[DisallowMultipleComponent]
public sealed class HeartBar : MonoBehaviour
{
    [Header("Heart Images")]
    [Tooltip("Assign the heart Image components from left to right.")]
    [SerializeField] private Image[] hearts;

    [SerializeField] private Sprite fullHeart;
    [SerializeField] private Sprite halfHeart;
    [SerializeField] private Sprite emptyHeart;

    [Header("Damage Feedback")]
    [Tooltip("Sprite shown briefly for half-heart damage.")]
    [SerializeField] private Sprite halfHeartDamageSprite;
    [Tooltip("Sprite shown briefly for full-heart damage.")]
    [SerializeField] private Sprite fullHeartDamageSprite;
    [Tooltip("One UI Image used to display the selected damage sprite.")]
    [SerializeField] private Image damageImage;
    [SerializeField, Min(1)] private int damageFlashCount = 3;
    [SerializeField, Min(0.01f)] private float damageFlashDuration = 0.1f;

    private Coroutine damageSpriteRoutine;

    [Header("Health")]
    [Tooltip("Health is measured in half-heart units. Six means three full hearts.")]
    [SerializeField, Min(1)] private int maxHealth = 6;
    [SerializeField, Min(0)] private int currentHealth = 6;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    private void Awake()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        if (damageImage != null)
            damageImage.enabled = false;
        UpdateHearts();
    }

    /// <summary>Removes health in half-heart units. TakeDamage(1) removes half a heart.</summary>
    public void TakeDamage(int amount = 1)
    {
        if (amount <= 0)
            return;

        int targetHeartIndex = GetDamageHeartIndex();
        currentHealth = Mathf.Max(0, currentHealth - amount);
        UpdateHearts();
        ShowDamageSprite(
            amount >= 2 ? fullHeartDamageSprite : halfHeartDamageSprite,
            targetHeartIndex
        );
    }

    /// <summary>Adds health in half-heart units.</summary>
    public void Heal(int amount = 1)
    {
        if (amount <= 0)
            return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateHearts();
    }

    /// <summary>Adds complete hearts. AddHearts(1) adds one full heart.</summary>
    public void AddHearts(int amount = 1)
    {
        if (amount <= 0)
            return;

        Heal(amount * 2);
    }

    /// <summary>Removes complete hearts. RemoveHearts(1) removes one full heart.</summary>
    public void RemoveHearts(int amount = 1)
    {
        if (amount <= 0)
            return;

        TakeDamage(amount * 2);
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        UpdateHearts();
    }

    private void UpdateHearts()
    {
        if (hearts == null)
            return;

        int remainingHealth = currentHealth;

        foreach (Image heart in hearts)
        {
            if (heart == null)
            {
                remainingHealth -= 2;
                continue;
            }

            heart.sprite = remainingHealth >= 2
                ? fullHeart
                : remainingHealth == 1
                    ? halfHeart
                    : emptyHeart;

            remainingHealth -= 2;
        }
    }

    private void ShowDamageSprite(Sprite damageSprite, int targetHeartIndex)
    {
        if (damageSprite == null || damageImage == null)
            return;

        if (damageSpriteRoutine != null)
            StopCoroutine(damageSpriteRoutine);

        damageSpriteRoutine = StartCoroutine(ShowDamageSpriteForDelay(damageSprite, targetHeartIndex));
    }

    private IEnumerator ShowDamageSpriteForDelay(Sprite damageSprite, int targetHeartIndex)
    {
        PositionDamageImage(targetHeartIndex);
        damageImage.sprite = damageSprite;

        int flashes = Mathf.Max(1, damageFlashCount);
        float duration = Mathf.Max(0.01f, damageFlashDuration);
        for (int flash = 0; flash < flashes; flash++)
        {
            damageImage.enabled = true;
            yield return new WaitForSecondsRealtime(duration);
            damageImage.enabled = false;

            if (flash < flashes - 1)
                yield return new WaitForSecondsRealtime(duration);
        }

        damageSpriteRoutine = null;
    }

    private int GetDamageHeartIndex()
    {
        if (hearts == null || hearts.Length == 0 || currentHealth <= 0)
            return 0;

        return Mathf.Clamp((currentHealth - 1) / 2, 0, hearts.Length - 1);
    }

    private void PositionDamageImage(int targetHeartIndex)
    {
        if (hearts == null || targetHeartIndex < 0 || targetHeartIndex >= hearts.Length)
            return;

        Image targetHeart = hearts[targetHeartIndex];
        if (targetHeart == null)
            return;

        RectTransform targetRect = targetHeart.rectTransform;
        RectTransform damageRect = damageImage.rectTransform;
        damageRect.anchorMin = targetRect.anchorMin;
        damageRect.anchorMax = targetRect.anchorMax;
        damageRect.pivot = targetRect.pivot;
        damageRect.anchoredPosition = targetRect.anchoredPosition;
        damageRect.sizeDelta = targetRect.sizeDelta;
        damageRect.localScale = targetRect.localScale;
        damageRect.SetAsLastSibling();
    }
}
