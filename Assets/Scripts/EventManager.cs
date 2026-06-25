using System;

public class EventManager : Singleton<EventManager>
{
    public event Action LevelStarted;
    public event Action LevelWon;
    public event Action LevelLost;
    public event Action<int> MovesChanged;
    public event Action<int> LivesChanged;
    public event Action<InventoryType, int> InventoryChanged;
    public event Action<int> ArrowSelected;
    public event Action<int> ArrowMoved;
    public event Action InvalidMove;
    public event Action<int> LevelSaved;

    public void TriggerLevelStarted()
    {
        LevelStarted?.Invoke();
    }

    public void TriggerLevelWon()
    {
        LevelWon?.Invoke();
    }

    public void TriggerLevelLost()
    {
        LevelLost?.Invoke();
    }

    public void TriggerMovesChanged(int moves)
    {
        MovesChanged?.Invoke(moves);
    }

    public void TriggerLivesChanged(int lives)
    {
        LivesChanged?.Invoke(lives);
    }

    public void TriggerInventoryChanged(InventoryType type, int quantity)
    {
        InventoryChanged?.Invoke(type, quantity);
    }

    public void TriggerArrowSelected(int arrowId)
    {
        ArrowSelected?.Invoke(arrowId);
    }

    public void TriggerArrowMoved(int arrowId)
    {
        ArrowMoved?.Invoke(arrowId);
    }

    public void TriggerInvalidMove()
    {
        InvalidMove?.Invoke();
    }

    public void TriggerLevelSaved(int levelIndex)
    {
        LevelSaved?.Invoke(levelIndex);
    }
}
