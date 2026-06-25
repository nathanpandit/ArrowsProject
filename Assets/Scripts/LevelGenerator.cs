using System.Collections.Generic;
using UnityEngine;

public class LevelGenerator : MonoBehaviour
{
    [SerializeField] private Tile tilePrefab;
    [SerializeField] private Transform levelRoot;
    [SerializeField] private bool generateLevelOnStart = true;
    [SerializeField] private int startingLevelIndex = 1;
    [SerializeField] private string resourcesLevelsFolder = "Levels";
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private float runtimeTileFillScale = 1f;
    [SerializeField] private Color emptyColor = Color.white;
    [SerializeField] private Color occupiedCellBackgroundColor = Color.white;
    [SerializeField] private Color arrowBodyColor = Color.black;
    [SerializeField] private Color arrowTipColor = Color.black;
    [SerializeField] private bool usePerArrowColors = true;
    [SerializeField] private List<Color> arrowColorPalette = new List<Color>
    {
        new Color(0.75f, 0.75f, 0.75f, 1f),
        new Color(0.95f, 0.38f, 0.65f, 1f),
        new Color(0.55f, 0.82f, 0.35f, 1f),
        new Color(0.18f, 0.65f, 1f, 1f),
        new Color(1f, 0.78f, 0.25f, 1f)
    };

    [Header("Arrow Sprites")]
    [SerializeField] private bool useConnectedArrowSprites = true;
    [Tooltip("Triangle tip sprite. Draw it pointing Up by default, with its stem connected downward.")]
    [SerializeField] private Sprite arrowTipSprite;
    [Tooltip("Straight body sprite. Draw it horizontal by default, connecting left and right edges.")]
    [SerializeField] private Sprite arrowBodyStraightSprite;
    [Tooltip("Corner body sprite. Draw it connecting Up and Right by default.")]
    [SerializeField] private Sprite arrowBodyCornerSprite;
    [Tooltip("Optional tail cap sprite. Draw its connector pointing Up by default. If empty, the straight body sprite is used.")]
    [SerializeField] private Sprite arrowTailSprite;
    [Tooltip("Slightly above 1 helps adjacent body pieces overlap enough to hide tiny seams.")]
    [SerializeField] private float arrowSpriteScale = 1.18f;

    [Header("Continuous Arrow Rendering")]
    [SerializeField] private bool useContinuousArrowRendering = true;
    [SerializeField] private float arrowLineWidth = 0.22f;
    [SerializeField] private float arrowHeadLength = 0.42f;
    [SerializeField] private float arrowHeadWidth = 0.44f;
    [SerializeField] private int arrowCornerVertices = 10;
    [SerializeField] private int arrowCapVertices = 10;
    [SerializeField] private int arrowSortingOrder = 5;

    private readonly List<Tile> generatedTiles = new List<Tile>();
    private readonly List<GameObject> generatedArrowVisuals = new List<GameObject>();
    private Vector2 gridOrigin;
    private Dictionary<Vector2Int, ArrowVisualCellData> arrowVisualDataByPosition = new Dictionary<Vector2Int, ArrowVisualCellData>();
    private Material lineMaterial;

    private void Start()
    {
        EnsureDefaultArrowPalette();

        if (generateLevelOnStart)
        {
            GenerateLevelFromResources(startingLevelIndex);
        }
    }

    public void GenerateLevelFromResources(int levelIndex)
    {
        string resourcePath = $"{NormalizeResourceFolder(resourcesLevelsFolder)}/Level_{levelIndex}";
        TextAsset levelJson = Resources.Load<TextAsset>(resourcePath);

        if (levelJson == null)
        {
            Debug.LogError($"LevelGenerator could not load Resources/{resourcePath}.json.");
            return;
        }

        LevelData levelData = JsonUtility.FromJson<LevelData>(levelJson.text);
        if (levelData == null)
        {
            Debug.LogError($"LevelGenerator failed to deserialize Resources/{resourcePath}.json.");
            return;
        }

        GenerateLevel(levelData);
    }

