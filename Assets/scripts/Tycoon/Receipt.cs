using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Tycoon/Receipt")]
    public sealed class Receipt : MonoBehaviour
    {
        [Serializable]
        public sealed class ResourcePhotos
        {
            public GameObject resourcePrefab;
            [Tooltip("드래그 1회당 사진 하나. 한 위치만 넣으면 대표 사진 하나로 표시합니다.")]
            public List<Transform> photoPositions = new List<Transform>();
        }

        [SerializeField] private List<ResourcePhotos> resourcePhotos = new List<ResourcePhotos>();
        [SerializeField] private Transform boxPosition;
        [SerializeField] private Transform quantityPosition;
        [SerializeField, Tooltip("비워두면 영수증 전체가 드롭 영역")] private GameObject dropArea;

        private readonly List<GameObject> photos = new List<GameObject>();
        private readonly List<Vector3> photoScales = new List<Vector3>();
        private Resources selectedResource;
        private int quantity;
        private GameObject quantityLabel;
        private Sprite[] numberSprites;
        private float numberScale = 1f;
        private Sprite slashSprite;
        private Sprite questionMarkSprite;

        public event Action Changed;
        public int Quantity => quantity;
        internal string SelectedResourceId => selectedResource != null ? selectedResource.ResourceId : null;
        public int RequiredQuantity => selectedResource != null ? selectedResource.RequiredQuantity : 0;
        public bool IsFull => selectedResource != null && quantity >= RequiredQuantity;
        public Transform BoxPosition => boxPosition != null ? boxPosition : transform;

        public void Initialize()
        {
            RefreshQuantity();
        }

        public void SetNumberSprites(Sprite[] sprites, float scale, Sprite slash = null, Sprite question = null)
        {
            numberSprites = sprites;
            numberScale = scale;
            slashSprite = slash;
            questionMarkSprite = question;
            if (quantityLabel != null) quantityLabel.GetComponent<SpriteNumberLabel>()?.Configure(sprites, scale, slash, question);
        }

        public bool Contains(Vector2 screen) => DeliveryView.Contains(dropArea != null ? dropArea : gameObject, screen);

        public bool CanAccept(Resources resource)
        {
            return resource != null && resource.BuildingPrefab != null && !IsFull &&
                (selectedResource == null || selectedResource.ResourceId == resource.ResourceId);
        }

        // Energy is charged only after a valid drop and immediately before the item is added.
        public bool TryAdd(Resources resource, VillageManagement village)
        {
            if (village == null || !CanAccept(resource) || !DeliveryView.HasReadyBuilding(resource.BuildingPrefab)) return false;
            if (!village.TrySpendEnergy(resource.EnergyPrice)) return false;
            if (selectedResource == null) selectedResource = resource;
            quantity++;
            AddPhoto(resource);
            RefreshQuantity();
            Changed?.Invoke();
            return true;
        }

        public ReceiptData Snapshot()
        {
            if (!IsFull) return null;
            return new ReceiptData(new[] { new ReceiptData.Line
            {
                resourceId = selectedResource.ResourceId,
                buildingId = selectedResource.BuildingPrefab.BuildingId,
                quantity = quantity,
                receiptPhotoPrefab = selectedResource.ReceiptPhotoPrefab
            }});
        }

        public void Clear()
        {
            foreach (GameObject photo in photos) DeliveryView.Remove(photo);
            photos.Clear();
            photoScales.Clear();
            selectedResource = null;
            quantity = 0;
            RefreshQuantity();
            Changed?.Invoke();
        }

        // Rebuild already-paid items without charging energy or requiring the
        // building to have finished its scene initialization yet.
        internal void RestoreContents(Resources resource, int savedQuantity)
        {
            Clear();
            if (resource == null || savedQuantity <= 0) return;
            selectedResource = resource;
            int count = Mathf.Min(savedQuantity, resource.RequiredQuantity);
            for (quantity = 1; quantity <= count; quantity++) AddPhoto(resource);
            quantity = count;
            RefreshQuantity();
            Changed?.Invoke();
        }

        internal Resources FindResource(string id)
        {
            foreach (ResourcePhotos layout in resourcePhotos)
            {
                Resources resource = layout?.resourcePrefab != null ? layout.resourcePrefab.GetComponent<Resources>() : null;
                if (resource != null && resource.ResourceId == id) return resource;
            }
            return null;
        }

        public void SetPhotoScale(float scale)
        {
            for (int i = 0; i < photos.Count; i++)
                if (photos[i] != null) photos[i].transform.localScale = photoScales[i] * scale;
        }

        private void AddPhoto(Resources resource)
        {
            if (resource.ReceiptPhotoPrefab == null) return;
            ResourcePhotos layout = resourcePhotos.Find(entry => entry != null && entry.resourcePrefab != null &&
                entry.resourcePrefab.GetComponent<Resources>() != null &&
                entry.resourcePrefab.GetComponent<Resources>().ResourceId == resource.ResourceId);
            Transform anchor = transform;
            if (layout != null && layout.photoPositions.Count > 0)
            {
                int index = quantity - 1;
                if (index >= layout.photoPositions.Count) return;
                anchor = layout.photoPositions[index];
            }
            else if (photos.Count > 0) return;
            GameObject photo = DeliveryView.Spawn(resource.ReceiptPhotoPrefab, anchor);
            if (photo == null) return;
            photos.Add(photo);
            photoScales.Add(photo.transform.localScale);
        }

        private void RefreshQuantity()
        {
            InitializeLabelIfNeeded();
            DeliveryView.SetLabel(quantityLabel, selectedResource == null ? "0" : $"{quantity}/{RequiredQuantity}");
        }

        private void InitializeLabelIfNeeded()
        {
            if (quantityLabel == null) quantityLabel = SpriteNumberLabel.Create(quantityPosition != null ? quantityPosition : transform, numberSprites, numberScale, slashSprite, questionMarkSprite);
        }
    }
}
