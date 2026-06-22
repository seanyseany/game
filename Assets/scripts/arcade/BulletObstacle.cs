using UnityEngine;
using System.Collections;

public class BulletObstacle : MonoBehaviour, IReinitializable
{
    [Header("Bullet Damage Animation")]
    [SerializeField] private Animator targetAnimator;
    [Min(1)] [SerializeField] private int bulletHitMiddleCount = 1;

    [Header("Bullet Damage")]
    [Min(1)] public int bulletHitCount = 1;

    private int currentHitCount;
    private bool destroyed;
    private bool halvedTriggered;
    private Coroutine destroyRoutine;

    private const string HalvedTriggerName = "halved";
    private const string DieTriggerName = "Die";
    private const string LowerDieTriggerName = "die";

    private void Awake()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
    }

    private void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        currentHitCount = 0;
        destroyed = false;
        halvedTriggered = false;

        if (destroyRoutine != null)
        {
            StopCoroutine(destroyRoutine);
            destroyRoutine = null;
        }

        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (targetAnimator != null)
        {
            if (HasTriggerParameter(targetAnimator, HalvedTriggerName))
                targetAnimator.ResetTrigger(HalvedTriggerName);
            if (HasTriggerParameter(targetAnimator, DieTriggerName))
                targetAnimator.ResetTrigger(DieTriggerName);
            if (HasTriggerParameter(targetAnimator, LowerDieTriggerName))
                targetAnimator.ResetTrigger(LowerDieTriggerName);
        }
    }

    public void RegisterBulletHit()
    {
        if (destroyed)
            return;

        currentHitCount++;

        if (ShouldTriggerHalved())
            TriggerHalved();

        if (currentHitCount < Mathf.Max(1, bulletHitCount))
            return;

        destroyed = true;
        TriggerDestroy();
    }

    private bool ShouldTriggerHalved()
    {
        if (halvedTriggered)
            return false;

        int middleCount = Mathf.Clamp(bulletHitMiddleCount, 1, Mathf.Max(1, bulletHitCount));
        return currentHitCount >= middleCount;
    }

    private void TriggerHalved()
    {
        halvedTriggered = true;

        if (targetAnimator != null && HasTriggerParameter(targetAnimator, HalvedTriggerName))
            targetAnimator.SetTrigger(HalvedTriggerName);
    }

    private void TriggerDestroy()
    {
        Obstacle obstacle = GetComponent<Obstacle>() ?? GetComponentInParent<Obstacle>();
        if (obstacle != null)
        {
            obstacle.Hit(1);
            return;
        }

        Monster monster = GetComponent<Monster>() ?? GetComponentInParent<Monster>();
        if (monster != null)
        {
            monster.Hit(1);
            return;
        }

        ObstacleMover obstacleMover = GetComponent<ObstacleMover>() ?? GetComponentInParent<ObstacleMover>();
        if (obstacleMover != null)
        {
            obstacleMover.NotifyDeathStarted();

            TriggerDieAnimator();

            Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }

            destroyRoutine = StartCoroutine(CoReturnAfterDelay(obstacleMover, 0.5f));
            return;
        }

        gameObject.SendMessage("Hit", 1, SendMessageOptions.DontRequireReceiver);
        transform.root.gameObject.SendMessage("Hit", 1, SendMessageOptions.DontRequireReceiver);
    }

    private void TriggerDieAnimator()
    {
        Animator animator = targetAnimator;
        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInParent<Animator>();

        if (animator == null)
            return;

        if (HasTriggerParameter(animator, DieTriggerName))
            animator.SetTrigger(DieTriggerName);
        else if (HasTriggerParameter(animator, LowerDieTriggerName))
            animator.SetTrigger(LowerDieTriggerName);
    }

    private IEnumerator CoReturnAfterDelay(ObstacleMover obstacleMover, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        destroyRoutine = null;

        if (obstacleMover != null && obstacleMover.TryReturnToObjectPool())
            yield break;

        gameObject.SetActive(false);
    }

    private static bool HasTriggerParameter(Animator animator, string parameterName)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Trigger &&
                parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }
}
