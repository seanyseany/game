using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Tycoon/Box Complete")]
    public sealed class boxComplete : MonoBehaviour
    {
        [Serializable]
        public sealed class PhotoMapping
        {
            [Tooltip("예: breadBoxComplete (일반 영수증 bread 사진과 별개)")]
            public GameObject receiptPhotoPrefab;
            [Tooltip("건물 프리팹의 루트 오브젝트")]
            public GameObject buildingPrefab;
            public Transform photoPosition;
            public Building BuildingPrefab => buildingPrefab != null ? buildingPrefab.GetComponent<Building>() : null;
        }

        [SerializeField] private List<PhotoMapping> receiptPhotos = new List<PhotoMapping>();
        private readonly List<GameObject> spawnedPhotos = new List<GameObject>();
        private BoxIn inventory;
        public ReceiptData Data { get; private set; }

        public Building FindDeliveryTarget(Vector2 screen)
        {
            if (Data == null) return null;
            IReadOnlyList<Building> buildings = DeliveryView.Buildings;
            for (int i = 0; i < buildings.Count; i++)
                if (Data.CanDeliverTo(buildings[i]) && DeliveryView.Contains(buildings[i].gameObject, screen)) return buildings[i];
            return null;
        }

        public void Bind(BoxIn owner, ReceiptData data)
        {
            inventory = owner;
            Data = new ReceiptData(data.Lines);
            foreach (GameObject photo in spawnedPhotos) DeliveryView.Remove(photo);
            spawnedPhotos.Clear();
            foreach (ReceiptData.Line line in Data.Lines)
            {
                PhotoMapping mapping = receiptPhotos.Find(entry => entry != null && entry.BuildingPrefab != null &&
                    entry.BuildingPrefab.BuildingId == line.buildingId);
                GameObject prefab = mapping != null && mapping.receiptPhotoPrefab != null ? mapping.receiptPhotoPrefab : line.receiptPhotoPrefab;
                Transform anchor = mapping != null && mapping.photoPosition != null ? mapping.photoPosition : transform;
                GameObject photo = DeliveryView.Spawn(prefab, anchor);
                if (photo != null) spawnedPhotos.Add(photo);
            }
            DeliveryPointer.Bind(gameObject, (screen, finger) =>
            {
                if (inventory != null) inventory.BeginDelivery(this, screen, finger);
            });
        }
    }
}
