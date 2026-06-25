using UnityEngine;
using UnityEngine.UI;

public class SettingsScreen : BaseScreen
{
    [SerializeField] private Button closeButton;
    [SerializeField] private Button soundToggleButton;
    [SerializeField] private Button musicToggleButton;

    private bool soundEnabled = true;
    private bool musicEnabled = true;

    private void OnEnable()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(HandleClose);
        }

        if (soundToggleButton != null)
        {
            soundToggleButton.onClick.AddListener(HandleSoundToggle);
        }

        if (musicToggleButton != null)
        {
            musicToggleButton.onClick.AddListener(HandleMusicToggle);
        }
    }

    private void OnDisable()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(HandleClose);
        }

        if (soundToggleButton != null)
        {
            soundToggleButton.onClick.RemoveListener(HandleSoundToggle);
        }

        if (musicToggleButton != null)
        {
            musicToggleButton.onClick.RemoveListener(HandleMusicToggle);
        }
    }

    private void HandleClose()
    {
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        Close();
    }

    private void HandleSoundToggle()
    {
        soundEnabled = !soundEnabled;
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        Debug.LogWarning($"Sound toggle placeholder: {(soundEnabled ? "enabled" : "disabled")}.");
    }

    private void HandleMusicToggle()
    {
        musicEnabled = !musicEnabled;
        AudioManager.Instance?.PlaySound(SoundType.ButtonClicked);
        Debug.LogWarning($"Music toggle placeholder: {(musicEnabled ? "enabled" : "disabled")}.");
    }
}
