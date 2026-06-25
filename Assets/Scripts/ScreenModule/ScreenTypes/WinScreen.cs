using UnityEngine;
using UnityEngine.UI;

public class WinScreen : BaseScreen
{
    [SerializeField] private Button nextLevelButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button homeButton;

    private void OnEnable()
    {
        AddListeners();
    }

    private void OnDisable()
    {
        RemoveListeners();
    }

    private void AddListeners()
    {
        if (nextLevelButton != null)
        {
            nextLevelButton.onClick.AddListener(HandleNextLevel);
        }

        if (retryButton != null)
        {
            retryButton.onClick.AddListener(HandleRetry);
        }

        if (homeButton != null)
        {
            homeButton.onClick.AddListener(HandleHome);
        }
    }

    private void RemoveListeners()
    {
        if (nextLevelButton != null)
        {
            nextLevelButton.onClick.RemoveListener(HandleNextLevel);
        }

        if (retryButton != null)
        {
            retryButton.onClick.RemoveListener(HandleRetry);
        }

        if (homeButton != null)
        {
            homeButton.onClick.RemoveListener(HandleHome);
        }
    }

    private void HandleNextLevel()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        GameManager.LoadNextLevel();
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
