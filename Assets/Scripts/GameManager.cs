using System.Collections.Generic;
using DG.Tweening;
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
    private static readonly Dictionary<int, List<GameObject>> runtimeArrowVisualsById = new Dictionary<int, List<GameObject>>();
    private const float ArrowExitSpeed = 12f;
    private const float ArrowExitMinDuration = 0.25f;
    private const float ArrowExitMaxDuration = 1.4f;
    private const float ArrowExitExtraPaddingCells = 2f;
    private static float runtimeCellSize = 1f;
    private static Vector2 runtimeOrigin;
    private static bool levelEnded;

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
        levelEnded = false;

        EventManager.Instance?.TriggerLevelStarted();
        EventManager.Instance?.TriggerLivesChanged(Lives);
        EventManager.Instance?.TriggerMovesChanged(Moves);
    }

    public static void ClearRuntimeState()
    {
        tilesByPosition.Clear();
        runtimeArrowVisualsById.Clear();
        CurrentLevelData = null;
        CurrentGridWidth = 0;
        CurrentGridHeight = 0;
        Moves = 0;
        Lives = 0;
        SelectedArrowId = -1;
        levelEnded = false;
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

        if (!runtimeArrowVisualsById.TryGetValue(arrowId, out List<GameObject> arrowVisuals))
        {
            arrowVisuals = new List<GameObject>();
            runtimeArrowVisualsById.Add(arrowId, arrowVisuals);
        }

        arrowVisuals.Add(arrowObject);
    }

    public static void HandlePrimaryInput(Vector2 worldPosition)
    {
        if (CurrentLevelData == null)
        {
            Debug.LogWarning("GameManager.HandlePrimaryInput ignored: no level is initialized.");
            return;
        }

        if (levelEnded)
        {
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

        if (levelEnded)
        {
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
            return;
        }

        TryClearArrow(vertex.arrowId);
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

    public static bool TryClearArrow(int arrowId)
    {
        if (CurrentLevelData == null || levelEnded)
        {
            return false;
        }

        ArrowData arrow = FindArrowById(arrowId);
        if (arrow == null || arrow.isSolved)
        {
            Debug.LogWarning($"TryClearArrow ignored missing or solved arrow id {arrowId}.");
            return false;
        }

        SelectArrow(arrowId);

        if (!CanArrowLeaveMap(arrow))
        {
            ConsumeLifeForBlockedArrow(arrowId);
            return false;
        }

        ClearArrow(arrow);
        Moves++;
        EventManager.Instance?.TriggerMovesChanged(Moves);
        EventManager.Instance?.TriggerArrowMoved(arrowId);

        if (CheckWinCondition())
        {
            TriggerWin();
        }

        return true;
    }

    public static bool CheckWinCondition()
    {
        if (CurrentLevelData == null)
        {
            return false;
        }

        if (CurrentLevelData.arrows != null)
        {
            for (int i = 0; i < CurrentLevelData.arrows.Count; i++)
            {
                ArrowData arrow = CurrentLevelData.arrows[i];
                if (arrow != null && !arrow.isSolved && arrow.occupiedCells != null && arrow.occupiedCells.Count > 0)
                {
                    return false;
                }
            }
        }

        if (CurrentLevelData.vertices != null)
        {
            for (int i = 0; i < CurrentLevelData.vertices.Count; i++)
            {
                VertexData vertex = CurrentLevelData.vertices[i];
                if (vertex != null && vertex.contentType != CellContentType.Empty)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static void TriggerWin()
    {
        if (levelEnded)
        {
            return;
        }

        levelEnded = true;
        EventManager.Instance?.TriggerLevelWon();
    }

    public static void TriggerLose()
    {
        if (levelEnded)
        {
            return;
        }

        levelEnded = true;
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

    private static ArrowData FindArrowById(int arrowId)
    {
        if (CurrentLevelData == null || CurrentLevelData.arrows == null)
        {
            return null;
        }

        for (int i = 0; i < CurrentLevelData.arrows.Count; i++)
        {
            ArrowData arrow = CurrentLevelData.arrows[i];
            if (arrow != null && arrow.arrowId == arrowId)
            {
                return arrow;
            }
        }

        return null;
    }

    private static bool CanArrowLeaveMap(ArrowData arrow)
    {
        if (CurrentLevelData == null || arrow == null || arrow.tipCell == null)
        {
            return false;
        }

        Vector2Int direction = DirectionToVector(arrow.tipDirection);
        if (direction == Vector2Int.zero)
        {
            return false;
        }

        Vector2Int cursor = new Vector2Int(
            arrow.tipCell.x + direction.x,
            arrow.tipCell.y + direction.y);

        while (CurrentLevelData.IsInsideGrid(cursor.x, cursor.y))
        {
            VertexData vertex = CurrentLevelData.GetVertexAt(cursor.x, cursor.y);
            if (vertex == null || vertex.contentType != CellContentType.Empty)
            {
                return false;
            }

            cursor += direction;
        }

        return true;
    }

    private static void ConsumeLifeForBlockedArrow(int arrowId)
    {
        Lives = Mathf.Max(0, Lives - 1);
        EventManager.Instance?.TriggerLivesChanged(Lives);
        EventManager.Instance?.TriggerInvalidMove();
        Debug.Log($"Arrow {arrowId} cannot leave the map. Remaining lives: {Lives}.");

        if (Lives <= 0)
        {
            TriggerLose();
        }
    }

    private static void ClearArrow(ArrowData arrow)
    {
        if (CurrentLevelData == null || arrow == null)
        {
            return;
        }

        int arrowId = arrow.arrowId;
        ArrowDirection exitDirection = arrow.tipDirection;
        if (arrow.occupiedCells != null)
        {
            for (int i = 0; i < arrow.occupiedCells.Count; i++)
            {
                GridPositionData cell = arrow.occupiedCells[i];
                if (cell == null)
                {
                    continue;
                }

                ClearArrowCell(arrowId, cell.x, cell.y);
            }

            arrow.occupiedCells.Clear();
        }

        arrow.length = 0;
        arrow.tipCell = null;
        arrow.tipDirection = ArrowDirection.None;
        arrow.isSolved = true;

        if (SelectedArrowId == arrowId)
        {
            SelectedArrowId = -1;
        }

        if (!AnimateRuntimeArrowExit(arrowId, exitDirection))
        {
            DestroyRuntimeArrowVisuals(arrowId);
        }

        Debug.Log($"Arrow {arrowId} cleared.");
    }

    private static void ClearArrowCell(int arrowId, int x, int y)
    {
        VertexData vertex = CurrentLevelData.GetVertexAt(x, y);
        if (vertex == null || vertex.arrowId != arrowId)
        {
            return;
        }

        vertex.contentType = CellContentType.Empty;
        vertex.arrowId = -1;
        vertex.tipDirection = ArrowDirection.None;

        Vector2Int position = new Vector2Int(x, y);
        if (!tilesByPosition.TryGetValue(position, out Tile tile))
        {
            return;
        }

        if (LevelGenerator.ActiveInstance != null)
        {
            LevelGenerator.ActiveInstance.ApplyVertexVisual(tile, vertex);
        }
        else
        {
            tile.SetEmptyVisual(Color.white);
        }
    }

    private static void DestroyRuntimeArrowVisuals(int arrowId)
    {
        if (!runtimeArrowVisualsById.TryGetValue(arrowId, out List<GameObject> arrowVisuals))
        {
            return;
        }

        runtimeArrowVisualsById.Remove(arrowId);
        DestroyRuntimeArrowVisualObjects(arrowVisuals);
    }

    private static bool AnimateRuntimeArrowExit(int arrowId, ArrowDirection exitDirection)
    {
        if (!Application.isPlaying || exitDirection == ArrowDirection.None)
        {
            return false;
        }

        if (!runtimeArrowVisualsById.TryGetValue(arrowId, out List<GameObject> registeredVisuals))
        {
            return false;
        }

        runtimeArrowVisualsById.Remove(arrowId);

        List<GameObject> arrowVisuals = new List<GameObject>(registeredVisuals);
        LineRenderer lineRenderer = FindRuntimeArrowLineRenderer(arrowVisuals);
        if (lineRenderer == null || lineRenderer.positionCount <= 0)
        {
            return AnimateRigidRuntimeArrowExit(arrowVisuals, exitDirection);
        }

        Vector3[] tailToTipPositions = new Vector3[lineRenderer.positionCount];
        lineRenderer.GetPositions(tailToTipPositions);
        List<GameObject> headVisuals = GetRuntimeArrowHeadVisuals(arrowVisuals, lineRenderer.gameObject);
        Vector3[] headStartPositions = GetTransformPositions(headVisuals);
        float totalProgress = CalculateSnakeExitProgress(tailToTipPositions[tailToTipPositions.Length - 1], tailToTipPositions.Length, exitDirection);
        float duration = Mathf.Clamp(totalProgress * runtimeCellSize / ArrowExitSpeed, ArrowExitMinDuration, ArrowExitMaxDuration);

        Tween tween = DOTween
            .To(
                () => 0f,
                progress => ApplySnakeExitProgress(lineRenderer, tailToTipPositions, headVisuals, headStartPositions, exitDirection, progress),
                totalProgress,
                duration)
            .SetEase(Ease.Linear)
            .SetTarget($"ArrowExit_{arrowId}")
            .OnComplete(() => DestroyRuntimeArrowVisualObjects(arrowVisuals));

        return tween != null;
    }

    private static bool AnimateRigidRuntimeArrowExit(List<GameObject> arrowVisuals, ArrowDirection exitDirection)
    {
        Vector2Int gridDirection = DirectionToVector(exitDirection);
        Vector3 direction = new Vector3(gridDirection.x, gridDirection.y, 0f);
        float exitDistance = CalculateRigidExitDistance(arrowVisuals, exitDirection);
        Vector3 exitOffset = direction * exitDistance;
        float duration = Mathf.Clamp(exitDistance / ArrowExitSpeed, ArrowExitMinDuration, ArrowExitMaxDuration);

        Sequence sequence = DOTween.Sequence();
        bool addedTween = false;

        for (int i = 0; i < arrowVisuals.Count; i++)
        {
            GameObject arrowVisual = arrowVisuals[i];
            if (arrowVisual == null)
            {
                continue;
            }

            sequence.Join(arrowVisual.transform.DOMove(arrowVisual.transform.position + exitOffset, duration));
            addedTween = true;
        }

        if (!addedTween)
        {
            sequence.Kill();
            DestroyRuntimeArrowVisualObjects(arrowVisuals);
            return true;
        }

        sequence
            .SetEase(Ease.InQuad)
            .OnComplete(() => DestroyRuntimeArrowVisualObjects(arrowVisuals));

        return true;
    }

    private static void ApplySnakeExitProgress(
        LineRenderer lineRenderer,
        Vector3[] tailToTipPositions,
        List<GameObject> headVisuals,
        Vector3[] headStartPositions,
        ArrowDirection exitDirection,
        float progress)
    {
        if (lineRenderer == null || tailToTipPositions == null || tailToTipPositions.Length == 0)
        {
            return;
        }

        Vector3 exitVector = DirectionToWorldVector(exitDirection);
        List<Vector3> visiblePolyline = BuildSnakeVisiblePolyline(tailToTipPositions, exitVector, progress);
        lineRenderer.positionCount = visiblePolyline.Count;
        lineRenderer.SetPositions(visiblePolyline.ToArray());

        ApplyHeadExitProgress(headVisuals, headStartPositions, exitVector, progress);
    }

    private static List<Vector3> BuildSnakeVisiblePolyline(Vector3[] tailToTipPositions, Vector3 exitVector, float progress)
    {
        List<Vector3> visiblePolyline = new List<Vector3>();
        if (tailToTipPositions == null || tailToTipPositions.Length == 0)
        {
            return visiblePolyline;
        }

        if (tailToTipPositions.Length == 1)
        {
            visiblePolyline.Add(tailToTipPositions[0]);
            return visiblePolyline;
        }

        Vector3[] trackPositions = BuildSnakeTrackPositions(tailToTipPositions, exitVector, progress);
        float[] cumulativeDistances = BuildCumulativeDistances(trackPositions);
        float originalPathLength = cumulativeDistances[tailToTipPositions.Length - 1];
        float startDistance = Mathf.Max(0f, progress * runtimeCellSize);
        float endDistance = startDistance + originalPathLength;

        visiblePolyline.Add(EvaluatePolylineAtDistance(trackPositions, cumulativeDistances, startDistance));

        const float epsilon = 0.001f;
        for (int i = 1; i < cumulativeDistances.Length - 1; i++)
        {
            float distance = cumulativeDistances[i];
            if (distance > startDistance + epsilon && distance < endDistance - epsilon)
            {
                AddPointIfDistinct(visiblePolyline, trackPositions[i]);
            }
        }

        AddPointIfDistinct(visiblePolyline, EvaluatePolylineAtDistance(trackPositions, cumulativeDistances, endDistance));
        return visiblePolyline;
    }

    private static Vector3[] BuildSnakeTrackPositions(Vector3[] tailToTipPositions, Vector3 exitVector, float progress)
    {
        Vector3[] trackPositions = new Vector3[tailToTipPositions.Length + 1];
        for (int i = 0; i < tailToTipPositions.Length; i++)
        {
            trackPositions[i] = tailToTipPositions[i];
        }

        Vector3 tipPosition = tailToTipPositions[tailToTipPositions.Length - 1];
        float exitLength = Mathf.Max(runtimeCellSize, progress * runtimeCellSize + runtimeCellSize * ArrowExitExtraPaddingCells);
        trackPositions[trackPositions.Length - 1] = tipPosition + exitVector * exitLength;
        return trackPositions;
    }

    private static float[] BuildCumulativeDistances(Vector3[] positions)
    {
        float[] distances = new float[positions.Length];
        for (int i = 1; i < positions.Length; i++)
        {
            distances[i] = distances[i - 1] + Vector3.Distance(positions[i - 1], positions[i]);
        }

        return distances;
    }

    private static Vector3 EvaluatePolylineAtDistance(Vector3[] positions, float[] cumulativeDistances, float targetDistance)
    {
        if (positions == null || positions.Length == 0)
        {
            return Vector3.zero;
        }

        if (targetDistance <= 0f)
        {
            return positions[0];
        }

        int lastIndex = positions.Length - 1;
        if (targetDistance >= cumulativeDistances[lastIndex])
        {
            return positions[lastIndex];
        }

        for (int i = 1; i < positions.Length; i++)
        {
            if (targetDistance > cumulativeDistances[i])
            {
                continue;
            }

            float segmentLength = cumulativeDistances[i] - cumulativeDistances[i - 1];
            if (segmentLength <= 0.0001f)
            {
                return positions[i];
            }

            float segmentProgress = (targetDistance - cumulativeDistances[i - 1]) / segmentLength;
            return Vector3.Lerp(positions[i - 1], positions[i], segmentProgress);
        }

        return positions[lastIndex];
    }

    private static void AddPointIfDistinct(List<Vector3> points, Vector3 point)
    {
        if (points == null)
        {
            return;
        }

        if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], point) > 0.001f)
        {
            points.Add(point);
        }
    }

    private static void ApplyHeadExitProgress(List<GameObject> headVisuals, Vector3[] headStartPositions, Vector3 exitVector, float progress)
    {
        if (headVisuals == null || headStartPositions == null)
        {
            return;
        }

        int visualCount = Mathf.Min(headVisuals.Count, headStartPositions.Length);
        for (int i = 0; i < visualCount; i++)
        {
            GameObject headVisual = headVisuals[i];
            if (headVisual == null)
            {
                continue;
            }

            headVisual.transform.position = headStartPositions[i] + exitVector * (progress * runtimeCellSize);
        }
    }

    private static float CalculateSnakeExitProgress(Vector3 tipPosition, int pathPointCount, ArrowDirection exitDirection)
    {
        float pathProgressToTip = Mathf.Max(0, pathPointCount - 1);
        float fallbackExitDistance = Mathf.Max(CurrentGridWidth, CurrentGridHeight, 1) * runtimeCellSize;
        float worldExitDistance = CalculateWorldDistanceFromPointToScreenExit(tipPosition, exitDirection, fallbackExitDistance);
        return pathProgressToTip + Mathf.Max(1f, worldExitDistance / Mathf.Max(0.01f, runtimeCellSize));
    }

    private static float CalculateRigidExitDistance(List<GameObject> arrowVisuals, ArrowDirection exitDirection)
    {
        float fallbackDistance = Mathf.Max(CurrentGridWidth, CurrentGridHeight, 1) * runtimeCellSize + runtimeCellSize * ArrowExitExtraPaddingCells;
        if (!TryGetCombinedVisualBounds(arrowVisuals, out Bounds visualBounds))
        {
            return fallbackDistance;
        }

        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
        {
            return fallbackDistance;
        }

        float zDistance = Mathf.Abs(camera.transform.position.z - visualBounds.center.z);
        Vector3 bottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, zDistance));
        Vector3 topRight = camera.ViewportToWorldPoint(new Vector3(1f, 1f, zDistance));
        float padding = runtimeCellSize * ArrowExitExtraPaddingCells;

        switch (exitDirection)
        {
            case ArrowDirection.Right:
                return Mathf.Max(runtimeCellSize, topRight.x - visualBounds.min.x + padding);
            case ArrowDirection.Left:
                return Mathf.Max(runtimeCellSize, visualBounds.max.x - bottomLeft.x + padding);
            case ArrowDirection.Up:
                return Mathf.Max(runtimeCellSize, topRight.y - visualBounds.min.y + padding);
            case ArrowDirection.Down:
                return Mathf.Max(runtimeCellSize, visualBounds.max.y - bottomLeft.y + padding);
            default:
                return fallbackDistance;
        }
    }

    private static float CalculateWorldDistanceFromPointToScreenExit(Vector3 worldPosition, ArrowDirection exitDirection, float fallbackDistance)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
        {
            return fallbackDistance;
        }

        float zDistance = Mathf.Abs(camera.transform.position.z - worldPosition.z);
        Vector3 bottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, zDistance));
        Vector3 topRight = camera.ViewportToWorldPoint(new Vector3(1f, 1f, zDistance));
        float padding = runtimeCellSize * ArrowExitExtraPaddingCells;

        switch (exitDirection)
        {
            case ArrowDirection.Right:
                return Mathf.Max(runtimeCellSize, topRight.x - worldPosition.x + padding);
            case ArrowDirection.Left:
                return Mathf.Max(runtimeCellSize, worldPosition.x - bottomLeft.x + padding);
            case ArrowDirection.Up:
                return Mathf.Max(runtimeCellSize, topRight.y - worldPosition.y + padding);
            case ArrowDirection.Down:
                return Mathf.Max(runtimeCellSize, worldPosition.y - bottomLeft.y + padding);
            default:
                return fallbackDistance;
        }
    }

    private static LineRenderer FindRuntimeArrowLineRenderer(List<GameObject> arrowVisuals)
    {
        if (arrowVisuals == null)
        {
            return null;
        }

        for (int i = 0; i < arrowVisuals.Count; i++)
        {
            GameObject arrowVisual = arrowVisuals[i];
            if (arrowVisual == null)
            {
                continue;
            }

            LineRenderer lineRenderer = arrowVisual.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                return lineRenderer;
            }
        }

        return null;
    }

    private static List<GameObject> GetRuntimeArrowHeadVisuals(List<GameObject> arrowVisuals, GameObject lineObject)
    {
        List<GameObject> headVisuals = new List<GameObject>();
        if (arrowVisuals == null)
        {
            return headVisuals;
        }

        for (int i = 0; i < arrowVisuals.Count; i++)
        {
            GameObject arrowVisual = arrowVisuals[i];
            if (arrowVisual == null || arrowVisual == lineObject)
            {
                continue;
            }

            headVisuals.Add(arrowVisual);
        }

        return headVisuals;
    }

    private static Vector3[] GetTransformPositions(List<GameObject> gameObjects)
    {
        if (gameObjects == null)
        {
            return new Vector3[0];
        }

        Vector3[] positions = new Vector3[gameObjects.Count];
        for (int i = 0; i < gameObjects.Count; i++)
        {
            positions[i] = gameObjects[i] != null ? gameObjects[i].transform.position : Vector3.zero;
        }

        return positions;
    }

    private static bool TryGetCombinedVisualBounds(List<GameObject> arrowVisuals, out Bounds combinedBounds)
    {
        combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        if (arrowVisuals == null)
        {
            return false;
        }

        for (int i = 0; i < arrowVisuals.Count; i++)
        {
            GameObject arrowVisual = arrowVisuals[i];
            if (arrowVisual == null)
            {
                continue;
            }

            Renderer renderer = arrowVisual.GetComponent<Renderer>();
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static void DestroyRuntimeArrowVisualObjects(List<GameObject> arrowVisuals)
    {
        if (arrowVisuals == null)
        {
            return;
        }

        for (int i = 0; i < arrowVisuals.Count; i++)
        {
            GameObject arrowVisual = arrowVisuals[i];
            if (arrowVisual == null)
            {
                continue;
            }

            LineRenderer lineRenderer = arrowVisual.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                DOTween.Kill(lineRenderer);
            }

            DOTween.Kill(arrowVisual.transform);

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(arrowVisual);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(arrowVisual);
            }
        }
    }

    private static Vector3 DirectionToWorldVector(ArrowDirection direction)
    {
        Vector2Int gridDirection = DirectionToVector(direction);
        return new Vector3(gridDirection.x, gridDirection.y, 0f);
    }

    private static Vector2Int DirectionToVector(ArrowDirection direction)
    {
        switch (direction)
        {
            case ArrowDirection.Up:
                return Vector2Int.up;
            case ArrowDirection.Down:
                return Vector2Int.down;
            case ArrowDirection.Left:
                return Vector2Int.left;
            case ArrowDirection.Right:
                return Vector2Int.right;
            default:
                return Vector2Int.zero;
        }
    }
}
