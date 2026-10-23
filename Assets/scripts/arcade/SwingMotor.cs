using UnityEngine;
using System.Collections;

public class SwingMotor : MonoBehaviour, IReinitializeOnEnable
{
    public Rigidbody2D rb;
    public float initialTorque = 200f; // 시작 회전 힘
    [SerializeField] private float fallbackAngularVelocity = 45f;

    private HingeJoint2D hinge;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Coroutine restartRoutine;
    private static readonly WaitForFixedUpdate NextPhysicsStep = new WaitForFixedUpdate();

    private void Awake()
    {
        CacheReferences();
        // Render the swing between fixed physics steps, including under a scrolling phase.
        if (rb != null && rb.interpolation == RigidbodyInterpolation2D.None)
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        CacheReferences();
        if (rb == null) return;

        if (restartRoutine != null)
        {
            StopCoroutine(restartRoutine);
            restartRoutine = null;
        }

        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;

        rb.simulated = true;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.position = transform.position;
        rb.rotation = transform.eulerAngles.z;

        if (hinge != null && hinge.connectedBody != null)
            hinge.connectedBody.WakeUp();

        rb.WakeUp();
        restartRoutine = StartCoroutine(CoRestartSwing());
    }

    private IEnumerator CoRestartSwing()
    {
        // Unity synchronizes bodies before the next physics step. Per-rope
        // global SyncTransforms calls repeat the whole world's work on spawn.
        yield return NextPhysicsStep;

        if (rb == null)
            yield break;

        rb.WakeUp();
        rb.AddTorque(initialTorque, ForceMode2D.Impulse);

        yield return NextPhysicsStep;

        if (rb == null)
            yield break;

        if (Mathf.Abs(rb.angularVelocity) < fallbackAngularVelocity)
        {
            float direction = Mathf.Sign(initialTorque);
            if (Mathf.Approximately(direction, 0f))
                direction = 1f;

            rb.angularVelocity = direction * fallbackAngularVelocity;
            rb.WakeUp();
        }

        restartRoutine = null;
    }

    private void OnDisable()
    {
        if (restartRoutine != null)
        {
            StopCoroutine(restartRoutine);
            restartRoutine = null;
        }
    }

    private void CacheReferences()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (hinge == null)
            hinge = GetComponent<HingeJoint2D>();
    }
}
