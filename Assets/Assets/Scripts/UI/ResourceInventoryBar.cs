using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>Refreshes all bottom-bar slots whenever the player's resources change.</summary>
[DisallowMultipleComponent]
public sealed class ResourceInventoryBar : MonoBehaviour
{
    [SerializeField] private ResourceInventory inventory;
    [SerializeField] private ResourceInventorySlot[] slots;
    [Header("Selection")]
    [SerializeField] private RectTransform selectionArrow;
    [SerializeField, Min(0)] private int selectedIndex;

    public event Action<int> SelectionChanged;

    public int SelectedIndex => selectedIndex;
    public ResourceInventorySlot SelectedSlot =>
        slots != null && selectedIndex >= 0 && selectedIndex < slots.Length
            ? slots[selectedIndex]
            : null;

    private void Start()
    {
        if (inventory == null)
            inventory = FindAnyObjectByType<ResourceInventory>();

        if (slots == null || slots.Length == 0)
            slots = GetComponentsInChildren<ResourceInventorySlot>(true);

        if (inventory != null)
            inventory.Changed += OnInventoryChanged;

        Refresh();
        SelectSlot(selectedIndex, false);
    }

    private void Update()
    {
        if (slots == null || slots.Length == 0)
            return;

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll < 0f)
                SelectNext();
            else if (scroll > 0f)
                SelectPrevious();
        }

        SelectSlotFromNumberKey();
    }

    private void OnDestroy()
    {
        if (inventory != null)
            inventory.Changed -= OnInventoryChanged;
    }

    private void OnInventoryChanged(string _, int __)
    {
        Refresh();
    }

    private void Refresh()
    {
        foreach (ResourceInventorySlot slot in slots)
        {
            if (slot != null)
                slot.Refresh(inventory);
        }
    }

    public void SelectNext()
    {
        SelectSlot(selectedIndex + 1);
    }

    public void SelectPrevious()
    {
        SelectSlot(selectedIndex - 1);
    }

    public void SelectSlot(int index)
    {
        SelectSlot(index, true);
    }

    private void SelectSlot(int index, bool notify)
    {
        if (slots == null || slots.Length == 0)
            return;

        selectedIndex = (index % slots.Length + slots.Length) % slots.Length;
        ResourceInventorySlot selectedSlot = slots[selectedIndex];

        if (selectionArrow != null && selectedSlot != null)
        {
            selectionArrow.SetParent(selectedSlot.transform, false);
            selectionArrow.anchorMin = new Vector2(0.5f, 0f);
            selectionArrow.anchorMax = new Vector2(0.5f, 0f);
            selectionArrow.pivot = new Vector2(0.5f, 0f);
            selectionArrow.anchoredPosition = new Vector2(0f, -12f);
            selectionArrow.SetAsLastSibling();
        }

        if (notify)
            SelectionChanged?.Invoke(selectedIndex);
    }

    private void SelectSlotFromNumberKey()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        // 1-9 select the first nine slots; 0 is the familiar tenth slot.
        if (WasPressed(keyboard.digit1Key, keyboard.numpad1Key)) SelectSlot(0);
        else if (WasPressed(keyboard.digit2Key, keyboard.numpad2Key)) SelectSlot(1);
        else if (WasPressed(keyboard.digit3Key, keyboard.numpad3Key)) SelectSlot(2);
        else if (WasPressed(keyboard.digit4Key, keyboard.numpad4Key)) SelectSlot(3);
        else if (WasPressed(keyboard.digit5Key, keyboard.numpad5Key)) SelectSlot(4);
        else if (WasPressed(keyboard.digit6Key, keyboard.numpad6Key)) SelectSlot(5);
        else if (WasPressed(keyboard.digit7Key, keyboard.numpad7Key)) SelectSlot(6);
        else if (WasPressed(keyboard.digit8Key, keyboard.numpad8Key)) SelectSlot(7);
        else if (WasPressed(keyboard.digit9Key, keyboard.numpad9Key)) SelectSlot(8);
        else if (slots.Length >= 10 && WasPressed(keyboard.digit0Key, keyboard.numpad0Key)) SelectSlot(9);
    }

    private static bool WasPressed(KeyControl mainKey, KeyControl numpadKey)
    {
        return mainKey.wasPressedThisFrame || numpadKey.wasPressedThisFrame;
    }
}
