using UnityEngine;
using System.Collections;

public class MissileSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject warningPrefab;
    public GameObject missilePrefab;

    [Header("Settings")]
    public float warningTime = 1.5f;
    public float fireX = 10f;        // 월드 좌표 발사 라인 X
    private float missileSpeed = 3f; // (Missile이 자체 속도 쓰면 무시될 수 있음)
    public float missileSpeedMultiplier = 10f;

    private bool fired = false;
    private Coroutine fireRoutine;

    private GameObject activeWarning;
    private bool warningFromPool = false;

    private bool isResetting = false;

    private Transform player;
    private float lockedY;
    private const float LOCK_EARLY_TIME = 0.35f;

    void OnEnable()
    {
        fired = false;
        isResetting = false;

        // 혹시 남아있던 코루틴 정리
        if (fireRoutine != null)
        {
            StopCoroutine(fireRoutine);
            fireRoutine = null;
        }

        // 혹시 남아있던 경고 정리
        CleanupWarning();
    }

    void Update()
    {
        if (isResetting) return;

        // 스포너가 fireX 라인을 지나갈 때 1회 발사
        if (!fired && transform.position.x <= fireX)
        {
            fired = true;
            fireRoutine = StartCoroutine(FireMissile());
        }
    }

    private IEnumerator FireMissile()
    {
        // 경고/미사일은 항상 월드 X=fireX 에서 생성
        Vector3 firePos = new Vector3(fireX, transform.position.y, 0f);

        try
        {
            // 1) Warning 생성 (무조건 여기서만!)
            SpawnWarning(firePos);

            // 경고 시간은 항상 고정값으로 사용 (배속 영향 제거)
            float totalTime = Mathf.Max(0f, warningTime);
            float followTime = Mathf.Max(0f, totalTime - LOCK_EARLY_TIME);
            float elapsed = 0f;
            while (elapsed < followTime)
            {
                if (isResetting) yield break;

                if (player == null)
                {
                    var p = FindObjectOfType<Player>();
                    if (p != null) player = p.transform;
                }

                if (player != null)
                {
                    firePos.y = player.position.y;
                    if (activeWarning != null)
                        activeWarning.transform.position = firePos;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            lockedY = firePos.y;

            // 마지막 LOCK_EARLY_TIME 동안은 Y를 고정해 급회피 반응을 유지
            float lockWait = totalTime - followTime;
            float waited = 0f;
            while (waited < lockWait)
            {
                if (isResetting) yield break;
                waited += Time.deltaTime;
                yield return null;
            }

            // 2) Warning 제거 (미사일 발사 직전에도 한 번 정리)
            CleanupWarning();

            // 3) Missile 생성
            if (!isResetting && missilePrefab != null)
            {
                Vector3 missilePos = new Vector3(fireX, lockedY, 0f);
                GameObject spawnedMissile = null;

                if (ObjectPool.Instance != null && ObjectPool.Instance.HasPool("missile"))
                {
                    spawnedMissile = ObjectPool.Instance.SpawnFromPool("missile", missilePos, Quaternion.identity);
                    if (spawnedMissile == null)
                        spawnedMissile = Instantiate(missilePrefab, missilePos, Quaternion.identity);
                }
                else
                {
                    spawnedMissile = Instantiate(missilePrefab, missilePos, Quaternion.identity);
                }

                if (spawnedMissile != null)
                {
                    var m = spawnedMissile.GetComponent<Missile>();
                    if (m != null) m.ForceLaunch(missileSpeed * Mathf.Max(0f, missileSpeedMultiplier));
                }
            }
        }
        finally
        {
            // 어떤 경우든 Warning은 무조건 정리
            CleanupWarning();
            fireRoutine = null;
        }
    }

    // ======= Warning Spawn / Cleanup =======

    private void SpawnWarning(Vector3 pos)
    {
        CleanupWarning(); // 혹시 남아있으면 먼저 정리

        warningFromPool = false;

        if (warningPrefab == null) return;

        if (ObjectPool.Instance != null && ObjectPool.Instance.HasPool("Warning"))
        {
            activeWarning = ObjectPool.Instance.SpawnFromPool("Warning", pos, Quaternion.identity);
            if (activeWarning != null) warningFromPool = true;
        }

        if (activeWarning == null)
        {
            activeWarning = Instantiate(warningPrefab, pos, Quaternion.identity);
            warningFromPool = false;
        }
    }

    private void CleanupWarning()
    {
        if (activeWarning == null) { warningFromPool = false; return; }

        // Destroy된 오브젝트 참조도 여기서 안전하게 처리
        // (Unity의 "fake null" 대응)
        if (activeWarning.Equals(null))
        {
            activeWarning = null;
            warningFromPool = false;
            return;
        }

        if (warningFromPool && ObjectPool.Instance != null && ObjectPool.Instance.HasPool("Warning"))
            ObjectPool.Instance.ReturnToPool("Warning", activeWarning);
        else
            Destroy(activeWarning);

        activeWarning = null;
        warningFromPool = false;
    }

    // ======= Disable / Reset =======

    void OnDisable()
    {
        isResetting = true;

        if (fireRoutine != null)
        {
            StopCoroutine(fireRoutine);
            fireRoutine = null;
        }

        CleanupWarning();
        fired = false;
    }

    public void ResetSpawner()
    {
        isResetting = true;

        if (fireRoutine != null)
        {
            StopCoroutine(fireRoutine);
            fireRoutine = null;
        }

        CleanupWarning();
        fired = false;

        isResetting = false;
    }
}
