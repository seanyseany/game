using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerAnimOverride : MonoBehaviour
{
    [Header("Refs")]
    public Animator anim;
    public RuntimeAnimatorController baseController; // HeroBase 컨트롤러

    [Header("Base Placeholder Clips")]
    public AnimationClip baseWalk;
    public AnimationClip baseJump;
    public AnimationClip baseAttack;
    public AnimationClip baseFlyAttack;
    public AnimationClip baseLand;
    public AnimationClip baseDie;
    public AnimationClip baseTransform;
    public AnimationClip baseHurt;
    public AnimationClip baseStill;

    [Header("Overrides per Type (index = 0..4 → Player1~5)")]
    public AnimationClip[] walkClips;
    public AnimationClip[] jumpClips;
    public AnimationClip[] attackClips;
    public AnimationClip[] flyAttackClips;
    public AnimationClip[] landClips;
    public AnimationClip[] dieClips;
    public AnimationClip[] transformClips;
    public AnimationClip[] hurtClips;
    public AnimationClip[] stillClips;

    [Header("Rage Clips (index = 0..4 → RageP1~RageP5)")]
    public AnimationClip[] rageWalkClips;
    public AnimationClip[] rageJumpClips;
    public AnimationClip[] rageAttackClips;
    public AnimationClip[] rageFlyAttackClips;
    public AnimationClip[] rageLandClips;
    public AnimationClip[] rageTransformClips;

    private AnimatorOverrideController aoc;
    private int cachedType = -1;
    private bool isRageMode = false;
    private bool needRebind = false;

    void Awake()
    {
        if (!anim) anim = GetComponent<Animator>();

        // AnimatorOverrideController 생성 (HeroBase 기반)
        aoc = new AnimatorOverrideController(baseController);
        anim.runtimeAnimatorController = aoc;
    }

    void Start()
    {
        StartCoroutine(DelayedInit());
    }

    private IEnumerator DelayedInit()
    {
        // GameData.Instance가 완전히 준비될 때까지 대기
        yield return new WaitUntil(() => GameData.Instance != null);
        ApplyOverrides(force: true);
    }

    void Update()
    {
        // 필요 시 즉시 Rebind 수행
        if (needRebind)
        {
            anim.Rebind();
            anim.Update(0f);
            needRebind = false;
        }
    }

    // ✅ 외부에서 호출 가능하도록 public
    public void ApplyOverrides(bool force = false)
    {
        if (aoc == null || GameData.Instance == null) return;

        int i = Mathf.Clamp(GameData.Instance.selectedPlayerType, 1, 5) - 1;
        if (!force && cachedType == i) return;

        var map = new List<KeyValuePair<AnimationClip, AnimationClip>>();

        if (isRageMode)
        {
            map.Add(new(baseWalk, SafeGet(rageWalkClips, i) ?? baseWalk));
            map.Add(new(baseJump, SafeGet(rageJumpClips, i) ?? baseJump));
            map.Add(new(baseAttack, SafeGet(rageAttackClips, i) ?? baseAttack));
            map.Add(new(baseFlyAttack, SafeGet(rageFlyAttackClips, i) ?? baseFlyAttack));
            map.Add(new(baseLand, SafeGet(rageLandClips, i) ?? baseLand));
            map.Add(new(baseTransform, SafeGet(rageTransformClips, i) ?? baseTransform));
        }
        else
        {
            map.Add(new(baseWalk, SafeGet(walkClips, i) ?? baseWalk));
            map.Add(new(baseJump, SafeGet(jumpClips, i) ?? baseJump));
            map.Add(new(baseAttack, SafeGet(attackClips, i) ?? baseAttack));
            map.Add(new(baseFlyAttack, SafeGet(flyAttackClips, i) ?? baseFlyAttack));
            map.Add(new(baseLand, SafeGet(landClips, i) ?? baseLand));
            map.Add(new(baseDie, SafeGet(dieClips, i) ?? baseDie));
            map.Add(new(baseTransform, SafeGet(transformClips, i) ?? baseTransform));
            map.Add(new(baseHurt, SafeGet(hurtClips, i) ?? baseHurt));
            map.Add(new(baseStill, SafeGet(stillClips, i) ?? baseStill));
        }

        aoc.ApplyOverrides(map);
        cachedType = i;
    }

    private AnimationClip SafeGet(AnimationClip[] arr, int idx)
    {
        if (arr != null && idx >= 0 && idx < arr.Length && arr[idx] != null)
            return arr[idx];
        return null;
    }

    public void SetRageMode(bool active)
    {
        if (isRageMode == active) return;
        isRageMode = active;
        ApplyOverrides(force: true);
    }

    public AnimationClip GetCurrentRageTransformClip()
    {
        if (GameData.Instance == null) return baseTransform;

        int i = Mathf.Clamp(GameData.Instance.selectedPlayerType, 1, 5) - 1;
        return SafeGet(rageTransformClips, i) ?? baseTransform;
    }

    // ✅ PreWarm: 애니메이터를 미리 준비해 첫 프레임 로드 딜레이 제거
    public void PreWarm()
    {
        if (anim == null) return;
        ApplyOverrides(force: true);
        anim.Rebind();
        anim.Update(0f);
    }
}
