using UnityEngine;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Tycoon/Packed Box")]
    public sealed class PackedBox : MonoBehaviour
    {
        public ReceiptData Data { get; private set; }

        public void Bind(ReceiptData receipt) { Data = new ReceiptData(receipt.Lines); }

        public Building FindDeliveryTarget(Vector2 screen)
        {
            if (Data == null) return null;
            var buildings = DeliveryView.Buildings;
            for (int i = 0; i < buildings.Count; i++)
                if (Data.CanDeliverTo(buildings[i]) && DeliveryView.Contains(buildings[i].gameObject, screen)) return buildings[i];
            return null;
        }
    }
}
