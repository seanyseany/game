using UnityEngine;

[DisallowMultipleComponent]
public class speedPhases : MonoBehaviour
{
    [Tooltip("랜덤과 무관하게 맨 처음 스폰할 시작 페이즈입니다. 이 페이즈의 PhaseEndTrigger가 PhaseDetector에 닿으면 속도 배율과 아래 페이즈 스폰을 시작합니다.")]
    public GameObject startPhasePrefab;

    [Tooltip("랜덤과 무관하게 맨 마지막에 스폰할 종료 페이즈입니다. 이 페이즈의 PhaseEndTrigger가 PhaseDetector에 닿으면 속도 배율을 해제하고 일반 페이즈로 복귀합니다.")]
    public GameObject endPhasePrefab;

    [Tooltip("이 스페셜 구간에서 스폰할 페이즈들입니다. 각 프리팹에 PhaseEndTrigger가 필요합니다. 목록을 섞어 사용하고, 부족하면 다시 순환합니다.")]
    public GameObject[] phasePrefabs;

    [Min(1)]
    [Tooltip("시작·종료 페이즈 사이에 스폰할 랜덤 페이즈 수입니다. 이 개수를 채운 뒤 End Phase Prefab을 스폰합니다.")]
    public int phaseSpawnCount = 3;

    [Min(0.01f)]
    [Tooltip("시작 페이즈의 종료 트리거를 통과한 뒤 기존 게임 속도에 곱할 배율입니다. 1 = 기존 속도, 2 = 두 배, 0.5 = 절반입니다.")]
    public float speedMultiplier = 1.5f;

    [Header("Speed Effect")]
    [Tooltip("스피드 배율이 적용되는 동안 표시할 이펙트입니다. 생성 위치·간격·삭제 위치는 Player의 Rage Speed Effect 설정을 함께 사용합니다.")]
    public GameObject speedEffectPrefab;

    [Min(0f)]
    [Tooltip("이펙트 기본 이동 속도입니다. 생성 시 정상 스테이지 속도 배율을 곱하며, 생성된 이펙트는 이후 감속·정지와 무관하게 끝 위치까지 이동합니다.")]
    public float speedEffectMoveSpeed = 26f;
}
