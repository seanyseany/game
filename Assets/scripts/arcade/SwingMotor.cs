using UnityEngine;
using System.Collections;

public class SwingMotor : MonoBehaviour, IReinitializable
{
    public Rigidbody2D rb;
    public float initialTorque = 200f; // 시작 회전 힘
    [SerializeField] private float fallbackAngularVelocity = 45f;

    private HingeJoint2D hinge;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Coroutine restartRoutine;

    private void Awake()
    {
        CacheReferences();
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        Reinit();
    }

    private void Start()
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
        Physics2D.SyncTransforms();

        restartRoutine = StartCoroutine(CoRestartSwing());
    }

    private IEnumerator CoRestartSwing()
    {
        yield return new WaitForFixedUpdate();

        if (rb == null)
            yield break;

        rb.WakeUp();
        rb.AddTorque(initialTorque, ForceMode2D.Impulse);

        yield return new WaitForFixedUpdate();

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
