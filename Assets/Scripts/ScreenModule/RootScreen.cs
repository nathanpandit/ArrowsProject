using UnityEngine;
using UnityEngine.UI;

public class RootScreen : BaseScreen
{
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button pauseButton;

    private void OnEnable()
    {
        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(OpenSettings);
        }
        else
        {
            Debug.LogWarning("RootScreen is missing settingsButton.");
        }

        if (pauseButton != null)
        {
            pauseButton.onClick.AddListener(OpenPause);
        }
        else
        {
            Debug.LogWarning("RootScreen is missing pauseButton.");
        }
    }

    private void OnDisable()
    {
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(OpenSettings);
        }

        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveListener(OpenPause);
        }
    }

    private void OpenSettings()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        ScreenManager.Instance?.OpenScreen(ScreenType.Settings);
    }

    private void OpenPause()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        ScreenManager.Instance?.OpenScreen(ScreenType.Pause);
    }
}
