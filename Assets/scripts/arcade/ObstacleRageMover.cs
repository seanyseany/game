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
    private bool movementFrozen = false;
    public bool IsMovementFrozen => movementFrozen;

    private float TargetY => originalY + (isTop ? moveDistance : -moveDistance);

    void OnEnable()
    {
        movementFrozen = false;

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
        // ✅ Rage 이벤트도 같이 구독해야 Rage 때 움직임
        GameData.OnRageStart += HandleExpandStart;
        GameData.OnRageEnd   += HandleExpandEnd;
        GameData.OnMachineGunSequenceStart += HandleExpandStart;
        GameData.OnMachineGunSequenceEnd += HandleExpandEnd;

        // ✅ 이미 Rage/머신건 진행 중이면 즉시 벌림
        if (ShouldStayExpanded())
        {
            StartMoveTo(TargetY, 0f);
        }
    }

    void OnDisable()
    {
        GameData.OnRageStart -= HandleExpandStart;
        GameData.OnRageEnd   -= HandleExpandEnd;
        GameData.OnMachineGunSequenceStart -= HandleExpandStart;
        GameData.OnMachineGunSequenceEnd -= HandleExpandEnd;

        if (moveCo != null)
        {
            StopCoroutine(moveCo);
            moveCo = null;
        }

        // GameObject가 실제로 비활성화될 때만 원위치로 복구한다.
        if (!gameObject.activeSelf)
        {
            var pos = transform.position;
            transform.position = new Vector3(pos.x, originalY, pos.z);
        }
    }

    private void HandleExpandStart()
    {
        if (movementFrozen)
            return;

        StartMoveTo(TargetY, moveDuration);
    }

    private void HandleExpandEnd()
    {
        if (movementFrozen)
            return;

        if (ShouldStayExpanded())
            return;

        StartMoveTo(originalY, returnMoveDuration);
    }

    private bool ShouldStayExpanded()
    {
        if (GameData.Instance == null)
            return false;

        return GameData.Instance.rageMode || GameData.Instance.IsMachineGunSequenceActive();
    }

    private void StartMoveTo(float targetY, float duration)
    {
        if (movementFrozen)
            return;

        if (moveCo != null) StopCoroutine(moveCo);
        moveCo = StartCoroutine(MoveYTo(targetY, duration));
    }

    public void FreezeAtCurrentPosition()
    {
        movementFrozen = true;

        if (moveCo != null)
        {
            StopCoroutine(moveCo);
            moveCo = null;
        }
    }

    public void ResumeMovement()
    {
        movementFrozen = false;
    }

    public void ResumeMovementForCurrentState()
    {
        movementFrozen = false;

        if (GameData.Instance != null && GameData.Instance.rageMode)
            StartMoveTo(TargetY, moveDuration);
        else
            StartMoveTo(originalY, returnMoveDuration);
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
