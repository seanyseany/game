using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class PhaseEndTrigger : MonoBehaviour
{
    private Collider2D col;
    private bool triggered = false;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        // 풀에서 복귀될 때마다 자동 초기화
        triggered = false;
        if (col != null) col.enabled = true;
    }

    private void Reset()
    {
        col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Detector가 아니면 무시
        if (!other.TryGetComponent<PhaseDetector>(out _))
            return;

        // 이미 꺼진 상태면 무시
        if (triggered) return;

        triggered = true;
        col.enabled = false;  // 즉시 비활성화

        if (StageManager.Instance != null)
            StageManager.Instance.OnPhasePassed(this);
        // A phase can pass only once per activation. OnEnable resets pooled reuse.
    }

    public void SuppressFuturePasses()
    {
        triggered = true;

        if (col != null)
            col.enabled = false;

        StopAllCoroutines();
    }
}
