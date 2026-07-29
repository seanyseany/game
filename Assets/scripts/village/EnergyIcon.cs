using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class EnergyIcon : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Color baseColor = Color.white;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;
    }

    public void Play(float duration, float moveSpeed)
    {
        if (duration <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        StopAllCoroutines();
        StartCoroutine(PlayRoutine(duration, moveSpeed));
    }

    private IEnumerator PlayRoutine(float duration, float moveSpeed)
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        float elapsed = 0f;
        Vector3 startPosition = transform.position;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            transform.position = startPosition + Vector3.up * (moveSpeed * elapsed);

            if (spriteRenderer != null)
            {
                Color color = baseColor;
                color.a = 1f - t;
                spriteRenderer.color = color;
            }

            yield return null;
        }

        Destroy(gameObject);
    }
}
