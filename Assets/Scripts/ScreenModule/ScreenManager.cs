using System.Collections.Generic;
using UnityEngine;

public class ScreenManager : Singleton<ScreenManager>
{
    [SerializeField] private List<BaseScreen> screens = new List<BaseScreen>();

    private readonly Dictionary<ScreenType, BaseScreen> screensByType = new Dictionary<ScreenType, BaseScreen>();

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this)
        {
            return;
        }

        RegisterScreens();
    }

    private void OnEnable()
    {
        if (EventManager.Instance == null)
        {
            return;
        }

        EventManager.Instance.LevelWon += HandleLevelWon;
        EventManager.Instance.LevelLost += HandleLevelLost;
    }

    private void OnDisable()
    {
        if (EventManager.Instance == null)
        {
            return;
        }

        EventManager.Instance.LevelWon -= HandleLevelWon;
        EventManager.Instance.LevelLost -= HandleLevelLost;
    }

    public void OpenScreen(ScreenType screenType)
    {
        if (screenType == ScreenType.None)
        {
            return;
        }

        if (!screensByType.TryGetValue(screenType, out BaseScreen screen) || screen == null)
        {
            Debug.LogWarning($"ScreenManager could not open missing screen {screenType}.");
            return;
        }

        screen.Open();
    }

    public void CloseScreen(ScreenType screenType)
    {
        if (!screensByType.TryGetValue(screenType, out BaseScreen screen) || screen == null)
        {
            return;
        }

        screen.Close();
    }

    public void CloseAllScreens()
    {
        foreach (KeyValuePair<ScreenType, BaseScreen> entry in screensByType)
        {
            if (entry.Value != null)
            {
                entry.Value.Close();
            }
        }
    }

    private void RegisterScreens()
    {
        screensByType.Clear();

        for (int i = 0; i < screens.Count; i++)
        {
            RegisterScreen(screens[i]);
        }

        BaseScreen[] foundScreens = FindObjectsByType<BaseScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < foundScreens.Length; i++)
        {
            RegisterScreen(foundScreens[i]);
        }
    }

    private void RegisterScreen(BaseScreen screen)
    {
        if (screen == null || screen.ScreenType == ScreenType.None)
        {
            return;
        }

        screensByType[screen.ScreenType] = screen;
    }

    private void HandleLevelWon()
    {
        OpenScreen(ScreenType.Win);
    }

    private void HandleLevelLost()
    {
        OpenScreen(ScreenType.Lose);
    }
}
