using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    [SerializeField] private float generatedArrowLineWidth = 0.22f;
    [SerializeField] private float generatedArrowHeadLength = 0.42f;
    [SerializeField] private float generatedArrowHeadWidth = 0.44f;
    [SerializeField] private int generatedArrowCornerVertices = 10;
    [SerializeField] private int generatedArrowCapVertices = 10;
    [SerializeField] private int generatedArrowSortingOrder = 5;

    [Header("Saving")]
    [SerializeField] private string outputFolderRelativeToResources = "Levels";
    [SerializeField] private bool saveIntoResourcesLevels = true;
    [SerializeField] private bool generateGraphEdgesOnSave = true;
    [Tooltip("0 means unlimited. A positive value aborts saving if the level has more solution rows than this.")]
    [SerializeField, Min(0)] private int maxSolutionRowsPerCsv;
    [SerializeField] private bool showDebugLogs;

    [Header("Generation")]
    [SerializeField] private LevelGenerationAlgorithmMode generationAlgorithm = LevelGenerationAlgorithmMode.ExactCover;
    [SerializeField, Range(1f, 100f)] private float partialCoverTargetPercent = 85f;
    [SerializeField] private bool useRandomSeed;
    [SerializeField] private int randomSeed = 12345;
    [SerializeField] private int maxSearchIterations = 250000;
    [SerializeField] private float maxGenerationSeconds = 5f;
    [SerializeField] private int maxCandidatesPerSearchState = 512;

    private readonly Dictionary<Vector2Int, Tile> editorTilesByPosition = new Dictionary<Vector2Int, Tile>();
    private readonly List<GameObject> generatedArrowPreviewVisuals = new List<GameObject>();
    private readonly HashSet<Vector2Int> paintedCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> lastGenerationInputPaintedCells = new HashSet<Vector2Int>();
    private Material generatedArrowLineMaterial;
    private LevelData lastGeneratedLevelData;
    private bool hasLastPrimaryPaintGridPosition;
    private bool hasLastSecondaryPaintGridPosition;
    private bool hasLastGenerationInputPaintedCells;
    private bool suppressManualEditingFromPaintedCellsUntilRelease;
    private bool currentConfigurationSolvable;
    private string currentSolvabilityStatus = "Not checked yet.";
    private int currentSolvabilityArrowCount;
    private int currentSolvabilityPlayableCellCount;
    private int currentSolvabilityBlockageCount;
    private Vector2Int lastPrimaryPaintGridPosition;
    private Vector2Int lastSecondaryPaintGridPosition;
    private int selectedTailArrowId = -1;
    private const int MaxLevelVariantScan = 999;
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

    private struct SolvabilityCheckResult
    {
        public bool IsSolvable { get; set; }
        public string Status { get; set; }
        public int ArrowCount { get; set; }
        public int PlayableCellCount { get; set; }
        public int BlockageCount { get; set; }
    }

    public static LevelEditor ActiveInstance { get; private set; }
    public bool HasDisplayedGeneratedLevel => lastGeneratedLevelData != null;
    public bool CurrentConfigurationSolvable => currentConfigurationSolvable;
    public string CurrentSolvabilityStatus => currentSolvabilityStatus;
    public int CurrentSolvabilityArrowCount => currentSolvabilityArrowCount;
    public int CurrentSolvabilityPlayableCellCount => currentSolvabilityPlayableCellCount;
    public int CurrentSolvabilityBlockageCount => currentSolvabilityBlockageCount;

    private Vector2 GridOrigin => originOffset - new Vector2(
        (gridSize - 1) * cellSize * 0.5f,
        (gridSize - 1) * cellSize * 0.5f);

    private void OnDestroy()
    {
        ClearGeneratedArrowPreviewVisuals();

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

        PaintAllGridCells();
        lastGenerationInputPaintedCells.Clear();
        hasLastGenerationInputPaintedCells = false;
        CreateEditorGrid();
        ConfigureEditorCamera();
        RefreshSolvabilityStatus("scene start");
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
        ClearGeneratedArrowPreviewVisuals();

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
        selectedTailArrowId = -1;

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

        SnapshotCurrentPaintedCellsAsGenerationInput();

        CreateEditorGrid();
        ConfigureEditorCamera();
        UpdateVisuals();
        RefreshSolvabilityStatus($"loaded level {baseLevelIndex}_{variantIndex}");
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

        this.levelIndex = baseLevelIndex;
        lastGeneratedLevelData.levelIndex = baseLevelIndex;
        lastGeneratedLevelData.levelVariantIndex = variantIndex;
        CompleteGeneratedLevelData(lastGeneratedLevelData);
        lastGeneratedLevelData.levelIndex = baseLevelIndex;
        lastGeneratedLevelData.levelVariantIndex = variantIndex;
        if (string.IsNullOrWhiteSpace(lastGeneratedLevelData.createdAt))
        {
            lastGeneratedLevelData.createdAt = DateTime.UtcNow.ToString("o");
        }

        SolvabilityCheckResult solvability = EvaluateCurrentSolvability();
        currentConfigurationSolvable = solvability.IsSolvable;
        currentSolvabilityStatus = solvability.Status;
        currentSolvabilityArrowCount = solvability.ArrowCount;
        currentSolvabilityPlayableCellCount = solvability.PlayableCellCount;
        currentSolvabilityBlockageCount = solvability.BlockageCount;

        if (!solvability.IsSolvable)
        {
            Debug.LogWarning($"LevelEditor.SaveLevel aborted: current level is not solvable. {solvability.Status}");
            return;
        }

        if (!TryBuildBlockageGraph(
                lastGeneratedLevelData,
                out HashSet<int> arrowIds,
                out Dictionary<int, HashSet<int>> blockageGraph,
                out _,
                out _,
                out string graphFailureReason))
        {
            Debug.LogWarning($"LevelEditor.SaveLevel aborted: could not build solution graph. {graphFailureReason}");
            return;
        }

        lastGeneratedLevelData.dependencies = BuildDependencyData(blockageGraph);
        if (!TryFindFirstSolutionOrder(arrowIds, blockageGraph, out List<int> firstSolutionOrder))
        {
            Debug.LogWarning("LevelEditor.SaveLevel aborted: no valid solution order could be found.");
            return;
        }

        lastGeneratedLevelData.solutionOrder = firstSolutionOrder;

        if (!lastGeneratedLevelData.Validate())
        {
            Debug.LogError("LevelEditor.SaveLevel aborted: generated LevelData is invalid.");
            return;
        }

        string filePath = Path.Combine(folderPath, BuildLevelVariantFileName(baseLevelIndex, variantIndex));
        string json = JsonUtility.ToJson(lastGeneratedLevelData, true);
        try
        {
            File.WriteAllText(filePath, json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"LevelEditor.SaveLevel failed while writing the level file. {exception.Message}");
            return;
        }

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        EventManager.Instance?.TriggerLevelSaved(baseLevelIndex);

        Debug.Log($"Saved level {baseLevelIndex}_{variantIndex} to {filePath}. Solution CSV generation is currently disabled during level saving.");
    }

    public void UpdateVisuals()
    {
        ClearGeneratedArrowPreviewVisuals();

        foreach (KeyValuePair<Vector2Int, Tile> entry in editorTilesByPosition)
        {
            Vector2Int position = entry.Key;
            Tile tile = entry.Value;

            if (tile == null)
            {
                continue;
            }

            VertexData generatedVertex = lastGeneratedLevelData?.GetVertexAt(position.x, position.y);
            if (generatedVertex != null && generatedVertex.contentType != CellContentType.Empty)
            {
                tile.SetEmptyVisual(emptyColor);
            }
            else if (paintedCells.Contains(position))
            {
                tile.SetPaintedVisual(paintedColor);
            }
            else
            {
                tile.SetEmptyVisual(emptyColor);
            }

            if (IsSelectedTailPosition(position))
            {
                tile.SetSelectedVisual();
            }
        }

        if (lastGeneratedLevelData != null)
        {
            CreateGeneratedArrowPreviewVisuals(lastGeneratedLevelData);
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
        suppressManualEditingFromPaintedCellsUntilRelease = false;
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
        selectedTailArrowId = -1;
        suppressManualEditingFromPaintedCellsUntilRelease = false;
        RestoreLastGenerationInputPaintedCells();
        EndPaintDrag();
        UpdateVisuals();
        RefreshSolvabilityStatus("reset displayed level");

        if (showDebugLogs)
        {
            Debug.Log($"Reset displayed generated level. Restored painted input mask cells: {paintedCells.Count}.");
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
            maxCandidatesPerSearchState = maxCandidatesPerSearchState,
            partialCoverTargetPercent = partialCoverTargetPercent * 0.01f
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
        SnapshotGenerationInputPaintedCells(request.paintedCells);
        LevelData generatedData = GenerateLevelData(request);

        if (generatedData == null)
        {
            Debug.LogWarning("Level generation failed. Check the painted shape, arrow count, and length range.");
            return null;
        }

        CompleteGeneratedLevelData(generatedData);

        int generatedArrowCount = generatedData.arrows?.Count ?? 0;
        int requestMinArrowCount = Mathf.Max(0, Mathf.Min(minArrowCount, maxArrowCount));
        int requestMaxArrowCount = Mathf.Max(requestMinArrowCount, Mathf.Max(minArrowCount, maxArrowCount));
        bool acceptsPartialExactFallback = generationAlgorithm == LevelGenerationAlgorithmMode.PartialExactCover &&
                                           generatedArrowCount > 0 &&
                                           generatedArrowCount < requestMinArrowCount;
        if ((!acceptsPartialExactFallback && generatedArrowCount < requestMinArrowCount) || generatedArrowCount > requestMaxArrowCount)
        {
            Debug.LogWarning($"Generated LevelData has {generatedArrowCount} arrows, but the requested range is {requestMinArrowCount}-{requestMaxArrowCount}. It was not saved.");
            return null;
        }

        if (acceptsPartialExactFallback)
        {
            Debug.Log($"PartialExactCover displayed a fallback level with {generatedArrowCount} arrows, below the requested minimum of {requestMinArrowCount}.");
        }

        if (!generatedData.Validate())
        {
            Debug.LogWarning("Generated LevelData is incomplete or invalid. It was not saved.");
            return null;
        }

        lastGeneratedLevelData = generatedData;
        selectedTailArrowId = -1;
        UpdateVisuals();
        RefreshSolvabilityStatus("level generated");
        Debug.Log($"Level generation completed and displayed. Algorithm: {generationAlgorithm}. Arrows: {generatedData.arrows.Count}. Occupied cells: {CountGeneratedOccupiedCells(generatedData)}. Press S to save or R to reset the displayed design.");
        return generatedData;
    }

    private LevelData GenerateLevelData(EditorGenerationRequest request)
    {
        switch (generationAlgorithm)
        {
            case LevelGenerationAlgorithmMode.PartialExactCover:
                return PartialExactCoverAlgorithm.Generate(request);
            case LevelGenerationAlgorithmMode.PartialCover:
                return PartialCoverLevelGeneratorAlgorithm.Generate(request);
            case LevelGenerationAlgorithmMode.ExactCover:
            default:
                // Legacy generator kept for reference while testing newer generation approaches.
                // return LevelGeneratorAlgorithm.Generate(request);
                // return PathCoverLevelGeneratorAlgorithm.Generate(request);
                return ExactCoverLevelGeneratorAlgorithm.Generate(request);
        }
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
        if (lastGeneratedLevelData == null &&
            isPainted &&
            paintedCells.Contains(gridPosition) &&
            !suppressManualEditingFromPaintedCellsUntilRelease)
        {
            BeginManualDesignedLevelFromPaintedMask();
            changed = HandleDesignedLevelPrimaryClick(gridPosition);

            if (changed)
            {
                RefreshGeneratedLevelAfterManualEdit();
            }

            if (changed && showDebugLogs)
            {
                Debug.Log($"Started manual level editing from painted cell {gridPosition}");
            }

            UpdateVisuals();
            if (changed)
            {
                RefreshSolvabilityStatus("manual level created");
            }

            return;
        }

        if (lastGeneratedLevelData != null)
        {
            changed = isPainted
                ? HandleDesignedLevelPrimaryClick(gridPosition)
                : HandleDesignedLevelSecondaryClick(gridPosition);

            if (changed)
            {
                RefreshGeneratedLevelAfterManualEdit();
            }

            if (changed && showDebugLogs)
            {
                Debug.Log(isPainted ? $"Edited generated level at {gridPosition}" : $"Removed generated level cell at {gridPosition}");
            }

            UpdateVisuals();
            if (changed)
            {
                RefreshSolvabilityStatus("manual edit");
            }

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
        selectedTailArrowId = -1;
        if (changed)
        {
            hasLastGenerationInputPaintedCells = false;
            lastGenerationInputPaintedCells.Clear();
            if (isPainted)
            {
                suppressManualEditingFromPaintedCellsUntilRelease = true;
            }
        }

        if (changed && showDebugLogs)
        {
            Debug.Log(isPainted ? $"Painted {gridPosition}" : $"Erased {gridPosition}");
        }

        UpdateVisuals();
    }

    private void BeginManualDesignedLevelFromPaintedMask()
    {
        SnapshotCurrentPaintedCellsAsGenerationInput();
        lastGeneratedLevelData = BuildEmptyLevelDataSkeleton();
        lastGeneratedLevelData.notes = "Manually designed from painted LevelEditor mask.";
        selectedTailArrowId = -1;
        hasLastPrimaryPaintGridPosition = false;
        hasLastSecondaryPaintGridPosition = false;
        suppressManualEditingFromPaintedCellsUntilRelease = false;
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

    private void RefreshSolvabilityStatus(string triggerLabel)
    {
        SolvabilityCheckResult result = EvaluateCurrentSolvability();
        currentConfigurationSolvable = result.IsSolvable;
        currentSolvabilityStatus = result.Status;
        currentSolvabilityArrowCount = result.ArrowCount;
        currentSolvabilityPlayableCellCount = result.PlayableCellCount;
        currentSolvabilityBlockageCount = result.BlockageCount;

        if (showDebugLogs)
        {
            string label = string.IsNullOrWhiteSpace(triggerLabel) ? "unknown trigger" : triggerLabel;
            Debug.Log($"Solvability check ({label}): {(result.IsSolvable ? "solvable" : "not solvable")} - {result.Status}");
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    private SolvabilityCheckResult EvaluateCurrentSolvability()
    {
        if (lastGeneratedLevelData == null)
        {
            return new SolvabilityCheckResult
            {
                IsSolvable = false,
                Status = "Not solvable: no generated arrow configuration is displayed."
            };
        }

        if (lastGeneratedLevelData.arrows == null || lastGeneratedLevelData.arrows.Count == 0)
        {
            return new SolvabilityCheckResult
            {
                IsSolvable = false,
                Status = "Not solvable: no arrows."
            };
        }

        if (!TryBuildBlockageGraph(
                lastGeneratedLevelData,
                out HashSet<int> arrowIds,
                out Dictionary<int, HashSet<int>> blockageGraph,
                out int playableCellCount,
                out int blockageCount,
                out string failureReason))
        {
            return new SolvabilityCheckResult
            {
                IsSolvable = false,
                Status = $"Not solvable: {failureReason}",
                ArrowCount = arrowIds?.Count ?? 0,
                PlayableCellCount = playableCellCount,
                BlockageCount = blockageCount
            };
        }

        if (arrowIds.Count == 0)
        {
            return new SolvabilityCheckResult
            {
                IsSolvable = false,
                Status = "Not solvable: no arrows.",
                PlayableCellCount = playableCellCount,
                BlockageCount = blockageCount
            };
        }

        if (playableCellCount == 0)
        {
            return new SolvabilityCheckResult
            {
                IsSolvable = false,
                Status = "Not solvable: no playable cells.",
                ArrowCount = arrowIds.Count,
                BlockageCount = blockageCount
            };
        }

        if (!IsBlockageGraphAcyclic(arrowIds, blockageGraph))
        {
            return new SolvabilityCheckResult
            {
                IsSolvable = false,
                Status = "Not solvable: blockage graph contains a cycle.",
                ArrowCount = arrowIds.Count,
                PlayableCellCount = playableCellCount,
                BlockageCount = blockageCount
            };
        }

        return new SolvabilityCheckResult
        {
            IsSolvable = true,
            Status = "Solvable: blockage graph is acyclic.",
            ArrowCount = arrowIds.Count,
            PlayableCellCount = playableCellCount,
            BlockageCount = blockageCount
        };
    }

    private bool TryBuildBlockageGraph(
        LevelData levelData,
        out HashSet<int> arrowIds,
        out Dictionary<int, HashSet<int>> blockageGraph,
        out int playableCellCount,
        out int blockageCount,
        out string failureReason)
    {
        arrowIds = new HashSet<int>();
        blockageGraph = new Dictionary<int, HashSet<int>>();
        playableCellCount = 0;
        blockageCount = 0;
        failureReason = string.Empty;

        Dictionary<Vector2Int, int> arrowIdByCell = new Dictionary<Vector2Int, int>();
        if (levelData == null || levelData.arrows == null)
        {
            failureReason = "missing level data.";
            return false;
        }

        for (int arrowIndex = 0; arrowIndex < levelData.arrows.Count; arrowIndex++)
        {
            ArrowData arrow = levelData.arrows[arrowIndex];
            if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
            {
                continue;
            }

            if (!arrowIds.Add(arrow.arrowId))
            {
                failureReason = $"duplicate arrow id {arrow.arrowId}.";
                return false;
            }

            blockageGraph[arrow.arrowId] = new HashSet<int>();

            for (int cellIndex = 0; cellIndex < arrow.occupiedCells.Count; cellIndex++)
            {
                GridPositionData cell = arrow.occupiedCells[cellIndex];
                if (cell == null || !levelData.IsInsideGrid(cell.x, cell.y))
                {
                    failureReason = $"arrow {arrow.arrowId} has an invalid occupied cell.";
                    return false;
                }

                Vector2Int position = new Vector2Int(cell.x, cell.y);
                if (arrowIdByCell.TryGetValue(position, out int existingArrowId))
                {
                    failureReason = $"cell {position} is occupied by arrows {existingArrowId} and {arrow.arrowId}.";
                    return false;
                }

                arrowIdByCell.Add(position, arrow.arrowId);
                playableCellCount++;
            }
        }

        foreach (int arrowId in arrowIds)
        {
            if (!blockageGraph.ContainsKey(arrowId))
            {
                blockageGraph[arrowId] = new HashSet<int>();
            }
        }

        for (int arrowIndex = 0; arrowIndex < levelData.arrows.Count; arrowIndex++)
        {
            ArrowData arrow = levelData.arrows[arrowIndex];
            if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
            {
                continue;
            }

            if (arrow.tipCell == null || arrow.tipDirection == ArrowDirection.None)
            {
                failureReason = $"arrow {arrow.arrowId} has no valid tip.";
                return false;
            }

            Vector2Int direction = DirectionToVector(arrow.tipDirection);
            if (direction == Vector2Int.zero)
            {
                failureReason = $"arrow {arrow.arrowId} has no valid exit direction.";
                return false;
            }

            Vector2Int cursor = new Vector2Int(arrow.tipCell.x, arrow.tipCell.y) + direction;
            HashSet<int> blockersForArrow = new HashSet<int>();
            while (levelData.IsInsideGrid(cursor.x, cursor.y))
            {
                if (arrowIdByCell.TryGetValue(cursor, out int blockerArrowId))
                {
                    if (blockerArrowId == arrow.arrowId)
                    {
                        failureReason = $"arrow {arrow.arrowId} blocks itself.";
                        return false;
                    }

                    blockersForArrow.Add(blockerArrowId);
                }

                cursor += direction;
            }

            foreach (int blockerArrowId in blockersForArrow)
            {
                if (blockageGraph[blockerArrowId].Add(arrow.arrowId))
                {
                    blockageCount++;
                }
            }
        }

        return true;
    }

    private static bool IsBlockageGraphAcyclic(HashSet<int> arrowIds, Dictionary<int, HashSet<int>> blockageGraph)
    {
        if (arrowIds == null || blockageGraph == null)
        {
            return false;
        }

        Dictionary<int, int> indegreeByArrowId = new Dictionary<int, int>();
        foreach (int arrowId in arrowIds)
        {
            indegreeByArrowId[arrowId] = 0;
        }

        foreach (KeyValuePair<int, HashSet<int>> entry in blockageGraph)
        {
            foreach (int blockedArrowId in entry.Value)
            {
                if (!indegreeByArrowId.ContainsKey(blockedArrowId))
                {
                    continue;
                }

                indegreeByArrowId[blockedArrowId]++;
            }
        }

        Queue<int> ready = new Queue<int>();
        foreach (KeyValuePair<int, int> entry in indegreeByArrowId)
        {
            if (entry.Value == 0)
            {
                ready.Enqueue(entry.Key);
            }
        }

        int visitedCount = 0;
        while (ready.Count > 0)
        {
            int arrowId = ready.Dequeue();
            visitedCount++;

            if (!blockageGraph.TryGetValue(arrowId, out HashSet<int> blockedArrows))
            {
                continue;
            }

            foreach (int blockedArrowId in blockedArrows)
            {
                if (!indegreeByArrowId.ContainsKey(blockedArrowId))
                {
                    continue;
                }

                indegreeByArrowId[blockedArrowId]--;
                if (indegreeByArrowId[blockedArrowId] == 0)
                {
                    ready.Enqueue(blockedArrowId);
                }
            }
        }

        return visitedCount == arrowIds.Count;
    }

    private static List<DependencyData> BuildDependencyData(Dictionary<int, HashSet<int>> blockageGraph)
    {
        List<DependencyData> dependencies = new List<DependencyData>();
        if (blockageGraph == null)
        {
            return dependencies;
        }

        List<int> blockerIds = new List<int>(blockageGraph.Keys);
        blockerIds.Sort();
        for (int blockerIndex = 0; blockerIndex < blockerIds.Count; blockerIndex++)
        {
            int blockerArrowId = blockerIds[blockerIndex];
            if (!blockageGraph.TryGetValue(blockerArrowId, out HashSet<int> blockedArrows))
            {
                continue;
            }

            List<int> blockedArrowIds = new List<int>(blockedArrows);
            blockedArrowIds.Sort();
            for (int blockedIndex = 0; blockedIndex < blockedArrowIds.Count; blockedIndex++)
            {
                dependencies.Add(new DependencyData(blockerArrowId, blockedArrowIds[blockedIndex]));
            }
        }

        return dependencies;
    }

    private static bool TryFindFirstSolutionOrder(
        HashSet<int> arrowIds,
        Dictionary<int, HashSet<int>> blockageGraph,
        out List<int> solutionOrder)
    {
        solutionOrder = new List<int>();
        if (arrowIds == null || blockageGraph == null)
        {
            return false;
        }

        Dictionary<int, int> indegreeByArrowId = BuildIndegreeByArrowId(arrowIds, blockageGraph);
        SortedSet<int> readyArrowIds = new SortedSet<int>();
        foreach (int arrowId in arrowIds)
        {
            if (indegreeByArrowId.TryGetValue(arrowId, out int indegree) && indegree == 0)
            {
                readyArrowIds.Add(arrowId);
            }
        }

        while (readyArrowIds.Count > 0)
        {
            int arrowId = readyArrowIds.Min;
            readyArrowIds.Remove(arrowId);
            solutionOrder.Add(arrowId);

            if (!blockageGraph.TryGetValue(arrowId, out HashSet<int> blockedArrows))
            {
                continue;
            }

            List<int> sortedBlockedArrows = new List<int>(blockedArrows);
            sortedBlockedArrows.Sort();
            for (int i = 0; i < sortedBlockedArrows.Count; i++)
            {
                int blockedArrowId = sortedBlockedArrows[i];
                if (!indegreeByArrowId.ContainsKey(blockedArrowId))
                {
                    continue;
                }

                indegreeByArrowId[blockedArrowId]--;
                if (indegreeByArrowId[blockedArrowId] == 0)
                {
                    readyArrowIds.Add(blockedArrowId);
                }
            }
        }

        return solutionOrder.Count == arrowIds.Count;
    }

    private bool TrySaveAllSolutionsCsv(
        HashSet<int> arrowIds,
        Dictionary<int, HashSet<int>> blockageGraph,
        int baseLevelIndex,
        int variantIndex,
        out string solutionFilePath,
        out long solutionCount,
        out string failureReason)
    {
        solutionFilePath = Path.Combine(GetSolutionsFolderPath(), BuildSolutionVariantFileName(baseLevelIndex, variantIndex));
        solutionCount = 0;
        failureReason = string.Empty;

        if (arrowIds == null || arrowIds.Count == 0)
        {
            failureReason = "there are no arrows to solve.";
            return false;
        }

        if (blockageGraph == null || !IsBlockageGraphAcyclic(arrowIds, blockageGraph))
        {
            failureReason = "the blockage graph is not acyclic.";
            return false;
        }

        string folderPath = GetSolutionsFolderPath();
        Directory.CreateDirectory(folderPath);
        string tempFilePath = solutionFilePath + ".tmp";

        try
        {
            using (StreamWriter writer = new StreamWriter(tempFilePath, false, Encoding.UTF8))
            {
                WriteSolutionCsvHeader(writer, arrowIds.Count);

                Dictionary<int, int> indegreeByArrowId = BuildIndegreeByArrowId(arrowIds, blockageGraph);
                SortedSet<int> readyArrowIds = new SortedSet<int>();
                foreach (int arrowId in arrowIds)
                {
                    if (indegreeByArrowId.TryGetValue(arrowId, out int indegree) && indegree == 0)
                    {
                        readyArrowIds.Add(arrowId);
                    }
                }

                List<int> currentOrder = new List<int>(arrowIds.Count);
                long maxRows = maxSolutionRowsPerCsv > 0 ? maxSolutionRowsPerCsv : long.MaxValue;
                if (!TryWriteAllSolutionOrders(
                        writer,
                        arrowIds.Count,
                        blockageGraph,
                        readyArrowIds,
                        indegreeByArrowId,
                        currentOrder,
                        maxRows,
                        ref solutionCount,
                        out failureReason))
                {
                    TryDeleteFile(tempFilePath);
                    return false;
                }
            }

            if (solutionCount == 0)
            {
                TryDeleteFile(tempFilePath);
                failureReason = "no solution orders were found.";
                return false;
            }

            if (File.Exists(solutionFilePath))
            {
                File.Delete(solutionFilePath);
            }

            File.Move(tempFilePath, solutionFilePath);
            return true;
        }
        catch (Exception exception)
        {
            TryDeleteFile(tempFilePath);
            failureReason = exception.Message;
            return false;
        }
    }

    private static bool TryWriteAllSolutionOrders(
        StreamWriter writer,
        int arrowCount,
        Dictionary<int, HashSet<int>> blockageGraph,
        SortedSet<int> readyArrowIds,
        Dictionary<int, int> indegreeByArrowId,
        List<int> currentOrder,
        long maxRows,
        ref long solutionCount,
        out string failureReason)
    {
        failureReason = string.Empty;

        if (currentOrder.Count == arrowCount)
        {
            if (solutionCount >= maxRows)
            {
                failureReason = $"the level has more than {maxRows} solution order(s). Increase Max Solution Rows Per Csv or add more blocking constraints.";
                return false;
            }

            solutionCount++;
            WriteSolutionCsvRow(writer, solutionCount, currentOrder);
            return true;
        }

        if (readyArrowIds.Count == 0)
        {
            failureReason = "there is no removable arrow before all arrows have been removed.";
            return false;
        }

        List<int> choices = new List<int>(readyArrowIds);
        for (int choiceIndex = 0; choiceIndex < choices.Count; choiceIndex++)
        {
            int arrowId = choices[choiceIndex];
            readyArrowIds.Remove(arrowId);
            currentOrder.Add(arrowId);

            List<int> newlyReadyArrowIds = new List<int>();
            if (blockageGraph.TryGetValue(arrowId, out HashSet<int> blockedArrows))
            {
                List<int> sortedBlockedArrows = new List<int>(blockedArrows);
                sortedBlockedArrows.Sort();
                for (int blockedIndex = 0; blockedIndex < sortedBlockedArrows.Count; blockedIndex++)
                {
                    int blockedArrowId = sortedBlockedArrows[blockedIndex];
                    if (!indegreeByArrowId.ContainsKey(blockedArrowId))
                    {
                        continue;
                    }

                    indegreeByArrowId[blockedArrowId]--;
                    if (indegreeByArrowId[blockedArrowId] == 0)
                    {
                        readyArrowIds.Add(blockedArrowId);
                        newlyReadyArrowIds.Add(blockedArrowId);
                    }
                }
            }

            bool wroteAllBranches = TryWriteAllSolutionOrders(
                writer,
                arrowCount,
                blockageGraph,
                readyArrowIds,
                indegreeByArrowId,
                currentOrder,
                maxRows,
                ref solutionCount,
                out failureReason);

            for (int i = 0; i < newlyReadyArrowIds.Count; i++)
            {
                readyArrowIds.Remove(newlyReadyArrowIds[i]);
            }

            if (blockageGraph.TryGetValue(arrowId, out HashSet<int> arrowsToRestore))
            {
                foreach (int blockedArrowId in arrowsToRestore)
                {
                    if (indegreeByArrowId.ContainsKey(blockedArrowId))
                    {
                        indegreeByArrowId[blockedArrowId]++;
                    }
                }
            }

            currentOrder.RemoveAt(currentOrder.Count - 1);
            readyArrowIds.Add(arrowId);

            if (!wroteAllBranches)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<int, int> BuildIndegreeByArrowId(
        HashSet<int> arrowIds,
        Dictionary<int, HashSet<int>> blockageGraph)
    {
        Dictionary<int, int> indegreeByArrowId = new Dictionary<int, int>();
        foreach (int arrowId in arrowIds)
        {
            indegreeByArrowId[arrowId] = 0;
        }

        foreach (KeyValuePair<int, HashSet<int>> entry in blockageGraph)
        {
            foreach (int blockedArrowId in entry.Value)
            {
                if (indegreeByArrowId.ContainsKey(blockedArrowId))
                {
                    indegreeByArrowId[blockedArrowId]++;
                }
            }
        }

        return indegreeByArrowId;
    }

    private static void WriteSolutionCsvHeader(StreamWriter writer, int arrowCount)
    {
        writer.Write("SolutionIndex");
        for (int i = 1; i <= arrowCount; i++)
        {
            writer.Write($",Step{i}");
        }

        writer.WriteLine();
    }

    private static void WriteSolutionCsvRow(StreamWriter writer, long solutionIndex, List<int> solutionOrder)
    {
        StringBuilder rowBuilder = new StringBuilder();
        rowBuilder.Append(solutionIndex);
        for (int i = 0; i < solutionOrder.Count; i++)
        {
            rowBuilder.Append(',');
            rowBuilder.Append(solutionOrder[i]);
        }

        writer.WriteLine(rowBuilder.ToString());
    }

    private static void TryDeleteFile(string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private void CreateGeneratedArrowPreviewVisuals(LevelData levelData)
    {
        if (levelData?.arrows == null)
        {
            return;
        }

        for (int i = 0; i < levelData.arrows.Count; i++)
        {
            ArrowData arrow = levelData.arrows[i];
            if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
            {
                continue;
            }

            CreateGeneratedArrowPreviewVisual(arrow);
        }
    }

    private void CreateGeneratedArrowPreviewVisual(ArrowData arrow)
    {
        GameObject arrowObject = new GameObject($"EditorArrow_{arrow.arrowId}_Visual");
        arrowObject.transform.SetParent(gridRoot);
        arrowObject.transform.localPosition = Vector3.zero;
        generatedArrowPreviewVisuals.Add(arrowObject);

        LineRenderer lineRenderer = arrowObject.AddComponent<LineRenderer>();
        Material material = GetGeneratedArrowLineMaterial();
        if (material == null)
        {
            Debug.LogWarning("LevelEditor generated arrow preview skipped because no line material could be created.");
            return;
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.material = material;
        lineRenderer.startColor = arrowBodyColor;
        lineRenderer.endColor = arrowBodyColor;
        lineRenderer.startWidth = generatedArrowLineWidth * cellSize;
        lineRenderer.endWidth = generatedArrowLineWidth * cellSize;
        lineRenderer.numCornerVertices = Mathf.Max(0, generatedArrowCornerVertices);
        lineRenderer.numCapVertices = Mathf.Max(0, generatedArrowCapVertices);
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.sortingOrder = generatedArrowSortingOrder;

        int cellCount = arrow.occupiedCells.Count;
        lineRenderer.positionCount = cellCount;
        bool tipIsFirst = IsArrowTipCell(arrow, 0);
        for (int i = 0; i < cellCount; i++)
        {
            GridPositionData cell = tipIsFirst
                ? arrow.occupiedCells[cellCount - 1 - i]
                : arrow.occupiedCells[i];

            if (cell == null)
            {
                continue;
            }

            lineRenderer.SetPosition(i, GridToPreviewWorldPosition(cell.x, cell.y, -0.08f));
        }

        CreateGeneratedArrowPreviewHead(arrow);
    }

    private void CreateGeneratedArrowPreviewHead(ArrowData arrow)
    {
        if (arrow == null || arrow.tipCell == null || arrow.tipDirection == ArrowDirection.None)
        {
            return;
        }

        Vector2Int gridDirection = DirectionToVector(arrow.tipDirection);
        if (gridDirection == Vector2Int.zero)
        {
            return;
        }

        Vector2 direction = new Vector2(gridDirection.x, gridDirection.y);
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        float headLength = generatedArrowHeadLength * cellSize;
        float headWidth = generatedArrowHeadWidth * cellSize;

        Vector3 tipWorld = GridToPreviewWorldPosition(arrow.tipCell.x, arrow.tipCell.y, -0.12f);
        Vector3 point = new Vector3(direction.x, direction.y, 0f) * (headLength * 0.45f);
        Vector3 baseCenter = -new Vector3(direction.x, direction.y, 0f) * (headLength * 0.15f);
        Vector3 left = baseCenter + new Vector3(perpendicular.x, perpendicular.y, 0f) * (headWidth * 0.5f);
        Vector3 right = baseCenter - new Vector3(perpendicular.x, perpendicular.y, 0f) * (headWidth * 0.5f);

        GameObject headObject = new GameObject($"EditorArrow_{arrow.arrowId}_Head");
        headObject.transform.SetParent(gridRoot);
        headObject.transform.position = tipWorld;
        generatedArrowPreviewVisuals.Add(headObject);

        Mesh mesh = new Mesh
        {
            name = $"EditorArrow_{arrow.arrowId}_HeadMesh"
        };

        mesh.vertices = new[]
        {
            point,
            left,
            right
        };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateBounds();

        MeshFilter meshFilter = headObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = headObject.AddComponent<MeshRenderer>();
        Material headMaterial = CreateGeneratedArrowColorMaterial(arrowTipColor);
        if (headMaterial == null)
        {
            return;
        }

        meshRenderer.sharedMaterial = headMaterial;
        meshRenderer.sortingOrder = generatedArrowSortingOrder + 1;
    }

    private void ClearGeneratedArrowPreviewVisuals()
    {
        for (int i = generatedArrowPreviewVisuals.Count - 1; i >= 0; i--)
        {
            GameObject previewVisual = generatedArrowPreviewVisuals[i];
            if (previewVisual == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(previewVisual);
            }
            else
            {
                DestroyImmediate(previewVisual);
            }
        }

        generatedArrowPreviewVisuals.Clear();
    }

    private Vector3 GridToPreviewWorldPosition(int x, int y, float z)
    {
        Vector2 worldPosition = GridToWorldPosition(new Vector2Int(x, y));
        return new Vector3(worldPosition.x, worldPosition.y, z);
    }

    private Material GetGeneratedArrowLineMaterial()
    {
        if (generatedArrowLineMaterial != null)
        {
            return generatedArrowLineMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            Debug.LogWarning("LevelEditor could not find a shader for generated arrow previews.");
            return null;
        }

        generatedArrowLineMaterial = new Material(shader)
        {
            name = "Generated_LevelEditorArrowLineMaterial"
        };

        return generatedArrowLineMaterial;
    }

    private Material CreateGeneratedArrowColorMaterial(Color color)
    {
        Material baseMaterial = GetGeneratedArrowLineMaterial();
        if (baseMaterial == null)
        {
            return null;
        }

        Material material = new Material(baseMaterial)
        {
            name = "Generated_LevelEditorArrowHeadMaterial",
            color = color
        };

        return material;
    }

    private void SnapshotCurrentPaintedCellsAsGenerationInput()
    {
        lastGenerationInputPaintedCells.Clear();
        foreach (Vector2Int position in paintedCells)
        {
            lastGenerationInputPaintedCells.Add(position);
        }

        hasLastGenerationInputPaintedCells = true;
    }

    private void SnapshotGenerationInputPaintedCells(List<GridPositionData> inputCells)
    {
        lastGenerationInputPaintedCells.Clear();
        if (inputCells != null)
        {
            for (int i = 0; i < inputCells.Count; i++)
            {
                GridPositionData cell = inputCells[i];
                if (cell != null)
                {
                    lastGenerationInputPaintedCells.Add(new Vector2Int(cell.x, cell.y));
                }
            }
        }

        hasLastGenerationInputPaintedCells = true;
    }

    private void PaintAllGridCells()
    {
        gridSize = Mathf.Max(1, gridSize);
        paintedCells.Clear();

        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                paintedCells.Add(new Vector2Int(x, y));
            }
        }
    }

    private void RestoreLastGenerationInputPaintedCells()
    {
        if (!hasLastGenerationInputPaintedCells)
        {
            return;
        }

        paintedCells.Clear();
        foreach (Vector2Int position in lastGenerationInputPaintedCells)
        {
            paintedCells.Add(position);
        }
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
            ? HandleDesignedLevelPrimaryClick(gridPosition)
            : HandleDesignedLevelSecondaryClick(gridPosition);
    }

    private bool HandleDesignedLevelPrimaryClick(Vector2Int gridPosition)
    {
        if (lastGeneratedLevelData == null || !IsInsideGrid(gridPosition))
        {
            return false;
        }

        if (lastGeneratedLevelData.arrows == null)
        {
            lastGeneratedLevelData.arrows = new List<ArrowData>();
        }

        if (TryFindArrowAtGridPosition(gridPosition, out ArrowData clickedArrow, out int clickedCellIndex))
        {
            if (IsArrowTipCell(clickedArrow, clickedCellIndex))
            {
                if (clickedArrow.occupiedCells != null && clickedArrow.occupiedCells.Count == 1)
                {
                    selectedTailArrowId = selectedTailArrowId == clickedArrow.arrowId
                        ? -1
                        : clickedArrow.arrowId;
                }
                else
                {
                    selectedTailArrowId = -1;
                }

                return false;
            }

            if (IsArrowTailCell(clickedArrow, clickedCellIndex))
            {
                selectedTailArrowId = selectedTailArrowId == clickedArrow.arrowId
                    ? -1
                    : clickedArrow.arrowId;
                return false;
            }

            selectedTailArrowId = -1;
            return false;
        }

        if (!paintedCells.Contains(gridPosition))
        {
            return paintedCells.Add(gridPosition);
        }

        if (TryGetSelectedTailArrow(out ArrowData selectedArrow) && CanExtendSelectedTailTo(selectedArrow, gridPosition))
        {
            ExtendSelectedArrowTail(selectedArrow, gridPosition);
            selectedTailArrowId = selectedArrow.arrowId;
            return true;
        }

        ArrowData newArrow = CreateSingleCellManualArrow(gridPosition);
        lastGeneratedLevelData.arrows.Add(newArrow);
        selectedTailArrowId = newArrow.arrowId;
        return true;
    }

    private bool HandleDesignedLevelSecondaryClick(Vector2Int gridPosition)
    {
        if (lastGeneratedLevelData == null || !IsInsideGrid(gridPosition))
        {
            return false;
        }

        if (RemoveDesignedArrowCell(gridPosition))
        {
            return true;
        }

        return paintedCells.Remove(gridPosition);
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

        if (IsArrowTipCell(arrow, cellIndex))
        {
            lastGeneratedLevelData.arrows.Remove(arrow);
            if (selectedTailArrowId == arrow.arrowId)
            {
                selectedTailArrowId = -1;
            }

            return true;
        }

        int tailIndex = GetArrowTailIndex(arrow);
        if (tailIndex < 0)
        {
            lastGeneratedLevelData.arrows.Remove(arrow);
            if (selectedTailArrowId == arrow.arrowId)
            {
                selectedTailArrowId = -1;
            }

            return true;
        }

        if (cellIndex == tailIndex)
        {
            arrow.occupiedCells.RemoveAt(cellIndex);
        }
        else if (tailIndex > cellIndex)
        {
            int removeCount = tailIndex - cellIndex + 1;
            arrow.occupiedCells.RemoveRange(cellIndex, removeCount);
        }
        else
        {
            arrow.occupiedCells.RemoveRange(tailIndex, cellIndex - tailIndex + 1);
        }

        if (arrow.occupiedCells.Count == 0)
        {
            lastGeneratedLevelData.arrows.Remove(arrow);
            if (selectedTailArrowId == arrow.arrowId)
            {
                selectedTailArrowId = -1;
            }

            return true;
        }

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

    private bool CanExtendSelectedTailTo(ArrowData arrow, Vector2Int gridPosition)
    {
        return arrow != null &&
               paintedCells.Contains(gridPosition) &&
               !TryFindArrowAtGridPosition(gridPosition, out _, out _) &&
               TryGetArrowTailPosition(arrow, out Vector2Int tailPosition) &&
               ManhattanDistance(tailPosition, gridPosition) == 1;
    }

    private void ExtendSelectedArrowTail(ArrowData arrow, Vector2Int gridPosition)
    {
        if (arrow == null || arrow.occupiedCells == null)
        {
            return;
        }

        int tailIndex = GetArrowTailIndex(arrow);
        GridPositionData newTailCell = new GridPositionData(gridPosition.x, gridPosition.y);
        if (tailIndex <= 0)
        {
            arrow.occupiedCells.Insert(0, newTailCell);
        }
        else
        {
            arrow.occupiedCells.Add(newTailCell);
        }

        RepairArrowTip(arrow);
    }

    private bool TryGetSelectedTailArrow(out ArrowData arrow)
    {
        arrow = FindArrowById(selectedTailArrowId);
        return arrow != null && GetArrowTailIndex(arrow) >= 0;
    }

    private ArrowData FindArrowById(int arrowId)
    {
        if (arrowId < 0 || lastGeneratedLevelData?.arrows == null)
        {
            return null;
        }

        for (int i = 0; i < lastGeneratedLevelData.arrows.Count; i++)
        {
            ArrowData arrow = lastGeneratedLevelData.arrows[i];
            if (arrow != null && arrow.arrowId == arrowId)
            {
                return arrow;
            }
        }

        return null;
    }

    private bool IsSelectedTailPosition(Vector2Int gridPosition)
    {
        return TryGetSelectedTailArrow(out ArrowData arrow) &&
               TryGetArrowTailPosition(arrow, out Vector2Int tailPosition) &&
               tailPosition == gridPosition;
    }

    private bool TryGetArrowTailPosition(ArrowData arrow, out Vector2Int tailPosition)
    {
        tailPosition = Vector2Int.zero;
        int tailIndex = GetArrowTailIndex(arrow);
        if (tailIndex < 0)
        {
            return false;
        }

        tailPosition = ToVector2Int(arrow.occupiedCells[tailIndex]);
        return true;
    }

    private bool IsArrowTipCell(ArrowData arrow, int cellIndex)
    {
        return arrow != null &&
               arrow.tipCell != null &&
               arrow.occupiedCells != null &&
               cellIndex >= 0 &&
               cellIndex < arrow.occupiedCells.Count &&
               arrow.occupiedCells[cellIndex] != null &&
               arrow.occupiedCells[cellIndex].x == arrow.tipCell.x &&
               arrow.occupiedCells[cellIndex].y == arrow.tipCell.y;
    }

    private bool IsArrowTailCell(ArrowData arrow, int cellIndex)
    {
        return cellIndex >= 0 && cellIndex == GetArrowTailIndex(arrow);
    }

    private int GetArrowTailIndex(ArrowData arrow)
    {
        if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
        {
            return -1;
        }

        int tipIndex = GetTipPathIndex(arrow);
        if (arrow.occupiedCells.Count == 1)
        {
            return 0;
        }

        if (tipIndex == 0)
        {
            return arrow.occupiedCells.Count - 1;
        }

        if (tipIndex == arrow.occupiedCells.Count - 1)
        {
            return 0;
        }

        RepairArrowTip(arrow);
        tipIndex = GetTipPathIndex(arrow);
        if (tipIndex == 0)
        {
            return arrow.occupiedCells.Count - 1;
        }

        if (tipIndex == arrow.occupiedCells.Count - 1)
        {
            return 0;
        }

        return -1;
    }

    private int GetTipPathIndex(ArrowData arrow)
    {
        if (arrow == null || arrow.tipCell == null || arrow.occupiedCells == null)
        {
            return -1;
        }

        for (int i = 0; i < arrow.occupiedCells.Count; i++)
        {
            GridPositionData cell = arrow.occupiedCells[i];
            if (cell != null && cell.x == arrow.tipCell.x && cell.y == arrow.tipCell.y)
            {
                return i;
            }
        }

        return -1;
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
        if (arrow.occupiedCells.Count == 1)
        {
            Vector2Int onlyCell = ToVector2Int(arrow.occupiedCells[0]);
            arrow.tipCell = new GridPositionData(onlyCell.x, onlyCell.y);
            arrow.tipDirection = ArrowDirection.Up;
            return;
        }

        if (arrow.tipCell != null)
        {
            Vector2Int currentTip = new Vector2Int(arrow.tipCell.x, arrow.tipCell.y);
            if (TrySetTipDirectionFromLeaf(arrow, currentTip))
            {
                return;
            }
        }

        Vector2Int fallbackTip = ToVector2Int(arrow.occupiedCells[arrow.occupiedCells.Count - 1]);
        TrySetTipDirectionFromLeaf(arrow, fallbackTip);
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

    private bool TrySetTipDirectionFromLeaf(ArrowData arrow, Vector2Int tipPosition)
    {
        if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count < 2)
        {
            return false;
        }

        int tipIndex = -1;
        for (int i = 0; i < arrow.occupiedCells.Count; i++)
        {
            GridPositionData cell = arrow.occupiedCells[i];
            if (cell != null && cell.x == tipPosition.x && cell.y == tipPosition.y)
            {
                tipIndex = i;
                break;
            }
        }

        if (tipIndex < 0)
        {
            return false;
        }

        int previousIndex;
        if (tipIndex == 0)
        {
            previousIndex = 1;
        }
        else if (tipIndex == arrow.occupiedCells.Count - 1)
        {
            previousIndex = arrow.occupiedCells.Count - 2;
        }
        else
        {
            return false;
        }

        Vector2Int previousPosition = ToVector2Int(arrow.occupiedCells[previousIndex]);
        ArrowDirection direction = DirectionFromDelta(tipPosition - previousPosition);
        if (direction == ArrowDirection.None)
        {
            return false;
        }

        arrow.tipCell = new GridPositionData(tipPosition.x, tipPosition.y);
        arrow.tipDirection = direction;
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

        if (!TryGetSelectedTailArrow(out _))
        {
            selectedTailArrowId = -1;
        }

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

    private static ArrowDirection DirectionFromDelta(Vector2Int delta)
    {
        if (delta == Vector2Int.up)
        {
            return ArrowDirection.Up;
        }

        if (delta == Vector2Int.down)
        {
            return ArrowDirection.Down;
        }

        if (delta == Vector2Int.left)
        {
            return ArrowDirection.Left;
        }

        if (delta == Vector2Int.right)
        {
            return ArrowDirection.Right;
        }

        return ArrowDirection.None;
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

        for (int i = 0; i < levelData.arrows.Count; i++)
        {
            RepairArrowTip(levelData.arrows[i]);
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

    private static string GetSolutionsFolderPath()
    {
        return Path.Combine(Application.dataPath, "Resources", "Solutions");
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

    private static string BuildSolutionVariantFileName(int baseLevelIndex, int variantIndex)
    {
        return $"Solution_{baseLevelIndex}_{variantIndex}.csv";
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

public enum LevelGenerationAlgorithmMode
{
    ExactCover,
    PartialCover,
    PartialExactCover
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
    public float partialCoverTargetPercent;
    public List<GridPositionData> paintedCells = new List<GridPositionData>();
}
