using UnityEngine;

public class CameraManager : Singleton<CameraManager>
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float defaultCellSize = 1f;
    [SerializeField] private float gridPadding = 1f;

    public void ConfigureCamera(LevelData levelData)
    {
        if (levelData == null)
        {
            Debug.LogWarning("CameraManager.ConfigureCamera ignored: LevelData is null.");
            return;
        }

        CenterOnGrid(levelData.width, levelData.height, defaultCellSize);
        SetOrthographicSizeForGrid(levelData.width, levelData.height, defaultCellSize);
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
        float safeCellSize = Mathf.Max(0.01f, cellSize);
        float aspect = Mathf.Max(0.01f, camera.aspect);
        float worldWidth = width * safeCellSize;
        float worldHeight = height * safeCellSize;
        float sizeForHeight = worldHeight * 0.5f + gridPadding;
        float sizeForWidth = (worldWidth * 0.5f + gridPadding) / aspect;
        camera.orthographicSize = Mathf.Max(sizeForHeight, sizeForWidth);
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
}
