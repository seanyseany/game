using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Blood : MonoBehaviour
{
    [Header("Sprites (Blink)")]
    public Sprite spriteA;
    public Sprite spriteB;

    [Header("Movement")]
    public float speed = 4f;

    [Header("Climb Excavator")]
    public float climbYSpeed = 2f;   // 🔥 추가

    [Header("Gate Line")]
    public float openX = -2f;

    private SpriteRenderer sr;
    private bool openSent = false;
    private Coroutine blinkRoutine;
    private Rigidbody2D rb;

    // 🔥 추가
    private bool onExcavator = false;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();

        // 🔒 기존 세팅 유지
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    void OnEnable()
    {
        openSent = false;
        UpdateVelocity();   // 🔥 변경
        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        blinkRoutine = StartCoroutine(Blink());
    }

    void OnDisable()
    {
        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
            blinkRoutine = null;
        }

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.Sleep();
    }

    void FixedUpdate()
    {
        // 🔥 이동 갱신
        UpdateVelocity();

        if (!openSent && transform.position.x <= openX)
        {
            openSent = true;
            if (GateHealth.Instance)
                GateHealth.Instance.OpenGate();
        }
    }

    // 🔥 추가 함수
    void UpdateVelocity()
    {
        if (onExcavator)
            rb.linearVelocity = new Vector2(-speed, climbYSpeed);
        else
            rb.linearVelocity = Vector2.left * speed;
    }

    private IEnumerator Blink()
    {
        while (true)
        {
            sr.sprite = spriteA;
            yield return new WaitForSeconds(0.18f);
            sr.sprite = spriteB;
            yield return new WaitForSeconds(0.18f);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 🔥 포크레인 접촉
        if (other.CompareTag("Excavator"))
        {
            onExcavator = true;
            return;
        }

        // ✅ 기존 Gate 로직 유지
        if (other.CompareTag("Gate"))
        {
            Debug.Log("✅ Blood hit Gate!");

            if (GateHealth.Instance)
                GateHealth.Instance.CloseGateOnBloodHit();

            if (GameData.Instance != null)
                GameData.Instance.AddO2(10);

            ObjectPool.Instance.ReturnToPool("Blood", gameObject);
        }
    }

    // 🔥 추가
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Excavator"))
        {
            onExcavator = false;
        }
    }
}
