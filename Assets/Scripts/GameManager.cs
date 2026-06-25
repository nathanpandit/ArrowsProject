using System.Collections.Generic;
using UnityEngine;

public static class GameManager
{
    public static LevelData CurrentLevelData { get; private set; }
    public static int CurrentGridWidth { get; private set; }
    public static int CurrentGridHeight { get; private set; }
    public static int SelectedArrowId { get; private set; } = -1;
    public static int Moves { get; private set; }
    public static int Lives { get; private set; }

    private static readonly Dictionary<Vector2Int, Tile> tilesByPosition = new Dictionary<Vector2Int, Tile>();
    private static readonly Dictionary<int, GameObject> runtimeArrowsById = new Dictionary<int, GameObject>();
    private static float runtimeCellSize = 1f;
    private static Vector2 runtimeOrigin;

    public static void Initialize(LevelData levelData)
    {
        ClearRuntimeState();

        if (levelData == null)
        {
            Debug.LogError("GameManager.Initialize failed: LevelData is null.");
            return;
        }

        CurrentLevelData = levelData;
        CurrentGridWidth = levelData.width;
        CurrentGridHeight = levelData.height;
        Lives = Mathf.Max(0, levelData.lives);
        Moves = 0;
        SelectedArrowId = -1;

        EventManager.Instance?.TriggerLevelStarted();
        EventManager.Instance?.TriggerLivesChanged(Lives);
        EventManager.Instance?.TriggerMovesChanged(Moves);
    }

    public static void ClearRuntimeState()
    {
        tilesByPosition.Clear();
        runtimeArrowsById.Clear();
        CurrentLevelData = null;
        CurrentGridWidth = 0;
        CurrentGridHeight = 0;
        Moves = 0;
        Lives = 0;
        SelectedArrowId = -1;
    }

    public static void SetRuntimeGridLayout(float cellSize, Vector2 origin)
    {
        runtimeCellSize = Mathf.Max(0.01f, cellSize);
        runtimeOrigin = origin;
    }

    public static void RegisterTile(Vector2Int position, Tile tile)
    {
        if (tile == null)
        {
            Debug.LogWarning($"GameManager.RegisterTile ignored null tile at {position}.");
            return;
        }

        tilesByPosition[position] = tile;
    }

    public static void UnregisterTile(Vector2Int position)
    {
        tilesByPosition.Remove(position);
    }

    public static void RegisterRuntimeArrow(int arrowId, GameObject arrowObject)
    {
        if (arrowId < 0 || arrowObject == null)
        {
            return;
        }

        runtimeArrowsById[arrowId] = arrowObject;
    }

    public static void HandlePrimaryInput(Vector2 worldPosition)
    {
        if (CurrentLevelData == null)
        {
            Debug.LogWarning("GameManager.HandlePrimaryInput ignored: no level is initialized.");
            return;
        }

        Vector2 localPosition = (worldPosition - runtimeOrigin) / runtimeCellSize;
        Vector2Int gridPosition = new Vector2Int(
            Mathf.FloorToInt(localPosition.x + 0.5f),
            Mathf.FloorToInt(localPosition.y + 0.5f));

        HandleCellClicked(gridPosition);
    }

    public static void HandleCellClicked(Vector2Int gridPosition)
    {
        if (CurrentLevelData == null)
        {
            Debug.LogWarning("GameManager.HandleCellClicked ignored: no level is initialized.");
            return;
        }

        if (!CurrentLevelData.IsInsideGrid(gridPosition.x, gridPosition.y))
        {
            return;
        }

        VertexData vertex = CurrentLevelData.GetVertexAt(gridPosition.x, gridPosition.y);
        if (vertex == null)
        {
            Debug.LogWarning($"No vertex data found at grid position {gridPosition}.");
            return;
        }

        if (vertex.contentType == CellContentType.Empty)
        {
            if (tilesByPosition.TryGetValue(gridPosition, out Tile tile))
            {
                tile.SetSelectedVisual();
            }

            return;
        }

        SelectArrow(vertex.arrowId);
    }

    public static void SelectArrow(int arrowId)
    {
        if (arrowId < 0)
        {
            Debug.LogWarning("GameManager.SelectArrow ignored invalid arrow id.");
            return;
        }

        SelectedArrowId = arrowId;
        EventManager.Instance?.TriggerArrowSelected(arrowId);
    }

    public static bool TryMoveSelectedArrow()
    {
        if (SelectedArrowId < 0)
        {
            Debug.LogWarning("TryMoveSelectedArrow called with no selected arrow.");
            EventManager.Instance?.TriggerInvalidMove();
            return false;
        }

        // TODO: Add final Arrows Puzzle movement rules here.
        // The selected arrow should attempt movement based on its path, tip direction,
        // blockers, board bounds, and the final solvability rules.
        Debug.LogWarning("TryMoveSelectedArrow is a gameplay placeholder.");
        EventManager.Instance?.TriggerInvalidMove();
        return false;
    }

    public static bool CheckWinCondition()
    {
        // TODO: Add final win condition once the movement and clear rules are defined.
        return false;
    }

    public static void TriggerWin()
    {
        EventManager.Instance?.TriggerLevelWon();
    }

    public static void TriggerLose()
    {
        EventManager.Instance?.TriggerLevelLost();
    }

    public static void RestartLevel()
    {
        Debug.LogWarning("RestartLevel is a placeholder. Connect this to scene or level reload logic later.");
    }

    public static void LoadNextLevel()
    {
        Debug.LogWarning("LoadNextLevel is a placeholder. Connect this to progression logic later.");
    }
}
