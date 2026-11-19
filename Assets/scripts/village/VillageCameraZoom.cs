using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(500)]
public sealed class VillageCameraZoom : MonoBehaviour
{
    [Header("Zoom Limits (배율)")]
    [SerializeField, Min(0.01f), Tooltip("정상 축소 한계입니다. 1 = 시작 화면, 2 = 두 배 확대입니다.")]
    private float minZoom = 1f;
    [SerializeField, Min(0.01f), Tooltip("정상 확대 한계입니다.")]
    private float maxZoom = 2f;
    [SerializeField, Min(0.01f), Tooltip("조작 중 잠깐 허용하는 축소 한계입니다. Min Zoom 이하로 설정합니다.")]
    private float preMinZoom = 0.85f;
    [SerializeField, Min(0.01f), Tooltip("조작 중 잠깐 허용하는 확대 한계입니다. Max Zoom 이상으로 설정합니다.")]
    private float preMaxZoom = 2.3f;

    [Header("Input")]
    [SerializeField, Min(0.001f)] private float mouseWheelSensitivity = 0.12f;
    [SerializeField, Min(0.01f)] private float pinchSensitivity = 1f;
    [SerializeField, Min(0f), Tooltip("마지막 휠/트랙패드 스크롤 입력 후 조작 종료로 판단하는 시간입니다.")]
    private float scrollReleaseDelay = 0.12f;

    [Header("Smoothing")]
    [SerializeField, Min(0.01f)] private float zoomSmoothTime = 0.05f;
    [SerializeField, Min(0f), Tooltip("Pre 한계에 닿으면 대기 없이 복귀를 시작하고, 이 시간(초) 안에 정상 Min/Max로 돌아옵니다. 0이면 즉시 복귀합니다.")]
    private float returnDuration = 0.3f;

    private Camera worldCamera;
    private VillageCameraScroller scroller;
    private float baseSize;
    private float currentZoom = 1f;
    private float targetZoom = 1f;
    private float zoomVelocity;
    private bool pinching;
    private bool scrolling;
    private bool waitForTouchesReleased;
    private bool pinchBlocked;
    private int firstFingerId = -1;
    private int secondFingerId = -1;
    private float previousPinchDistance;
    private float lastScrollTime;
    private bool returning;
    private float returnElapsed;
    private float returnStartZoom;
    private int blockedZoomDirection;

    public float CurrentZoom => currentZoom;
    internal float MinimumZoomCameraSize => baseSize / Mathf.Max(0.01f, minZoom);
    public bool BlocksPanning => isActiveAndEnabled && (pinching || scrolling || waitForTouchesReleased);

    private void Awake()
    {
        worldCamera = GetComponent<Camera>();
        baseSize = Mathf.Max(0.01f, worldCamera.orthographicSize);
        ValidateLimits();
        currentZoom = targetZoom = Mathf.Clamp(1f, minZoom, maxZoom);
        ApplyCameraSize();
    }

    private void OnValidate()
    {
        ValidateLimits();
        targetZoom = pinching || scrolling ? Mathf.Clamp(targetZoom, preMinZoom, preMaxZoom)
            : Mathf.Clamp(targetZoom, minZoom, maxZoom);
        if (returning) { returnStartZoom = currentZoom; returnElapsed = 0f; }
    }

    private void ValidateLimits()
    {
        minZoom = Mathf.Max(0.01f, minZoom);
        maxZoom = Mathf.Max(minZoom, maxZoom);
        preMinZoom = Mathf.Clamp(preMinZoom, 0.01f, minZoom);
        preMaxZoom = Mathf.Max(maxZoom, preMaxZoom);
        zoomSmoothTime = Mathf.Max(0.01f, zoomSmoothTime);
        returnDuration = Mathf.Max(0f, returnDuration);
    }

