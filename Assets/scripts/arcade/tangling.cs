using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D))]
public class tangling : MonoBehaviour, IReinitializable
{
    [Header("Bounce Visual")]
    [SerializeField] private float stretchAmountX = 0.5f;
    [SerializeField] private float stretchAmountY = 1f;
    [SerializeField] private float stretchSpeed = 4f;
    [SerializeField] private float stretchDuration = 1f;

    [Header("Contact")]
    [SerializeField] private bool useTriggerContact = true;

    private readonly HashSet<int> activePlayerContacts = new HashSet<int>();
    private Vector3 initialLocalScale;
    private Coroutine stretchRoutine;

    private void Awake()
    {
        initialLocalScale = transform.localScale;
    }

    private void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        activePlayerContacts.Clear();
        transform.localScale = initialLocalScale;

        if (stretchRoutine != null)
        {
            StopCoroutine(stretchRoutine);
            stretchRoutine = null;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null)
            return;

        TryReact(collision.collider);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision == null)
            return;

        RemoveContact(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!useTriggerContact)
            return;

        TryReact(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!useTriggerContact)
            return;

        RemoveContact(other);
    }

    private void TryReact(Collider2D other)
    {
        Player player = ExtractPlayer(other);
        if (player == null || player.isDead)
            return;

        int playerId = player.GetInstanceID();
        if (!activePlayerContacts.Add(playerId))
            return;

        PlayStretch();
    }

    private void RemoveContact(Collider2D other)
    {
        Player player = ExtractPlayer(other);
        if (player == null)
            return;

        activePlayerContacts.Remove(player.GetInstanceID());
    }

    private Player ExtractPlayer(Collider2D other)
    {
        if (other == null)
            return null;

        return other.GetComponent<Player>() ?? other.GetComponentInParent<Player>();
    }

    private void PlayStretch()
    {
        if (stretchRoutine != null)
            StopCoroutine(stretchRoutine);

        stretchRoutine = StartCoroutine(StretchRoutine());
    }

    private System.Collections.IEnumerator StretchRoutine()
    {
        float elapsed = 0f;

        while (elapsed < stretchDuration)
        {
            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / stretchDuration);
            float damping = 1f - normalized;
            float wave = Mathf.Sin(elapsed * stretchSpeed * Mathf.PI * 2f) * damping;

            float scaleX = initialLocalScale.x + (wave * stretchAmountX);
            float scaleY = initialLocalScale.y - (wave * stretchAmountY);
            transform.localScale = new Vector3(scaleX, scaleY, initialLocalScale.z);

            yield return null;
        }

        transform.localScale = initialLocalScale;
        stretchRoutine = null;
    }
}
