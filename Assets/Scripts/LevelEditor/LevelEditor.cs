using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class LevelEditor : MonoBehaviour
{
    [Header("Level")]
    [SerializeField] private int gridSize = 8;
    [SerializeField] private int levelIndex = 1;
    [SerializeField, FormerlySerializedAs("targetArrowCount")] private int minArrowCount = 5;
    [SerializeField] private int maxArrowCount = 10;
    [SerializeField] private int minArrowLength = 2;
    [SerializeField] private int maxArrowLength = 6;
    [SerializeField] private int lives = 3;

    [Header("Grid")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector2 originOffset;
    [SerializeField] private Transform gridRoot;
    [SerializeField] private Tile tilePrefab;

    [Header("Camera")]
    [SerializeField] private Camera editorCamera;
    [SerializeField] private float cameraPanSpeed = 8f;
    [SerializeField] private float cameraZoomSpeed = 1f;
    [SerializeField] private float minOrthographicSize = 2f;
    [SerializeField] private float maxOrthographicSize = 30f;

    [Header("Visuals")]
    [SerializeField] private Color emptyColor = new Color(0.92f, 0.92f, 0.92f, 1f);
    [SerializeField] private Color paintedColor = Color.black;
    [SerializeField] private Color arrowBodyColor = Color.black;
    [SerializeField] private Color arrowTipColor = Color.black;

    [Header("Saving")]
    [SerializeField] private string outputFolderRelativeToResources = "Levels";
    [SerializeField] private bool saveIntoResourcesLevels = true;
    [SerializeField] private bool generateGraphEdgesOnSave = true;
    [SerializeField] private bool showDebugLogs;

    [Header("Generation")]
    [SerializeField] private bool useRandomSeed;
    [SerializeField] private int randomSeed = 12345;
    [SerializeField] private int maxSearchIterations = 250000;
    [SerializeField] private float maxGenerationSeconds = 5f;
    [SerializeField] private int maxCandidatesPerSearchState = 512;

    private readonly Dictionary<Vector2Int, Tile> editorTilesByPosition = new Dictionary<Vector2Int, Tile>();
    private readonly HashSet<Vector2Int> paintedCells = new HashSet<Vector2Int>();
    private LevelData lastGeneratedLevelData;
    private bool hasLastPrimaryPaintGridPosition;
    private bool hasLastSecondaryPaintGridPosition;
    private Vector2Int lastPrimaryPaintGridPosition;
    private Vector2Int lastSecondaryPaintGridPosition;
    private const int MaxLevelVariantScan = 999;
    private static readonly ArrowDirection[] ManualTipDirections =
    {
        ArrowDirection.Up,
        ArrowDirection.Down,
        ArrowDirection.Left,
        ArrowDirection.Right
    };
    private static readonly Color[] GeneratedArrowPreviewPalette =
    {
        new Color(0.75f, 0.75f, 0.75f, 1f),
        new Color(0.95f, 0.38f, 0.65f, 1f),
        new Color(0.55f, 0.82f, 0.35f, 1f),
        new Color(0.18f, 0.65f, 1f, 1f),
        new Color(1f, 0.78f, 0.25f, 1f),
        new Color(0.36f, 0.82f, 0.86f, 1f),
        new Color(0.66f, 0.48f, 0.98f, 1f)
    };

    private struct ArrowEndpointConnection
    {
        public ArrowEndpointConnection(ArrowData arrow, bool connectsToStart)
        {
            Arrow = arrow;
            ConnectsToStart = connectsToStart;
        }

        public ArrowData Arrow { get; }
        public bool ConnectsToStart { get; }
    }

    public static LevelEditor ActiveInstance { get; private set; }

    private Vector2 GridOrigin => originOffset - new Vector2(
        (gridSize - 1) * cellSize * 0.5f,
        (gridSize - 1) * cellSize * 0.5f);

    private void OnDestroy()
    {
        if (ActiveInstance == this)
        {
            ActiveInstance = null;
        }
    }

    public void Start()
    {
        ActiveInstance = this;

        if (gridRoot == null)
        {
            GameObject rootObject = new GameObject("EditorGridRoot");
            gridRoot = rootObject.transform;
        }

        if (editorCamera == null)
        {
            editorCamera = Camera.main;
        }

        paintedCells.Clear();
        CreateEditorGrid();
        ConfigureEditorCamera();
    }

    public void CreateEditorGrid()
    {
        gridSize = Mathf.Max(1, gridSize);
        cellSize = Mathf.Max(0.01f, cellSize);

        ClearEditorGrid();

        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                Vector2Int gridPosition = new Vector2Int(x, y);
                Tile tile = CreateEditorTile(gridPosition);
                editorTilesByPosition.Add(gridPosition, tile);
            }
        }

        UpdateVisuals();
    }

    public void ClearEditorGrid()
    {
        foreach (KeyValuePair<Vector2Int, Tile> entry in editorTilesByPosition)
        {
            if (entry.Value != null)
            {
                entry.Value.DestroySelf();
            }
        }

        editorTilesByPosition.Clear();
    }

    public void LoadLevel(int levelIndex)
    {
        int baseLevelIndex = Mathf.Max(1, levelIndex);
        int variantIndex = FindHighestLevelVariantIndexInResources(baseLevelIndex);
        if (variantIndex <= 0)
        {
            Debug.LogWarning($"LevelEditor.LoadLevel could not find any Resources/{NormalizeResourcesPath(outputFolderRelativeToResources)}/Level_{baseLevelIndex}_*.json file.");
            return;
        }

        string resourcePath = $"{NormalizeResourcesPath(outputFolderRelativeToResources)}/Level_{baseLevelIndex}_{variantIndex}";
        TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);

        if (textAsset == null)
        {
            Debug.LogWarning($"LevelEditor.LoadLevel could not find Resources/{resourcePath}.json.");
            return;
        }

        LevelData loadedData = JsonUtility.FromJson<LevelData>(textAsset.text);
        if (loadedData == null)
        {
            Debug.LogError($"LevelEditor.LoadLevel failed to deserialize level {baseLevelIndex}_{variantIndex}.");
            return;
        }

        if (!loadedData.Validate())
        {
            Debug.LogWarning($"Loaded level {baseLevelIndex}_{variantIndex} has validation warnings or errors. Editor visuals will still be reconstructed.");
        }

        this.levelIndex = loadedData.levelIndex;
        loadedData.levelVariantIndex = variantIndex;
        gridSize = loadedData.gridSize > 0 ? loadedData.gridSize : loadedData.width;
        int loadedMinimumArrowCount = loadedData.minArrowCount > 0 ? loadedData.minArrowCount : loadedData.targetArrowCount;
        int loadedMaximumArrowCount = loadedData.maxArrowCount > 0 ? loadedData.maxArrowCount : loadedData.targetArrowCount;
        minArrowCount = Mathf.Max(0, loadedMinimumArrowCount);
        maxArrowCount = Mathf.Max(minArrowCount, loadedMaximumArrowCount);
        minArrowLength = loadedData.minArrowLength;
        maxArrowLength = loadedData.maxArrowLength;
        lives = loadedData.lives;
        lastGeneratedLevelData = loadedData;

        paintedCells.Clear();
        if (loadedData.vertices != null)
        {
            for (int i = 0; i < loadedData.vertices.Count; i++)
            {
                VertexData vertex = loadedData.vertices[i];
                if (vertex != null && vertex.contentType != CellContentType.Empty)
                {
                    paintedCells.Add(new Vector2Int(vertex.x, vertex.y));
                }
            }
        }

        CreateEditorGrid();
        ConfigureEditorCamera();
        UpdateVisuals();
    }

    public void SaveLevel(int levelIndex)
    {
        if (lastGeneratedLevelData == null)
        {
            Debug.LogWarning("LevelEditor.SaveLevel ignored: no generated LevelData is available yet.");
            return;
        }

        int baseLevelIndex = Mathf.Max(1, levelIndex);
        string folderPath = GetOutputFolderPath();
        Directory.CreateDirectory(folderPath);
        int variantIndex = GetNextLevelVariantIndex(folderPath, baseLevelIndex);

        lastGeneratedLevelData.levelIndex = baseLevelIndex;
        lastGeneratedLevelData.levelVariantIndex = variantIndex;
        if (string.IsNullOrWhiteSpace(lastGeneratedLevelData.createdAt))
        {
            lastGeneratedLevelData.createdAt = DateTime.UtcNow.ToString("o");
        }

        if (!lastGeneratedLevelData.Validate())
        {
            Debug.LogError("LevelEditor.SaveLevel aborted: generated LevelData is invalid.");
            return;
        }

        string filePath = Path.Combine(folderPath, BuildLevelVariantFileName(baseLevelIndex, variantIndex));
        string json = JsonUtility.ToJson(lastGeneratedLevelData, true);
        File.WriteAllText(filePath, json);

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        EventManager.Instance?.TriggerLevelSaved(baseLevelIndex);

        if (showDebugLogs)
        {
            Debug.Log($"Saved level {baseLevelIndex}_{variantIndex} to {filePath}");
        }
    }

    public void UpdateVisuals()
    {
        foreach (KeyValuePair<Vector2Int, Tile> entry in editorTilesByPosition)
        {
            Vector2Int position = entry.Key;
            Tile tile = entry.Value;

            if (tile == null)
            {
                continue;
            }

            VertexData generatedVertex = lastGeneratedLevelData?.GetVertexAt(position.x, position.y);
            if (generatedVertex != null && generatedVertex.contentType == CellContentType.ArrowTip)
            {
                tile.SetArrowTipVisual(GetGeneratedArrowPreviewColor(generatedVertex.arrowId), generatedVertex.tipDirection);
            }
            else if (generatedVertex != null && generatedVertex.contentType == CellContentType.ArrowBody)
            {
                tile.SetArrowBodyVisual(GetGeneratedArrowPreviewColor(generatedVertex.arrowId));
            }
            else if (paintedCells.Contains(position))
            {
                tile.SetPaintedVisual(paintedColor);
            }
            else
            {
                tile.SetEmptyVisual(emptyColor);
            }
        }
    }

    public void ApplyPaint(Vector2 worldPosition)
    {
        SetPaintedState(worldPosition, true);
    }

    public void RemovePaint(Vector2 worldPosition)
    {
        SetPaintedState(worldPosition, false);
    }

    public void HandlePrimaryPaintInput(Vector2 worldPosition)
    {
        ApplyPaint(worldPosition);
    }

    public void HandleSecondaryPaintInput(Vector2 worldPosition)
    {
        RemovePaint(worldPosition);
    }

    public void EndPaintDrag()
    {
        hasLastPrimaryPaintGridPosition = false;
        hasLastSecondaryPaintGridPosition = false;
    }

    public void HandleSaveGenerateShortcut()
    {
        GenerateAndSaveLevel();
    }

    public void HandleGenerateShortcut()
    {
        Debug.Log($"Level generation requested. Painted cells: {paintedCells.Count}. Arrow count range: {Mathf.Min(minArrowCount, maxArrowCount)}-{Mathf.Max(minArrowCount, maxArrowCount)}. Length range: {minArrowLength}-{maxArrowLength}.");
        GenerateSolvableLevelFromPaintedMask();
    }

    public void HandleSaveShortcut()
    {
        SaveLevel(levelIndex);
    }

    public void HandleResetDesignedLevelShortcut()
    {
        ResetDesignedLevel();
    }

    public void ResetDesignedLevel()
    {
        lastGeneratedLevelData = null;
        EndPaintDrag();
        UpdateVisuals();

        if (showDebugLogs)
        {
            Debug.Log("Reset displayed generated level. Painted input mask is still available.");
        }
    }

    public void GenerateAndSaveLevel()
    {
        LevelData generatedData = GenerateSolvableLevelFromPaintedMask();
        if (generatedData != null)
        {
            SaveLevel(levelIndex);
        }
    }

    public void HandleCameraPanInput(Vector2 direction)
    {
        if (editorCamera == null || direction.sqrMagnitude <= 0f)
        {
            return;
        }

        Vector3 movement = new Vector3(direction.x, direction.y, 0f) * cameraPanSpeed * Time.deltaTime;
        editorCamera.transform.position += movement;
    }

    public void HandleCameraZoomInput(float scrollValue)
    {
        if (editorCamera == null || !editorCamera.orthographic)
        {
            return;
        }

        float nextSize = editorCamera.orthographicSize - scrollValue * cameraZoomSpeed;
        editorCamera.orthographicSize = Mathf.Clamp(nextSize, minOrthographicSize, maxOrthographicSize);
    }

    public Vector2 GridToWorldPosition(Vector2Int gridPosition)
    {
        Vector2 origin = GridOrigin;
        return new Vector2(
            origin.x + gridPosition.x * cellSize,
            origin.y + gridPosition.y * cellSize);
    }

    public bool WorldToGridPosition(Vector2 worldPosition, out Vector2Int gridPosition)
    {
        Vector2 origin = GridOrigin;
        Vector2 localPosition = (worldPosition - origin) / cellSize;
        gridPosition = new Vector2Int(
            Mathf.FloorToInt(localPosition.x + 0.5f),
            Mathf.FloorToInt(localPosition.y + 0.5f));

        return IsInsideGrid(gridPosition);
    }

    public bool IsInsideGrid(Vector2Int gridPosition)
    {
        return gridPosition.x >= 0 &&
               gridPosition.x < gridSize &&
               gridPosition.y >= 0 &&
               gridPosition.y < gridSize;
    }

    public EditorGenerationRequest BuildGenerationRequest()
    {
        int normalizedMinArrowCount = Mathf.Max(0, Mathf.Min(minArrowCount, maxArrowCount));
        int normalizedMaxArrowCount = Mathf.Max(normalizedMinArrowCount, Mathf.Max(minArrowCount, maxArrowCount));

        EditorGenerationRequest request = new EditorGenerationRequest
        {
            gridSize = gridSize,
            targetArrowCount = normalizedMaxArrowCount,
            minArrowCount = normalizedMinArrowCount,
            maxArrowCount = normalizedMaxArrowCount,
            minArrowLength = minArrowLength,
            maxArrowLength = maxArrowLength,
            lives = lives,
            paintedCells = new List<GridPositionData>(),
            useRandomSeed = useRandomSeed,
            randomSeed = randomSeed,
            maxSearchIterations = maxSearchIterations,
            maxGenerationSeconds = maxGenerationSeconds,
            maxCandidatesPerSearchState = maxCandidatesPerSearchState
        };

        foreach (Vector2Int position in paintedCells)
        {
            request.paintedCells.Add(new GridPositionData(position.x, position.y));
        }

        return request;
    }

    public LevelData GenerateSolvableLevelFromPaintedMask()
    {
        EditorGenerationRequest request = BuildGenerationRequest();
        Debug.Log($"Level generation started for level {levelIndex}. Painted cells: {request.paintedCells.Count}.");
        // Legacy generator kept for reference while testing newer generation approaches.
        // LevelData generatedData = LevelGeneratorAlgorithm.Generate(request);
        // LevelData generatedData = ExactCoverLevelGeneratorAlgorithm.Generate(request);
        LevelData generatedData = PathCoverLevelGeneratorAlgorithm.Generate(request);

        if (generatedData == null)
        {
            Debug.LogWarning("Level generation failed. Check the painted shape, arrow count, and length range.");
            return null;
        }

        CompleteGeneratedLevelData(generatedData);

        int generatedArrowCount = generatedData.arrows?.Count ?? 0;
        int requestMinArrowCount = Mathf.Max(0, Mathf.Min(minArrowCount, maxArrowCount));
        int requestMaxArrowCount = Mathf.Max(requestMinArrowCount, Mathf.Max(minArrowCount, maxArrowCount));
        if (generatedArrowCount < requestMinArrowCount || generatedArrowCount > requestMaxArrowCount)
        {
            Debug.LogWarning($"Generated LevelData has {generatedArrowCount} arrows, but the requested range is {requestMinArrowCount}-{requestMaxArrowCount}. It was not saved.");
            return null;
        }

        if (!generatedData.Validate())
        {
            Debug.LogWarning("Generated LevelData is incomplete or invalid. It was not saved.");
            return null;
        }

        lastGeneratedLevelData = generatedData;
        UpdateVisuals();
        Debug.Log($"Level generation completed and displayed. Arrows: {generatedData.arrows.Count}. Occupied cells: {CountGeneratedOccupiedCells(generatedData)}. Press S to save or R to reset the displayed design.");
        return generatedData;
    }

    public LevelData BuildEmptyLevelDataSkeleton()
    {
        int normalizedMinArrowCount = Mathf.Max(0, Mathf.Min(minArrowCount, maxArrowCount));
        int normalizedMaxArrowCount = Mathf.Max(normalizedMinArrowCount, Mathf.Max(minArrowCount, maxArrowCount));

        LevelData levelData = new LevelData
        {
            levelIndex = levelIndex,
            gridSize = gridSize,
            width = gridSize,
            height = gridSize,
            targetArrowCount = 0,
            minArrowCount = normalizedMinArrowCount,
            maxArrowCount = normalizedMaxArrowCount,
            minArrowLength = minArrowLength,
            maxArrowLength = maxArrowLength,
            lives = lives,
            arrows = new List<ArrowData>(),
            shapeCells = new List<GridPositionData>(),
            createdAt = DateTime.UtcNow.ToString("o"),
            notes = "Generated from LevelEditor skeleton."
        };

        foreach (Vector2Int position in paintedCells)
        {
            levelData.shapeCells.Add(new GridPositionData(position.x, position.y));
        }

        CreateVerticesFromGrid(levelData);

        if (generateGraphEdgesOnSave)
        {
            CreateGraphEdges(levelData);
        }

        AddNeighborIds(levelData);
        return levelData;
    }

    public void CreateVerticesFromGrid(LevelData levelData)
    {
        if (levelData == null)
        {
            Debug.LogError("CreateVerticesFromGrid failed: LevelData is null.");
            return;
        }

        levelData.gridSize = Mathf.Max(1, levelData.gridSize);
        levelData.width = levelData.width > 0 ? levelData.width : levelData.gridSize;
        levelData.height = levelData.height > 0 ? levelData.height : levelData.gridSize;
        levelData.vertices = new List<VertexData>();

        for (int y = 0; y < levelData.height; y++)
        {
            for (int x = 0; x < levelData.width; x++)
            {
                levelData.vertices.Add(new VertexData
                {
                    vertexId = LevelData.GetVertexId(x, y, levelData.width),
                    x = x,
                    y = y,
                    contentType = CellContentType.Empty,
                    arrowId = -1,
                    tipDirection = ArrowDirection.None,
                    neighborVertexIds = new List<int>()
                });
            }
        }
    }

    public void CreateGraphEdges(LevelData levelData)
    {
        if (levelData == null)
        {
            Debug.LogError("CreateGraphEdges failed: LevelData is null.");
            return;
        }

        levelData.edges = new List<EdgeData>();

        for (int y = 0; y < levelData.height; y++)
        {
            for (int x = 0; x < levelData.width; x++)
            {
                int currentVertexId = LevelData.GetVertexId(x, y, levelData.width);

                if (ShouldCreateShapeEdge(levelData, x, y, x + 1, y))
                {
                    levelData.edges.Add(new EdgeData
                    {
                        fromVertexId = currentVertexId,
                        toVertexId = LevelData.GetVertexId(x + 1, y, levelData.width)
                    });
                }

                if (ShouldCreateShapeEdge(levelData, x, y, x, y + 1))
                {
                    levelData.edges.Add(new EdgeData
                    {
                        fromVertexId = currentVertexId,
                        toVertexId = LevelData.GetVertexId(x, y + 1, levelData.width)
                    });
                }
            }
        }
    }

    public void AddNeighborIds(LevelData levelData)
    {
        if (levelData == null || levelData.vertices == null)
        {
            Debug.LogError("AddNeighborIds failed: LevelData or vertices are null.");
            return;
        }

        for (int i = 0; i < levelData.vertices.Count; i++)
        {
            VertexData vertex = levelData.vertices[i];
            vertex.neighborVertexIds = new List<int>();

            AddNeighborIfInsideShape(levelData, vertex, vertex.x + 1, vertex.y);
            AddNeighborIfInsideShape(levelData, vertex, vertex.x - 1, vertex.y);
            AddNeighborIfInsideShape(levelData, vertex, vertex.x, vertex.y + 1);
            AddNeighborIfInsideShape(levelData, vertex, vertex.x, vertex.y - 1);
        }
    }

    private Tile CreateEditorTile(Vector2Int gridPosition)
    {
        Vector3 worldPosition = GridToWorldPosition(gridPosition);

        Tile tile;
        if (tilePrefab != null)
        {
            tile = Instantiate(tilePrefab, worldPosition, Quaternion.identity, gridRoot);
        }
        else
        {
            GameObject tileObject = new GameObject($"EditorTile_{gridPosition.x}_{gridPosition.y}");
            tileObject.transform.SetParent(gridRoot);
            tileObject.transform.position = worldPosition;
            tileObject.AddComponent<SpriteRenderer>();
            tileObject.AddComponent<BoxCollider2D>();
            tile = tileObject.AddComponent<Tile>();
        }

        tile.Initialize(gridPosition);
        tile.SetDefaultScale(cellSize);
        return tile;
    }

    private void SetPaintedState(Vector2 worldPosition, bool isPainted)
    {
        if (!WorldToGridPosition(worldPosition, out Vector2Int gridPosition))
        {
            if (isPainted)
            {
                hasLastPrimaryPaintGridPosition = false;
            }
            else
            {
                hasLastSecondaryPaintGridPosition = false;
            }

            return;
        }

        bool changed;
        if (lastGeneratedLevelData != null)
        {
            if (isPainted)
            {
                changed = hasLastPrimaryPaintGridPosition
                    ? SetDesignedLevelLine(lastPrimaryPaintGridPosition, gridPosition, true)
                    : SetDesignedLevelGridCell(gridPosition, true);

                lastPrimaryPaintGridPosition = gridPosition;
                hasLastPrimaryPaintGridPosition = true;
                hasLastSecondaryPaintGridPosition = false;
            }
            else
            {
                changed = hasLastSecondaryPaintGridPosition
                    ? SetDesignedLevelLine(lastSecondaryPaintGridPosition, gridPosition, false)
                    : SetDesignedLevelGridCell(gridPosition, false);

                lastSecondaryPaintGridPosition = gridPosition;
                hasLastSecondaryPaintGridPosition = true;
                hasLastPrimaryPaintGridPosition = false;
            }

            if (changed)
            {
                RefreshGeneratedLevelAfterManualEdit();
            }

            if (changed && showDebugLogs)
            {
                Debug.Log(isPainted ? $"Edited generated level at {gridPosition}" : $"Removed generated level cell at {gridPosition}");
            }

            UpdateVisuals();
            return;
        }

        if (isPainted)
        {
            changed = hasLastPrimaryPaintGridPosition
                ? SetPaintedLine(lastPrimaryPaintGridPosition, gridPosition, true)
                : SetPaintedGridCell(gridPosition, true);

            lastPrimaryPaintGridPosition = gridPosition;
            hasLastPrimaryPaintGridPosition = true;
            hasLastSecondaryPaintGridPosition = false;
        }
        else
        {
            changed = hasLastSecondaryPaintGridPosition
                ? SetPaintedLine(lastSecondaryPaintGridPosition, gridPosition, false)
                : SetPaintedGridCell(gridPosition, false);

            lastSecondaryPaintGridPosition = gridPosition;
            hasLastSecondaryPaintGridPosition = true;
            hasLastPrimaryPaintGridPosition = false;
        }

        lastGeneratedLevelData = null;

        if (changed && showDebugLogs)
        {
            Debug.Log(isPainted ? $"Painted {gridPosition}" : $"Erased {gridPosition}");
        }

        UpdateVisuals();
    }

    private bool SetPaintedLine(Vector2Int from, Vector2Int to, bool isPainted)
    {
        bool changed = false;
        int deltaX = Mathf.Abs(to.x - from.x);
        int deltaY = Mathf.Abs(to.y - from.y);
        int stepX = from.x < to.x ? 1 : -1;
        int stepY = from.y < to.y ? 1 : -1;
        int error = deltaX - deltaY;

        Vector2Int current = from;
        while (true)
        {
            changed |= SetPaintedGridCell(current, isPainted);

            if (current == to)
            {
                break;
            }

            int doubleError = error * 2;
            if (doubleError > -deltaY)
            {
                error -= deltaY;
                current.x += stepX;
            }

            if (doubleError < deltaX)
            {
                error += deltaX;
                current.y += stepY;
            }
        }

        return changed;
    }

    private bool SetPaintedGridCell(Vector2Int gridPosition, bool isPainted)
    {
        if (!IsInsideGrid(gridPosition))
        {
            return false;
        }

        return isPainted ? paintedCells.Add(gridPosition) : paintedCells.Remove(gridPosition);
    }

    private static Color GetGeneratedArrowPreviewColor(int arrowId)
    {
        if (GeneratedArrowPreviewPalette == null || GeneratedArrowPreviewPalette.Length == 0)
        {
            return Color.white;
        }

        int colorIndex = Mathf.Abs(arrowId - 1) % GeneratedArrowPreviewPalette.Length;
        return GeneratedArrowPreviewPalette[colorIndex];
    }

    private static int CountGeneratedOccupiedCells(LevelData levelData)
    {
        if (levelData?.arrows == null)
        {
            return 0;
        }

        int occupiedCellCount = 0;
        for (int i = 0; i < levelData.arrows.Count; i++)
        {
            ArrowData arrow = levelData.arrows[i];
            if (arrow?.occupiedCells != null)
            {
                occupiedCellCount += arrow.occupiedCells.Count;
            }
        }

        return occupiedCellCount;
    }

    private bool SetDesignedLevelLine(Vector2Int from, Vector2Int to, bool isPainted)
    {
        bool changed = false;
        int deltaX = Mathf.Abs(to.x - from.x);
        int deltaY = Mathf.Abs(to.y - from.y);
        int stepX = from.x < to.x ? 1 : -1;
        int stepY = from.y < to.y ? 1 : -1;
        int error = deltaX - deltaY;

        Vector2Int current = from;
        while (true)
        {
            changed |= SetDesignedLevelGridCell(current, isPainted);

            if (current == to)
            {
                break;
            }

            int doubleError = error * 2;
            if (doubleError > -deltaY)
            {
                error -= deltaY;
                current.x += stepX;
            }

            if (doubleError < deltaX)
            {
                error += deltaX;
                current.y += stepY;
            }
        }

        return changed;
    }

    private bool SetDesignedLevelGridCell(Vector2Int gridPosition, bool isPainted)
    {
        if (lastGeneratedLevelData == null || !IsInsideGrid(gridPosition))
        {
            return false;
        }

        return isPainted
            ? AddOrExtendDesignedArrow(gridPosition)
            : RemoveDesignedArrowCell(gridPosition);
    }

    private bool AddOrExtendDesignedArrow(Vector2Int gridPosition)
    {
        if (TryFindArrowAtGridPosition(gridPosition, out _, out _))
        {
            return false;
        }

        if (lastGeneratedLevelData.arrows == null)
        {
            lastGeneratedLevelData.arrows = new List<ArrowData>();
        }

        List<ArrowEndpointConnection> endpointConnections = FindDesignedArrowEndpointConnections(gridPosition);
        if (endpointConnections.Count == 0)
        {
            lastGeneratedLevelData.arrows.Add(CreateSingleCellManualArrow(gridPosition));
            return true;
        }

        if (endpointConnections.Count == 1)
        {
            ExtendDesignedArrow(endpointConnections[0], gridPosition);
            return true;
        }

        if (endpointConnections.Count == 2 && endpointConnections[0].Arrow != endpointConnections[1].Arrow)
        {
            return MergeDesignedArrows(endpointConnections[0], endpointConnections[1], gridPosition);
        }

        if (showDebugLogs)
        {
            Debug.LogWarning("Manual edit ignored: the painted cell would create a branch or close a loop. Arrows must remain simple paths.");
        }

        return false;
    }

    private bool RemoveDesignedArrowCell(Vector2Int gridPosition)
    {
        if (!TryFindArrowAtGridPosition(gridPosition, out ArrowData arrow, out int cellIndex))
        {
            return false;
        }

        if (arrow.occupiedCells.Count <= 1)
        {
            lastGeneratedLevelData.arrows.Remove(arrow);
            return true;
        }

        bool removesLeaf = cellIndex == 0 || cellIndex == arrow.occupiedCells.Count - 1;
        if (!removesLeaf)
        {
            lastGeneratedLevelData.arrows.Remove(arrow);
            return true;
        }

        arrow.occupiedCells.RemoveAt(cellIndex);
        RepairArrowTip(arrow);
        return true;
    }

    private List<ArrowEndpointConnection> FindDesignedArrowEndpointConnections(Vector2Int gridPosition)
    {
        List<ArrowEndpointConnection> endpointConnections = new List<ArrowEndpointConnection>();
        if (lastGeneratedLevelData?.arrows == null)
        {
            return endpointConnections;
        }

        for (int i = 0; i < lastGeneratedLevelData.arrows.Count; i++)
        {
            ArrowData arrow = lastGeneratedLevelData.arrows[i];
            if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
            {
                continue;
            }

            Vector2Int firstCell = ToVector2Int(arrow.occupiedCells[0]);
            if (ManhattanDistance(gridPosition, firstCell) == 1)
            {
                endpointConnections.Add(new ArrowEndpointConnection(arrow, true));
            }

            Vector2Int lastCell = ToVector2Int(arrow.occupiedCells[arrow.occupiedCells.Count - 1]);
            if (lastCell != firstCell && ManhattanDistance(gridPosition, lastCell) == 1)
            {
                endpointConnections.Add(new ArrowEndpointConnection(arrow, false));
            }
        }

        return endpointConnections;
    }

    private void ExtendDesignedArrow(ArrowEndpointConnection connection, Vector2Int gridPosition)
    {
        GridPositionData cell = new GridPositionData(gridPosition.x, gridPosition.y);
        if (connection.ConnectsToStart)
        {
            connection.Arrow.occupiedCells.Insert(0, cell);
        }
        else
        {
            connection.Arrow.occupiedCells.Add(cell);
        }

        RepairArrowTip(connection.Arrow);
    }

    private bool MergeDesignedArrows(ArrowEndpointConnection firstConnection, ArrowEndpointConnection secondConnection, Vector2Int bridgePosition)
    {
        ArrowData firstArrow = firstConnection.Arrow;
        ArrowData secondArrow = secondConnection.Arrow;
        if (firstArrow == null || secondArrow == null || firstArrow == secondArrow)
        {
            return false;
        }

        List<GridPositionData> mergedCells = new List<GridPositionData>();
        AppendArrowCells(mergedCells, firstArrow, firstConnection.ConnectsToStart);
        mergedCells.Add(new GridPositionData(bridgePosition.x, bridgePosition.y));
        AppendArrowCells(mergedCells, secondArrow, !secondConnection.ConnectsToStart);

        firstArrow.occupiedCells = mergedCells;
        firstArrow.length = mergedCells.Count;
        lastGeneratedLevelData.arrows.Remove(secondArrow);
        RepairArrowTip(firstArrow);

        if (showDebugLogs)
        {
            Debug.Log($"Merged arrows {firstArrow.arrowId} and {secondArrow.arrowId} into one manual arrow.");
        }

        return true;
    }

    private static void AppendArrowCells(List<GridPositionData> destination, ArrowData arrow, bool reverse)
    {
        if (destination == null || arrow?.occupiedCells == null)
        {
            return;
        }

        if (reverse)
        {
            for (int i = arrow.occupiedCells.Count - 1; i >= 0; i--)
            {
                GridPositionData cell = arrow.occupiedCells[i];
                if (cell != null)
                {
                    destination.Add(new GridPositionData(cell.x, cell.y));
                }
            }

            return;
        }

        for (int i = 0; i < arrow.occupiedCells.Count; i++)
        {
            GridPositionData cell = arrow.occupiedCells[i];
            if (cell != null)
            {
                destination.Add(new GridPositionData(cell.x, cell.y));
            }
        }
    }

    private bool TryFindArrowAtGridPosition(Vector2Int gridPosition, out ArrowData arrow, out int cellIndex)
    {
        arrow = null;
        cellIndex = -1;
        if (lastGeneratedLevelData?.arrows == null)
        {
            return false;
        }

        for (int arrowIndex = 0; arrowIndex < lastGeneratedLevelData.arrows.Count; arrowIndex++)
        {
            ArrowData candidateArrow = lastGeneratedLevelData.arrows[arrowIndex];
            if (candidateArrow?.occupiedCells == null)
            {
                continue;
            }

            for (int i = 0; i < candidateArrow.occupiedCells.Count; i++)
            {
                GridPositionData cell = candidateArrow.occupiedCells[i];
                if (cell != null && cell.x == gridPosition.x && cell.y == gridPosition.y)
                {
                    arrow = candidateArrow;
                    cellIndex = i;
                    return true;
                }
            }
        }

        return false;
    }

    private ArrowData CreateSingleCellManualArrow(Vector2Int gridPosition)
    {
        ArrowData arrow = new ArrowData
        {
            arrowId = GetNextArrowId(),
            occupiedCells = new List<GridPositionData>
            {
                new GridPositionData(gridPosition.x, gridPosition.y)
            },
            tipCell = new GridPositionData(gridPosition.x, gridPosition.y),
            tipDirection = ArrowDirection.Up,
            isSolved = false
        };

        RepairArrowTip(arrow);
        return arrow;
    }

    private int GetNextArrowId()
    {
        int nextArrowId = 1;
        if (lastGeneratedLevelData?.arrows == null)
        {
            return nextArrowId;
        }

        for (int i = 0; i < lastGeneratedLevelData.arrows.Count; i++)
        {
            ArrowData arrow = lastGeneratedLevelData.arrows[i];
            if (arrow != null)
            {
                nextArrowId = Mathf.Max(nextArrowId, arrow.arrowId + 1);
            }
        }

        return nextArrowId;
    }

    private void RepairArrowTip(ArrowData arrow)
    {
        if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
        {
            return;
        }

        arrow.length = arrow.occupiedCells.Count;
        List<Vector2Int> leaves = GetArrowLeaves(arrow);

        if (arrow.tipCell != null)
        {
            Vector2Int currentTip = new Vector2Int(arrow.tipCell.x, arrow.tipCell.y);
            if (leaves.Contains(currentTip) && IsTipDirectionLegalForArrow(arrow, currentTip, arrow.tipDirection))
            {
                return;
            }
        }

        for (int leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
        {
            Vector2Int leaf = leaves[leafIndex];
            for (int directionIndex = 0; directionIndex < ManualTipDirections.Length; directionIndex++)
            {
                ArrowDirection direction = ManualTipDirections[directionIndex];
                if (!IsTipDirectionLegalForArrow(arrow, leaf, direction))
                {
                    continue;
                }

                arrow.tipCell = new GridPositionData(leaf.x, leaf.y);
                arrow.tipDirection = direction;
                return;
            }
        }

        Vector2Int fallbackLeaf = leaves[0];
        arrow.tipCell = new GridPositionData(fallbackLeaf.x, fallbackLeaf.y);
        arrow.tipDirection = ArrowDirection.Up;
    }

    private List<Vector2Int> GetArrowLeaves(ArrowData arrow)
    {
        List<Vector2Int> leaves = new List<Vector2Int>();
        if (arrow?.occupiedCells == null || arrow.occupiedCells.Count == 0)
        {
            return leaves;
        }

        leaves.Add(ToVector2Int(arrow.occupiedCells[0]));
        Vector2Int lastCell = ToVector2Int(arrow.occupiedCells[arrow.occupiedCells.Count - 1]);
        if (lastCell != leaves[0])
        {
            leaves.Add(lastCell);
        }

        return leaves;
    }

    private bool IsTipDirectionLegalForArrow(ArrowData arrow, Vector2Int tipPosition, ArrowDirection direction)
    {
        if (direction == ArrowDirection.None || arrow?.occupiedCells == null)
        {
            return false;
        }

        Vector2Int directionVector = DirectionToVector(direction);
        if (directionVector == Vector2Int.zero)
        {
            return false;
        }

        Vector2Int cursor = tipPosition + directionVector;
        while (IsInsideGrid(cursor))
        {
            for (int i = 0; i < arrow.occupiedCells.Count; i++)
            {
                GridPositionData cell = arrow.occupiedCells[i];
                if (cell == null || cell.x == tipPosition.x && cell.y == tipPosition.y)
                {
                    continue;
                }

                if (cell.x == cursor.x && cell.y == cursor.y)
                {
                    return false;
                }
            }

            cursor += directionVector;
        }

        return true;
    }

    private void RefreshGeneratedLevelAfterManualEdit()
    {
        if (lastGeneratedLevelData == null)
        {
            return;
        }

        if (lastGeneratedLevelData.arrows == null)
        {
            lastGeneratedLevelData.arrows = new List<ArrowData>();
        }

        for (int i = lastGeneratedLevelData.arrows.Count - 1; i >= 0; i--)
        {
            ArrowData arrow = lastGeneratedLevelData.arrows[i];
            if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
            {
                lastGeneratedLevelData.arrows.RemoveAt(i);
                continue;
            }

            RepairArrowTip(arrow);
        }

        paintedCells.Clear();
        lastGeneratedLevelData.shapeCells = new List<GridPositionData>();
        HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
        for (int arrowIndex = 0; arrowIndex < lastGeneratedLevelData.arrows.Count; arrowIndex++)
        {
            ArrowData arrow = lastGeneratedLevelData.arrows[arrowIndex];
            for (int cellIndex = 0; cellIndex < arrow.occupiedCells.Count; cellIndex++)
            {
                GridPositionData cell = arrow.occupiedCells[cellIndex];
                if (cell == null || !lastGeneratedLevelData.IsInsideGrid(cell.x, cell.y))
                {
                    continue;
                }

                Vector2Int position = new Vector2Int(cell.x, cell.y);
                if (!occupiedCells.Add(position))
                {
                    continue;
                }

                paintedCells.Add(position);
                lastGeneratedLevelData.shapeCells.Add(new GridPositionData(position.x, position.y));
            }
        }

        lastGeneratedLevelData.dependencies = new List<DependencyData>();
        lastGeneratedLevelData.solutionOrder = new List<int>();
        CompleteGeneratedLevelData(lastGeneratedLevelData);
    }

    private static Vector2Int ToVector2Int(GridPositionData cell)
    {
        return cell == null ? Vector2Int.zero : new Vector2Int(cell.x, cell.y);
    }

    private static int ManhattanDistance(Vector2Int first, Vector2Int second)
    {
        return Mathf.Abs(first.x - second.x) + Mathf.Abs(first.y - second.y);
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

    private void CompleteGeneratedLevelData(LevelData levelData)
    {
        levelData.levelIndex = levelIndex;
        levelData.gridSize = gridSize;
        levelData.width = gridSize;
        levelData.height = gridSize;
        levelData.targetArrowCount = levelData.arrows?.Count ?? 0;
        levelData.minArrowCount = Mathf.Max(0, Mathf.Min(minArrowCount, maxArrowCount));
        levelData.maxArrowCount = Mathf.Max(levelData.minArrowCount, Mathf.Max(minArrowCount, maxArrowCount));
        levelData.minArrowLength = minArrowLength;
        levelData.maxArrowLength = maxArrowLength;
        levelData.lives = lives;
        levelData.createdAt = string.IsNullOrWhiteSpace(levelData.createdAt) ? DateTime.UtcNow.ToString("o") : levelData.createdAt;
        levelData.notes = string.IsNullOrWhiteSpace(levelData.notes) ? "Generated from painted LevelEditor mask." : levelData.notes;

        if (levelData.shapeCells == null)
        {
            levelData.shapeCells = new List<GridPositionData>();
        }

        if (levelData.shapeCells.Count == 0)
        {
            foreach (Vector2Int position in paintedCells)
            {
                levelData.shapeCells.Add(new GridPositionData(position.x, position.y));
            }
        }

        if (levelData.vertices == null || levelData.vertices.Count != gridSize * gridSize)
        {
            CreateVerticesFromGrid(levelData);
        }

        if (levelData.arrows == null)
        {
            levelData.arrows = new List<ArrowData>();
        }

        ApplyArrowDataToVertices(levelData);

        if (generateGraphEdgesOnSave)
        {
            CreateGraphEdges(levelData);
        }
        else if (levelData.edges == null)
        {
            levelData.edges = new List<EdgeData>();
        }

        AddNeighborIds(levelData);
    }

    private void ApplyArrowDataToVertices(LevelData levelData)
    {
        for (int i = 0; i < levelData.vertices.Count; i++)
        {
            VertexData vertex = levelData.vertices[i];
            vertex.contentType = CellContentType.Empty;
            vertex.arrowId = -1;
            vertex.tipDirection = ArrowDirection.None;
        }

        for (int arrowIndex = 0; arrowIndex < levelData.arrows.Count; arrowIndex++)
        {
            ArrowData arrow = levelData.arrows[arrowIndex];
            if (arrow == null || arrow.occupiedCells == null)
            {
                Debug.LogWarning("ApplyArrowDataToVertices skipped a null or incomplete arrow.");
                continue;
            }

            arrow.length = arrow.occupiedCells.Count;

            for (int cellIndex = 0; cellIndex < arrow.occupiedCells.Count; cellIndex++)
            {
                GridPositionData cell = arrow.occupiedCells[cellIndex];
                if (cell == null || !levelData.IsInsideGrid(cell.x, cell.y))
                {
                    Debug.LogWarning($"Arrow {arrow.arrowId} contains an invalid cell.");
                    continue;
                }

                VertexData vertex = levelData.GetVertexAt(cell.x, cell.y);
                if (vertex == null)
                {
                    continue;
                }

                bool isTip = arrow.tipCell != null && cell.x == arrow.tipCell.x && cell.y == arrow.tipCell.y;
                vertex.contentType = isTip ? CellContentType.ArrowTip : CellContentType.ArrowBody;
                vertex.arrowId = arrow.arrowId;
                vertex.tipDirection = isTip ? arrow.tipDirection : ArrowDirection.None;
            }
        }
    }

    private void AddNeighborIfInsideShape(LevelData levelData, VertexData vertex, int x, int y)
    {
        if (ShouldCreateShapeEdge(levelData, vertex.x, vertex.y, x, y))
        {
            vertex.neighborVertexIds.Add(LevelData.GetVertexId(x, y, levelData.width));
        }
    }

    private bool ShouldCreateShapeEdge(LevelData levelData, int fromX, int fromY, int toX, int toY)
    {
        if (!levelData.IsInsideGrid(fromX, fromY) || !levelData.IsInsideGrid(toX, toY))
        {
            return false;
        }

        if (levelData.shapeCells == null || levelData.shapeCells.Count == 0)
        {
            return true;
        }

        return ContainsShapeCell(levelData, fromX, fromY) && ContainsShapeCell(levelData, toX, toY);
    }

    private bool ContainsShapeCell(LevelData levelData, int x, int y)
    {
        for (int i = 0; i < levelData.shapeCells.Count; i++)
        {
            GridPositionData cell = levelData.shapeCells[i];
            if (cell != null && cell.x == x && cell.y == y)
            {
                return true;
            }
        }

        return false;
    }

    private void ConfigureEditorCamera()
    {
        if (editorCamera == null)
        {
            Debug.LogWarning("LevelEditor has no editor camera assigned and no Camera.main was found.");
            return;
        }

        editorCamera.orthographic = true;
        Vector2 center = originOffset;
        editorCamera.transform.position = new Vector3(center.x, center.y, -10f);

        float aspect = Mathf.Max(0.01f, editorCamera.aspect);
        float gridWorldWidth = gridSize * cellSize;
        float gridWorldHeight = gridSize * cellSize;
        float sizeForHeight = gridWorldHeight * 0.5f + cellSize;
        float sizeForWidth = (gridWorldWidth * 0.5f + cellSize) / aspect;
        editorCamera.orthographicSize = Mathf.Clamp(
            Mathf.Max(sizeForHeight, sizeForWidth),
            minOrthographicSize,
            maxOrthographicSize);
    }

    private string GetOutputFolderPath()
    {
        if (saveIntoResourcesLevels)
        {
            return Path.Combine(Application.dataPath, "Resources", NormalizeResourcesPath(outputFolderRelativeToResources));
        }

        return Path.Combine(Application.dataPath, NormalizeResourcesPath(outputFolderRelativeToResources));
    }

    private static int GetNextLevelVariantIndex(string folderPath, int baseLevelIndex)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return 1;
        }

        int highestVariantIndex = 0;
        string searchPattern = $"Level_{baseLevelIndex}_*.json";
        string[] files = Directory.GetFiles(folderPath, searchPattern, SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(files[i]);
            if (TryParseLevelVariantIndex(fileNameWithoutExtension, baseLevelIndex, out int variantIndex))
            {
                highestVariantIndex = Mathf.Max(highestVariantIndex, variantIndex);
            }
        }

        return highestVariantIndex + 1;
    }

    private int FindHighestLevelVariantIndexInResources(int baseLevelIndex)
    {
        int highestVariantIndex = 0;
        string resourceFolder = NormalizeResourcesPath(outputFolderRelativeToResources);
        for (int variantIndex = 1; variantIndex <= MaxLevelVariantScan; variantIndex++)
        {
            string resourcePath = $"{resourceFolder}/Level_{baseLevelIndex}_{variantIndex}";
            if (Resources.Load<TextAsset>(resourcePath) == null)
            {
                break;
            }

            highestVariantIndex = variantIndex;
        }

        return highestVariantIndex;
    }

    private static bool TryParseLevelVariantIndex(string fileNameWithoutExtension, int baseLevelIndex, out int variantIndex)
    {
        variantIndex = 0;
        string prefix = $"Level_{baseLevelIndex}_";
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension) ||
            !fileNameWithoutExtension.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string suffix = fileNameWithoutExtension.Substring(prefix.Length);
        return int.TryParse(suffix, out variantIndex) && variantIndex > 0;
    }

    private static string BuildLevelVariantFileName(int baseLevelIndex, int variantIndex)
    {
        return $"Level_{baseLevelIndex}_{variantIndex}.json";
    }

    private static string NormalizeResourcesPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Levels";
        }

        return path.Trim().Trim('/').Trim('\\').Replace("\\", "/");
    }
}

[Serializable]
public class EditorGenerationRequest
{
    public int gridSize;
    public int targetArrowCount;
    public int minArrowCount;
    public int maxArrowCount;
    public int minArrowLength;
    public int maxArrowLength;
    public int lives;
    public bool useRandomSeed;
    public int randomSeed;
    public int maxSearchIterations;
    public float maxGenerationSeconds;
    public int maxCandidatesPerSearchState;
    public List<GridPositionData> paintedCells = new List<GridPositionData>();
}