    public void GenerateLevel(LevelData levelData)
    {
        ClearLevel();
        EnsureDefaultArrowPalette();

        if (levelData == null)
        {
            Debug.LogError("LevelGenerator.GenerateLevel failed: LevelData is null.");
            return;
        }

        if (!levelData.Validate())
        {
            Debug.LogError("LevelGenerator.GenerateLevel failed: LevelData is invalid.");
            return;
        }

        if (levelRoot == null)
        {
            GameObject rootObject = new GameObject("GeneratedLevelRoot");
            levelRoot = rootObject.transform;
        }

        gridOrigin = CalculateGridOrigin(levelData.width, levelData.height);
        GameManager.SetRuntimeGridLayout(cellSize, gridOrigin);
        GameManager.Initialize(levelData);
        arrowVisualDataByPosition = BuildArrowVisualLookup(levelData);

        for (int i = 0; i < levelData.vertices.Count; i++)
        {
            Tile tile = CreateTile(levelData.vertices[i]);
            ApplyVertexVisual(tile, levelData.vertices[i]);
            generatedTiles.Add(tile);
            GameManager.RegisterTile(tile.GridPosition, tile);
        }

        if (useContinuousArrowRendering)
        {
            CreateContinuousArrowVisuals(levelData);
        }

        CameraManager.Instance?.ConfigureCamera(levelData);
    }

    public void ClearLevel()
    {
        for (int i = generatedTiles.Count - 1; i >= 0; i--)
        {
            if (generatedTiles[i] != null)
            {
                generatedTiles[i].DestroySelf();
            }
        }

        generatedTiles.Clear();
        arrowVisualDataByPosition.Clear();

        for (int i = generatedArrowVisuals.Count - 1; i >= 0; i--)
        {
            if (generatedArrowVisuals[i] != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(generatedArrowVisuals[i]);
                }
                else
                {
                    DestroyImmediate(generatedArrowVisuals[i]);
                }
            }
        }

