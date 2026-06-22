using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Bullet : MonoBehaviour
{
    [Header("Purchase")]
    [SerializeField] private int oxygenPrice30 = 3;
    [SerializeField] private int oxygenPrice60 = 6;
    [SerializeField] private int oxygenPrice100 = 10;

    [Header("Motion")]
    [SerializeField] private float spawnSpeed = 0.5f;
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private Vector2 attackRange = new Vector2(8f, 3f);
    [SerializeField] private int attackPower = 1;

    [Header("Destroy")]
    [SerializeField] private Animator animator;
    [SerializeField] private string destroyTrigger = "Die";
    [SerializeField] private float destroyDelay = 0.2f;

    private Rigidbody2D body;
    private Vector2 direction;
    private bool destroyed;

    public int OxygenPrice30 => oxygenPrice30;
    public int OxygenPrice60 => oxygenPrice60;
    public int OxygenPrice100 => oxygenPrice100;
    public float SpawnSpeed => spawnSpeed;
    public float MoveSpeed => moveSpeed;
    public Vector2 AttackRange => attackRange;
    public int AttackPower => attackPower;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        Collider2D hitCollider = GetComponent<Collider2D>();
        hitCollider.isTrigger = true;
    }

    private void Update()
    {
        if (destroyed)
            return;

        body.linearVelocity = direction * moveSpeed;
    }

    public void Launch(Vector2 nextDirection)
    {
        destroyed = false;
        direction = nextDirection.sqrMagnitude > 0.0001f ? nextDirection.normalized : Vector2.right;
        body.linearVelocity = direction * moveSpeed;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (destroyed || other == null)
            return;

        Villan villan = other.GetComponent<Villan>() ?? other.GetComponentInParent<Villan>();
        if (villan != null)
        {
            villan.TakeDamage(attackPower);
            StartCoroutine(DestroyRoutine());
            return;
        }

        if (other.GetComponent<VillanPath>() != null || other.GetComponentInParent<VillanPath>() != null)
            StartCoroutine(DestroyRoutine());
    }

    private IEnumerator DestroyRoutine()
    {
        if (destroyed)
            yield break;

        destroyed = true;
        body.linearVelocity = Vector2.zero;
        if (animator != null && !string.IsNullOrWhiteSpace(destroyTrigger))
            animator.SetTrigger(destroyTrigger);

        yield return new WaitForSeconds(destroyDelay);
        Destroy(gameObject);
    }
}
