using UnityEngine;
using UnityEngine.UI;

public class LoseScreen : BaseScreen
{
    [SerializeField] private Button retryButton;
    [SerializeField] private Button homeButton;

    private void OnEnable()
    {
        if (retryButton != null)
        {
            retryButton.onClick.AddListener(HandleRetry);
        }

        if (homeButton != null)
        {
            homeButton.onClick.AddListener(HandleHome);
        }
    }

    private void OnDisable()
    {
        if (retryButton != null)
        {
            retryButton.onClick.RemoveListener(HandleRetry);
        }

        if (homeButton != null)
        {
            homeButton.onClick.RemoveListener(HandleHome);
        }
    }

    private void HandleRetry()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        GameManager.RestartLevel();
    }

    private void HandleHome()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        Debug.LogWarning("Home button is a placeholder.");
    }
}
