using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class InventoryDataItem
{
    public InventoryType inventoryType;
    public int quantity;
}

[CreateAssetMenu(fileName = "InventoryData", menuName = "Arrows Puzzle/Inventory Data")]
public class InventoryScriptableObject : ScriptableObject
{
    public List<InventoryDataItem> items = new List<InventoryDataItem>();
}
