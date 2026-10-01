using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BossSlimeDie : MonoBehaviour
{
    [Header("Death FX Groups (Playback Order)")]
    [Tooltip("Register child group roots in order: BossSlimeBB group 1, 2, 3, 4.")]
    [SerializeField] private GameObject[] deathFxGroups = new GameObject[0];
    [Tooltip("Delay after this death prefab spawns before the first group starts, in seconds.")]
    [Min(0f)] [SerializeField] private float deathFxStartDelay = 0f;
    [Min(0f)] [SerializeField] private float deathFxGroupInterval = 0.4f;
    [Tooltip("How long each group stays active. Match this to the child animation duration.")]
    [Min(0.01f)] [SerializeField] private float deathFxGroupDuration = 1f;

    private Coroutine playRoutine;

    private void OnEnable()
    {
        GameData.OnGameOver += Finish;
        var animator = GetComponent<Animator>();
        RestartAnimator(animator);
        ResetGroups();

        float animationDuration = 0f;
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
            animationDuration = animator.GetCurrentAnimatorStateInfo(0).length / Mathf.Max(0.01f, animator.speed);

        playRoutine = StartCoroutine(CoPlay(animationDuration));
    }

    private void OnDisable()
    {
        GameData.OnGameOver -= Finish;
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }
        ResetGroups();
    }

    private IEnumerator CoPlay(float animationDuration)
    {
        float startDelay = Mathf.Max(0f, deathFxStartDelay);
        float interval = Mathf.Max(0f, deathFxGroupInterval);
        float groupDuration = Mathf.Max(0.01f, deathFxGroupDuration);
        // Always yield before finishing so the pool can finish registering this spawn.
        float duration = Mathf.Max(0.01f, animationDuration);
        int groupCount = deathFxGroups != null ? deathFxGroups.Length : 0;
        for (int i = 0; i < groupCount; i++)
        {
            if (deathFxGroups[i] != null)
                duration = Mathf.Max(duration, startDelay + i * interval + groupDuration);
        }

        int nextGroup = 0;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (RageTransformFreezeController.ShouldSkipGameplayFrame())
            {
                yield return null;
                continue;
            }

            while (nextGroup < groupCount && elapsed >= startDelay + nextGroup * interval)
            {
                PlayGroup(deathFxGroups[nextGroup]);
                nextGroup++;
            }
            for (int i = 0; i < nextGroup; i++)
            {
                if (deathFxGroups[i] != null && elapsed >= startDelay + i * interval + groupDuration)
                    deathFxGroups[i].SetActive(false);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        playRoutine = null;
        Finish();
    }

    private void ResetGroups()
    {
        if (deathFxGroups == null) return;

        for (int i = 0; i < deathFxGroups.Length; i++)
        {
            if (deathFxGroups[i] != null)
                deathFxGroups[i].SetActive(false);
        }
    }

    private static void PlayGroup(GameObject group)
    {
        if (group == null) return;

        group.SetActive(true);
        var animators = group.GetComponentsInChildren<Animator>();
        for (int i = 0; i < animators.Length; i++)
            RestartAnimator(animators[i]);
    }

    private static void RestartAnimator(Animator animator)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return;

        animator.Rebind();
        animator.Update(0f);
    }

    private void Finish()
    {
        if (ObjectPool.Instance != null && ObjectPool.Instance.TryReturnActive(gameObject))
            return;

        gameObject.SetActive(false);
    }
}
