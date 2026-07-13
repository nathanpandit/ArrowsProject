using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    [SerializeField] private bool autoDetectLevelEditor = true;
    [SerializeField] private bool routeToLevelEditor;
    [SerializeField] private Camera inputCamera;

    private bool leftMouseWasPressed;
    private bool rightMouseWasPressed;
    private bool primaryTouchWasPressed;

    private void Awake()
    {
        if (inputCamera == null)
        {
            inputCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (inputCamera == null)
        {
            inputCamera = Camera.main;
            if (inputCamera == null)
            {
                Debug.LogWarning("InputManager has no camera available for screen-to-world conversion.");
                return;
            }
        }

        bool editorModeActive = routeToLevelEditor;
        if (autoDetectLevelEditor)
        {
            editorModeActive = LevelEditor.ActiveInstance != null;
        }

        if (editorModeActive && LevelEditor.ActiveInstance != null)
        {
            RouteLevelEditorInput();
            return;
        }

        RouteGameplayInput();
    }

    private void RouteGameplayInput()
    {
        if (HandleGameplayLevelShortcuts())
        {
            return;
        }

        bool handledPrimaryInput = false;

        if (WasPrimaryTouchPressedThisFrame())
        {
            GameManager.HandlePrimaryInput(ScreenToWorld(Touchscreen.current.primaryTouch.position.ReadValue()));
            handledPrimaryInput = true;
        }

        if (!handledPrimaryInput && WasLeftMousePressedThisFrame())
        {
            GameManager.HandlePrimaryInput(ScreenToWorld(Mouse.current.position.ReadValue()));
        }
    }

    private bool HandleGameplayLevelShortcuts()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        LevelGenerator levelGenerator = LevelGenerator.ActiveInstance;
        if (levelGenerator == null)
        {
            return false;
        }

        if (keyboard.nKey.wasPressedThisFrame)
        {
            levelGenerator.GenerateNextLevel();
            return true;
        }

        if (keyboard.pKey.wasPressedThisFrame)
        {
            levelGenerator.GeneratePreviousLevel();
            return true;
        }

        return false;
    }

    private void RouteLevelEditorInput()
    {
        LevelEditor levelEditor = LevelEditor.ActiveInstance;
        bool handledPrimaryInput = false;

        if (levelEditor.HasDisplayedGeneratedLevel)
        {
            if (WasPrimaryTouchPressedThisFrame())
            {
                levelEditor.HandlePrimaryPaintInput(ScreenToWorld(Touchscreen.current.primaryTouch.position.ReadValue()));
                handledPrimaryInput = true;
            }

            if (!handledPrimaryInput && WasLeftMousePressedThisFrame())
            {
                levelEditor.HandlePrimaryPaintInput(ScreenToWorld(Mouse.current.position.ReadValue()));
            }

            if (WasRightMousePressedThisFrame())
            {
                levelEditor.HandleSecondaryPaintInput(ScreenToWorld(Mouse.current.position.ReadValue()));
            }
        }
        else
        {
            if (IsPrimaryTouchPressed())
            {
                levelEditor.HandlePrimaryPaintInput(ScreenToWorld(Touchscreen.current.primaryTouch.position.ReadValue()));
                handledPrimaryInput = true;
            }

            bool leftMousePressed = IsLeftMousePressed();
            bool rightMousePressed = IsRightMousePressed();

            if (!handledPrimaryInput && leftMousePressed)
            {
                levelEditor.HandlePrimaryPaintInput(ScreenToWorld(Mouse.current.position.ReadValue()));
            }

            if (rightMousePressed)
            {
                levelEditor.HandleSecondaryPaintInput(ScreenToWorld(Mouse.current.position.ReadValue()));
            }

            if (!handledPrimaryInput && !leftMousePressed && !rightMousePressed)
            {
                levelEditor.EndPaintDrag();
            }
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.gKey.wasPressedThisFrame)
        {
            levelEditor.HandleGenerateShortcut();
        }

        if (keyboard != null && keyboard.sKey.wasPressedThisFrame)
        {
            levelEditor.HandleSaveShortcut();
        }

        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            levelEditor.HandleResetDesignedLevelShortcut();
        }

        Vector2 panDirection = Vector2.zero;
        if (keyboard != null && keyboard.upArrowKey.isPressed)
        {
            panDirection += Vector2.up;
        }

        if (keyboard != null && keyboard.downArrowKey.isPressed)
        {
            panDirection += Vector2.down;
        }

        if (keyboard != null && keyboard.leftArrowKey.isPressed)
        {
            panDirection += Vector2.left;
        }

        if (keyboard != null && keyboard.rightArrowKey.isPressed)
        {
            panDirection += Vector2.right;
        }

        if (panDirection.sqrMagnitude > 0f)
        {
            levelEditor.HandleCameraPanInput(panDirection.normalized);
        }

        if (Mouse.current != null)
        {
            float scrollValue = Mouse.current.scroll.ReadValue().y;
            if (!Mathf.Approximately(scrollValue, 0f))
            {
                levelEditor.HandleCameraZoomInput(scrollValue);
            }
        }
    }

    private Vector2 ScreenToWorld(Vector2 screenPosition)
    {
        Vector3 screenPoint = new Vector3(screenPosition.x, screenPosition.y, -inputCamera.transform.position.z);
        return inputCamera.ScreenToWorldPoint(screenPoint);
    }

    private bool WasLeftMousePressedThisFrame()
    {
        if (Mouse.current == null)
        {
            leftMouseWasPressed = false;
            return false;
        }

        bool isPressed = Mouse.current.leftButton.isPressed;
        bool wasPressedThisFrame = isPressed && !leftMouseWasPressed;
        leftMouseWasPressed = isPressed;
        return wasPressedThisFrame;
    }

    private bool IsLeftMousePressed()
    {
        if (Mouse.current == null)
        {
            leftMouseWasPressed = false;
            return false;
        }

        bool isPressed = Mouse.current.leftButton.isPressed;
        leftMouseWasPressed = isPressed;
        return isPressed;
    }

    private bool WasRightMousePressedThisFrame()
    {
        if (Mouse.current == null)
        {
            rightMouseWasPressed = false;
            return false;
        }
        bool isPressed = Mouse.current.rightButton.isPressed;
        bool wasPressedThisFrame = isPressed && !rightMouseWasPressed;
        rightMouseWasPressed = isPressed;
        return wasPressedThisFrame;
    }

    private bool IsRightMousePressed()
    {
        if (Mouse.current == null)
        {
            rightMouseWasPressed = false;
            return false;
        }

        bool isPressed = Mouse.current.rightButton.isPressed;
        rightMouseWasPressed = isPressed;
        return isPressed;
    }

    private bool WasPrimaryTouchPressedThisFrame()
    {
        if (Touchscreen.current == null)
        {
            primaryTouchWasPressed = false;
            return false;
        }

        bool isPressed = Touchscreen.current.primaryTouch.press.isPressed;
        bool wasPressedThisFrame = isPressed && !primaryTouchWasPressed;
        primaryTouchWasPressed = isPressed;
        return wasPressedThisFrame;
    }

    private bool IsPrimaryTouchPressed()
    {
        if (Touchscreen.current == null)
        {
            primaryTouchWasPressed = false;
            return false;
        }

        bool isPressed = Touchscreen.current.primaryTouch.press.isPressed;
        primaryTouchWasPressed = isPressed;
        return isPressed;
    }
}
