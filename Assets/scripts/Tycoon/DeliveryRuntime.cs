using System;
using UnityEngine;

namespace Tycoon
{
    // Kept outside panels: hiding BoxIn during delivery must not stop the drag.
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("")]
    public sealed class DeliveryRuntime : MonoBehaviour
    {
        private const float MissingPointerGraceSeconds = 0.15f;
        private static DeliveryRuntime instance;
        private bool captured;
        private int fingerId = -1;
        private Vector2 lastPosition;
        private Action<Vector2> move;
        private Action<Vector2, bool> finish;
        private UnityEngine.Object dragOwner;
        private int releasedFrame = -1;
        private float missingPointerSince = -1f;
        public static DeliveryRuntime Current => instance;

        private void Awake()
        {
            if (instance != null && instance != this) { enabled = false; Destroy(gameObject); return; }
            instance = this;
            GameplayFramePacing.Acquire(this);
        }

        public static bool BlocksWorldInput
        {
            get
            {
                if (DeliveryModal.IsOpen) return true;
                if (instance == null) return false;
                if (instance.captured || instance.releasedFrame == Time.frameCount) return true;
                // Legacy OnMouseDown can precede Update. Resolve that initial
                // press, but never rescan every UI/renderer while simply idle.
                if (Input.touchCount > 0)
                {
                    Touch touch = Input.GetTouch(0);
                    return touch.phase == TouchPhase.Began && DeliveryPointer.Find(touch.position) != null;
                }
                return Input.GetMouseButtonDown(0) && DeliveryPointer.Find(Input.mousePosition) != null;
            }
        }

        public static DeliveryRuntime Ensure()
        {
            if (instance == null) instance = new GameObject("TycoonInteractions").AddComponent<DeliveryRuntime>();
            return instance;
        }

        public bool BeginDrag(UnityEngine.Object owner, Vector2 screen, int finger,
            Action<Vector2> onMove, Action<Vector2, bool> onFinish)
        {
            if (finish != null) return false;
            dragOwner = owner;
            captured = true;
            fingerId = finger;
            lastPosition = screen;
            missingPointerSince = -1f;
            move = onMove;
            finish = onFinish;
            VillagePointerCapture.Acquire(this);
            move?.Invoke(screen);
            return true;
        }

        public void CancelDrag(UnityEngine.Object owner)
        {
            if (finish != null && dragOwner == owner) End(true);
        }

        private void Update()
        {
            if (!captured)
            {
                bool down = false;
                int finger = -1;
                Vector2 position = Input.mousePosition;
                if (Input.touchCount > 0)
                {
                    Touch touch = Input.GetTouch(0);
                    down = touch.phase == TouchPhase.Began;
                    finger = touch.fingerId;
                    position = touch.position;
                }
                else down = Input.GetMouseButtonDown(0);
                if (!down) return;
                Press(position, finger);
                if (!captured) return;
            }

            if (fingerId >= 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.fingerId != fingerId) continue;
                    ProcessPointerSample(touch.position, true, touch.phase == TouchPhase.Ended,
                        touch.phase == TouchPhase.Canceled, Time.unscaledTime);
                    return;
                }
                ProcessPointerSample(lastPosition, false, false, false, Time.unscaledTime);
            }
            else
            {
                bool released = Input.GetMouseButtonUp(0);
                ProcessPointerSample(Input.mousePosition, Input.GetMouseButton(0) || released,
                    released, false, Time.unscaledTime);
            }
        }

        private void ProcessPointerSample(Vector2 position, bool present, bool released, bool cancelled, float now)
        {
            if (!captured) return;
            if (finish != null && dragOwner == null) { End(true); return; }
            if (cancelled || released)
            {
                lastPosition = position;
                End(cancelled);
                return;
            }
            if (!present)
            {
                // Keep the original pointer and preview through a brief input gap.
                // Do not treat another finger or a synthetic mouse event as its release.
                if (missingPointerSince < 0f) missingPointerSince = now;
                if (now - missingPointerSince >= MissingPointerGraceSeconds) End(true);
                return;
            }
            missingPointerSince = -1f;
            lastPosition = position;
            move?.Invoke(lastPosition);
        }

        private void Press(Vector2 position, int finger)
        {
            DeliveryPointer target = DeliveryPointer.Resolve(position, out GameObject ui);
            // Keep consuming this gesture even if its button hides the popup on
            // pointer-down. EventSystem buttons still receive their normal events.
            bool uiGesture = DeliveryModal.IsOpen || target != null || ui != null;
            if (uiGesture)
            {
                captured = true;
                fingerId = finger;
                lastPosition = position;
                missingPointerSince = -1f;
                VillagePointerCapture.Acquire(this);
            }
            DeliveryWindows.NotifyUiPressed(ui != null ? ui : target != null ? target.gameObject : null);
            if (target == null) return;
            target.Press(position, finger);
        }

        private void End(bool cancelled)
        {
            Action<Vector2, bool> callback = finish;
            finish = null;
            move = null;
            dragOwner = null;
            captured = false;
            fingerId = -1;
            missingPointerSince = -1f;
            releasedFrame = Time.frameCount;
            VillagePointerCapture.Release(this);
            callback?.Invoke(lastPosition, cancelled);
        }

        // Touch focus can change without ending the gesture (for example when a
        // mobile keyboard opens). Backgrounding and real touch cancellation still end it.
        private void OnApplicationFocus(bool focused) { if (!focused && captured && fingerId < 0) End(true); }
        private void OnApplicationPause(bool paused) { if (paused && captured) End(true); }
        private void OnDisable() { if (captured) End(true); }
        private void OnDestroy()
        {
            if (instance != this) return;
            GameplayFramePacing.Release(this);
            instance = null;
        }
    }
}