    private void Update()
    {
        if (!worldCamera.orthographic) return;
        int count = Input.touchCount;
        if (count >= 2)
        {
            waitForTouchesReleased = true;
            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);
            if (pinching)
            {
                for (int i = 0; i < count; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.fingerId == firstFingerId) first = touch;
                    if (touch.fingerId == secondFingerId) second = touch;
                }
            }
            if (pinchBlocked || InputBlocked(first.position) || InputBlocked(second.position))
            {
                pinchBlocked = true;
                EndGesture();
                return;
            }
            if (IsReleased(first) || IsReleased(second)) { EndGesture(); return; }
            ProcessPinch(first.fingerId, second.fingerId, Vector2.Distance(first.position, second.position));
            return;
        }

        if (pinching) EndGesture();
        if (count > 0) return; // Do not turn the remaining pinch finger into a mouse/drag gesture.
        waitForTouchesReleased = false;
        pinchBlocked = false;
        float scroll = Input.mouseScrollDelta.y;
        if (InputBlocked(Input.mousePosition))
        {
            if (scrolling) EndGesture();
            return;
        }
        if (Mathf.Abs(scroll) > 0.0001f) ProcessScroll(scroll, Time.unscaledTime);
        else if (scrolling && Time.unscaledTime - lastScrollTime >= scrollReleaseDelay) EndGesture();
    }

    private static bool IsReleased(Touch touch) => touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;

    private static bool InputBlocked(Vector2 screen) => Tycoon.DeliveryRuntime.BlocksWorldInput ||
        VillagePointerCapture.HasActiveCapture || Tycoon.DeliveryPointer.FindUiTarget(screen) != null;

    private void BeginGesture()
    {
        // A continued outward gesture must not restart an automatic limit return.
        if (!returning || blockedZoomDirection == 0)
        {
            targetZoom = currentZoom;
            zoomVelocity = 0f;
            returning = false;
            blockedZoomDirection = 0;
        }
        if (scroller == null) scroller = GetComponent<VillageCameraScroller>();
        if (scroller != null) scroller.CancelPointerMotion();
    }

    private void ProcessPinch(int firstId, int secondId, float distance)
    {
        if (!pinching || firstId != firstFingerId || secondId != secondFingerId)
        {
            BeginGesture();
            pinching = true;
            scrolling = false;
            firstFingerId = firstId;
            secondFingerId = secondId;
            previousPinchDistance = Mathf.Max(1f, distance);
            return;
        }
        float ratio = Mathf.Max(1f, distance) / previousPinchDistance;
        ApplyGestureRatio(Mathf.Pow(ratio, pinchSensitivity));
        previousPinchDistance = Mathf.Max(1f, distance);
    }

    private void ProcessScroll(float delta, float now)
    {
        if (!scrolling) BeginGesture();
        scrolling = true;
        lastScrollTime = now;
        ApplyGestureRatio(Mathf.Exp(delta * mouseWheelSensitivity));
    }

    private void ApplyGestureRatio(float ratio)
    {
        int direction = Mathf.Approximately(ratio, 1f) ? 0 : ratio > 1f ? 1 : -1;
        if (blockedZoomDirection != 0)
        {
            if (direction == 0 || direction == blockedZoomDirection) return;
            returning = false;
            blockedZoomDirection = 0;
            zoomVelocity = 0f;
            targetZoom = currentZoom;
        }
        targetZoom = Mathf.Clamp(targetZoom * ratio, preMinZoom, preMaxZoom);
    }

    private void EndGesture()
    {
        pinching = scrolling = false;
        firstFingerId = secondFingerId = -1;
        if (returning) return;
        blockedZoomDirection = 0;
        zoomVelocity = 0f;
        targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
        if (currentZoom < minZoom || currentZoom > maxZoom)
            StartReturn(false);
    }

    private void StartReturn(bool reachedPreLimit)
    {
        targetZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);
        returnStartZoom = currentZoom;
        returnElapsed = 0f;
        zoomVelocity = 0f;
        returning = true;
        if (reachedPreLimit) blockedZoomDirection = currentZoom > maxZoom ? 1 : -1;
    }

    private void LateUpdate() => AdvanceZoom(Time.unscaledDeltaTime);

    private void AdvanceZoom(float deltaSeconds)
    {
        if (worldCamera == null || !worldCamera.orthographic || deltaSeconds <= 0f) return;
        float next;
        if (returning)
        {
            returnElapsed += deltaSeconds;
            float progress = returnDuration > 0f ? Mathf.Clamp01(returnElapsed / returnDuration) : 1f;
            next = Mathf.Lerp(returnStartZoom, targetZoom, Mathf.SmoothStep(0f, 1f, progress));
            if (progress >= 1f)
            {
                next = targetZoom;
                returning = false;
                if (!pinching && !scrolling) blockedZoomDirection = 0;
            }
        }
        else
        {
            next = Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, zoomSmoothTime,
                Mathf.Infinity, deltaSeconds);
            if (Mathf.Abs(next - targetZoom) < 0.0001f) { next = targetZoom; zoomVelocity = 0f; }
        }
        bool reachedPre = !returning && blockedZoomDirection == 0 &&
            ((preMaxZoom > maxZoom && targetZoom >= preMaxZoom && next >= preMaxZoom - 0.001f) ||
             (preMinZoom < minZoom && targetZoom <= preMinZoom && next <= preMinZoom + 0.001f));
        if (reachedPre) next = targetZoom;
        next = Mathf.Clamp(next, preMinZoom, preMaxZoom);
        if (Mathf.Approximately(next, currentZoom) && !reachedPre) return;
        currentZoom = next;
        ApplyCameraSize();
        if (scroller == null) scroller = GetComponent<VillageCameraScroller>();
        if (scroller != null) scroller.ClampToCurrentView();
        if (reachedPre) StartReturn(true);
    }

    private void ApplyCameraSize() => worldCamera.orthographicSize = baseSize / currentZoom;

    private void OnApplicationFocus(bool focused) { if (!focused) EndGesture(); }
    private void OnApplicationPause(bool paused) { if (paused) EndGesture(); }
    private void OnDisable()
    {
        EndGesture();
        waitForTouchesReleased = pinchBlocked = false;
        returning = false;
        blockedZoomDirection = 0;
        currentZoom = targetZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);
        zoomVelocity = 0f;
        if (worldCamera != null) ApplyCameraSize();
    }
}
