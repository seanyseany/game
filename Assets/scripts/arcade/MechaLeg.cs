using UnityEngine;

/// <summary>
/// 스프라이트(다리)는 제자리 고정.
/// Mover와 동일한 배속 소스를 사용해 애니메이션 속도만 조절한다.
/// - Rage: GameData.stageSpeedMult = 1.5 → 애니도 1.5배
/// - 충돌 감속/정지/역재생 등: stageSpeedMult 변화에 그대로 반응
/// </summary>
[RequireComponent(typeof(Animator))]
public class MechaLeg : MonoBehaviour
{
    [Header("Animation Speed")]
    public float baseAnimSpeed = 1f;

    public bool useAbsoluteMultiplier = true;

    public float smooth = 0f;

    private Animator anim;
    private float currentSpeed;

    void Awake()
    {
        anim = GetComponent<Animator>();
        currentSpeed = baseAnimSpeed;
        anim.speed = currentSpeed;
    }

    void Update()
    {
        // Mover와 동일한 배속 소스 사용
        float mult = 1f;
        if (GameData.Instance != null)
            mult = GameData.Instance.GetStageSpeedMult();

        // 역재생 배속(-)이 들어올 수 있으니 필요 시 절댓값 처리
        if (useAbsoluteMultiplier) mult = Mathf.Abs(mult);

        // 최종 애니메이션 속도
        float target = baseAnimSpeed * mult;

        // 부드럽게 반영(선택)
        if (smooth > 0f)
            currentSpeed = Mathf.Lerp(currentSpeed, target, 1f - Mathf.Exp(-smooth * Time.deltaTime));
        else
            currentSpeed = target;

        anim.speed = currentSpeed;
    }
}
