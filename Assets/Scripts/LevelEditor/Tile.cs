using UnityEngine;

public class Tile : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private SpriteRenderer arrowSpriteRenderer;
    [SerializeField] private Material arrowOverlayMaterial;
    [SerializeField] private Collider2D tileCollider;
    [SerializeField] private Transform arrowDirectionTransform;

    private static Sprite defaultSprite;
    private static Material generatedArrowOverlayMaterial;
    private VertexData vertexData;

    public Vector2Int GridPosition { get; private set; }
    public VertexData VertexData => vertexData;

    private void Awake()
    {
        EnsureReferences();
    }

    public void Initialize(Vector2Int gridPosition)
    {
        EnsureReferences();
        GridPosition = gridPosition;
        vertexData = null;
        gameObject.name = $"Tile_{gridPosition.x}_{gridPosition.y}";
    }

    public void Initialize(VertexData vertexData)
    {
        if (vertexData == null)
        {
            Debug.LogWarning("Tile.Initialize received null VertexData.");
            return;
        }

        EnsureReferences();
        this.vertexData = vertexData;
        GridPosition = new Vector2Int(vertexData.x, vertexData.y);
        gameObject.name = $"Tile_{vertexData.x}_{vertexData.y}";
    }

    public void SetEmptyVisual(Color color)
    {
        EnsureReferences();
        spriteRenderer.color = color;
        HideArrowOverlay();
    }

    public void SetPaintedVisual(Color color)
    {
        EnsureReferences();
        spriteRenderer.color = color;
        HideArrowOverlay();
    }

    public void SetArrowBodyVisual(Color color)
    {
        EnsureReferences();
        spriteRenderer.color = color;
        HideArrowOverlay();
    }

    public void SetArrowTipVisual(Color color, ArrowDirection direction)
    {
        EnsureReferences();
        spriteRenderer.color = color;
        HideArrowOverlay();

        if (arrowDirectionTransform != null)
        {
            arrowDirectionTransform.localRotation = DirectionToRotation(direction);
        }
    }

    public void SetArrowOverlayVisual(Sprite sprite, Color backgroundColor, Color arrowColor, float rotationZ, float scaleMultiplier)
    {
        EnsureReferences();
        spriteRenderer.color = backgroundColor;

        if (sprite == null)
        {
            HideArrowOverlay();
            return;
        }

        EnsureArrowRenderer();
        arrowSpriteRenderer.gameObject.SetActive(true);
        arrowSpriteRenderer.sprite = sprite;
        arrowSpriteRenderer.color = arrowColor;
        arrowSpriteRenderer.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        arrowSpriteRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
        arrowSpriteRenderer.transform.localScale = Vector3.one * Mathf.Max(0.01f, scaleMultiplier);
    }

    public void HideArrowOverlay()
    {
        if (arrowSpriteRenderer != null)
        {
            arrowSpriteRenderer.gameObject.SetActive(false);
        }
    }

    public void SetSelectedVisual()
    {
        EnsureReferences();
        spriteRenderer.color = Color.yellow;
    }

    public void SetDefaultScale(float cellSize)
    {
        SetDefaultScale(cellSize, 0.95f);
    }

    public void SetDefaultScale(float cellSize, float fillScale)
    {
        float safeSize = Mathf.Max(0.01f, cellSize);
        float safeFillScale = Mathf.Max(0.01f, fillScale);
        transform.localScale = new Vector3(safeSize * safeFillScale, safeSize * safeFillScale, 1f);

        if (tileCollider is BoxCollider2D boxCollider)
        {
            boxCollider.size = Vector2.one;
        }
    }

    public void DestroySelf()
    {
        if (Application.isPlaying)
        {
            Destroy(gameObject);
        }
        else
        {
            DestroyImmediate(gameObject);
        }
    }

    private void EnsureReferences()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }
        }

        if (spriteRenderer.sprite == null)
        {
            spriteRenderer.sprite = GetDefaultSprite();
        }

        if (tileCollider == null)
        {
            tileCollider = GetComponent<Collider2D>();
            if (tileCollider == null)
            {
                tileCollider = gameObject.AddComponent<BoxCollider2D>();
            }
        }
    }

    private void EnsureArrowRenderer()
    {
        if (arrowSpriteRenderer != null)
        {
            ApplyArrowOverlayMaterial();
            return;
        }

        Transform arrowTransform = transform.Find("ArrowVisual");
        if (arrowTransform == null)
        {
            GameObject arrowObject = new GameObject("ArrowVisual");
            arrowObject.transform.SetParent(transform);
            arrowObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            arrowObject.transform.localRotation = Quaternion.identity;
            arrowObject.transform.localScale = Vector3.one;
            arrowTransform = arrowObject.transform;
        }

        arrowSpriteRenderer = arrowTransform.GetComponent<SpriteRenderer>();
        if (arrowSpriteRenderer == null)
        {
            arrowSpriteRenderer = arrowTransform.gameObject.AddComponent<SpriteRenderer>();
        }

        arrowSpriteRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        arrowSpriteRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;
        ApplyArrowOverlayMaterial();
    }

    private void ApplyArrowOverlayMaterial()
    {
        Material material = GetArrowOverlayMaterial();
        if (material != null)
        {
            arrowSpriteRenderer.sharedMaterial = material;
        }
    }

    private Material GetArrowOverlayMaterial()
    {
        if (arrowOverlayMaterial != null)
        {
            return arrowOverlayMaterial;
        }

        if (generatedArrowOverlayMaterial != null)
        {
            return generatedArrowOverlayMaterial;
        }

        Shader shader = Shader.Find("ArrowsPuzzle/AlphaTintSprite");
        if (shader == null)
        {
            return null;
        }

        generatedArrowOverlayMaterial = new Material(shader)
        {
            name = "Generated_AlphaTintSprite"
        };

        return generatedArrowOverlayMaterial;
    }

    private static Sprite GetDefaultSprite()
    {
        if (defaultSprite != null)
        {
            return defaultSprite;
        }

        Texture2D texture = Texture2D.whiteTexture;
        defaultSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            texture.width);

        return defaultSprite;
    }

    private static Quaternion DirectionToRotation(ArrowDirection direction)
    {
        switch (direction)
        {
            case ArrowDirection.Up:
                return Quaternion.Euler(0f, 0f, 0f);
            case ArrowDirection.Right:
                return Quaternion.Euler(0f, 0f, -90f);
            case ArrowDirection.Down:
                return Quaternion.Euler(0f, 0f, 180f);
            case ArrowDirection.Left:
                return Quaternion.Euler(0f, 0f, 90f);
            default:
                return Quaternion.identity;
        }
    }
}
