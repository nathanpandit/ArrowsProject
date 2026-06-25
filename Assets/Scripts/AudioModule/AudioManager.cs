using System.Collections.Generic;
using UnityEngine;

public class AudioManager : Singleton<AudioManager>
{
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource soundSource;
    [SerializeField] private List<AudioData> audioData = new List<AudioData>();

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this)
        {
            return;
        }

        EnsureSources();
    }

    private void OnEnable()
    {
        if (EventManager.Instance == null)
        {
            return;
        }

        EventManager.Instance.LevelWon += HandleLevelWon;
        EventManager.Instance.LevelLost += HandleLevelLost;
        EventManager.Instance.InvalidMove += HandleInvalidMove;
        EventManager.Instance.LevelSaved += HandleLevelSaved;
        EventManager.Instance.ArrowSelected += HandleArrowSelected;
        EventManager.Instance.ArrowMoved += HandleArrowMoved;
    }

    private void OnDisable()
    {
        if (EventManager.Instance == null)
        {
            return;
        }

        EventManager.Instance.LevelWon -= HandleLevelWon;
        EventManager.Instance.LevelLost -= HandleLevelLost;
        EventManager.Instance.InvalidMove -= HandleInvalidMove;
        EventManager.Instance.LevelSaved -= HandleLevelSaved;
        EventManager.Instance.ArrowSelected -= HandleArrowSelected;
        EventManager.Instance.ArrowMoved -= HandleArrowMoved;
    }

    public void PlaySound(SoundType soundType)
    {
        if (soundType == SoundType.None)
        {
            return;
        }

        EnsureSources();
        AudioData data = FindSoundData(soundType);
        if (data == null || data.clip == null)
        {
            Debug.LogWarning($"AudioManager.PlaySound missing clip for {soundType}.");
            return;
        }

        soundSource.PlayOneShot(data.clip, data.volume);
    }

    public void PlayMusic(MusicType musicType)
    {
        if (musicType == MusicType.None)
        {
            return;
        }

        EnsureSources();
        AudioData data = FindMusicData(musicType);
        if (data == null || data.clip == null)
        {
            Debug.LogWarning($"AudioManager.PlayMusic missing clip for {musicType}.");
            return;
        }

        musicSource.clip = data.clip;
        musicSource.volume = data.volume;
        musicSource.loop = data.loop;
        musicSource.Play();
    }

    private void EnsureSources()
    {
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
        }

        if (soundSource == null)
        {
            soundSource = gameObject.AddComponent<AudioSource>();
        }
    }

    private AudioData FindSoundData(SoundType soundType)
    {
        for (int i = 0; i < audioData.Count; i++)
        {
            AudioData data = audioData[i];
            if (data != null && data.soundType == soundType)
            {
                return data;
            }
        }

        return null;
    }

    private AudioData FindMusicData(MusicType musicType)
    {
        for (int i = 0; i < audioData.Count; i++)
        {
            AudioData data = audioData[i];
            if (data != null && data.musicType == musicType)
            {
                return data;
            }
        }

        return null;
    }

    private void HandleLevelWon()
    {
        PlaySound(SoundType.LevelWon);
    }

    private void HandleLevelLost()
    {
        PlaySound(SoundType.LevelLost);
    }

    private void HandleInvalidMove()
    {
        PlaySound(SoundType.InvalidMove);
    }

    private void HandleLevelSaved(int savedLevelIndex)
    {
        PlaySound(SoundType.LevelSaved);
    }

    private void HandleArrowSelected(int arrowId)
    {
        PlaySound(SoundType.ArrowSelected);
    }

    private void HandleArrowMoved(int arrowId)
    {
        PlaySound(SoundType.ArrowMoved);
    }
}
