using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Assign the two sprites for this specific category directly in its Inspector.
/// </summary>
[RequireComponent(typeof(Button), typeof(Image))]
public sealed class PauseCategoryTabButton : MonoBehaviour
{
    [Header("Sprites für diesen Button")]
    [SerializeField] private Sprite selectedSprite;
    [SerializeField] private Sprite unselectedSprite;
    [SerializeField] private Image targetImage;

    public Button Button
    {
        get
        {
            if (targetImage == null)
                targetImage = GetComponent<Image>();

            return GetComponent<Button>();
        }
    }

    private void Reset()
    {
        targetImage = GetComponent<Image>();
    }

    private void OnValidate()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
    }

    public void SetSelected(bool selected, Color activeColor, Color inactiveColor)
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        Sprite sprite = selected ? selectedSprite : unselectedSprite;
        if (sprite != null)
        {
            targetImage.sprite = sprite;
            targetImage.preserveAspect = true;
            targetImage.color = Color.white;
        }
        else
        {
            targetImage.color = selected ? activeColor : inactiveColor;
        }
    }
}
