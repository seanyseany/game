using UnityEngine;

// Added to pooled launch effects at runtime, so assigning a visual prefab is enough.
[DisallowMultipleComponent]
public class BombLaunchSmoke : MonoBehaviour
{
    private ParticleSystem[] particles;
    private Animator[] animators;
    private SmokeMover[] movers;
    private BombLauncher owner;
    private string poolTag;
    private float remainingAnimationTime;
    private bool playing;

    private void Awake()
    {
        particles = GetComponentsInChildren<ParticleSystem>(true);
        animators = GetComponentsInChildren<Animator>(true);

        // Existing smoke prefabs can include their own return timers. This effect
        // owns their lifetime while preserving SmokeMover's movement behaviour.
        movers = GetComponentsInChildren<SmokeMover>(true);
        foreach (SmokeMover mover in movers)
            mover.UseExternalLifetime();
        foreach (AutoReturnToPool timer in GetComponentsInChildren<AutoReturnToPool>(true))
            DisableReturnTimer(timer);
        foreach (AnimatorAutoDespawn timer in GetComponentsInChildren<AnimatorAutoDespawn>(true))
            DisableReturnTimer(timer);
        foreach (MissileSmoke timer in GetComponentsInChildren<MissileSmoke>(true))
            DisableReturnTimer(timer);
    }

    private static void DisableReturnTimer(MonoBehaviour timer)
    {
        timer.StopAllCoroutines();
        timer.enabled = false;
    }

    internal void Play(BombLauncher launcher, string tag)
    {
        owner = launcher;
        poolTag = tag;
        foreach (SmokeMover mover in movers)
        {
            if (mover != null) mover.ConfigurePooling(true, tag);
        }
        remainingAnimationTime = 0f;
        bool hasVisual = false;

        foreach (Animator animator in animators)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                continue;
            animator.Rebind();
            animator.Update(0f);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            float speed = Mathf.Abs(animator.speed * state.speed * state.speedMultiplier);
            remainingAnimationTime = Mathf.Max(remainingAnimationTime, state.length / Mathf.Max(0.01f, speed));
            hasVisual = true;
        }

        foreach (ParticleSystem system in particles)
        {
            if (system == null) continue;
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!system.gameObject.activeInHierarchy) continue;
            var main = system.main;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            system.Play(false);
            hasVisual = true;
        }

        // Also clean up static-sprite placeholders that have no animation yet.
        if (!hasVisual) remainingAnimationTime = 1f;
        playing = true;
    }

    private void Update()
    {
        if (!playing || RageTransformFreezeController.IsGameplayPauseActive) return;
        remainingAnimationTime -= Time.deltaTime;
        if (remainingAnimationTime > 0f) return;

        foreach (ParticleSystem system in particles)
        {
            if (system != null && system.gameObject.activeInHierarchy && system.IsAlive(false))
                return;
        }
        ReturnToPool();
    }

    public void ReturnToPool()
    {
        playing = false;
        // Scene teardown already owns these roots; reparenting them into a pool
        // during unload can invalidate Unity's root-object destruction list.
        if (!Application.isPlaying || !gameObject.scene.isLoaded) return;
#if UNITY_EDITOR
        if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
#endif
        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }

    private void OnDisable()
    {
        playing = false;
        foreach (ParticleSystem system in particles)
        {
            if (system != null)
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (owner != null) owner.NotifyLaunchSmokeReturned(this);
        owner = null;
    }
}
