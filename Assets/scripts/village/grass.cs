using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class grass : MonoBehaviour
{
    [Serializable]
    public class TimeTerm
    {
        [Min(0f)] public float minDelay = 1f;
        [Min(0f)] public float maxDelay = 2f;
    }

    [Header("Movement")]
    [SerializeField] private List<TimeTerm> timeTerms = new List<TimeTerm>();
    [SerializeField] private Vector2 verticalStretchRange = new Vector2(-0.04f, 0.08f);
    [SerializeField] private float horizontalResponse = 0.6f;
    [SerializeField] private float moveSpeed = 0.35f;
    [SerializeField] private float arriveDistance = 0.005f;

    private Coroutine moveRoutine;
    private SpriteRenderer spriteRenderer;
    private Vector3 baseLocalPosition;
    private Vector3 baseLocalScale;
    private Vector3 bottomAnchorLocalPosition;
    private float spriteHeight = 1f;
    private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        CacheBasePose();
    }

    private void OnEnable()
    {
        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        CacheBasePose();
        ApplyStretch(0f);
        moveRoutine = StartCoroutine(MoveLoopRoutine());
    }

    private void OnDisable()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        ApplyStretch(0f);
    }

    private IEnumerator MoveLoopRoutine()
    {
        while (enabled && gameObject.activeInHierarchy)
        {
            float delay = GetRandomDelay();
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            float targetStretch = GetRandomStretchAmount();
            if (Mathf.Abs(targetStretch) <= Mathf.Epsilon)
                continue;

            yield return MoveToRoutine(targetStretch);
            yield return MoveToRoutine(0f);
            yield return MoveToRoutine(targetStretch * 0.5f);
            yield return MoveToRoutine(0f);
        }
    }

    private IEnumerator MoveToRoutine(float targetStretch)
    {
        while (Mathf.Abs(GetCurrentStretch() - targetStretch) > arriveDistance)
        {
            float nextStretch = Mathf.MoveTowards(
                GetCurrentStretch(),
                targetStretch,
                moveSpeed * Time.fixedDeltaTime);

            ApplyStretch(nextStretch);
            yield return waitForFixedUpdate;
        }

        ApplyStretch(targetStretch);
    }

    private void CacheBasePose()
    {
        baseLocalPosition = transform.localPosition;
        baseLocalScale = transform.localScale;

        if (spriteRenderer != null && spriteRenderer.sprite != null)
            spriteHeight = spriteRenderer.sprite.bounds.size.y;
        else
            spriteHeight = 1f;

        bottomAnchorLocalPosition = baseLocalPosition - Vector3.up * (spriteHeight * baseLocalScale.y * 0.5f);
    }

    private void ApplyStretch(float stretchAmount)
    {
        float targetScaleY = Mathf.Max(0.01f, baseLocalScale.y + stretchAmount);
        float normalizedStretch = baseLocalScale.y <= Mathf.Epsilon ? 0f : stretchAmount / baseLocalScale.y;
        float targetScaleX = Mathf.Max(0.01f, baseLocalScale.x * (1f - normalizedStretch * horizontalResponse));
        Vector3 pivotOffset = Vector3.up * (spriteHeight * targetScaleY * 0.5f);

        transform.localScale = new Vector3(targetScaleX, targetScaleY, baseLocalScale.z);
        transform.localPosition = bottomAnchorLocalPosition + pivotOffset;
    }

    private float GetCurrentStretch()
    {
        return transform.localScale.y - baseLocalScale.y;
    }

    private float GetRandomDelay()
    {
        if (timeTerms == null || timeTerms.Count == 0)
            return 0f;

        TimeTerm selectedTerm = timeTerms[UnityEngine.Random.Range(0, timeTerms.Count)];
        float minDelay = Mathf.Max(0f, selectedTerm.minDelay);
        float maxDelay = Mathf.Max(minDelay, selectedTerm.maxDelay);
        return maxDelay <= 0f ? 0f : UnityEngine.Random.Range(minDelay, maxDelay);
    }

    private float GetRandomStretchAmount()
    {
        float minRange = Mathf.Min(verticalStretchRange.x, verticalStretchRange.y);
        float maxRange = Mathf.Max(verticalStretchRange.x, verticalStretchRange.y);
        return Mathf.Abs(maxRange - minRange) <= Mathf.Epsilon
            ? minRange
            : UnityEngine.Random.Range(minRange, maxRange);
    }
}
