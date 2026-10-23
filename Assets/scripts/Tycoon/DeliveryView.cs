using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Tycoon
{
    internal static class DeliveryView
    {
        internal const string LabelCanvasName = "DeliveryTextCanvas";
        internal static IReadOnlyList<Building> Buildings => Application.isPlaying ? Building.DeliveryBuildings
            : UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
        public static GameObject Spawn(GameObject prefab, Transform anchor)
        {
            if (prefab == null || anchor == null) return null;
            GameObject instance = UnityEngine.Object.Instantiate(prefab, anchor, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.SetActive(true);
            return instance;
        }

        // Scene children are reused; project prefab assets are instantiated exactly once.
        public static GameObject Resolve(GameObject reference, Transform owner, Transform anchor = null)
        {
            if (reference == null) return null;
            if (reference.scene.IsValid()) return reference;
            GameObject instance = UnityEngine.Object.Instantiate(reference, anchor != null ? anchor : owner, false);
            if (anchor != null) instance.transform.localPosition = Vector3.zero;
            instance.SetActive(true);
            return instance;
        }

        public static GameObject ResolvePanel(GameObject reference, Transform position)
        {
            GameObject panel = Resolve(reference, null);
            if (panel == null) return null;
            if (panel.transform is RectTransform && position != null)
            {
                panel.transform.SetParent(position, false);
                panel.transform.localPosition = Vector3.zero;
                panel.transform.localRotation = Quaternion.identity;
            }
            DeliveryPanel.Ensure(panel).SetPosition(position);
            return panel;
        }

        public static void BindClick(GameObject target, Action callback)
        {
            if (target == null) return;
            Button button = target.GetComponent<Button>();
            if (button != null) button.onClick.AddListener(() => callback());
            else DeliveryPointer.Bind(target, (position, finger) => callback());
        }

        public static GameObject SpawnBadge(GameObject prefab, Transform anchor)
        {
            SpriteRenderer sprite = prefab.GetComponent<SpriteRenderer>();
            if (sprite == null || !(anchor is RectTransform) || anchor.GetComponentInParent<Canvas>() == null)
                return Spawn(prefab, anchor);
            var badge = new GameObject("BoxCount", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            badge.transform.SetParent(anchor, false);
            ((RectTransform)badge.transform).sizeDelta = new Vector2(60f, 60f);
            Image image = badge.GetComponent<Image>();
            image.sprite = sprite.sprite;
            image.color = sprite.color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return badge;
        }

        public static bool IsReady(Building building)
        {
            if (building == null || !building.isActiveAndEnabled || !building.IsPlaced || !building.gameObject.scene.IsValid())
                return false;
            Path path = building.GetComponentInParent<Path>();
            return path == null || (!path.HasActiveConstruction && path.Building == building);
        }

        public static bool HasReadyBuilding(Building prefab)
        {
            if (prefab == null) return false;
            IReadOnlyList<Building> buildings = Buildings;
            string id = prefab.BuildingId;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (IsReady(building) && building.BuildingId == id) return true;
            }
            return false;
        }

        public static Vector3 WorldPoint(Vector2 screen, float z, Camera camera = null)
        {
            if (camera == null) camera = Camera.main;
            if (camera == null) return new Vector3(screen.x, screen.y, z);
            Ray ray = camera.ScreenPointToRay(screen);
            Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, z));
            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : new Vector3(screen.x, screen.y, z);
        }

        public static void MoveToScreen(Transform target, Vector2 screen)
        {
            Canvas canvas = target.GetComponentInParent<Canvas>();
            if (canvas != null && target.parent is RectTransform parent)
            {
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screen, camera, out Vector3 world))
                    target.position = world;
            }
            else target.position = WorldPoint(screen, target.position.z);
        }

        public static bool Contains(GameObject target, Vector2 screen)
        {
            if (target == null || !target.activeInHierarchy) return false;
            if (target.transform is RectTransform rect)
            {
                Canvas canvas = target.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null
                    : canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
                return RectTransformUtility.RectangleContainsScreenPoint(rect, screen, camera);
            }
            foreach (Collider2D collider in target.GetComponentsInChildren<Collider2D>())
                if (collider.enabled && collider.OverlapPoint(WorldPoint(screen, collider.transform.position.z))) return true;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                Vector3 point = WorldPoint(screen, renderer.bounds.center.z);
                Bounds bounds = renderer.bounds;
                if (point.x >= bounds.min.x && point.x <= bounds.max.x && point.y >= bounds.min.y && point.y <= bounds.max.y)
                    return true;
            }
            return false;
        }

        public static GameObject CreateLabel(Transform anchor, string text, int sortingOrder = 1500, GameObject textPrefab = null)
        {
            // An authored Text at the registered position is used without changing its style.
            if (anchor.GetComponent<Text>() != null || anchor.GetComponent<TMP_Text>() != null)
            {
                SetLabel(anchor.gameObject, text);
                return anchor.gameObject;
            }
            if (textPrefab != null)
            {
                if (textPrefab.GetComponentInChildren<Text>(true) != null || textPrefab.GetComponentInChildren<TMP_Text>(true) != null)
                    return CreatePrefabLabel(anchor, textPrefab, text, sortingOrder);
                Debug.LogWarning("Number Text Prefab에는 UI Text 또는 TextMeshPro UI가 있는 프리팹을 등록하세요.", textPrefab);
            }
            GameObject label;
            if (anchor is RectTransform)
            {
                label = new GameObject("DeliveryText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                label.transform.SetParent(anchor, false);
                RectTransform rect = (RectTransform)label.transform;
                rect.sizeDelta = new Vector2(180f, 50f);
                Text ui = label.GetComponent<Text>();
                ui.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                ui.fontSize = 26;
                ui.alignment = TextAnchor.MiddleCenter;
                ui.color = new Color(0.2f, 0.12f, 0.08f);
                ui.raycastTarget = false;
                ui.text = text;
            }
            else
            {
                label = new GameObject("DeliveryText", typeof(TextMesh));
                label.transform.SetParent(anchor, false);
                TextMesh mesh = label.GetComponent<TextMesh>();
                mesh.anchor = TextAnchor.MiddleCenter;
                mesh.alignment = TextAlignment.Center;
                mesh.fontSize = 64;
                mesh.characterSize = 0.075f;
                mesh.color = new Color(0.2f, 0.12f, 0.08f);
                mesh.text = text;
                label.GetComponent<MeshRenderer>().sortingOrder = sortingOrder;
            }
            return label;
        }

        private static GameObject CreatePrefabLabel(Transform anchor, GameObject prefab, string text, int sortingOrder)
        {
            Transform parent = anchor;
            // The supplied store art is made of world sprites; UI text needs its own canvas.
            if (anchor.GetComponentInParent<Canvas>() == null && prefab.GetComponent<Canvas>() == null)
            {
                var canvasObject = new GameObject(LabelCanvasName, typeof(RectTransform), typeof(Canvas));
                canvasObject.transform.SetParent(anchor, false);
                canvasObject.transform.localScale = Vector3.one * 0.01f;
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true;
                SortingGroup group = anchor.GetComponentInParent<SortingGroup>();
                canvas.sortingOrder = group != null ? group.sortingOrder + 1 : sortingOrder;
                if (group != null) canvas.sortingLayerID = group.sortingLayerID;
                RectTransform templateRect = prefab.transform as RectTransform;
                ((RectTransform)canvasObject.transform).sizeDelta = templateRect != null
                    ? new Vector2(Mathf.Max(1f, templateRect.rect.width), Mathf.Max(1f, templateRect.rect.height))
                    : new Vector2(180f, 50f);
                parent = canvasObject.transform;
            }
            GameObject label = Spawn(prefab, parent);
            if (label.transform is RectTransform rect) rect.anchoredPosition = Vector2.zero;
            foreach (Graphic graphic in label.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            SetLabel(label, text);
            DeliveryPanel panel = anchor.GetComponentInParent<DeliveryPanel>();
            if (panel != null) panel.InvalidateTextCanvases();
            return label;
        }

        public static void RemoveLabel(GameObject label)
        {
            if (label == null) return;
            Transform parent = label.transform.parent;
            Remove(parent != null && parent.name == LabelCanvasName ? parent.gameObject : label);
        }

        public static void SetLabel(GameObject label, string value)
        {
            if (label == null) return;
            SpriteNumberLabel sprites = label.GetComponent<SpriteNumberLabel>();
            if (sprites != null) { sprites.SetValue(value); return; }
            Text ui = label.GetComponentInChildren<Text>(true);
            if (ui != null) ui.text = value;
            TMP_Text tmp = label.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) tmp.text = value;
            TextMesh mesh = label.GetComponent<TextMesh>();
            if (mesh != null) mesh.text = value;
        }

        public static void Trigger(GameObject target, string trigger)
        {
            if (target == null || string.IsNullOrEmpty(trigger)) return;
            Animator animator = target.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == trigger)
                {
                    animator.SetTrigger(trigger);
                    return;
                }
        }

        public static void Remove(GameObject target)
        {
            if (target == null) return;
            target.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
