using UnityEngine;
using UnityEngine.Serialization;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("Tycoon/Resources")]
    public sealed class Resources : MonoBehaviour
    {
        [SerializeField, Tooltip("비워두면 프리팹 이름 사용")] private string resourceId;
        [FormerlySerializedAs("oxygenPrice")]
        [SerializeField, Min(0)] private int energyPrice = 1;
        [SerializeField, Tooltip("건물 프리팹의 루트 오브젝트를 등록하세요.")]
        private GameObject buildingPrefab;
        [SerializeField, Min(1)] private int requiredQuantity = 4;
        [SerializeField] private GameObject receiptPhotoPrefab;

        private StoreIn store;
        private Resources definition;
        private GameObject dragVisual;
        [SerializeField, HideInInspector] private GameObject preparedDragVisual;
        private Camera dragCamera;
        private RectTransform dragRect;
        private Vector2 dragScreenPosition;
        private Vector2 dragTargetPosition;
        private Vector2 grabOffset;
        private float dragPlaneZ;

        public string ResourceId => string.IsNullOrWhiteSpace(resourceId) ? name.Replace("(Clone)", "").Trim() : resourceId.Trim();
        public int EnergyPrice => Mathf.Max(0, energyPrice);
        public int RequiredQuantity => Mathf.Max(1, requiredQuantity);
        public Building BuildingPrefab => buildingPrefab != null ? buildingPrefab.GetComponent<Building>() : null;
        public GameObject ReceiptPhotoPrefab => receiptPhotoPrefab;

        public void Bind(StoreIn owner, Resources source)
        {
            store = owner;
            definition = source;
            DeliveryPointer.Bind(gameObject, BeginDrag);
            PrepareDragVisual();
        }

        private void PrepareDragVisual()
        {
            if (preparedDragVisual != null) return;
            Transform existing = transform.Find("ResourceDragPreview");
            if (existing != null) { preparedDragVisual = existing.gameObject; return; }
            // Prepare once while the store opens, rather than cloning the board
            // hierarchy and adding components on every pointer-down.
            preparedDragVisual = Instantiate(gameObject, transform.parent, false);
            preparedDragVisual.name = "ResourceDragPreview";
            foreach (Resources resource in preparedDragVisual.GetComponentsInChildren<Resources>(true)) resource.enabled = false;
            foreach (DeliveryPointer pointer in preparedDragVisual.GetComponentsInChildren<DeliveryPointer>(true)) pointer.enabled = false;
            foreach (Collider2D collider in preparedDragVisual.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (Collider collider in preparedDragVisual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (UnityEngine.UI.Graphic graphic in preparedDragVisual.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
            var group = preparedDragVisual.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (group == null) group = preparedDragVisual.AddComponent<UnityEngine.Rendering.SortingGroup>();
            group.enabled = true;
            group.sortingOrder = 30000;
            preparedDragVisual.SetActive(false);
        }

        private void BeginDrag(Vector2 screen, int finger)
        {
            if (store == null || definition == null || !store.isActiveAndEnabled || dragVisual != null) return;
            if (!store.CanBuy(definition))
            {
                VillageManagement village = VillageManagement.Instance;
                if (village != null && village.CurrentEnergy < definition.EnergyPrice)
                    DeliveryMotion.ShakeInsufficientEnergy(transform);
                return;
            }
            DeliveryMotion.Reset(transform);
            PrepareDragVisual();
            dragVisual = preparedDragVisual;
            Transform preview = dragVisual.transform;
            preview.SetParent(transform.parent, false);
            preview.localPosition = transform.localPosition;
            preview.localRotation = transform.localRotation;
            preview.localScale = transform.localScale;
            Canvas canvas = preview.GetComponentInParent<Canvas>();
            dragRect = canvas != null ? preview.parent as RectTransform : null;
            dragCamera = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null
                : canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            dragPlaneZ = preview.position.z;
            dragScreenPosition = dragCamera != null ? (Vector2)dragCamera.WorldToScreenPoint(preview.position)
                : (Vector2)preview.position;
            // Preserve the point the user grabbed: no initial snap to the icon's pivot.
            grabOffset = dragScreenPosition - screen;
            dragTargetPosition = dragScreenPosition;
            dragVisual.SetActive(true);
            if (!DeliveryRuntime.Ensure().BeginDrag(this, screen, finger, MoveDragPreview, FinishDrag))
                ReleaseDragVisual();
            else DeliveryMotion.ShakeResource(transform);
        }

        private void MoveDragPreview(Vector2 screen) { dragTargetPosition = screen + grabOffset; }

        // Run after the panel/camera has moved, with no hierarchy searches or
        // allocations in the follow loop. Use unscaled time like the store effects.
        private void LateUpdate() { AdvanceDrag(Time.unscaledDeltaTime); }

        private void AdvanceDrag(float deltaSeconds)
        {
            if (dragVisual == null) return;
            float blend = 1f - Mathf.Exp(-40f * Mathf.Max(0f, deltaSeconds));
            dragScreenPosition = Vector2.Lerp(dragScreenPosition, dragTargetPosition, blend);
            if (dragRect != null)
            {
                if (RectTransformUtility.ScreenPointToWorldPointInRectangle(dragRect, dragScreenPosition, dragCamera, out Vector3 world))
                    dragVisual.transform.position = world;
            }
            else dragVisual.transform.position = DeliveryView.WorldPoint(dragScreenPosition, dragPlaneZ, dragCamera);
        }

        private void ReleaseDragVisual()
        {
            if (dragVisual == null) return;
            dragVisual.SetActive(false);
            dragVisual = null;
            dragRect = null;
            dragCamera = null;
        }

        private void FinishDrag(Vector2 screen, bool cancelled)
        {
            ReleaseDragVisual();
            // Purchase uses the release position, not the smoothed visual position.
            if (!cancelled && store != null) store.TryBuyAt(definition, screen);
        }

        private void OnDisable()
        {
            if (dragVisual != null && DeliveryRuntime.Current != null) DeliveryRuntime.Current.CancelDrag(this);
        }

        private void OnDestroy()
        {
            if (preparedDragVisual != null) DeliveryView.Remove(preparedDragVisual);
        }

        private void OnValidate() { energyPrice = Mathf.Max(0, energyPrice); requiredQuantity = Mathf.Max(1, requiredQuantity); }
    }
}
