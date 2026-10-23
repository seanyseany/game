using UnityEngine;
using System;
using System.Collections.Generic;

namespace Tycoon
{
    // Opening a window is explicit: preparing a hidden BoxIn for packing must not
    // close the store. Internal resource/receipt clicks keep their own window open.
    public static class DeliveryWindows
    {
        private static readonly List<DeliveryModal> windows = new List<DeliveryModal>();

        public static void Register(GameObject panel, Action close)
        {
            DeliveryModal window = DeliveryModal.Track(panel);
            if (window == null) return;
            window.CloseWindow = close;
            if (!windows.Contains(window)) windows.Add(window);
        }

        internal static void Unregister(DeliveryModal window) { windows.Remove(window); }

        internal static void Open(GameObject panel)
        {
            CloseOthers(panel);
            panel.SetActive(true);
        }

        public static void CloseAll() { CloseOthers(null); }

        public static void NotifyUiPressed(GameObject pressed)
        {
            if (pressed == null) return;
            // These buttons toggle on release; closing on press would reopen an
            // already visible window when its own toggle is released.
            if (pressed.GetComponentInParent<Store>() != null || pressed.GetComponentInParent<Box>() != null) return;
            DeliveryModal window = pressed.GetComponentInParent<DeliveryModal>();
            CloseOthers(window != null && window.CloseWindow != null ? window.gameObject : null);
        }

        private static void CloseOthers(GameObject keep)
        {
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                DeliveryModal window = windows[i];
                if (window == null) { windows.RemoveAt(i); continue; }
                if (window.gameObject != keep) window.CloseWindow?.Invoke();
            }
        }
    }
}
