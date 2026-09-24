using UnityEngine;

// Shared by the text and emoji prefabs. Scale 1 means the prefab's authored size.
[DisallowMultipleComponent]
public class ArcadePopup : MonoBehaviour
{
    public enum AnimationType { Text, Emoji, RageMode }

    [SerializeField] private AnimationType animationType;
    [SerializeField, Min(0.01f)] private float growDuration = 0.3f;
    [SerializeField, Min(0f)] private float holdDuration = 0.2f;
    [SerializeField, Min(0.01f)] private float disappearDuration = 0.5f;
    [SerializeField] private float riseDistance = 0.5f;

    private SpriteRenderer[] sprites;
    private Color[] colors;
    private Vector3 fullScale;
    private Vector3 origin;
    private Transform followTarget;
    private float elapsed;
    private float scaleMultiplier = 1f;

    public bool IsEmoji => animationType == AnimationType.Emoji;

    private void Awake()
    {
        fullScale = transform.localScale;
        sprites = GetComponentsInChildren<SpriteRenderer>(true);
        colors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
            colors[i] = sprites[i].color;
    }

    public void Play(Vector3 position, Transform target = null, float sizeMultiplier = 1f)
    {
        origin = position;
        followTarget = target;
        elapsed = 0f;
        scaleMultiplier = Mathf.Max(0f, sizeMultiplier);
        transform.position = position;
        transform.localScale = animationType == AnimationType.RageMode
            ? fullScale * scaleMultiplier
            : Vector3.zero;
        SetAlpha(1f);
    }

    private void LateUpdate()
    {
        // RageMode is part of the rage transformation presentation, so it must
        // keep playing while the transformation pauses gameplay.
        if (animationType == AnimationType.RageMode || !RageTransformFreezeController.IsGameplayPauseActive)
            elapsed += Time.deltaTime;

        if (animationType == AnimationType.RageMode)
        {
            UpdateRageMode();
            return;
        }

        Vector3 position = followTarget != null ? followTarget.position : origin;
        float grow = Mathf.Max(0.01f, growDuration);
        float disappear = Mathf.Max(0.01f, disappearDuration);
        float exitProgress = Mathf.Clamp01((elapsed - grow - holdDuration) / disappear);
        float scale = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / grow));

        if (animationType == AnimationType.Text)
        {
            position.y += riseDistance * exitProgress;
            SetAlpha(1f - exitProgress);
        }
        else
        {
            scale *= 1f - Mathf.SmoothStep(0f, 1f, exitProgress);
        }

        transform.position = position;
        transform.localScale = fullScale * scale * scaleMultiplier;
        if (elapsed >= grow + holdDuration + disappear)
            Destroy(gameObject);
    }

    private void UpdateRageMode()
    {
        float enterDuration = Mathf.Max(0.01f, growDuration);
        float exitDuration = Mathf.Max(0.01f, disappearDuration);
        float exitStart = enterDuration + holdDuration;
        Vector3 middle = origin + Vector3.right * 2f;
        Vector3 end = origin + Vector3.right * 4f;

        if (elapsed < enterDuration)
        {
            float progress = Mathf.Clamp01(elapsed / enterDuration);
            float easedProgress = 1f - Mathf.Pow(1f - progress, 3f);
            transform.position = Vector3.Lerp(origin, middle, easedProgress);
            SetAlpha(1f);
        }
        else if (elapsed < exitStart)
        {
            transform.position = middle;
            SetAlpha(1f);
        }
        else
        {
            float progress = Mathf.Clamp01((elapsed - exitStart) / exitDuration);
            float easedProgress = progress * progress * progress;
            transform.position = Vector3.Lerp(middle, end, easedProgress);
            SetAlpha(1f - progress);
        }

        transform.localScale = fullScale * scaleMultiplier;
        if (elapsed >= exitStart + exitDuration)
            Destroy(gameObject);
    }

    private void SetAlpha(float alpha)
    {
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] == null) continue;
            Color color = colors[i];
            color.a *= alpha;
            sprites[i].color = color;
        }
    }
}
