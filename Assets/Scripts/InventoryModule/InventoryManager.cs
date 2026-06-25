using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : Singleton<InventoryManager>
{
    [SerializeField] private InventoryScriptableObject startingInventory;

    private readonly Dictionary<InventoryType, int> quantities = new Dictionary<InventoryType, int>();

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this)
        {
            return;
        }

        InitializeFromStartingInventory();
    }

    public int GetQuantity(InventoryType type)
    {
        if (type == InventoryType.None)
        {
            return 0;
        }

        return quantities.TryGetValue(type, out int quantity) ? quantity : 0;
    }

    public void SetQuantity(InventoryType type, int quantity)
    {
        if (type == InventoryType.None)
        {
            Debug.LogWarning("InventoryManager.SetQuantity ignored InventoryType.None.");
            return;
        }

        int safeQuantity = Mathf.Max(0, quantity);
        quantities[type] = safeQuantity;
        EventManager.Instance?.TriggerInventoryChanged(type, safeQuantity);
    }

    public void AddItem(InventoryType type, int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("InventoryManager.AddItem ignored non-positive amount.");
            return;
        }

        SetQuantity(type, GetQuantity(type) + amount);
    }

    public void RemoveItem(InventoryType type, int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("InventoryManager.RemoveItem ignored non-positive amount.");
            return;
        }

        SetQuantity(type, Mathf.Max(0, GetQuantity(type) - amount));
    }

    public bool TrySpend(InventoryType type, int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("InventoryManager.TrySpend ignored non-positive amount.");
            return false;
        }

        int currentQuantity = GetQuantity(type);
        if (currentQuantity < amount)
        {
            return false;
        }

        SetQuantity(type, currentQuantity - amount);
        return true;
    }

    private void InitializeFromStartingInventory()
    {
        quantities.Clear();

        if (startingInventory == null || startingInventory.items == null)
        {
            return;
        }

        for (int i = 0; i < startingInventory.items.Count; i++)
        {
            InventoryDataItem item = startingInventory.items[i];
            if (item == null || item.inventoryType == InventoryType.None)
            {
                continue;
            }

            quantities[item.inventoryType] = Mathf.Max(0, item.quantity);
        }
    }
}
