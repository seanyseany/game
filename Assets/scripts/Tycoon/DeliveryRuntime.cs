using System;
using UnityEngine;

namespace Tycoon
{
    // Kept outside panels: hiding BoxIn during delivery must not stop the drag.
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("")]
    public sealed class DeliveryRuntime : MonoBehaviour
    {
        private static DeliveryRuntime instance;
        private bool captured;
        private int fingerId = -1;
        private Vector2 lastPosition;
        private Action<Vector2> move;
        private Action<Vector2, bool> finish;
        private UnityEngine.Object dragOwner;
        private int releasedFrame = -1;
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

            bool held = false;
            bool cancelled = !Application.isFocused;
            if (fingerId >= 0)
            {
                bool found = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.fingerId != fingerId) continue;
                    found = true;
                    lastPosition = touch.position;
                    held = touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
                    cancelled |= touch.phase == TouchPhase.Canceled;
                    break;
                }
                cancelled |= !found;
            }
            else
            {
                lastPosition = Input.mousePosition;
                held = Input.GetMouseButton(0);
            }
            if (cancelled || !held || (finish != null && dragOwner == null)) End(cancelled || dragOwner == null);
            else move?.Invoke(lastPosition);
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
            releasedFrame = Time.frameCount;
            VillagePointerCapture.Release(this);
            callback?.Invoke(lastPosition, cancelled);
        }

        private void OnApplicationFocus(bool focused) { if (!focused && captured) End(true); }
        private void OnDisable() { if (captured) End(true); }
        private void OnDestroy()
        {
            if (instance != this) return;
            GameplayFramePacing.Release(this);
            instance = null;
        }
    }
}
