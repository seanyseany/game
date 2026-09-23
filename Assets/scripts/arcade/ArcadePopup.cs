using UnityEngine;

// Shared by the text and emoji prefabs. Scale 1 means the prefab's authored size.
[DisallowMultipleComponent]
public class ArcadePopup : MonoBehaviour
{
    public enum AnimationType { Text, Emoji }

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

    private void Awake()
    {
        fullScale = transform.localScale;
        sprites = GetComponentsInChildren<SpriteRenderer>(true);
        colors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
            colors[i] = sprites[i].color;
    }

    public void Play(Vector3 position, Transform target = null)
    {
        origin = position;
        followTarget = target;
        elapsed = 0f;
        transform.position = position;
        transform.localScale = Vector3.zero;
        SetAlpha(1f);
    }

    private void LateUpdate()
    {
        if (!RageTransformFreezeController.IsGameplayPauseActive)
            elapsed += Time.deltaTime;

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
        transform.localScale = fullScale * scale;
        if (elapsed >= grow + holdDuration + disappear)
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
