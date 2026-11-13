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
    [SerializeField, Min(0.01f)] private float damageSpriteDuration = 0.1f;

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
            damageImage.gameObject.SetActive(false);
        UpdateHearts();
    }

    /// <summary>Removes health in half-heart units. TakeDamage(1) removes half a heart.</summary>
    public void TakeDamage(int amount = 1)
    {
        if (amount <= 0)
            return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        UpdateHearts();
        ShowDamageSprite(amount >= 2 ? fullHeartDamageSprite : halfHeartDamageSprite);
    }

    /// <summary>Adds health in half-heart units.</summary>
    public void Heal(int amount = 1)
    {
        if (amount <= 0)
            return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateHearts();
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

    private void ShowDamageSprite(Sprite damageSprite)
    {
        if (damageSprite == null || damageImage == null)
            return;

        if (damageSpriteRoutine != null)
            StopCoroutine(damageSpriteRoutine);

        damageSpriteRoutine = StartCoroutine(ShowDamageSpriteForDelay(damageSprite));
    }

    private IEnumerator ShowDamageSpriteForDelay(Sprite damageSprite)
    {
        damageImage.sprite = damageSprite;
        damageImage.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(damageSpriteDuration);
        damageImage.gameObject.SetActive(false);
        damageSpriteRoutine = null;
    }
}