        generatedArrowVisuals.Clear();
        GameManager.ClearRuntimeState();
    }

    public Tile CreateTile(VertexData vertexData)
    {
        if (vertexData == null)
        {
            Debug.LogError("LevelGenerator.CreateTile failed: VertexData is null.");
            return null;
        }

        Vector3 worldPosition = new Vector3(
            gridOrigin.x + vertexData.x * cellSize,
            gridOrigin.y + vertexData.y * cellSize,
            0f);

        Tile tile;
        if (tilePrefab != null)
        {
            tile = Instantiate(tilePrefab, worldPosition, Quaternion.identity, levelRoot);
        }
        else
        {
            GameObject tileObject = new GameObject($"Tile_{vertexData.x}_{vertexData.y}");
            tileObject.transform.SetParent(levelRoot);
            tileObject.transform.position = worldPosition;
            tileObject.AddComponent<SpriteRenderer>();
            tileObject.AddComponent<BoxCollider2D>();
            tile = tileObject.AddComponent<Tile>();
        }

        tile.Initialize(vertexData);
        tile.SetDefaultScale(cellSize, runtimeTileFillScale);
        return tile;
    }

    public void ApplyVertexVisual(Tile tile, VertexData vertexData)
    {
        if (tile == null || vertexData == null)
        {
            Debug.LogWarning("LevelGenerator.ApplyVertexVisual ignored because tile or vertex data is missing.");
            return;
        }

        switch (vertexData.contentType)
        {
            case CellContentType.ArrowBody:
                if (useContinuousArrowRendering)
                {
                    tile.SetEmptyVisual(occupiedCellBackgroundColor);
                    break;
                }

                ApplyArrowOverlayOrFallback(tile, vertexData, false);
                break;
            case CellContentType.ArrowTip:
                if (useContinuousArrowRendering)
                {
                    tile.SetEmptyVisual(occupiedCellBackgroundColor);
                    break;
                }

                ApplyArrowOverlayOrFallback(tile, vertexData, true);
                break;
            default:
                tile.SetEmptyVisual(emptyColor);
                break;
        }
    }

    private Dictionary<Vector2Int, ArrowVisualCellData> BuildArrowVisualLookup(LevelData levelData)
    {
        Dictionary<Vector2Int, ArrowVisualCellData> lookup = new Dictionary<Vector2Int, ArrowVisualCellData>();
        if (levelData == null || levelData.arrows == null)
        {
            return lookup;
        }

        for (int arrowIndex = 0; arrowIndex < levelData.arrows.Count; arrowIndex++)
        {
            ArrowData arrow = levelData.arrows[arrowIndex];
            if (arrow == null || arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
            {
                continue;
            }

            for (int cellIndex = 0; cellIndex < arrow.occupiedCells.Count; cellIndex++)
            {
                GridPositionData cell = arrow.occupiedCells[cellIndex];
                if (cell == null)
                {
                    continue;
                }

                Vector2Int position = new Vector2Int(cell.x, cell.y);
                lookup[position] = CreateVisualCellData(arrow, cellIndex);
            }
        }

        return lookup;
    }

    private void CreateContinuousArrowVisuals(LevelData levelData)
    {
        if (levelData == null || levelData.arrows == null)
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

            CreateContinuousArrowVisual(arrow);
        }
    }

    private void CreateContinuousArrowVisual(ArrowData arrow)
    {
        GameObject arrowObject = new GameObject($"Arrow_{arrow.arrowId}_Visual");
        arrowObject.transform.SetParent(levelRoot);
        arrowObject.transform.localPosition = Vector3.zero;
        generatedArrowVisuals.Add(arrowObject);

        Color arrowColor = GetArrowColor(arrow.arrowId, false);
        LineRenderer lineRenderer = arrowObject.AddComponent<LineRenderer>();
        Material material = GetLineMaterial();
        if (material == null)
        {
            Debug.LogWarning("Continuous arrow rendering skipped because no line material could be created.");
            return;
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.material = material;
        lineRenderer.startColor = arrowColor;
        lineRenderer.endColor = arrowColor;
        lineRenderer.startWidth = arrowLineWidth * cellSize;
        lineRenderer.endWidth = arrowLineWidth * cellSize;
        lineRenderer.numCornerVertices = Mathf.Max(0, arrowCornerVertices);
        lineRenderer.numCapVertices = Mathf.Max(0, arrowCapVertices);
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.sortingOrder = arrowSortingOrder;

        int cellCount = arrow.occupiedCells.Count;
        lineRenderer.positionCount = cellCount;
        bool tipIsFirst = IsTipAtPathIndex(arrow, 0);
        for (int i = 0; i < cellCount; i++)
        {
            GridPositionData cell = tipIsFirst
                ? arrow.occupiedCells[cellCount - 1 - i]
                : arrow.occupiedCells[i];
            lineRenderer.SetPosition(i, GridToWorldPosition(cell.x, cell.y, -0.08f));
        }

        CreateArrowHead(arrow, arrowColor);
    }

    private void CreateArrowHead(ArrowData arrow, Color arrowColor)
    {
        if (arrow.tipCell == null || arrow.tipDirection == ArrowDirection.None)
        {
            return;
        }

        Vector2 direction = DirectionToVector2(arrow.tipDirection);
        if (direction.sqrMagnitude <= 0f)
        {
            return;
        }

        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        float headLength = arrowHeadLength * cellSize;
        float headWidth = arrowHeadWidth * cellSize;

        Vector3 tipWorld = GridToWorldPosition(arrow.tipCell.x, arrow.tipCell.y, -0.12f);
        Vector3 point = new Vector3(direction.x, direction.y, 0f) * (headLength * 0.45f);
        Vector3 baseCenter = -new Vector3(direction.x, direction.y, 0f) * (headLength * 0.15f);
        Vector3 left = baseCenter + new Vector3(perpendicular.x, perpendicular.y, 0f) * (headWidth * 0.5f);
        Vector3 right = baseCenter - new Vector3(perpendicular.x, perpendicular.y, 0f) * (headWidth * 0.5f);

        GameObject headObject = new GameObject($"Arrow_{arrow.arrowId}_Head");
        headObject.transform.SetParent(levelRoot);
        headObject.transform.position = tipWorld;
        generatedArrowVisuals.Add(headObject);

        Mesh mesh = new Mesh
        {
            name = $"Arrow_{arrow.arrowId}_HeadMesh"
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
        Material headMaterial = CreateColorMaterial(arrowColor);
        if (headMaterial == null)
        {
            return;
        }

        meshRenderer.sharedMaterial = headMaterial;
        meshRenderer.sortingOrder = arrowSortingOrder + 1;
    }

    private ArrowVisualCellData CreateVisualCellData(ArrowData arrow, int cellIndex)
    {
        Vector2Int current = ToVector2Int(arrow.occupiedCells[cellIndex]);

        if (IsTipAtPathIndex(arrow, cellIndex))
        {
            return new ArrowVisualCellData
            {
                pieceType = ArrowVisualPieceType.Tip,
                rotationZ = DirectionToRotationZ(arrow.tipDirection)
            };
        }

        int tipIndex = GetTipPathIndex(arrow);
        int previousIndex = GetPathNeighborIndexTowardTip(arrow, cellIndex, tipIndex);
        if (previousIndex < 0)
        {
            return new ArrowVisualCellData
            {
                pieceType = arrowTailSprite != null ? ArrowVisualPieceType.Tail : ArrowVisualPieceType.Straight,
                rotationZ = arrowTailSprite != null
                    ? DirectionToRotationZ(arrow.tipDirection)
                    : StraightRotationZ(DirectionToVector2Int(arrow.tipDirection))
            };
        }

        Vector2Int previous = ToVector2Int(arrow.occupiedCells[previousIndex]);
        int nextIndex = GetPathNeighborIndexAwayFromTip(arrow, cellIndex, tipIndex);

        if (nextIndex < 0)
        {
            Vector2Int connectionDirection = previous - current;
            return new ArrowVisualCellData
            {
                pieceType = arrowTailSprite != null ? ArrowVisualPieceType.Tail : ArrowVisualPieceType.Straight,
                rotationZ = arrowTailSprite != null
                    ? DirectionVectorToRotationZ(connectionDirection)
                    : StraightRotationZ(connectionDirection)
            };
        }

        Vector2Int next = ToVector2Int(arrow.occupiedCells[nextIndex]);
        Vector2Int directionToPrevious = previous - current;
        Vector2Int directionToNext = next - current;

        if (directionToPrevious.x == -directionToNext.x && directionToPrevious.y == -directionToNext.y)
        {
            return new ArrowVisualCellData
            {
                pieceType = ArrowVisualPieceType.Straight,
                rotationZ = StraightRotationZ(directionToPrevious)
            };
        }

        return new ArrowVisualCellData
        {
            pieceType = ArrowVisualPieceType.Corner,
            rotationZ = CornerRotationZ(directionToPrevious, directionToNext)
        };
    }

    private bool IsTipAtPathIndex(ArrowData arrow, int cellIndex)
    {
        if (arrow == null || arrow.tipCell == null || arrow.occupiedCells == null || cellIndex < 0 || cellIndex >= arrow.occupiedCells.Count)
        {
            return false;
        }

        GridPositionData cell = arrow.occupiedCells[cellIndex];
        return cell != null && cell.x == arrow.tipCell.x && cell.y == arrow.tipCell.y;
    }

    private int GetTipPathIndex(ArrowData arrow)
    {
        if (arrow == null || arrow.occupiedCells == null)
        {
            return -1;
        }

        for (int i = 0; i < arrow.occupiedCells.Count; i++)
        {
            if (IsTipAtPathIndex(arrow, i))
            {
                return i;
            }
        }

        return -1;
    }

    private int GetPathNeighborIndexTowardTip(ArrowData arrow, int cellIndex, int tipIndex)
    {
        if (arrow == null || arrow.occupiedCells == null || tipIndex < 0 || cellIndex == tipIndex)
        {
            return -1;
        }

        if (cellIndex < tipIndex)
        {
            return cellIndex + 1;
        }

        return cellIndex - 1;
    }

    private int GetPathNeighborIndexAwayFromTip(ArrowData arrow, int cellIndex, int tipIndex)
    {
        if (arrow == null || arrow.occupiedCells == null || tipIndex < 0 || cellIndex == tipIndex)
        {
            return -1;
        }

        if (cellIndex < tipIndex)
        {
            return cellIndex - 1;
        }

        return cellIndex + 1 < arrow.occupiedCells.Count ? cellIndex + 1 : -1;
    }

    private void ApplyArrowOverlayOrFallback(Tile tile, VertexData vertexData, bool isTip)
    {
        if (!useConnectedArrowSprites || !arrowVisualDataByPosition.TryGetValue(tile.GridPosition, out ArrowVisualCellData visualData))
        {
            if (isTip)
            {
                tile.SetArrowTipVisual(GetArrowColor(vertexData.arrowId, true), vertexData.tipDirection);
            }
            else
            {
                tile.SetArrowBodyVisual(GetArrowColor(vertexData.arrowId, false));
            }

            return;
        }

        Sprite sprite = GetSpriteForPiece(visualData.pieceType);
        if (sprite == null)
        {
            if (isTip)
            {
                tile.SetArrowTipVisual(GetArrowColor(vertexData.arrowId, true), vertexData.tipDirection);
            }
            else
            {
                tile.SetArrowBodyVisual(GetArrowColor(vertexData.arrowId, false));
            }

            return;
        }

        Color arrowColor = GetArrowColor(vertexData.arrowId, isTip);
        tile.SetArrowOverlayVisual(sprite, occupiedCellBackgroundColor, arrowColor, visualData.rotationZ, arrowSpriteScale);
    }

    private Color GetArrowColor(int arrowId, bool isTip)
    {
        EnsureDefaultArrowPalette();

        if (!usePerArrowColors || arrowColorPalette == null || arrowColorPalette.Count == 0)
        {
            return isTip ? arrowTipColor : arrowBodyColor;
        }

        int colorIndex = Mathf.Abs(arrowId - 1) % arrowColorPalette.Count;
        return arrowColorPalette[colorIndex];
    }

    private void EnsureDefaultArrowPalette()
    {
        if (arrowColorPalette == null)
        {
            arrowColorPalette = new List<Color>();
        }

        if (arrowColorPalette.Count > 0)
        {
            return;
        }

        arrowColorPalette.Add(new Color(0.75f, 0.75f, 0.75f, 1f));
        arrowColorPalette.Add(new Color(0.95f, 0.38f, 0.65f, 1f));
        arrowColorPalette.Add(new Color(0.55f, 0.82f, 0.35f, 1f));
        arrowColorPalette.Add(new Color(0.18f, 0.65f, 1f, 1f));
        arrowColorPalette.Add(new Color(1f, 0.78f, 0.25f, 1f));
    }

    private Sprite GetSpriteForPiece(ArrowVisualPieceType pieceType)
    {
        switch (pieceType)
        {
            case ArrowVisualPieceType.Tip:
                return arrowTipSprite;
            case ArrowVisualPieceType.Corner:
                return arrowBodyCornerSprite;
            case ArrowVisualPieceType.Tail:
                return arrowTailSprite != null ? arrowTailSprite : arrowBodyStraightSprite;
            default:
                return arrowBodyStraightSprite;
        }
    }

    private static Vector2Int ToVector2Int(GridPositionData position)
    {
        return new Vector2Int(position.x, position.y);
    }

    private static float StraightRotationZ(Vector2Int connectionDirection)
    {
        return connectionDirection.x != 0 ? 0f : 90f;
    }

    private static float CornerRotationZ(Vector2Int firstDirection, Vector2Int secondDirection)
    {
        bool connectsUp = firstDirection == Vector2Int.up || secondDirection == Vector2Int.up;
        bool connectsRight = firstDirection == Vector2Int.right || secondDirection == Vector2Int.right;
        bool connectsDown = firstDirection == Vector2Int.down || secondDirection == Vector2Int.down;
        bool connectsLeft = firstDirection == Vector2Int.left || secondDirection == Vector2Int.left;

        if (connectsUp && connectsRight)
        {
            return 0f;
        }

        if (connectsRight && connectsDown)
        {
            return -90f;
        }

        if (connectsDown && connectsLeft)
        {
            return 180f;
        }

        if (connectsLeft && connectsUp)
        {
            return 90f;
        }

        return 0f;
    }

    private static float DirectionToRotationZ(ArrowDirection direction)
    {
        switch (direction)
        {
            case ArrowDirection.Up:
                return 0f;
            case ArrowDirection.Right:
                return -90f;
            case ArrowDirection.Down:
                return 180f;
            case ArrowDirection.Left:
                return 90f;
            default:
                return 0f;
        }
    }

    private static float DirectionVectorToRotationZ(Vector2Int direction)
    {
        if (direction == Vector2Int.up)
        {
            return 0f;
        }

        if (direction == Vector2Int.right)
        {
            return -90f;
        }

        if (direction == Vector2Int.down)
        {
            return 180f;
        }

        if (direction == Vector2Int.left)
        {
            return 90f;
        }

        return 0f;
    }

    private Vector3 GridToWorldPosition(int x, int y, float z)
    {
        return new Vector3(
            gridOrigin.x + x * cellSize,
            gridOrigin.y + y * cellSize,
            z);
    }

    private Material GetLineMaterial()
    {
        if (lineMaterial != null)
        {
            return lineMaterial;
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
            Debug.LogWarning("LevelGenerator could not find a shader for continuous arrow lines.");
            return null;
        }

        lineMaterial = new Material(shader)
        {
            name = "Generated_ArrowLineMaterial"
        };

        return lineMaterial;
    }

    private Material CreateColorMaterial(Color color)
    {
        Material baseMaterial = GetLineMaterial();
        if (baseMaterial == null)
        {
            return null;
        }

        Material material = new Material(baseMaterial)
        {
            name = "Generated_ArrowHeadMaterial"
        };

        material.color = color;
        return material;
    }

    private static Vector2 DirectionToVector2(ArrowDirection direction)
    {
        switch (direction)
        {
            case ArrowDirection.Up:
                return Vector2.up;
            case ArrowDirection.Down:
                return Vector2.down;
            case ArrowDirection.Left:
                return Vector2.left;
            case ArrowDirection.Right:
                return Vector2.right;
            default:
                return Vector2.zero;
        }
    }

    private static Vector2Int DirectionToVector2Int(ArrowDirection direction)
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
                return Vector2Int.up;
        }
    }

    private Vector2 CalculateGridOrigin(int width, int height)
    {
        return new Vector2(
            -(width - 1) * cellSize * 0.5f,
            -(height - 1) * cellSize * 0.5f);
    }

    private static string NormalizeResourceFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return "Levels";
        }

        return folder.Trim().Trim('/').Trim('\\').Replace("\\", "/");
    }

    private enum ArrowVisualPieceType
    {
        Tip,
        Straight,
        Corner,
        Tail
    }

    private struct ArrowVisualCellData
    {
        public ArrowVisualPieceType pieceType;
        public float rotationZ;
    }
}
