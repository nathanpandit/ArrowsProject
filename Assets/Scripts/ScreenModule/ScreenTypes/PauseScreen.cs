using UnityEngine;
using UnityEngine.UI;

public class PauseScreen : BaseScreen
{
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button settingsButton;

    private void OnEnable()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.AddListener(HandleResume);
        }

        if (retryButton != null)
        {
            retryButton.onClick.AddListener(HandleRetry);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(HandleSettings);
        }
    }

    private void OnDisable()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveListener(HandleResume);
        }

        if (retryButton != null)
        {
            retryButton.onClick.RemoveListener(HandleRetry);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(HandleSettings);
        }
    }

    private void HandleResume()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        Close();
    }

    private void HandleRetry()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        GameManager.RestartLevel();
    }

    private void HandleSettings()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        ScreenManager.Instance?.OpenScreen(ScreenType.Settings);
    }
}
