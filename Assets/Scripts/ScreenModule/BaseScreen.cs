using UnityEngine;

public class BaseScreen : MonoBehaviour
{
    [SerializeField] private ScreenType screenType = ScreenType.None;
    [SerializeField] private CanvasGroup canvasGroup;

    public ScreenType ScreenType => screenType;

    protected virtual void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }
    }

    public virtual void Open()
    {
        gameObject.SetActive(true);
        SetVisible(true);
        OnOpened();
    }

    public virtual void Close()
    {
        SetVisible(false);
        OnClosed();
        gameObject.SetActive(false);
    }

    protected virtual void OnOpened()
    {
    }

    protected virtual void OnClosed()
    {
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }
}
