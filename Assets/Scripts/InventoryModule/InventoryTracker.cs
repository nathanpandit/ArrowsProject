using UnityEngine;
using UnityEngine.UI;

public class InventoryTracker : MonoBehaviour
{
    [SerializeField] private InventoryType inventoryType;
    [SerializeField] private Text quantityText;
    [SerializeField] private string prefix;

    private void Awake()
    {
        if (quantityText == null)
        {
            quantityText = GetComponent<Text>();
        }
    }

    private void OnEnable()
    {
        if (EventManager.Instance != null)
        {
            EventManager.Instance.InventoryChanged += HandleInventoryChanged;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (EventManager.Instance != null)
        {
            EventManager.Instance.InventoryChanged -= HandleInventoryChanged;
        }
    }

    private void HandleInventoryChanged(InventoryType changedType, int quantity)
    {
        if (changedType == inventoryType)
        {
            SetText(quantity);
        }
    }

    private void Refresh()
    {
        int quantity = InventoryManager.Instance != null
            ? InventoryManager.Instance.GetQuantity(inventoryType)
            : 0;

        SetText(quantity);
    }

    private void SetText(int quantity)
    {
        if (quantityText == null)
        {
            Debug.LogWarning("InventoryTracker has no Text reference.");
            return;
        }

        quantityText.text = $"{prefix}{quantity}";
    }
}
