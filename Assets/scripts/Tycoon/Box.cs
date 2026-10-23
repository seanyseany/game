using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Tycoon/Box")]
    public sealed class Box : MonoBehaviour
    {
        [SerializeField] private GameObject boxInPrefab;
        [SerializeField, FormerlySerializedAs("panelParent"), Tooltip("패널이 표시될 위치. Canvas 안의 PanelPosition 또는 월드 위치를 등록하세요. 비우면 화면 중앙입니다.")]
        private Transform panelPosition;
        [SerializeField, Tooltip("0번 = 숫자1, 1번 = 숫자2, ... 6번 = 숫자7")]
        private List<GameObject> numberPrefabs = new List<GameObject>(new GameObject[7]);
        [SerializeField] private Transform numberPosition;

        private BoxIn panel;
        private GameObject badge;
        private bool ownsPanel;

        private void Awake() { DeliveryView.BindClick(gameObject, Toggle); }
        private void Start() { EnsurePanel(); }

        public BoxIn EnsurePanel()
        {
            if (panel != null) return panel;
            if (boxInPrefab == null) return null;
            ownsPanel = !boxInPrefab.scene.IsValid();
            GameObject root = DeliveryView.ResolvePanel(boxInPrefab, panelPosition);
            panel = root.GetComponent<BoxIn>();
            if (panel == null) panel = root.AddComponent<BoxIn>();
            panel.Initialize();
            panel.Changed += UpdateBadge;
            panel.Close();
            UpdateBadge();
            return panel;
        }

        public void Toggle()
        {
            BoxIn inventory = EnsurePanel();
            if (inventory == null) { Debug.LogWarning("Box: BoxIn 프리팹을 등록하세요.", this); return; }
            if (inventory.IsDelivering) return;
            if (inventory.gameObject.activeSelf) inventory.Close();
            else inventory.Show();
        }

        private void UpdateBadge()
        {
            DeliveryView.Remove(badge);
            badge = null;
            if (panel == null || panel.Count == 0 || numberPosition == null) return;
            int index = panel.Count - 1;
            badge = index < numberPrefabs.Count && numberPrefabs[index] != null
                ? DeliveryView.SpawnBadge(numberPrefabs[index], numberPosition)
                : DeliveryView.CreateLabel(numberPosition, panel.Count.ToString());
        }

        private void OnDestroy()
        {
            if (panel != null) panel.Changed -= UpdateBadge;
            if (ownsPanel && panel != null) Destroy(panel.gameObject);
        }
    }
}
