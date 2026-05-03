using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Mana : MonoBehaviour, IReinitializable
{
    [Header("Numbering")]
    [Range(1, 2)] public int manaNumber = 1;

    [Header("Collect Animation")]
    public float fadeDuration = 0.5f;
    public float floatUpDistance = 0.5f;

    private SpriteRenderer sr;
    private Collider2D col;
    private bool collected = false;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    public void Reinit()
    {
        collected = false;

        if (col != null) col.enabled = true;

        if (sr != null)
        {
            var c = sr.color;
            sr.color = new Color(c.r, c.g, c.b, 1f);
        }

        if (GameData.Instance == null) return;

        if (GameData.Instance.currentManaDisabled)
        {
            gameObject.SetActive(false);
            return;
        }

        int target = GameData.Instance.currentManaNumber;
        gameObject.SetActive(manaNumber == target);
    }

    public void Collect()
    {
        if (collected) return;
        collected = true;

        col.enabled = false;
        StartCoroutine(FadeAndFloatUp());
    }

    private IEnumerator FadeAndFloatUp()
    {
        float elapsed = 0f;
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.up * floatUpDistance;
        Color startColor = sr.color;

        while (elapsed < fadeDuration)
        {
            float t = elapsed / fadeDuration;
            transform.position = Vector3.Lerp(startPos, endPos, t);
            sr.color = new Color(startColor.r, startColor.g, startColor.b, 1f - t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        sr.color = new Color(startColor.r, startColor.g, startColor.b, 0f);

        if (ObjectPool.Instance != null)
            ObjectPool.Instance.ReturnToPool("Mana", gameObject);
        else
            Destroy(gameObject);
    }
}
