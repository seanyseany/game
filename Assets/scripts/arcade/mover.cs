using UnityEngine;

[DefaultExecutionOrder(100)]
public class Mover : MonoBehaviour
{
    public float baseSpeed = 1f;
    [HideInInspector] public bool applyStageSpeedMultiplier = true;

    // 🔹 기본 속도 백업 (prefab 기준)
    [HideInInspector] public float defaultBaseSpeed;
    private Rigidbody2D scrollBody;
    private Rigidbody2D[] nestedBodies;
    private bool resetScrollPoseOnEnable = true;

    void Awake()
    {
        defaultBaseSpeed = baseSpeed;

        // Transform scrolling clears interpolation history on nested swinging bodies.
        // Keep these phases on the physics clock and interpolate their scrolling root.
        if (GetComponent<Rigidbody2D>() != null || GetComponentInChildren<SwingMotor>(true) == null)
            return;

        scrollBody = gameObject.AddComponent<Rigidbody2D>();
        scrollBody.bodyType = RigidbodyType2D.Kinematic;
        scrollBody.gravityScale = 0f;
        scrollBody.interpolation = RigidbodyInterpolation2D.Interpolate;
        nestedBodies = GetComponentsInChildren<Rigidbody2D>(true);
    }

    void Update()
    {
        if (scrollBody != null) return;
        float mult = applyStageSpeedMultiplier && GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;
        transform.position += Vector3.left * baseSpeed * mult * Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (scrollBody == null) return;
        float mult = applyStageSpeedMultiplier && GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;
        Vector2 delta = Vector2.left * baseSpeed * mult * Time.fixedDeltaTime;
        if (delta.sqrMagnitude <= 0f)
        {
            scrollBody.linearVelocity = Vector2.zero;
            return;
        }

        // Nested rigidbodies have independent world poses. Move their physics origins
        // too, so anchors and saws keep scrolling with the root without changing swing velocity.
        for (int i = 0; i < nestedBodies.Length; i++)
        {
            Rigidbody2D body = nestedBodies[i];
            if (body != null && body != scrollBody && body.simulated &&
                body.gameObject.activeInHierarchy && body.transform.IsChildOf(transform))
                body.position += delta;
        }
        scrollBody.MovePosition(scrollBody.position + delta);
    }

    void OnEnable()
    {
        if (scrollBody == null) return;
        // Pool activation may move the whole object. A component-only pause must
        // retain the physics pose instead of copying its delayed displayed pose.
        if (resetScrollPoseOnEnable) scrollBody.position = transform.position;
        resetScrollPoseOnEnable = false;
        scrollBody.linearVelocity = Vector2.zero;
    }

    void OnDisable()
    {
        resetScrollPoseOnEnable = !gameObject.activeInHierarchy;
        if (scrollBody != null) scrollBody.linearVelocity = Vector2.zero;
    }

    public void ApplyPhaseMultiplier(float phaseMult)
    {
        baseSpeed = defaultBaseSpeed * phaseMult;
    }
}
