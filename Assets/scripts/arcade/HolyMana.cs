using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class holyMana : MonoBehaviour, IReinitializable
{
    [Header("HolyMana")]
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

        // 꺼진 phase면 비활성
        if (GameData.Instance.currentHolyManaDisabled)
        {
            gameObject.SetActive(false);
            return;
        }

        // 일단 1개만 쓰니까 그냥 true
        gameObject.SetActive(true);
    }

    public void Collect()
    {
        if (collected) return;
        collected = true;

        if (col != null) col.enabled = false;

        // ✅ Holy 트리거
        if (GameData.Instance != null)
            GameData.Instance.CollectHolyMana();

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

        // holyMana는 풀링 없어도 되고 있어도 됨
        if (ObjectPool.Instance != null && ObjectPool.Instance.HasPool("HolyMana"))
            ObjectPool.Instance.ReturnToPool("HolyMana", gameObject);
        else
            Destroy(gameObject);
    }
}
