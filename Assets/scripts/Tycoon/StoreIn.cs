using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Tycoon/Store In")]
    public sealed class StoreIn : MonoBehaviour
    {
        private struct PendingReceipt
        {
            internal string resourceId;
            internal int quantity;
        }

        // Session memory only: scene reloads retain purchases, restarting the
        // game starts with an empty receipt. Never retain scene object references.
        private static readonly Dictionary<string, PendingReceipt> sessionReceipts = new Dictionary<string, PendingReceipt>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionReceipts() { sessionReceipts.Clear(); }

        [Serializable]
        public sealed class ResourceSlot
        {
            public GameObject resourcePrefab;
            public Transform resourcePosition;
            public Transform pricePosition;
            [NonSerialized] internal GameObject resource;
            [NonSerialized] internal GameObject padlock;
            [NonSerialized] internal GameObject price;
            [NonSerialized] internal Resources definition;
            [NonSerialized] internal int displayedPrice = -2;
        }

        [SerializeField] private GameObject xPrefab;
        [SerializeField] private List<ResourceSlot> resources = new List<ResourceSlot>();
        [SerializeField] private GameObject padlockPrefab;
        [SerializeField] private GameObject receiptPrefab;
        [SerializeField, Tooltip("영수증이 기존 자식이면 비워도 됩니다.")] private Transform receiptPosition;
        [SerializeField] private GameObject boxPrefab;
        [SerializeField, Tooltip("Receipt의 Box Position보다 우선합니다.")] private Transform boxPosition;
        [SerializeField, Tooltip("선택: 보관함 인스턴스. 비우면 Box 버튼에서 자동 연결")] private BoxIn boxIn;
        [SerializeField, Min(0f)] private float packAnimationSeconds = 0.6f;
        [SerializeField, Min(0f)] private float shrinkSeconds = 0.25f;
        [SerializeField] private string packTrigger = "pack";
        [SerializeField] private string openTrigger = "boxopen";
        [Header("등장 연출")]
        [SerializeField, Tooltip("기존 자식 오브젝트 또는 프리팹. 열 때마다 원래 로컬 위치보다 Y -0.5 아래에서 1초 동안 올라옵니다.")]
        private GameObject backgroundUp;
        [SerializeField, Tooltip("기존 자식 오브젝트 또는 프리팹. 열 때마다 Animator의 1, 2, 3 Trigger 중 하나를 무작위로 실행합니다.")]
        private GameObject backgroundOwner;
        [Header("숫자 스프라이트")]
        [SerializeField, Tooltip("0번째=숫자0, 1번째=숫자1, ... 9번째=숫자9")]
        private Sprite[] numberSprites = new Sprite[10];
        [SerializeField, Min(0.01f), Tooltip("가격과 영수증 수량의 크기 배율. 1=기본 크기, 2=2배")]
        private float numberScale = 1f;
        [SerializeField, Tooltip("선택: 1/4의 / 스프라이트. 비워도 기본 기호를 표시합니다.")] private Sprite slashSprite;
        [SerializeField, Tooltip("선택: 잠긴 가격의 ? 스프라이트. 비워도 기본 기호를 표시합니다.")] private Sprite questionMarkSprite;

        private Receipt receipt;
        private GameObject packingBox;
        private GameObject backgroundUpView;
        private Animator backgroundOwnerAnimator;
        private static readonly int[] BackgroundOwnerTriggers =
        {
            Animator.StringToHash("1"), Animator.StringToHash("2"), Animator.StringToHash("3")
        };
        private bool initialized;
        private bool packing;
        private Coroutine packingRoutine;
        private BoxIn reservedInventory;
        private object reservation;
        private float nextRefresh;
        private bool numberSpritesDirty;
        private string receiptSessionKey;
        private readonly HashSet<string> readyBuildingIds = new HashSet<string>();

        public Receipt CurrentReceipt => receipt;
        public bool IsPacking => packing;

        private void Start() { Initialize(); }
        private void OnEnable()
        {
            if (initialized) DeliveryWindows.Register(gameObject, Close);
            nextRefresh = 0f;
            numberSpritesDirty = true;
            if (initialized)
            {
                RefreshAvailability();
                RefreshFullBoxMotion();
            }
        }

        private void OnValidate()
        {
            if (numberSprites == null || numberSprites.Length != 10) Array.Resize(ref numberSprites, 10);
            numberScale = Mathf.Max(0.01f, numberScale);
            numberSpritesDirty = true;
        }

        public void Initialize()
        {
            InitializeContent(true);
        }

        private void InitializeContent(bool playBoxOpen)
        {
            if (initialized) return;
            initialized = true;
            DeliveryWindows.Register(gameObject, Close);
            DeliveryPanel.Ensure(gameObject);
            Transform background = transform.Find("Background");
            if (background != null) DeliveryPointer.Bind(background.gameObject, (screen, finger) => { });
            DeliveryView.BindClick(DeliveryView.Resolve(xPrefab, transform), Close);
            backgroundUpView = DeliveryView.Resolve(backgroundUp, transform);
            GameObject ownerView = DeliveryView.Resolve(backgroundOwner, transform);
            if (ownerView != null) backgroundOwnerAnimator = ownerView.GetComponentInChildren<Animator>(true);
            GameObject receiptObject = DeliveryView.Resolve(receiptPrefab, transform, receiptPosition);
            if (receiptObject != null)
            {
                receipt = receiptObject.GetComponent<Receipt>();
                if (receipt == null) receipt = receiptObject.AddComponent<Receipt>();
                receipt.SetNumberSprites(numberSprites, numberScale, slashSprite, questionMarkSprite);
                receipt.Initialize();
                RestoreSessionReceipt();
                receipt.Changed += OnReceiptChanged;
            }
            else Debug.LogWarning("StoreIn: Receipt 프리팹을 등록하세요.", this);

            foreach (ResourceSlot slot in resources)
                if (slot != null && slot.resourcePrefab != null && slot.resourcePosition != null && slot.resourcePrefab.GetComponent<Resources>() == null)
                    Debug.LogWarning($"{slot.resourcePrefab.name}: Resources 컴포넌트를 추가하고 건물/가격/수량/사진을 등록하세요.", this);

            CreatePackingBox(playBoxOpen);
            RefreshAvailability();
            numberSpritesDirty = false;
        }

        private void RestoreResourceViews()
        {
            foreach (ResourceSlot slot in resources)
            {
                if (slot == null || slot.resourcePrefab == null || slot.resourcePosition == null) continue;
                if (slot.definition == null) slot.definition = slot.resourcePrefab.GetComponent<Resources>();
                if (slot.definition == null) continue;
                // Unity discards NonSerialized slot caches on a script reload. Reuse
                // the existing views so lock checks recover without duplicate icons.
                if (slot.resource == null)
                {
                    foreach (Transform child in slot.resourcePosition)
                    {
                        Resources item = child.GetComponent<Resources>();
                        if (item != null && item.ResourceId == slot.definition.ResourceId)
                        {
                            slot.resource = child.gameObject;
                            break;
                        }
                    }
                    if (slot.resource == null) slot.resource = DeliveryView.Spawn(slot.resourcePrefab, slot.resourcePosition);
                    slot.resource.GetComponent<Resources>().Bind(this, slot.definition);
                }
                if (slot.padlock == null && padlockPrefab != null)
                {
                    foreach (Transform child in slot.resourcePosition)
                        if (child.name == padlockPrefab.name || child.name == padlockPrefab.name + "(Clone)")
                        {
                            slot.padlock = child.gameObject;
                            break;
                        }
                    if (slot.padlock == null) slot.padlock = DeliveryView.Spawn(padlockPrefab, slot.resourcePosition);
                }
                if (slot.price == null && slot.pricePosition != null)
                {
                    SpriteNumberLabel label = slot.pricePosition.GetComponentInChildren<SpriteNumberLabel>(true);
                    slot.price = label != null ? label.gameObject : SpriteNumberLabel.Create(slot.pricePosition, numberSprites, numberScale, slashSprite, questionMarkSprite);
                    slot.price.GetComponent<SpriteNumberLabel>().Configure(numberSprites, numberScale, slashSprite, questionMarkSprite);
                }
            }
        }

        private void Update()
        {
            if (numberSpritesDirty) ApplyNumberSprites();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            RefreshAvailability();
        }

        public void Show()
        {
            // First-show creation waits for the panel to be active before
            // setting the trigger; every later show replays it on the same box.
            InitializeContent(false);
            DeliveryWindows.Open(gameObject);
            DeliveryView.Trigger(packingBox, openTrigger);
            if (receipt != null) DeliveryMotion.SlideReceipt(receipt.transform);
            if (backgroundUpView != null) DeliveryMotion.SlideBackgroundUp(backgroundUpView.transform);
            PlayBackgroundOwner();
        }

        private void PlayBackgroundOwner()
        {
            if (backgroundOwnerAnimator == null || !backgroundOwnerAnimator.isActiveAndEnabled ||
                backgroundOwnerAnimator.runtimeAnimatorController == null) return;
            int selected = BackgroundOwnerTriggers[UnityEngine.Random.Range(0, BackgroundOwnerTriggers.Length)];
            // Restart on every show, including when the same pose is chosen twice.
            backgroundOwnerAnimator.Rebind();
            backgroundOwnerAnimator.Update(0f);
            bool found = false;
            foreach (AnimatorControllerParameter parameter in backgroundOwnerAnimator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Trigger) continue;
                if (Array.IndexOf(BackgroundOwnerTriggers, parameter.nameHash) < 0) continue;
                backgroundOwnerAnimator.ResetTrigger(parameter.nameHash);
                found |= parameter.nameHash == selected;
            }
            if (found) backgroundOwnerAnimator.SetTrigger(selected);
        }

        public void Close() { gameObject.SetActive(false); }

        public bool CanBuy(Resources resource)
        {
            if (!isActiveAndEnabled || packing || receipt == null || !receipt.CanAccept(resource)) return false;
            VillageManagement village = VillageManagement.Instance;
            return village != null && village.CurrentEnergy >= resource.EnergyPrice && DeliveryView.HasReadyBuilding(resource.BuildingPrefab);
        }

        public bool TryBuyAt(Resources resource, Vector2 screen)
        {
            if (!CanBuy(resource) || !receipt.Contains(screen) || !receipt.TryAdd(resource, VillageManagement.Instance)) return false;
            if (packingBox != null && !receipt.IsFull) DeliveryMotion.WobbleBox(packingBox.transform);
            else RefreshFullBoxMotion();
            return true;
        }

        private void RefreshFullBoxMotion()
        {
            if (packingBox == null) return;
            if (isActiveAndEnabled && !packing && receipt != null && receipt.IsFull)
                DeliveryMotion.ShakeFullBox(packingBox.transform);
            else DeliveryMotion.StopFullBoxShake(packingBox.transform);
        }

        private void RestoreSessionReceipt()
        {
            if (!Application.isPlaying) return;
            var scene = gameObject.scene;
            receiptSessionKey = (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path)
                + "|" + name.Replace("(Clone)", "").Trim();
            if (!sessionReceipts.TryGetValue(receiptSessionKey, out PendingReceipt saved)) return;
            Resources resource = null;
            foreach (ResourceSlot slot in resources)
            {
                Resources candidate = slot?.resourcePrefab != null ? slot.resourcePrefab.GetComponent<Resources>() : null;
                if (candidate != null && candidate.ResourceId == saved.resourceId) { resource = candidate; break; }
            }
            if (resource == null) resource = receipt.FindResource(saved.resourceId);
            if (resource != null) receipt.RestoreContents(resource, saved.quantity);
        }

        private void OnReceiptChanged()
        {
            if (receiptSessionKey != null && receipt != null)
            {
                if (receipt.Quantity == 0 || string.IsNullOrEmpty(receipt.SelectedResourceId))
                    sessionReceipts.Remove(receiptSessionKey);
                else sessionReceipts[receiptSessionKey] = new PendingReceipt
                {
                    resourceId = receipt.SelectedResourceId,
                    quantity = receipt.Quantity
                };
            }
            RefreshFullBoxMotion();
        }

        public bool TryPack()
        {
            if (packing || receipt == null || !receipt.IsFull || packingBox == null) return false;
            BoxIn inventory = ResolveInventory();
            if (inventory == null || !inventory.TryReserve(out object token)) return false;
            DeliveryMotion.Reset(packingBox.transform);
            packing = true;
            reservedInventory = inventory;
            reservation = token;
            packingRoutine = DeliveryRuntime.Ensure().StartCoroutine(Pack(receipt.Snapshot()));
            return true;
        }

        private IEnumerator Pack(ReceiptData data)
        {
            GameObject oldBox = packingBox;
            DeliveryView.Trigger(oldBox, packTrigger);
            yield return new WaitForSecondsRealtime(packAnimationSeconds);
            Vector3 scale = oldBox != null ? oldBox.transform.localScale : Vector3.one;
            float elapsed = 0f;
            while (elapsed < shrinkSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float factor = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.001f, shrinkSeconds));
                if (oldBox != null) oldBox.transform.localScale = scale * factor;
                if (receipt != null) receipt.SetPhotoScale(factor);
                yield return null;
            }

            bool stored = reservedInventory != null && reservedInventory.CommitReservation(reservation, data);
            if (stored)
            {
                receipt.Clear();
                DeliveryView.Remove(oldBox);
                packingBox = null;
                CreatePackingBox();
            }
            else
            {
                if (oldBox != null) oldBox.transform.localScale = scale;
                if (receipt != null) receipt.SetPhotoScale(1f);
                DeliveryView.Trigger(oldBox, openTrigger);
                if (reservedInventory != null) reservedInventory.ReleaseReservation(reservation);
            }
            reservedInventory = null;
            reservation = null;
            packing = false;
            packingRoutine = null;
            RefreshFullBoxMotion();
        }

        private void CreatePackingBox(bool playOpen = true)
        {
            if (boxPrefab == null || receipt == null) return;
            packingBox = DeliveryView.Spawn(boxPrefab, boxPosition != null ? boxPosition : receipt.BoxPosition);
            DeliveryView.BindClick(packingBox, () => TryPack());
            if (playOpen) DeliveryView.Trigger(packingBox, openTrigger);
            RefreshFullBoxMotion();
        }

        private BoxIn ResolveInventory()
        {
            if (boxIn != null && boxIn.gameObject.scene.IsValid()) return boxIn;
            foreach (Box button in FindObjectsByType<Box>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                BoxIn candidate = button.EnsurePanel();
                if (candidate != null) return boxIn = candidate;
            }
            foreach (BoxIn candidate in FindObjectsByType<BoxIn>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                return boxIn = candidate;
            Debug.LogWarning("StoreIn: Box 버튼에 BoxIn 프리팹을 등록하거나 Box In 필드를 연결하세요.", this);
            return null;
        }

        private void RefreshAvailability()
        {
            RestoreResourceViews();
            readyBuildingIds.Clear();
            IReadOnlyList<Building> buildings = DeliveryView.Buildings;
            for (int i = 0; i < buildings.Count; i++)
                if (DeliveryView.IsReady(buildings[i])) readyBuildingIds.Add(buildings[i].BuildingId);
            foreach (ResourceSlot slot in resources)
            {
                if (slot == null || slot.definition == null) continue;
                Building prefab = slot.definition.BuildingPrefab;
                bool ready = prefab != null && readyBuildingIds.Contains(prefab.BuildingId);
                if (slot.resource != null && slot.resource.activeSelf != ready) slot.resource.SetActive(ready);
                if (slot.padlock != null && slot.padlock.activeSelf == ready) slot.padlock.SetActive(!ready);
                int price = ready ? slot.definition.EnergyPrice : -1;
                if (slot.displayedPrice != price)
                {
                    DeliveryView.SetLabel(slot.price, ready ? price.ToString() : "?");
                    slot.displayedPrice = price;
                }
            }
        }

        private void OnDisable()
        {
            if (packingBox != null) DeliveryMotion.StopFullBoxShake(packingBox.transform);
        }

        private void OnDestroy()
        {
            if (receipt != null) receipt.Changed -= OnReceiptChanged;
            if (packingRoutine != null && DeliveryRuntime.Current != null) DeliveryRuntime.Current.StopCoroutine(packingRoutine);
            if (reservedInventory != null) reservedInventory.ReleaseReservation(reservation);
        }

        private void ApplyNumberSprites()
        {
            foreach (ResourceSlot slot in resources)
                if (slot != null && slot.price != null)
                    slot.price.GetComponent<SpriteNumberLabel>()?.Configure(numberSprites, numberScale, slashSprite, questionMarkSprite);
            if (receipt != null) receipt.SetNumberSprites(numberSprites, numberScale, slashSprite, questionMarkSprite);
            numberSpritesDirty = false;
        }
    }
}
