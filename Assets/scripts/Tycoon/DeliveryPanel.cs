using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Tycoon
{
    // Sprite panels live in world space. Canvas points supply a screen position, not a parent scale.
    [AddComponentMenu("")]
    public sealed class DeliveryPanel : MonoBehaviour
    {
        private static readonly Vector2 PanelSize = new Vector2(14.5f, 9f);
        private Vector3 authoredScale;
        private SortingGroup sorting;
        private static int nextOrder = 2000;
        private Transform positionPoint;
        private bool prepared;
        private float planeZ;
        private readonly List<Canvas> textCanvases = new List<Canvas>();
        private bool canvasesDirty = true;

        private void OnValidate() { hideFlags |= HideFlags.HideInInspector; }

        internal static DeliveryPanel Ensure(GameObject panel)
        {
            DeliveryModal.Track(panel);
            DeliveryPanel helper = panel.GetComponent<DeliveryPanel>();
            if (helper == null) helper = panel.AddComponent<DeliveryPanel>();
            helper.hideFlags = HideFlags.HideInInspector;
            helper.Prepare();
            return helper;
        }

        private void Awake() { Prepare(); }

        private void Prepare()
        {
            if (prepared) return;
            prepared = true;
            authoredScale = transform.localScale;
            planeZ = transform.position.z;
            sorting = GetComponent<SortingGroup>();
            if (sorting == null) sorting = gameObject.AddComponent<SortingGroup>();
        }
        private void OnEnable()
        {
            canvasesDirty = true;
            Prepare();
            if (sorting != null) sorting.sortingOrder = nextOrder += 10;
            Align();
            SyncTextCanvases();
        }
        private void LateUpdate() { Align(); if (canvasesDirty) SyncTextCanvases(); }
        private void OnTransformChildrenChanged() { canvasesDirty = true; }
        internal void InvalidateTextCanvases() { canvasesDirty = true; }

        private void SyncTextCanvases()
        {
            if (sorting == null) return;
            GetComponentsInChildren(true, textCanvases);
            canvasesDirty = false;
            foreach (Canvas canvas in textCanvases)
                if (canvas.name == DeliveryView.LabelCanvasName)
                {
                    canvas.sortingLayerID = sorting.sortingLayerID;
                    canvas.sortingOrder = sorting.sortingOrder + 1;
                }
        }

        internal void SetPosition(Transform point)
        {
            positionPoint = point;
            Prepare();
            if (!(transform is RectTransform)) transform.SetParent(null, true);
            Align();
        }

        private void Align()
        {
            Camera camera = Camera.main;
            if (transform is RectTransform) return;
            Canvas canvas = positionPoint != null ? positionPoint.GetComponentInParent<Canvas>() : null;
            if (positionPoint != null && canvas == null)
            {
                SetPositionIfChanged(positionPoint.position);
                if (transform.rotation != positionPoint.rotation) transform.rotation = positionPoint.rotation;
                SetScaleIfChanged(Vector3.Scale(authoredScale, positionPoint.lossyScale));
                return;
            }
            if (camera == null) return;
            Vector2 screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 pointScale = Vector3.one;
            if (positionPoint != null)
            {
                Canvas rootCanvas = canvas.rootCanvas;
                Camera uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null
                    : rootCanvas.worldCamera != null ? rootCanvas.worldCamera : camera;
                screen = RectTransformUtility.WorldToScreenPoint(uiCamera, positionPoint.position);
                Vector3 canvasScale = rootCanvas.transform.lossyScale;
                Vector3 anchorScale = positionPoint.lossyScale;
                pointScale = new Vector3(RelativeScale(anchorScale.x, canvasScale.x), RelativeScale(anchorScale.y, canvasScale.y), 1f);
            }
            SetPositionIfChanged(DeliveryView.WorldPoint(screen, planeZ, camera));
            if (camera.orthographic)
            {
                float height = camera.orthographicSize * 2f;
                float fit = Mathf.Min(1f, height / PanelSize.y, height * camera.aspect / PanelSize.x);
                SetScaleIfChanged(Vector3.Scale(authoredScale * fit, pointScale));
            }
        }

        private static float RelativeScale(float value, float canvasScale)
        {
            return Mathf.Abs(canvasScale) > 0.000001f ? value / canvasScale : 1f;
        }

        private void SetPositionIfChanged(Vector3 position) { if (transform.position != position) transform.position = position; }
        private void SetScaleIfChanged(Vector3 scale) { if (transform.localScale != scale) transform.localScale = scale; }
    }
}
