using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class MachineGunTrigger : MonoBehaviour
{
    [SerializeField] private bool consumeOnGateContact = true;

    private bool consumed;

    private void OnEnable()
    {
        consumed = false;
    }

    private void Reset()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHandleGateContact(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null)
            return;

        TryHandleGateContact(collision.collider);
    }

    private void TryHandleGateContact(Collider2D other)
    {
        if (consumed || other == null || !IsGateCollider(other))
            return;

        consumed = true;

        GameData gameData = GameData.Instance;
        Player player = Player.Instance != null ? Player.Instance : Object.FindFirstObjectByType<Player>();
        bool playerIsRaging = (player != null && player.IsRageModeActive()) || (gameData != null && gameData.rageMode);

        if (!playerIsRaging)
        {
            int queuedBossStage = 0;
            if (StageManager.Instance != null &&
                StageManager.Instance.TryResolveBossStageFromTaggedObject(gameObject, out int bossStage))
            {
                queuedBossStage = bossStage;
            }

            bool machineGunTriggered = gameData != null && gameData.TriggerMachineGun();
            if (machineGunTriggered && queuedBossStage != 0 && StageManager.Instance != null)
                StageManager.Instance.QueueBossEncounterAfterMachineGun(queuedBossStage);
        }

        if (consumeOnGateContact)
            gameObject.SetActive(false);
    }

    private static bool IsGateCollider(Collider2D other)
    {
        return other.GetComponent<GateHealth>() != null || other.GetComponentInParent<GateHealth>() != null;
    }
}
