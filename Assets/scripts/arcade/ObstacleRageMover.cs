using UnityEngine;
using System.Collections;

public class ObstacleRageMover : MonoBehaviour
{
    [Header("Move Settings")]
    public float moveDistance = 10f;
    public bool isTop = true;

    private float moveDuration = 2f;
    private float returnMoveDuration = 4f;

    private float originalY;
    private Coroutine moveCo;
    private bool originalYSet = false;

    private float TargetY => originalY + (isTop ? moveDistance : -moveDistance);

    void OnEnable()
    {
        if (!originalYSet)
        {
            originalY = transform.position.y;
            originalYSet = true;
        }
        else
        {
            // 풀/재스폰 시 원위치 정렬
            transform.position = new Vector3(transform.position.x, originalY, transform.position.z);
        }
        // ✅ Holy 이벤트
        GameData.OnHolyStart += HandleExpandStart;
        GameData.OnHolyEnd   += HandleExpandEnd;

        // ✅ Rage 이벤트도 같이 구독해야 Rage 때 움직임
        GameData.OnRageStart += HandleExpandStart;
        GameData.OnRageEnd   += HandleExpandEnd;

        // ✅ 이미 Holy/Rage 상태면 즉시 벌림
        if (GameData.Instance != null &&
            (GameData.Instance.holyActive || GameData.Instance.rageMode))
        {
            StartMoveTo(TargetY, 0f);
        }
    }

    void OnDisable()
    {
        GameData.OnHolyStart -= HandleExpandStart;
        GameData.OnHolyEnd   -= HandleExpandEnd;

        GameData.OnRageStart -= HandleExpandStart;
        GameData.OnRageEnd   -= HandleExpandEnd;

        if (moveCo != null)
        {
            StopCoroutine(moveCo);
            moveCo = null;
        }

        // 풀로 돌아갈 때 원상복구
        var pos = transform.position;
        transform.position = new Vector3(pos.x, originalY, pos.z);
    }

    private void HandleExpandStart()
    {
        StartMoveTo(TargetY, moveDuration);
    }

    private void HandleExpandEnd()
    {
        // ✅ Holy 또는 Rage 중 하나라도 살아있으면 복귀하면 안됨
        if (GameData.Instance != null &&
            (GameData.Instance.holyActive || GameData.Instance.rageMode))
            return;

        StartMoveTo(originalY, returnMoveDuration);
    }

    private void StartMoveTo(float targetY, float duration)
    {
        if (moveCo != null) StopCoroutine(moveCo);
        moveCo = StartCoroutine(MoveYTo(targetY, duration));
    }

    private IEnumerator MoveYTo(float targetY, float duration)
    {
        float startY = transform.position.y;
        float t = 0f;
        duration = Mathf.Max(0.0001f, duration);

        while (t < duration)
        {
            t += Time.deltaTime;
            float newY = Mathf.Lerp(startY, targetY, t / duration);
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, newY, p.z);
            yield return null;
        }

        Vector3 fp = transform.position;
        transform.position = new Vector3(fp.x, targetY, fp.z);
        moveCo = null;
    }
}
