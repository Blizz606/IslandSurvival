using UnityEngine;
using UnityEngine.UI;

/// <summary>Keeps one pause-menu category selected at a time.</summary>
public sealed class PauseCategoryTabs : MonoBehaviour
{
    [SerializeField] private PauseCategoryTabButton[] tabs;
    [SerializeField] private Color activeColor = new(0.82f, 0.5f, 0.14f, 1f);
    [SerializeField] private Color inactiveColor = new(0.12f, 0.2f, 0.24f, 1f);

    private void Awake()
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            int tabIndex = i;
            Button button = tabs[i] != null ? tabs[i].Button : null;
            if (button != null)
                button.onClick.AddListener(() => Select(tabIndex));
        }

        Select(0);
    }

    /// <summary>Called by the prefab builder to store the tab references.</summary>
    public void Configure(PauseCategoryTabButton[] buttons)
    {
        tabs = buttons;
        Select(0);
    }

    public void Select(int selectedIndex)
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] != null)
                tabs[i].SetSelected(i == selectedIndex, activeColor, inactiveColor);
        }
    }
}
