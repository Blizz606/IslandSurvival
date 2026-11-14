using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct ResourceInventoryEntry
{
    [SerializeField] private string itemId;
    [SerializeField, Min(0)] private int amount;

    public string ItemId => itemId;
    public int Amount => amount;

    public ResourceInventoryEntry(string itemId, int amount)
    {
        this.itemId = itemId;
        this.amount = amount;
    }
}

[DisallowMultipleComponent]
public sealed class ResourceInventory : MonoBehaviour
{
    [SerializeField] private List<ResourceInventoryEntry> entries = new();

    [SerializeField, Min(1)] private int maxStackSize = 99;

    public event Action<string, int> Changed;

    public IReadOnlyList<ResourceInventoryEntry> Entries => entries;

    public int GetAmount(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return 0;

        foreach (ResourceInventoryEntry entry in entries)
        {
            if (entry.ItemId == itemId)
                return entry.Amount;
        }

        return 0;
    }

    public void AddYields(ResourceDefinition definition)
    {
        if (definition == null)
            return;

        foreach (ResourceYield yield in definition.Yields)
        {
            if (string.IsNullOrWhiteSpace(yield.ItemId))
                continue;

            int amount = UnityEngine.Random.Range(
                yield.MinimumAmount,
                yield.MaximumAmount + 1
            );

            Add(yield.ItemId, amount);
        }
    }

    public void Add(string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
            return;

        for (int index = 0; index < entries.Count; index++)
        {
            ResourceInventoryEntry entry = entries[index];

            if (entry.ItemId != itemId)
                continue;

            int newAmount = Mathf.Min(entry.Amount + amount, maxStackSize);

            entries[index] = new ResourceInventoryEntry(itemId, newAmount);
            Changed?.Invoke(itemId, newAmount);
            return;
        }

        int firstAmount = Mathf.Min(amount, maxStackSize);

        entries.Add(new ResourceInventoryEntry(itemId, firstAmount));
        Changed?.Invoke(itemId, firstAmount);
    }

    /// <summary>Adds the entire amount only if it fits in the existing stack.</summary>
    public bool TryAdd(string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0 || amount > maxStackSize - GetAmount(itemId))
            return false;
        Add(itemId, amount);
        return true;
    }

    /// <summary>Atomically consumes items when enough are available.</summary>
    public bool TryRemove(string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0) return false;
        for (int i = 0; i < entries.Count; i++)
        {
            ResourceInventoryEntry entry = entries[i];
            if (entry.ItemId != itemId) continue;
            if (entry.Amount < amount) return false;
            int remaining = entry.Amount - amount;
            if (remaining == 0) entries.RemoveAt(i);
            else entries[i] = new ResourceInventoryEntry(itemId, remaining);
            Changed?.Invoke(itemId, remaining);
            return true;
        }
        return false;
    }
}
