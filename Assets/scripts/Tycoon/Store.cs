using UnityEngine;
using UnityEngine.Serialization;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Tycoon/Store")]
    public sealed class Store : MonoBehaviour
    {
        [SerializeField] private GameObject storeInPrefab;
        [SerializeField, FormerlySerializedAs("panelParent"), Tooltip("패널이 표시될 위치. Canvas 안의 PanelPosition 또는 월드 위치를 등록하세요. 비우면 화면 중앙입니다.")]
        private Transform panelPosition;
        private StoreIn panel;
        private bool ownsPanel;

        private void Awake() { DeliveryView.BindClick(gameObject, Toggle); }

        public void Toggle()
        {
            if (panel == null)
            {
                if (storeInPrefab == null) { Debug.LogWarning("Store: StoreIn 프리팹을 등록하세요.", this); return; }
                ownsPanel = !storeInPrefab.scene.IsValid();
                GameObject root = DeliveryView.ResolvePanel(storeInPrefab, panelPosition);
                panel = root.GetComponent<StoreIn>();
                if (panel == null) panel = root.AddComponent<StoreIn>();
                panel.Show();
            }
            else if (panel.gameObject.activeSelf) panel.Close();
            else panel.Show();
        }

        private void OnDestroy() { if (ownsPanel && panel != null) Destroy(panel.gameObject); }
    }
}
