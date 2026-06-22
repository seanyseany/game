using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Villan : MonoBehaviour
{
    [SerializeField] private float baseMoveSpeed = 2f;
    [SerializeField] private Transform aimTarget;
    [SerializeField] private Animator animator;
    [SerializeField] private string attackTrigger = "Attack";
    [SerializeField] private string dieTrigger = "Die";
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private Image hpFill;

    private VillanPath path;
    private Rigidbody2D body;
    private int level = 1;
    private int defense = 1;
    private int attack = 10;
    private float moveSpeed;
    private int leg = 1;
    private bool dead;
    private bool facingLeft = true;

    public Transform AimTarget => aimTarget != null ? aimTarget : transform;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void Update()
    {
        if (dead || path == null)
            return;

        float direction = leg == 1 ? -1f : 1f;
        body.linearVelocity = new Vector2(direction * moveSpeed, body.linearVelocity.y);
        UpdateFacing(direction < 0f);

        if (leg == 1 && transform.position.x <= path.Exit1WorldX)
        {
            StartCoroutine(SwitchToSecondLeg());
        }
        else if (leg == 2 && transform.position.x >= path.Exit2WorldX)
        {
            Destroy(gameObject);
        }
    }

    public void Initialize(VillanPath nextPath, int nextLevel)
    {
        path = nextPath;
        level = Mathf.Clamp(nextLevel, 1, 10);
        transform.position = path != null ? path.Entry1World : transform.position;
        leg = 1;
        dead = false;

        float scale = Mathf.Clamp(0.7f + (level - 1) * 0.03f, 0.7f, 1f);
        transform.localScale = new Vector3(scale, scale, 1f);
        defense = level;
        attack = 10 + (level - 1) * 10;
        moveSpeed = Mathf.Max(0.2f, baseMoveSpeed - (level - 1) * 0.05f);
        RefreshUi();
    }

    public void TakeDamage(int amount)
    {
        if (dead)
            return;

        defense = Mathf.Max(0, defense - Mathf.Max(0, amount));
        RefreshUi();
        if (defense <= 0)
            StartCoroutine(DieRoutine());
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (dead || other == null)
            return;

        Bank bank = other.GetComponent<Bank>() ?? other.GetComponentInParent<Bank>();
        if (bank != null)
        {
            bank.TakeDamage(attack);
            StartCoroutine(DieRoutine(attackAnimationFirst: true));
        }
    }

    private IEnumerator SwitchToSecondLeg()
    {
        if (dead || leg != 1)
            yield break;

        leg = 0;
        body.linearVelocity = Vector2.zero;
        yield return new WaitForSeconds(1f);
        if (dead || path == null)
            yield break;

        transform.position = path.Entry2World;
        leg = 2;
    }

    private IEnumerator DieRoutine(bool attackAnimationFirst = false)
    {
        if (dead)
            yield break;

        dead = true;
        body.linearVelocity = Vector2.zero;

        if (animator != null)
        {
            animator.SetTrigger(attackAnimationFirst ? attackTrigger : dieTrigger);
            yield return new WaitForSeconds(0.3f);
        }

        Destroy(gameObject);
    }

    private void RefreshUi()
    {
        if (levelText != null)
            levelText.text = $"Lv.{level}";
        if (hpFill != null)
            hpFill.fillAmount = Mathf.Clamp01(defense / (float)Mathf.Max(1, level));
    }

    private void UpdateFacing(bool shouldFaceLeft)
    {
        if (facingLeft == shouldFaceLeft)
            return;

        facingLeft = shouldFaceLeft;
        Vector3 angles = transform.localEulerAngles;
        angles.y = facingLeft ? 0f : 180f;
        transform.localEulerAngles = angles;
    }
}
