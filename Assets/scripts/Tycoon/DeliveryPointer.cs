using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Tycoon
{
    // Sprite prefabs need neither colliders nor a PhysicsRaycaster. UI graphics use the
    // existing EventSystem raycast so buttons in front of the store still block it.
    [AddComponentMenu("")]
    public sealed class DeliveryPointer : MonoBehaviour
    {
        private static readonly List<DeliveryPointer> targets = new List<DeliveryPointer>();
        private static readonly List<RaycastResult> uiHits = new List<RaycastResult>(32);
        private static PointerEventData pointerData;
        private static EventSystem pointerEventSystem;
        private Action<Vector2, int> pressed;

        public static void Bind(GameObject target, Action<Vector2, int> callback)
        {
            DeliveryPointer pointer = target.GetComponent<DeliveryPointer>();
            if (pointer == null) pointer = target.AddComponent<DeliveryPointer>();
            pointer.pressed = callback;
            DeliveryRuntime.Ensure();
        }

        private void OnEnable() { if (!targets.Contains(this)) targets.Add(this); }
        private void OnDisable() { targets.Remove(this); }

        internal static GameObject FindUiTarget(Vector2 screen) { return FindUiTargetForPanel(screen, null); }

        private static GameObject FindUiTargetForPanel(Vector2 screen, DeliveryPointer panelTarget)
        {
            if (EventSystem.current != null)
            {
                if (pointerData == null || pointerEventSystem != EventSystem.current)
                {
                    pointerEventSystem = EventSystem.current;
                    pointerData = new PointerEventData(pointerEventSystem);
                }
                pointerData.Reset();
                pointerData.position = screen;
                uiHits.Clear();
                EventSystem.current.RaycastAll(pointerData, uiHits);
                try
                {
                    foreach (RaycastResult result in uiHits)
                    {
                        if (result.module is UnityEngine.UI.GraphicRaycaster)
                        {
                            // A building's gauge/label is decoration, not a UI
                            // window. It must not steal that building's long press.
                            bool worldLabel = result.gameObject.GetComponentInParent<Building>() != null ||
                                result.gameObject.GetComponentInParent<Oxygen>() != null ||
                                result.gameObject.GetComponentInParent<O2Icon>() != null;
                            if (worldLabel &&
                                result.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() == null &&
                                result.gameObject.GetComponentInParent<DeliveryModal>() == null)
                                continue;
                            // Sprite panels also contain UI labels and can overlap
                            // decorative HUD images. These have no interaction to
                            // handle and must not swallow a resource or box press.
                            if (panelTarget != null && IsPanelTarget(panelTarget) &&
                                CanPassDecoration(result.gameObject, panelTarget))
                                continue;
                            return result.gameObject;
                        }
                    }
                }
                finally { uiHits.Clear(); }
            }
            return null;
        }

        internal static DeliveryPointer Find(Vector2 screen)
        {
            return Resolve(screen, out _);
        }

        internal static DeliveryPointer Resolve(Vector2 screen, out GameObject ui)
        {
            DeliveryPointer best = null;
            long bestOrder = long.MinValue;
            foreach (DeliveryPointer target in targets)
            {
                if (target == null || target.pressed == null || !DeliveryView.Contains(target.gameObject, screen)) continue;
                long order = 0;
                Renderer ownRenderer = target.GetComponent<Renderer>();
                Renderer[] renderers = ownRenderer != null ? new[] { ownRenderer } : target.GetComponentsInChildren<Renderer>();
                foreach (Renderer renderer in renderers)
                    order = Math.Max(order, (long)SortingLayer.GetLayerValueFromID(renderer.sortingLayerID) * 100000L + renderer.sortingOrder);
                SortingGroup group = null;
                foreach (SortingGroup ancestor in target.GetComponentsInParent<SortingGroup>())
                    if (ancestor.isActiveAndEnabled) { group = ancestor; break; }
                // A disabled temporary group (after cancelling a box drag) does
                // not replace the active panel group used to draw this item.
                if (group != null)
                    order += ((long)SortingLayer.GetLayerValueFromID(group.sortingLayerID) * 100000L + group.sortingOrder) * 100000L;
                // A child interaction wins over its panel backdrop when orders match.
                if (best == null || order > bestOrder || (order == bestOrder && target.transform.IsChildOf(best.transform)))
                { best = target; bestOrder = order; }
            }
            ui = FindUiTargetForPanel(screen, best);
            return ui != null ? ui.GetComponentInParent<DeliveryPointer>() : best;
        }

        private static bool IsPanelTarget(DeliveryPointer target)
        {
            return target.GetComponentInParent<StoreIn>() != null || target.GetComponentInParent<BoxIn>() != null;
        }

        private static bool CanPassDecoration(GameObject graphic, DeliveryPointer panelTarget)
        {
            if (graphic.GetComponentInParent<DeliveryPointer>() != null ||
                ExecuteEvents.GetEventHandler<IPointerDownHandler>(graphic) != null ||
                ExecuteEvents.GetEventHandler<IPointerClickHandler>(graphic) != null ||
                ExecuteEvents.GetEventHandler<IBeginDragHandler>(graphic) != null ||
                ExecuteEvents.GetEventHandler<IDragHandler>(graphic) != null ||
                ExecuteEvents.GetEventHandler<IScrollHandler>(graphic) != null)
                return false;
            // A different popup's backdrop still blocks the panel underneath.
            DeliveryModal graphicPanel = graphic.GetComponentInParent<DeliveryModal>();
            return graphicPanel == null || graphicPanel == panelTarget.GetComponentInParent<DeliveryModal>();
        }

        internal void Press(Vector2 screen, int finger) { pressed?.Invoke(screen, finger); }
    }
}
