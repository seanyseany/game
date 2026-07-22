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
    [SerializeField] private Vector2 moveRange = new Vector2(0.05f, 0.12f);
    [SerializeField] private float moveSpeed = 0.35f;
    [SerializeField] private float arriveDistance = 0.005f;

    private Coroutine moveRoutine;
    private Vector3 baseLocalPosition;
    private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

    private void Awake()
    {
        baseLocalPosition = transform.localPosition;
    }

    private void OnEnable()
    {
        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        baseLocalPosition = transform.localPosition;
        moveRoutine = StartCoroutine(MoveLoopRoutine());
    }

    private void OnDisable()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        transform.localPosition = baseLocalPosition;
    }

    private IEnumerator MoveLoopRoutine()
    {
        while (enabled && gameObject.activeInHierarchy)
        {
            float delay = GetRandomDelay();
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            float targetOffset = GetRandomVerticalOffset();
            if (Mathf.Abs(targetOffset) <= Mathf.Epsilon)
                continue;

            Vector3 targetLocalPosition = baseLocalPosition + Vector3.up * targetOffset;
            yield return MoveToRoutine(targetLocalPosition);
            yield return MoveToRoutine(baseLocalPosition);
        }
    }

    private IEnumerator MoveToRoutine(Vector3 targetLocalPosition)
    {
        targetLocalPosition.z = baseLocalPosition.z;

        while (Vector3.Distance(transform.localPosition, targetLocalPosition) > arriveDistance)
        {
            transform.localPosition = Vector3.MoveTowards(
                transform.localPosition,
                targetLocalPosition,
                moveSpeed * Time.fixedDeltaTime);

            yield return waitForFixedUpdate;
        }

        transform.localPosition = targetLocalPosition;
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

    private float GetRandomVerticalOffset()
    {
        float minRange = Mathf.Min(Mathf.Abs(moveRange.x), Mathf.Abs(moveRange.y));
        float maxRange = Mathf.Max(Mathf.Abs(moveRange.x), Mathf.Abs(moveRange.y));
        float distance = maxRange <= 0f ? 0f : UnityEngine.Random.Range(minRange, maxRange);
        return UnityEngine.Random.value < 0.5f ? -distance : distance;
    }
}
