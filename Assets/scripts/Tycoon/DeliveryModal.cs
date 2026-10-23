using System.Collections.Generic;
using UnityEngine;

namespace Tycoon
{
    // Tracks the actual popup, rather than its always-visible toolbar or Canvas.
    [AddComponentMenu("")]
    public sealed class DeliveryModal : MonoBehaviour
    {
        private static readonly HashSet<DeliveryModal> panels = new HashSet<DeliveryModal>();
        internal System.Action CloseWindow;

        public static bool IsOpen
        {
            get
            {
                panels.RemoveWhere(panel => panel == null);
                foreach (DeliveryModal panel in panels)
                    if (panel.isActiveAndEnabled) return true;
                return false;
            }
        }

        public static DeliveryModal Track(GameObject panel)
        {
            if (panel == null) return null;
            DeliveryModal scope = panel.GetComponent<DeliveryModal>();
            if (scope == null) scope = panel.AddComponent<DeliveryModal>();
            scope.hideFlags = HideFlags.HideInInspector;
            panels.Add(scope);
            DeliveryRuntime.Ensure();
            return scope;
        }

        private void OnEnable() { panels.Add(this); }
        private void OnDestroy() { panels.Remove(this); DeliveryWindows.Unregister(this); }
    }
}
