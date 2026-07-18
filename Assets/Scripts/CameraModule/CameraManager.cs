using UnityEngine;

public class CameraManager : Singleton<CameraManager>
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float defaultCellSize = 1f;
    [SerializeField] private float gridPadding = 1.25f;
    [SerializeField] private float minimumWorldPadding = 0.75f;

    public void ConfigureCamera(LevelData levelData)
    {
        ConfigureCamera(levelData, defaultCellSize);
    }

    public void ConfigureCamera(LevelData levelData, float cellSize)
    {
        if (levelData == null)
        {
            Debug.LogWarning("CameraManager.ConfigureCamera ignored: LevelData is null.");
            return;
        }

        CenterOnGrid(levelData.width, levelData.height, cellSize);
        SetOrthographicSizeForGrid(levelData.width, levelData.height, cellSize);
    }

    public void ConfigureCameraForBounds(Vector2 center, int widthInCells, int heightInCells, float cellSize)
    {
        Camera camera = GetTargetCamera();
        if (camera == null)
        {
            Debug.LogWarning("CameraManager.ConfigureCameraForBounds failed: no camera is available.");
            return;
        }

        ConfigureCameraForBounds(camera, center, widthInCells, heightInCells, cellSize, gridPadding, minimumWorldPadding);
    }

    public static bool TryConfigureMainCamera(LevelData levelData, float cellSize)
    {
        Camera camera = Camera.main;
        if (camera == null || levelData == null)
        {
            return false;
        }

        ConfigureCameraForGrid(camera, levelData.width, levelData.height, cellSize, 1.25f, 0.75f);
        return true;
    }

    public static bool TryConfigureMainCameraForBounds(Vector2 center, int widthInCells, int heightInCells, float cellSize)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        ConfigureCameraForBounds(camera, center, widthInCells, heightInCells, cellSize, 1.25f, 0.75f);
        return true;
    }

    public void CenterOnGrid(int width, int height, float cellSize)
    {
        Camera camera = GetTargetCamera();
        if (camera == null)
        {
            Debug.LogWarning("CameraManager.CenterOnGrid failed: no camera is available.");
            return;
        }

        Vector3 position = camera.transform.position;
        position.x = 0f;
        position.y = 0f;
        camera.transform.position = position;
    }

    public void SetOrthographicSizeForGrid(int width, int height, float cellSize)
    {
        Camera camera = GetTargetCamera();
        if (camera == null)
        {
            Debug.LogWarning("CameraManager.SetOrthographicSizeForGrid failed: no camera is available.");
            return;
        }

        camera.orthographic = true;
        ConfigureCameraForGrid(camera, width, height, cellSize, gridPadding, minimumWorldPadding);
    }

    public void Pan(Vector2 direction, float speed)
    {
        Camera camera = GetTargetCamera();
        if (camera == null || direction.sqrMagnitude <= 0f)
        {
            return;
        }

        camera.transform.position += new Vector3(direction.x, direction.y, 0f) * speed * Time.deltaTime;
    }

    public void Zoom(float scrollValue, float zoomSpeed, float minSize, float maxSize)
    {
        Camera camera = GetTargetCamera();
        if (camera == null || !camera.orthographic)
        {
            return;
        }

        float nextSize = camera.orthographicSize - scrollValue * zoomSpeed;
        camera.orthographicSize = Mathf.Clamp(nextSize, minSize, maxSize);
    }

    private Camera GetTargetCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        return targetCamera;
    }

    private static void ConfigureCameraForGrid(Camera camera, int width, int height, float cellSize, float paddingCells, float minWorldPadding)
    {
        if (camera == null)
        {
            return;
        }

        camera.orthographic = true;

        Vector3 position = camera.transform.position;
        position.x = 0f;
        position.y = 0f;
        camera.transform.position = position;

        float safeCellSize = Mathf.Max(0.01f, cellSize);
        float aspect = Mathf.Max(0.01f, camera.aspect);
        float safePadding = Mathf.Max(Mathf.Max(0f, paddingCells) * safeCellSize, Mathf.Max(0f, minWorldPadding));
        float worldWidth = Mathf.Max(1, width) * safeCellSize + safePadding * 2f;
        float worldHeight = Mathf.Max(1, height) * safeCellSize + safePadding * 2f;
        float sizeForHeight = worldHeight * 0.5f;
        float sizeForWidth = worldWidth * 0.5f / aspect;
        camera.orthographicSize = Mathf.Max(sizeForHeight, sizeForWidth);
    }

    private static void ConfigureCameraForBounds(
        Camera camera,
        Vector2 center,
        int widthInCells,
        int heightInCells,
        float cellSize,
        float paddingCells,
        float minWorldPadding)
    {
        if (camera == null)
        {
            return;
        }

        camera.orthographic = true;

        Vector3 position = camera.transform.position;
        position.x = center.x;
        position.y = center.y;
        camera.transform.position = position;

        float safeCellSize = Mathf.Max(0.01f, cellSize);
        float aspect = Mathf.Max(0.01f, camera.aspect);
        float safePadding = Mathf.Max(Mathf.Max(0f, paddingCells) * safeCellSize, Mathf.Max(0f, minWorldPadding));
        float worldWidth = Mathf.Max(1, widthInCells) * safeCellSize + safePadding * 2f;
        float worldHeight = Mathf.Max(1, heightInCells) * safeCellSize + safePadding * 2f;
        float sizeForHeight = worldHeight * 0.5f;
        float sizeForWidth = worldWidth * 0.5f / aspect;
        camera.orthographicSize = Mathf.Max(sizeForHeight, sizeForWidth);
    }
}
