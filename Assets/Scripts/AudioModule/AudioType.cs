using System;
using UnityEngine;

[Serializable]
public class AudioData
{
    public SoundType soundType;
    public MusicType musicType;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    public bool loop;
}

public enum SoundType
{
    None,
    ButtonClicked,
    LevelWon,
    LevelLost,
    TileSelected,
    ArrowSelected,
    ArrowMoved,
    InvalidMove,
    LevelSaved
}

public enum MusicType
{
    None,
    InGameBackgroundMusic,
    MainMenuBackgroundMusic
}
