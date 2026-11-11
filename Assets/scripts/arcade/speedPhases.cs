using UnityEngine;

[DisallowMultipleComponent]
public class speedPhases : MonoBehaviour
{
    [Tooltip("랜덤과 무관하게 맨 처음 스폰할 시작 페이즈입니다. 이 페이즈의 PhaseEndTrigger가 PhaseDetector에 닿으면 속도 배율과 아래 페이즈 스폰을 시작합니다.")]
    public GameObject startPhasePrefab;

    [Tooltip("랜덤과 무관하게 맨 마지막에 스폰할 종료 페이즈입니다. 이 페이즈의 PhaseEndTrigger가 PhaseDetector에 닿으면 일반 페이즈로 복귀하고 1.5초 동안 속도와 스피드 애니메이션을 유지한 뒤 해제합니다.")]
    public GameObject endPhasePrefab;

    [Tooltip("이 스페셜 구간에서 스폰할 페이즈들입니다. 각 프리팹에 PhaseEndTrigger가 필요합니다. 목록을 섞어 사용하고, 부족하면 다시 순환합니다.")]
    public GameObject[] phasePrefabs;

    [Min(1)]
    [Tooltip("시작·종료 페이즈 사이에 스폰할 랜덤 페이즈 수입니다. 이 개수를 채운 뒤 End Phase Prefab을 스폰합니다.")]
    public int phaseSpawnCount = 3;

    [Min(0.01f)]
    [Tooltip("진입 배속 연출이 끝난 뒤 유지할 스피드 페이즈 배율입니다. 기존 게임 속도 기준이며, 1 = 기존 속도, 2 = 두 배입니다.")]
    public float speedMultiplier = 1.5f;

    [Header("Entry Speed")]
    [Min(0.01f)]
    [Tooltip("스피드 트리거 순간 즉시 적용할 배율입니다. 기존 게임 속도 기준이며 Speed Multiplier에 추가로 곱하지 않습니다.")]
    public float entrySpeedMultiplier = 1.7f;

    [Min(0f)]
    [Tooltip("진입 배율을 유지할 시간(초)입니다.")]
    public float entryHoldDuration = 0.5f;

    [Min(0f)]
    [Tooltip("유지 시간이 끝난 뒤 Speed Multiplier로 서서히 복귀하는 시간(초)입니다. 0이면 즉시 복귀합니다.")]
    public float entryReturnDuration = 0.5f;

    [Header("Speed Effect")]
    [Tooltip("스피드 배율이 적용되는 동안 표시할 이펙트입니다. 생성 위치·간격·삭제 위치는 Player의 Rage Speed Effect 설정을 함께 사용합니다.")]
    public GameObject speedEffectPrefab;

    [Min(0f)]
    [Tooltip("이펙트 기본 이동 속도입니다. 진입 배속 변화는 현재 구간의 이펙트 전체에 함께 적용하며, 장애물 감속 중에도 이동합니다. 빠를수록 생성 간격을 줄여 앞뒤 이펙트를 이어 줍니다.")]
    public float speedEffectMoveSpeed = 26f;

    [Min(0f)]
    [Tooltip("Speed Multiplier 기준의 정상 속도보다 느려지거나 구간이 끝날 때 이펙트가 투명해지는 시간(초)입니다. 정상 속도 이상에서는 계속 보이며, 감속 중에도 스폰과 이동은 유지합니다. 0이면 즉시 전환합니다.")]
    public float speedEffectFadeDuration = 1f;
}
