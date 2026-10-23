using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Tycoon/Box In")]
    public sealed class BoxIn : MonoBehaviour
    {
        [SerializeField] private GameObject xPrefab;
        [SerializeField] private GameObject boxCompletePrefab;
        [SerializeField, Tooltip("배달 성공 후 건물 Line Two Point에 표시할 박스. 3초 대기 후 1초 동안 사라집니다.")]
        private GameObject deliveredBoxPrefab;
        [SerializeField, Tooltip("왼쪽부터 순서대로 등록. 비어 있지 않은 위치 수가 최대 보관량입니다.")]
        private List<Transform> boxCompletePositions = new List<Transform>();

        private readonly List<boxComplete> boxes = new List<boxComplete>();
        private readonly HashSet<object> reservations = new HashSet<object>();
        private readonly List<SavedBoxOrder> storedOrders = new List<SavedBoxOrder>();
        private readonly Dictionary<boxComplete, string> orderIds = new Dictionary<boxComplete, string>();
        private readonly HashSet<string> pendingDeliveryIds = new HashSet<string>();
        private VillageManagement saveOwner;
        private string inventorySaveKey;
        private bool initialized;
        private bool delivering;
        private bool showAfterDelivery;
        private bool destroying;
        private const float DragScaleMultiplier = 1.1f;
        private boxComplete draggedComplete;
        private Transform dragOriginalParent;
        private Vector3 dragOriginalPosition;
        private Quaternion dragOriginalRotation;
        private Vector3 dragOriginalScale;
        private int dragOriginalSibling;
        private SortingGroup dragSorting;
        private bool hadSortingGroup;
        private bool originalSortingEnabled;
        private int originalSortingLayer;
        private int originalSortingOrder;

        public event Action Changed;
        public event Action<ReceiptData, Building> Delivered;
        public int Count => boxes.Count;
        public int Capacity
        {
            get
            {
                var unique = new HashSet<Transform>();
                foreach (Transform position in boxCompletePositions)
                    if (position != null) unique.Add(position);
                return unique.Count;
            }
        }
        public bool IsFull => Math.Max(Count, storedOrders.Count) + reservations.Count >= Capacity;
        public bool IsDelivering => delivering;

        private void Start() { Initialize(); }
        private void OnEnable() { if (initialized) DeliveryWindows.Register(gameObject, Close); }

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            DeliveryWindows.Register(gameObject, Close);
            DeliveryPanel.Ensure(gameObject);
            Transform background = transform.Find("Background");
            if (background != null) DeliveryPointer.Bind(background.gameObject, (screen, finger) => { });
            DeliveryView.BindClick(DeliveryView.Resolve(xPrefab, transform), Close);
            if (Application.isPlaying)
            {
                var scene = gameObject.scene;
                inventorySaveKey = (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path) + "|" + name.Replace("(Clone)", "").Trim();
                VillageManagement.InstanceReady += AttachSaveOwner;
                AttachSaveOwner(VillageManagement.Instance);
            }
        }

        public void Show()
        {
            if (delivering) return;
            Initialize();
            DeliveryWindows.Open(gameObject);
        }

        public void Close()
        {
            showAfterDelivery = false;
            if (!delivering) gameObject.SetActive(false);
        }

        public bool TryReserve(out object token)
        {
            token = null;
            Initialize();
            if (boxCompletePrefab == null || IsFull) return false;
            token = new object();
            reservations.Add(token);
            Changed?.Invoke();
            return true;
        }

        public void ReleaseReservation(object token)
        {
            if (token != null && reservations.Remove(token)) Changed?.Invoke();
        }

        public bool CommitReservation(object token, ReceiptData receipt)
        {
            if (token == null || !reservations.Contains(token) || receipt == null || receipt.Lines.Count == 0 || Count >= Capacity || boxCompletePrefab == null)
                return false;
            string id = Guid.NewGuid().ToString("N");
            if (!SpawnBox(id, receipt)) return false;
            storedOrders.Add(SavedBoxOrder.FromReceipt(id, receipt));
            reservations.Remove(token);
            PersistOrders();
            Changed?.Invoke();
            return true;
        }

        private bool SpawnBox(string id, ReceiptData receipt)
        {
            List<Transform> positions = GetPositions();
            if (boxCompletePrefab == null || boxes.Count >= positions.Count) return false;
            GameObject root = DeliveryView.Spawn(boxCompletePrefab, positions[boxes.Count]);
            if (root == null) return false;
            boxComplete completed = root.GetComponent<boxComplete>();
            if (completed == null) completed = root.AddComponent<boxComplete>();
            completed.Bind(this, receipt);
            boxes.Add(completed);
            orderIds[completed] = id;
            return true;
        }

        private void AttachSaveOwner(VillageManagement owner)
        {
            if (owner == null || owner == saveOwner || inventorySaveKey == null) return;
            if (saveOwner != null) saveOwner.BoxInventoriesReloaded -= ReloadOrders;
            saveOwner = owner;
            saveOwner.BoxInventoriesReloaded += ReloadOrders;
            if (storedOrders.Count == 0) ReloadOrders();
            else PersistOrders();
        }

        private void ReloadOrders()
        {
            if (saveOwner == null) return;
            BoxInventoryState state = saveOwner.GetBoxInventory(inventorySaveKey);
            showAfterDelivery = false;
            if (delivering && DeliveryRuntime.Current != null) DeliveryRuntime.Current.CancelDrag(this);
            foreach (boxComplete entry in boxes)
                if (entry != null) DeliveryView.Remove(entry.gameObject);
            boxes.Clear();
            orderIds.Clear();
            storedOrders.Clear();
            pendingDeliveryIds.Clear();
            var ids = new HashSet<string>();
            if (state?.boxes != null)
                foreach (SavedBoxOrder order in state.boxes)
                    if (order != null && order.IsValid && ids.Add(order.id)) storedOrders.Add(order.Copy());
            FillAvailableSlots();
            Changed?.Invoke();
        }

        private void FillAvailableSlots()
        {
            foreach (SavedBoxOrder order in storedOrders)
            {
                if (boxes.Count >= Capacity) break;
                if (pendingDeliveryIds.Contains(order.id) || orderIds.ContainsValue(order.id)) continue;
                SpawnBox(order.id, order.ToReceipt());
            }
        }

        private void PersistOrders()
        {
            if (saveOwner != null) saveOwner.SaveBoxInventory(inventorySaveKey, storedOrders);
        }

        public bool BeginDelivery(boxComplete source, Vector2 screen, int finger)
        {
            if (delivering || source == null || !boxes.Contains(source) || source.Data == null) return false;
            draggedComplete = source;
            dragOriginalParent = source.transform.parent;
            dragOriginalPosition = source.transform.localPosition;
            dragOriginalRotation = source.transform.localRotation;
            dragOriginalScale = source.transform.localScale;
            dragOriginalSibling = source.transform.GetSiblingIndex();

            // Detach the same object before hiding BoxIn so its photos remain visible too.
            source.transform.SetParent(null, true);
            source.transform.localScale *= DragScaleMultiplier;
            dragSorting = source.GetComponent<SortingGroup>();
            hadSortingGroup = dragSorting != null;
            if (dragSorting == null) dragSorting = source.gameObject.AddComponent<SortingGroup>();
            originalSortingEnabled = dragSorting.enabled;
            originalSortingLayer = dragSorting.sortingLayerID;
            originalSortingOrder = dragSorting.sortingOrder;
            dragSorting.enabled = true;
            dragSorting.sortingOrder = 30000;
            delivering = true;
            showAfterDelivery = true;
            if (!DeliveryRuntime.Ensure().BeginDrag(this, screen, finger,
                position => { if (draggedComplete != null) DeliveryView.MoveToScreen(draggedComplete.transform, position); },
                (position, cancelled) => FinishDelivery(source, position, cancelled)))
            {
                delivering = false;
                RestoreDraggedComplete();
                return false;
            }
            gameObject.SetActive(false);
            return true;
        }

        private void FinishDelivery(boxComplete source, Vector2 screen, bool cancelled)
        {
            Building target = !cancelled && source != null ? source.FindDeliveryTarget(screen) : null;
            ReceiptData receipt = source != null ? source.Data : null;
            string orderId = source != null && orderIds.TryGetValue(source, out string id) ? id : null;
            VillageManagement deliverySaveOwner = saveOwner;
            string deliverySaveKey = inventorySaveKey;
            delivering = false;
            if (this == null || destroying)
            {
                if (source != null) DeliveryView.Remove(source.gameObject);
                draggedComplete = null;
                return;
            }
            if (target != null && source != null && DeliveryArrival.Begin(source, deliveredBoxPrefab, target,
                () =>
                {
                    // Completion can outlive BoxIn. Retain the saved order until
                    // the building is credited so an interrupted arrival is recoverable.
                    VillageManagement owner = deliverySaveOwner != null ? deliverySaveOwner : VillageManagement.Instance;
                    if (owner != null && deliverySaveKey != null && orderId != null) owner.CompleteSavedBoxDelivery(deliverySaveKey, orderId);
                    if (this == null || destroying) return;
                    storedOrders.RemoveAll(order => order.id == orderId);
                    pendingDeliveryIds.Remove(orderId);
                    FillAvailableSlots();
                    Changed?.Invoke();
                    Delivered?.Invoke(receipt, target);
                }))
            {
                boxes.Remove(source);
                orderIds.Remove(source);
                if (orderId != null) pendingDeliveryIds.Add(orderId);
                draggedComplete = null;
                dragSorting = null;
                dragOriginalParent = null;
                Rearrange();
            }
            else
            {
                target = null;
                RestoreDraggedComplete();
            }
            if (showAfterDelivery && Count > 0) DeliveryWindows.Open(gameObject);
            else gameObject.SetActive(false);
            Changed?.Invoke();
        }

        private void RestoreDraggedComplete()
        {
            if (draggedComplete != null)
            {
                Transform item = draggedComplete.transform;
                item.SetParent(dragOriginalParent != null ? dragOriginalParent : transform, false);
                item.localPosition = dragOriginalPosition;
                item.localRotation = dragOriginalRotation;
                item.localScale = dragOriginalScale;
                item.SetSiblingIndex(dragOriginalSibling);
                if (dragSorting != null)
                {
                    dragSorting.sortingLayerID = originalSortingLayer;
                    dragSorting.sortingOrder = originalSortingOrder;
                    dragSorting.enabled = hadSortingGroup && originalSortingEnabled;
                    if (!hadSortingGroup)
                    {
                        if (Application.isPlaying) Destroy(dragSorting);
                        else DestroyImmediate(dragSorting);
                    }
                }
            }
            draggedComplete = null;
            dragSorting = null;
            dragOriginalParent = null;
        }

        private void Rearrange()
        {
            List<Transform> positions = GetPositions();
            for (int i = 0; i < boxes.Count && i < positions.Count; i++)
            {
                boxes[i].transform.SetParent(positions[i], false);
                boxes[i].transform.localPosition = Vector3.zero;
            }
        }

        private List<Transform> GetPositions()
        {
            var positions = new List<Transform>();
            foreach (Transform position in boxCompletePositions)
                if (position != null && !positions.Contains(position)) positions.Add(position);
            return positions;
        }

        private void OnDestroy()
        {
            destroying = true;
            VillageManagement.InstanceReady -= AttachSaveOwner;
            if (saveOwner != null) saveOwner.BoxInventoriesReloaded -= ReloadOrders;
            if (delivering && DeliveryRuntime.Current != null) DeliveryRuntime.Current.CancelDrag(this);
            if (draggedComplete != null) DeliveryView.Remove(draggedComplete.gameObject);
            reservations.Clear();
        }
    }
}
