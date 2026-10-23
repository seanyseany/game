using UnityEngine;

namespace Tycoon
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class DeliveryMotion : MonoBehaviour
    {
        private enum Motion { None, ResourceShake, ReceiptEntrance, BoxWobble, InsufficientEnergy, FullBoxShake, BackgroundEntrance }
        private Motion motion;
        private float elapsed;
        private float duration;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 localCenter;

        public bool IsPlaying => motion != Motion.None;
        public bool IsFullBoxShaking => motion == Motion.FullBoxShake;

        public static void ShakeResource(Transform target) { Begin(target, Motion.ResourceShake, 0.3f); }
        public static void ShakeInsufficientEnergy(Transform target) { Begin(target, Motion.InsufficientEnergy, 0.3f); }
        public static void SlideReceipt(Transform target) { Begin(target, Motion.ReceiptEntrance, 1f); }
        public static void SlideBackgroundUp(Transform target) { Begin(target, Motion.BackgroundEntrance, 1f); }
        public static void WobbleBox(Transform target) { Begin(target, Motion.BoxWobble, 0.35f); }
        public static void ShakeFullBox(Transform target) { Begin(target, Motion.FullBoxShake, 0.7f); }

        public static void StopFullBoxShake(Transform target)
        {
            if (target != null && target.TryGetComponent(out DeliveryMotion effect) && effect.IsFullBoxShaking) effect.Stop();
        }

        public static void Reset(Transform target)
        {
            if (target != null && target.TryGetComponent(out DeliveryMotion effect)) effect.Stop();
        }

        private static void Begin(Transform target, Motion kind, float seconds)
        {
            if (target == null) return;
            DeliveryMotion effect = target.GetComponent<DeliveryMotion>();
            if (effect == null) effect = target.gameObject.AddComponent<DeliveryMotion>();
            if (kind == Motion.FullBoxShake && effect.IsFullBoxShaking) return;
            effect.hideFlags |= HideFlags.HideInInspector;
            effect.Stop();
            effect.restPosition = target.localPosition;
            effect.restRotation = target.localRotation;
            effect.localCenter = kind == Motion.ResourceShake || kind == Motion.BoxWobble ? effect.FindVisualCenter() : Vector3.zero;
            effect.elapsed = 0f;
            effect.duration = seconds;
            effect.motion = kind;
            effect.Apply(0f);
        }

        private Vector3 FindVisualCenter()
        {
            if (transform is RectTransform rect) return rect.rect.center;
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return Vector3.zero;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return transform.InverseTransformPoint(bounds.center);
        }

        private void LateUpdate() { Advance(Time.unscaledDeltaTime); }

        private void Advance(float deltaSeconds)
        {
            if (!IsPlaying) return;
            elapsed += Mathf.Max(0f, deltaSeconds);
            if (motion == Motion.FullBoxShake) elapsed = Mathf.Repeat(elapsed, duration);
            else if (elapsed >= duration) { Stop(); return; }
            Apply(elapsed / duration);
        }

        private void Apply(float progress)
        {
            if (motion == Motion.FullBoxShake)
            {
                float amplitude = transform is RectTransform ? 6f : 0.1f;
                transform.localPosition = restPosition + Vector3.right * (Mathf.Sin(progress * Mathf.PI * 2f) * amplitude);
                return;
            }
            if (motion == Motion.InsufficientEnergy)
            {
                float amplitude = transform is RectTransform ? 8f : 0.12f;
                float offset = Mathf.Sin(progress * Mathf.PI * 6f) * amplitude * (1f - progress);
                transform.localPosition = restPosition + Vector3.right * offset;
                return;
            }
            if (motion == Motion.ReceiptEntrance || motion == Motion.BackgroundEntrance)
            {
                float remaining = Mathf.Pow(1f - progress, 3f);
                float offsetY = motion == Motion.ReceiptEntrance ? 1f : -0.3f;
                transform.localPosition = restPosition + Vector3.up * (offsetY * remaining);
                return;
            }

            float cycles = motion == Motion.ResourceShake ? 3f : 2f;
            float degrees = motion == Motion.ResourceShake ? 7f : 9f;
            float angle = Mathf.Sin(progress * Mathf.PI * 2f * cycles) * degrees * (1f - progress);
            Quaternion rotation = restRotation * Quaternion.Euler(0f, 0f, angle);
            Vector3 center = Vector3.Scale(localCenter, transform.localScale);
            transform.localRotation = rotation;
            transform.localPosition = restPosition + restRotation * center - rotation * center;
        }

        private void Stop()
        {
            if (!IsPlaying) return;
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
            motion = Motion.None;
        }

        private void OnDisable() { Stop(); }
    }
}
