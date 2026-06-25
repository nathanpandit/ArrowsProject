using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class LevelEditor : MonoBehaviour
{
    [Header("Level")]
    [SerializeField] private int gridSize = 8;
    [SerializeField] private int levelIndex = 1;
    [SerializeField] private int targetArrowCount = 5;
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
        string resourcePath = $"{NormalizeResourcesPath(outputFolderRelativeToResources)}/Level_{levelIndex}";
        TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);

        if (textAsset == null)
        {
            Debug.LogWarning($"LevelEditor.LoadLevel could not find Resources/{resourcePath}.json.");
            return;
        }

        LevelData loadedData = JsonUtility.FromJson<LevelData>(textAsset.text);
        if (loadedData == null)
        {
            Debug.LogError($"LevelEditor.LoadLevel failed to deserialize level {levelIndex}.");
            return;
        }

        if (!loadedData.Validate())
        {
            Debug.LogWarning($"Loaded level {levelIndex} has validation warnings or errors. Editor visuals will still be reconstructed.");
        }

        this.levelIndex = loadedData.levelIndex;
        gridSize = loadedData.gridSize > 0 ? loadedData.gridSize : loadedData.width;
        targetArrowCount = loadedData.targetArrowCount;
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

        lastGeneratedLevelData.levelIndex = levelIndex;
        if (string.IsNullOrWhiteSpace(lastGeneratedLevelData.createdAt))
        {
            lastGeneratedLevelData.createdAt = DateTime.UtcNow.ToString("o");
        }

        if (!lastGeneratedLevelData.Validate())
        {
            Debug.LogError("LevelEditor.SaveLevel aborted: generated LevelData is invalid.");
            return;
        }

        string folderPath = GetOutputFolderPath();
        Directory.CreateDirectory(folderPath);

        string filePath = Path.Combine(folderPath, $"Level_{levelIndex}.json");
        string json = JsonUtility.ToJson(lastGeneratedLevelData, true);
        File.WriteAllText(filePath, json);

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        EventManager.Instance?.TriggerLevelSaved(levelIndex);

        if (showDebugLogs)
        {
            Debug.Log($"Saved level {levelIndex} to {filePath}");
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
                tile.SetArrowTipVisual(arrowTipColor, generatedVertex.tipDirection);
            }
            else if (generatedVertex != null && generatedVertex.contentType == CellContentType.ArrowBody)
            {
                tile.SetArrowBodyVisual(arrowBodyColor);
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
        EditorGenerationRequest request = new EditorGenerationRequest
        {
            gridSize = gridSize,
            targetArrowCount = targetArrowCount,
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
        // Legacy generator kept for reference while testing the exact-cover DAG generator.
        // LevelData generatedData = LevelGeneratorAlgorithm.Generate(request);
        LevelData generatedData = ExactCoverLevelGeneratorAlgorithm.Generate(request);

        if (generatedData == null)
        {
            Debug.LogWarning("Level generation failed. Check the painted shape, arrow count, and length range.");
            return null;
        }

        CompleteGeneratedLevelData(generatedData);

        if (targetArrowCount > 0 && (generatedData.arrows == null || generatedData.arrows.Count != targetArrowCount))
        {
            Debug.LogWarning($"Generated LevelData has {generatedData.arrows?.Count ?? 0} arrows, but {targetArrowCount} were requested. It was not saved.");
            return null;
        }

        if (!generatedData.Validate())
        {
            Debug.LogWarning("Generated LevelData is incomplete or invalid. It was not saved.");
            return null;
        }

        lastGeneratedLevelData = generatedData;
        UpdateVisuals();
        return generatedData;
    }

    public LevelData BuildEmptyLevelDataSkeleton()
    {
        LevelData levelData = new LevelData
        {
            levelIndex = levelIndex,
            gridSize = gridSize,
            width = gridSize,
            height = gridSize,
            targetArrowCount = targetArrowCount,
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

    private void CompleteGeneratedLevelData(LevelData levelData)
    {
        levelData.levelIndex = levelIndex;
        levelData.gridSize = gridSize;
        levelData.width = gridSize;
        levelData.height = gridSize;
        levelData.targetArrowCount = targetArrowCount;
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
