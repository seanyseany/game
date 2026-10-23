using System;
using UnityEngine;
using UnityEngine.UI;

namespace Tycoon
{
    // Lives on the building so closing/destroying the inventory cannot stop a delivery.
    [AddComponentMenu("")]
    public sealed class DeliveryArrival : MonoBehaviour
    {
        private const float ShrinkSeconds = 0.25f;
        private const float HoldSeconds = 3f;
        private const float FadeSeconds = 1f;
        private Building target;
        private GameObject source;
        private GameObject deliveredPrefab;
        private GameObject delivered;
        private Vector3 sourceScale;
        private SpriteRenderer[] sprites;
        private Color[] spriteColors;
        private Graphic[] graphics;
        private Color[] graphicColors;
        private Action completed;
        private float elapsed;
        private bool spawned;
        private bool disappeared;
        private bool credited;

        internal static bool Begin(boxComplete box, GameObject prefab, Building building, Action onCompleted)
        {
            if (box == null || prefab == null || building == null || box.Data == null || !box.Data.CanDeliverTo(building))
                return false;
            var root = new GameObject("DeliveryArrival");
            root.transform.SetParent(building.transform, false);
            DeliveryArrival arrival = root.AddComponent<DeliveryArrival>();
            arrival.hideFlags = HideFlags.HideInInspector;
            arrival.target = building;
            arrival.source = box.gameObject;
            arrival.deliveredPrefab = prefab;
            arrival.completed = onCompleted;
            box.transform.SetParent(root.transform, true);
            arrival.sourceScale = box.transform.localScale;
            DisableInteraction(box.gameObject);
            return true;
        }

        private static void DisableInteraction(GameObject root)
        {
            foreach (DeliveryPointer pointer in root.GetComponentsInChildren<DeliveryPointer>(true)) pointer.enabled = false;
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }

        private void LateUpdate() { Advance(Time.deltaTime); }

        private void Advance(float deltaSeconds)
        {
            if (credited) return;
            if (target == null) { DeliveryView.Remove(gameObject); return; }
            elapsed += Mathf.Max(0f, deltaSeconds);
            if (elapsed < ShrinkSeconds)
            {
                if (source != null) source.transform.localScale = sourceScale * (1f - elapsed / ShrinkSeconds);
                return;
            }
            if (!spawned)
            {
                DeliveryView.Remove(source);
                source = null;
                delivered = DeliveryView.Spawn(deliveredPrefab, target.Line2Point);
                spawned = true;
                DisableInteraction(delivered);
                sprites = delivered.GetComponentsInChildren<SpriteRenderer>(true);
                spriteColors = new Color[sprites.Length];
                for (int i = 0; i < sprites.Length; i++) spriteColors[i] = sprites[i].color;
                graphics = delivered.GetComponentsInChildren<Graphic>(true);
                graphicColors = new Color[graphics.Length];
                for (int i = 0; i < graphics.Length; i++) graphicColors[i] = graphics[i].color;
            }
            if (elapsed < ShrinkSeconds + HoldSeconds) return;
            if (!disappeared)
            {
                float alpha = 1f - Mathf.Clamp01((elapsed - ShrinkSeconds - HoldSeconds) / FadeSeconds);
                for (int i = 0; i < sprites.Length; i++)
                    if (sprites[i] != null) { Color color = spriteColors[i]; color.a *= alpha; sprites[i].color = color; }
                for (int i = 0; i < graphics.Length; i++)
                    if (graphics[i] != null) { Color color = graphicColors[i]; color.a *= alpha; graphics[i].color = color; }
                if (alpha > 0f) return;
                DeliveryView.Remove(delivered);
                delivered = null;
                disappeared = true;
            }
            // If an upgrade/move started during the visual, retain its credit
            // until the same building is ready instead of losing the order.
            if (!target.TryReceiveDelivery()) return;
            credited = true;
            Action callback = completed;
            completed = null;
            DeliveryView.Remove(gameObject);
            callback?.Invoke();
        }

        private void OnDestroy()
        {
            if (source != null) DeliveryView.Remove(source);
            if (delivered != null) DeliveryView.Remove(delivered);
        }
    }
}
