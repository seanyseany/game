using System.Collections;
using UnityEngine;

public class MachineGunBullet : MonoBehaviour
{
    public float speed = 20f;

    [Header("Pooling")]
    public string poolTag = "MachineGunBullet";
    public float lifeTime = 5f;

    [Header("Death Animation")]
    [SerializeField] private Animator bulletAnimator;
    [SerializeField] private Collider2D hitCollider;
    [SerializeField] private string dieTriggerName = "die";
    [SerializeField] private float dieAnimationDuration = 0.1f;

    private Vector2 moveDirection = Vector2.right;
    private float despawnTime;
    private bool despawning;
    private Coroutine despawnRoutine;

    private void Awake()
    {
        CacheComponents();
        ResetVisualState();
    }

    private void OnEnable()
    {
        CacheComponents();
        despawnTime = Time.time + Mathf.Max(0.1f, lifeTime);
        despawning = false;

        if (despawnRoutine != null)
        {
            StopCoroutine(despawnRoutine);
            despawnRoutine = null;
        }

        if (hitCollider != null)
            hitCollider.enabled = true;

        ResetVisualState();
    }

    public void Launch(Vector2 direction)
    {
        moveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        despawning = false;
        despawnTime = Time.time + Mathf.Max(0.1f, lifeTime);
    }

    private void Update()
    {
        if (despawning)
            return;

        transform.position += (Vector3)(moveDirection * speed * Time.deltaTime);

        if (Time.time >= despawnTime)
            Despawn();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (despawning || other == null)
            return;

        CholesterolBomb cholesterolBomb = FindMatchingComponent<CholesterolBomb>(other);
        ObstacleMover obstacleMover = cholesterolBomb == null ? FindMatchingComponent<ObstacleMover>(other) : null;
        BulletObstacle bulletObstacle = FindMatchingComponent<BulletObstacle>(other);

        bool reactedObstacleMover =
            cholesterolBomb != null ? cholesterolBomb.ReactToMachineGunBullet() :
            obstacleMover != null && obstacleMover.ReactToMachineGunBullet();
        bool triggeredDie = false;

        if (cholesterolBomb != null)
        {
            cholesterolBomb.RegisterBulletHit();
            triggeredDie = true;
        }
        else if (bulletObstacle != null)
        {
            bulletObstacle.RegisterBulletHit();
            triggeredDie = true;
        }
        else
        {
            triggeredDie = TryTriggerDie(other);
        }

        if (!triggeredDie && !reactedObstacleMover)
            return;

        Despawn();
    }

    private static T FindMatchingComponent<T>(Collider2D other) where T : Component
    {
        return other.GetComponent<T>() ??
               other.GetComponentInParent<T>() ??
               other.GetComponentInChildren<T>(true);
    }

    private bool TryTriggerDie(Collider2D other)
    {
        if (FindMatchingComponent<Player>(other) != null)
            return false;

        if (FindMatchingComponent<GateHealth>(other) != null)
            return false;

        Obstacle obstacle = FindMatchingComponent<Obstacle>(other);
        if (obstacle != null)
        {
            obstacle.Hit(1);
            return true;
        }

        Monster monster = FindMatchingComponent<Monster>(other);
        if (monster != null)
        {
            monster.Hit(1, false);
            return true;
        }

        Animator animator = FindMatchingComponent<Animator>(other);
        if (animator == null)
            return false;

        if (HasTriggerParameter(animator, "die"))
        {
            animator.SetTrigger("die");
            return true;
        }

        if (HasTriggerParameter(animator, "Die"))
        {
            animator.SetTrigger("Die");
            return true;
        }

        return false;
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

    private void Despawn()
    {
        if (despawning)
            return;

        despawning = true;

        if (hitCollider != null)
            hitCollider.enabled = false;

        if (bulletAnimator != null && HasTriggerParameter(bulletAnimator, dieTriggerName))
        {
            despawnRoutine = StartCoroutine(CoPlayDieAndDespawn());
            return;
        }

        CompleteDespawn();
    }

    private IEnumerator CoPlayDieAndDespawn()
    {
        bulletAnimator.enabled = true;
        bulletAnimator.Rebind();
        bulletAnimator.Update(0f);
        bulletAnimator.SetTrigger(dieTriggerName);

        yield return new WaitForSeconds(Mathf.Max(0.01f, dieAnimationDuration));

        despawnRoutine = null;
        CompleteDespawn();
    }

    private void CompleteDespawn()
    {
        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }

    private void CacheComponents()
    {
        if (bulletAnimator == null)
            bulletAnimator = GetComponent<Animator>();

        if (hitCollider == null)
            hitCollider = GetComponent<Collider2D>();
    }

    private void ResetVisualState()
    {
        if (bulletAnimator == null)
            return;

        bulletAnimator.ResetTrigger(dieTriggerName);
        bulletAnimator.Rebind();
        bulletAnimator.Update(0f);
        bulletAnimator.enabled = false;
    }
}
