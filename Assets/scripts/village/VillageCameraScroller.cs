using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class VillageCameraScroller : MonoBehaviour
{
    private const string VillageSceneName = "Village";

    private static VillageCameraScroller instance;
    private static bool sceneHookRegistered;

    [Header("Camera")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Transform contentRoot;
    [SerializeField] private Transform topLimit;
    [SerializeField] private Transform bottomLimit;

    [Header("Buttons")]
    [SerializeField] private Button moveToTopButton;
    [SerializeField] private Button moveToBottomButton;

    [Header("Drag")]
    [SerializeField] private float dragThresholdPixels = 18f;
    [SerializeField] private float interactableDragThresholdPixels = 42f;
    [SerializeField] private float buttonStepNormalized = 1f;

    [Header("Release Motion")]
    [SerializeField] private float inertiaDamping = 5.5f;
    [SerializeField] private float maxInertiaSpeed = 22f;
    [SerializeField] private float minReleaseDragNormalized = 0.1f;
    [SerializeField] private float releaseVelocityBlend = 0.45f;

    [Header("Smoothing")]
    [SerializeField] private float buttonMoveSpeed = 10f;

    private bool pointerActive;
    private bool draggingCamera;
    private bool blockedByUi;
    private bool moveToTargetActive;
    private Vector2 pointerDownScreenPosition;
    private Vector2 lastScreenPosition;
    private float inertialVelocityY;
    private float accumulatedDragWorldY;
    private float targetCameraY;
    private float initialCameraY;

    public static VillageCameraScroller Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        if (sceneHookRegistered)
            return;

        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneHookRegistered = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureVillageScrollerExistsAfterLoad()
    {
        EnsureVillageScrollerExists();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid())
            return;

        if (scene.name != VillageSceneName)
        {
            instance = null;
            VillagePointerCapture.ReleaseAll();
            return;
        }

        EnsureVillageScrollerExists();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private static void EnsureVillageScrollerExists()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.name != VillageSceneName)
            return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        VillageCameraScroller[] scrollers = mainCamera.GetComponents<VillageCameraScroller>();
        VillageCameraScroller scroller = scrollers.Length > 0 ? scrollers[0] : mainCamera.gameObject.AddComponent<VillageCameraScroller>();
        for (int i = 1; i < scrollers.Length; i++)
            Destroy(scrollers[i]);

        scroller.worldCamera = mainCamera;
        scroller.contentRoot = GameObject.Find("VillageWorldRoot")?.transform;
        scroller.ForceReinitialize();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ForceReinitialize();
    }

    private void OnEnable()
    {
        BindButtons();
    }

    private void OnDisable()
    {
        VillagePointerCapture.ReleaseAll();
        UnbindButtons();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;

        if (worldCamera == null)
            return;

        if (!TryGetPrimaryPointer(out Vector2 screenPosition, out bool isPressed, out bool pressedThisFrame, out bool releasedThisFrame))
        {
            ApplyIdleMotion();
            return;
        }

        if (pressedThisFrame)
            BeginPointer(screenPosition);

        if (pointerActive && isPressed)
            UpdatePointer(screenPosition);

        if (releasedThisFrame)
            EndPointer();

        if (!pointerActive)
            ApplyIdleMotion();
    }

    public void MoveToTop()
    {
        MoveToNormalized(0f);
    }

    public void MoveToBottom()
    {
        MoveToNormalized(1f);
    }

    public void MoveByButtonStep(bool moveTowardBottom)
    {
        float range = GetCameraMaxY() - GetCameraMinY();
        if (range <= 0.001f)
            return;

        float currentNormalized = Mathf.InverseLerp(GetCameraMinY(), GetCameraMaxY(), worldCamera.transform.position.y);
        float delta = Mathf.Abs(buttonStepNormalized);
        float nextNormalized = moveTowardBottom ? currentNormalized - delta : currentNormalized + delta;
        MoveToNormalized(1f - nextNormalized);
    }

    public void ResetToDefaultPosition()
    {
        if (worldCamera == null)
            return;

        VillagePointerCapture.ReleaseAll();
        pointerActive = false;
        draggingCamera = false;
        blockedByUi = false;
        moveToTargetActive = false;
        inertialVelocityY = 0f;
        accumulatedDragWorldY = 0f;
        targetCameraY = initialCameraY;
        SetCameraY(Mathf.Clamp(initialCameraY, GetCameraMinY(), GetCameraMaxY()));
    }

    public static void ResetActiveToDefaultPosition()
    {
        if (instance != null)
            instance.ResetToDefaultPosition();
    }

    public void ForceReinitialize()
    {
        VillagePointerCapture.ReleaseAll();
        pointerActive = false;
        draggingCamera = false;
        blockedByUi = false;
        moveToTargetActive = false;
        inertialVelocityY = 0f;
        accumulatedDragWorldY = 0f;

        if (worldCamera == null)
            worldCamera = Camera.main;

        if (contentRoot == null)
            contentRoot = ResolveContentRoot();

        if (worldCamera != null)
        {
            initialCameraY = worldCamera.transform.position.y;
            targetCameraY = initialCameraY;
        }

        BindButtons();
        SnapCameraWithinBounds();
    }

    private void BeginPointer(Vector2 screenPosition)
    {
        pointerActive = true;
        draggingCamera = false;
        pointerDownScreenPosition = screenPosition;
        lastScreenPosition = screenPosition;
        accumulatedDragWorldY = 0f;
        moveToTargetActive = false;
        blockedByUi = IsPointerOverUi();
        inertialVelocityY = 0f;
    }

    private void UpdatePointer(Vector2 screenPosition)
    {
        if (blockedByUi)
        {
            lastScreenPosition = screenPosition;
            return;
        }

        if (VillagePointerCapture.HasActiveCapture)
        {
            draggingCamera = false;
            inertialVelocityY = 0f;
            accumulatedDragWorldY = 0f;
            lastScreenPosition = screenPosition;
            return;
        }

        float dragDistance = Mathf.Abs(screenPosition.y - pointerDownScreenPosition.y);
        float requiredThreshold = dragThresholdPixels;

        if (!draggingCamera)
        {
            if (dragDistance < requiredThreshold)
            {
                lastScreenPosition = screenPosition;
                return;
            }

            draggingCamera = true;
        }

        float deltaScreenY = screenPosition.y - lastScreenPosition.y;
        if (Mathf.Approximately(deltaScreenY, 0f))
            return;

        float deltaWorldY = -GetWorldDeltaFromScreenDelta(deltaScreenY);
        float appliedDelta = MoveCameraBy(deltaWorldY);
        float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        float instantVelocity = appliedDelta / deltaTime;
        inertialVelocityY = Mathf.Lerp(inertialVelocityY, instantVelocity, releaseVelocityBlend);
        accumulatedDragWorldY += appliedDelta;
        lastScreenPosition = screenPosition;
    }

    private void EndPointer()
    {
        if (draggingCamera && !blockedByUi)
            ApplyReleaseMomentum();

        pointerActive = false;
        draggingCamera = false;
        blockedByUi = false;
        accumulatedDragWorldY = 0f;
    }

    private void ApplyReleaseMomentum()
    {
        float direction = Mathf.Sign(inertialVelocityY);
        if (Mathf.Abs(direction) < 0.001f)
            direction = Mathf.Sign(accumulatedDragWorldY);

        if (Mathf.Abs(direction) < 0.001f)
            return;

        float cameraRange = Mathf.Max(0.001f, GetCameraMaxY() - GetCameraMinY());
        float dragNormalized = Mathf.Abs(accumulatedDragWorldY) / cameraRange;
        if (dragNormalized <= minReleaseDragNormalized)
        {
            inertialVelocityY = 0f;
            return;
        }

        float releaseNormalized = Mathf.InverseLerp(minReleaseDragNormalized, 1f, Mathf.Clamp01(dragNormalized));
        float releaseStrength = Mathf.SmoothStep(0f, 1f, releaseNormalized);
        inertialVelocityY = direction * (maxInertiaSpeed * releaseStrength);
    }

    private void ApplyIdleMotion()
    {
        if (moveToTargetActive)
        {
            float nextY = Mathf.MoveTowards(worldCamera.transform.position.y, targetCameraY, buttonMoveSpeed * Time.unscaledDeltaTime);
            SetCameraY(nextY);
            if (Mathf.Abs(worldCamera.transform.position.y - targetCameraY) <= 0.001f)
                moveToTargetActive = false;
            return;
        }

        if (Mathf.Abs(inertialVelocityY) <= 0.001f)
        {
            inertialVelocityY = 0f;
            return;
        }

        float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        float clampedVelocity = Mathf.Clamp(inertialVelocityY, -maxInertiaSpeed, maxInertiaSpeed);
        float appliedDelta = MoveCameraBy(clampedVelocity * deltaTime);

        if (Mathf.Abs(appliedDelta) <= 0.0001f)
        {
            inertialVelocityY = 0f;
            return;
        }

        inertialVelocityY = Mathf.Lerp(clampedVelocity, 0f, deltaTime * Mathf.Max(0.01f, inertiaDamping));
    }

    private void MoveToNormalized(float normalizedFromTop)
    {
        if (worldCamera == null)
            return;

        float normalizedFromBottom = 1f - Mathf.Clamp01(normalizedFromTop);
        targetCameraY = Mathf.Lerp(GetCameraMinY(), GetCameraMaxY(), normalizedFromBottom);
        targetCameraY = Mathf.Clamp(targetCameraY, GetCameraMinY(), GetCameraMaxY());
        moveToTargetActive = true;
        inertialVelocityY = 0f;
    }

    private float MoveCameraBy(float deltaY)
    {
        float currentY = worldCamera.transform.position.y;
        float nextY = Mathf.Clamp(currentY + deltaY, GetCameraMinY(), GetCameraMaxY());
        SetCameraY(nextY);
        return nextY - currentY;
    }

    private void SetCameraY(float y)
    {
        Vector3 position = worldCamera.transform.position;
        position.y = y;
        worldCamera.transform.position = position;
    }

    private void SnapCameraWithinBounds()
    {
        if (worldCamera == null)
            return;

        SetCameraY(Mathf.Clamp(worldCamera.transform.position.y, GetCameraMinY(), GetCameraMaxY()));
        targetCameraY = worldCamera.transform.position.y;
    }

    private float GetCameraMinY()
    {
        GetVerticalLimits(out float bottomY, out _);
        return bottomY + GetCameraHalfHeight();
    }

    private float GetCameraMaxY()
    {
        GetVerticalLimits(out _, out float topY);
        return topY - GetCameraHalfHeight();
    }

    private void GetVerticalLimits(out float bottomY, out float topY)
    {
        if (topLimit != null || bottomLimit != null)
        {
            topY = topLimit != null ? topLimit.position.y : GetFallbackBounds().max.y;
            bottomY = bottomLimit != null ? bottomLimit.position.y : GetFallbackBounds().min.y;
        }
        else
        {
            Bounds bounds = GetFallbackBounds();
            topY = bounds.max.y;
            bottomY = bounds.min.y;
        }

        if (bottomY > topY)
        {
            float swap = bottomY;
            bottomY = topY;
            topY = swap;
        }

        float halfHeight = GetCameraHalfHeight();
        if (topY - bottomY < halfHeight * 2f)
        {
            float center = (topY + bottomY) * 0.5f;
            topY = center + halfHeight;
            bottomY = center - halfHeight;
        }
    }

    private Bounds GetFallbackBounds()
    {
        Transform root = contentRoot != null ? contentRoot : ResolveContentRoot();
        if (root == null)
            return new Bounds(Vector3.zero, new Vector3(0f, GetCameraHalfHeight() * 2f, 0f));

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
            return bounds;

        return new Bounds(root.position, new Vector3(0f, GetCameraHalfHeight() * 2f, 0f));
    }

    private float GetCameraHalfHeight()
    {
        if (worldCamera == null)
            return 5f;

        if (worldCamera.orthographic)
            return worldCamera.orthographicSize;

        float distance = Mathf.Abs(worldCamera.transform.position.z);
        Vector3 bottom = worldCamera.ScreenToWorldPoint(new Vector3(0f, 0f, distance));
        Vector3 top = worldCamera.ScreenToWorldPoint(new Vector3(0f, Screen.height, distance));
        return Mathf.Abs(top.y - bottom.y) * 0.5f;
    }

    private float GetWorldDeltaFromScreenDelta(float deltaScreenY)
    {
        if (worldCamera == null)
            return deltaScreenY * 0.01f;

        if (worldCamera.orthographic)
            return deltaScreenY * ((worldCamera.orthographicSize * 2f) / Mathf.Max(1f, Screen.height));

        float distance = Mathf.Abs(worldCamera.transform.position.z);
        Vector3 from = worldCamera.ScreenToWorldPoint(new Vector3(0f, 0f, distance));
        Vector3 to = worldCamera.ScreenToWorldPoint(new Vector3(0f, deltaScreenY, distance));
        return to.y - from.y;
    }

    private void BindButtons()
    {
        UnbindButtons();

        if (moveToTopButton != null)
            moveToTopButton.onClick.AddListener(MoveToTop);

        if (moveToBottomButton != null)
            moveToBottomButton.onClick.AddListener(MoveToBottom);
    }

    private void UnbindButtons()
    {
        if (moveToTopButton != null)
            moveToTopButton.onClick.RemoveListener(MoveToTop);

        if (moveToBottomButton != null)
            moveToBottomButton.onClick.RemoveListener(MoveToBottom);
    }

    private Transform ResolveContentRoot()
    {
        GameObject found = GameObject.Find("VillageWorldRoot");
        return found != null ? found.transform : transform;
    }

    private static bool TryGetPrimaryPointer(out Vector2 screenPosition, out bool isPressed, out bool pressedThisFrame, out bool releasedThisFrame)
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            screenPosition = touch.position;
            isPressed = touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary;
            pressedThisFrame = touch.phase == TouchPhase.Began;
            releasedThisFrame = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
            return true;
        }

        screenPosition = Input.mousePosition;
        isPressed = Input.GetMouseButton(0);
        pressedThisFrame = Input.GetMouseButtonDown(0);
        releasedThisFrame = Input.GetMouseButtonUp(0);
        return pressedThisFrame || isPressed || releasedThisFrame;
    }

    private static bool IsPointerOverUi()
    {
        if (EventSystem.current == null)
            return false;

        if (Input.touchCount > 0)
            return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);

        return EventSystem.current.IsPointerOverGameObject();
    }
}
