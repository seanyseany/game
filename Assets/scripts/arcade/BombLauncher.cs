using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BombLauncher : MonoBehaviour
{
    [Header("Positions")]
    public Vector3 activePos = new Vector3(-7f, 1.4f, 0f);
    public float moveDuration = 0.5f;

    [Header("Bomb Head Setup")]
    public GameObject bombHeadPrefab;
    public int ammoCount = 6;
    public Vector3[] headLocalPositions = new Vector3[6];

    [Header("Launch Smoke")]
    [Tooltip("각 캡에서 Bomb이 발사될 때 생성할 스모크. 풀은 자동으로 준비됩니다.")]
    public GameObject launchSmokePrefab;

    [Header("Pool Tags")]
    public string bombHeadPoolTag = "BombHead";
    public string bombPoolTag = "Bomb";
    public string launchSmokePoolTag = "BombLaunchSmoke";

    [Header("Fire Settings")]
    public bool fireFromLast = true;
    public float autoReturnDelay = 0.1f;

    [Header("Rage Forced Fire")]
    [Tooltip("분노 발동 시점부터 남은 탄을 강제 발사하기까지의 시간(초)")]
    [Min(0f)] public float rageForcedFireDelay = 2f;
    public Vector2[] forcedTargetWorldPositions = new Vector2[3];

    // runtime
    private Vector3 startPos;
    private List<GameObject> loadedHeads = new List<GameObject>();
    private bool isActive = false;
    private bool isMoving = false;
    private Coroutine forcedFireCoroutine;
    private Coroutine returnAndReloadCoroutine;
    private readonly List<Transform> forcedTargetPoints = new List<Transform>();
    private readonly List<BombLaunchSmoke> activeLaunchSmokes = new List<BombLaunchSmoke>();

    void Awake()
    {
        startPos = transform.position; // ✅ 원래 위치 저장
        EnsureForcedTargetPoints();
        EnsurePoolCapacity();
    }

    void OnEnable()
    {
        GameData.OnRageStart += HandleRageStart;
        // 혹시 풀/재시작으로 Enable 될 때 기본값 복구
        transform.position = startPos;
        isActive = false;
        isMoving = false;
        returnAndReloadCoroutine = null;

        // 여기서 바로 장전하면 “항상 보임”이라서,
        // Activate 때 장전하도록 둠.
        StopForcedFireTimer();
        ClearHeads();
        UpdateForcedTargetPoints();
        EnsurePoolCapacity();
    }

    // =========================
    // Public API
    // =========================
    public void ActivateLauncher()
    {
        if (isActive || isMoving)
        {
            RefillMissingAmmo();
            if (returnAndReloadCoroutine != null)
            {
                StopCoroutine(returnAndReloadCoroutine);
                returnAndReloadCoroutine = null;
                // A pickup during the return trip brings the launcher back from its current position.
                if (isMoving) StartCoroutine(CoActivate(false));
            }
            return;
        }

        // 런쳐가 비활성 오브젝트면 코루틴이 안 돌아가니까, 혹시 꺼져있으면 켜줌
        if (!gameObject.activeInHierarchy) gameObject.SetActive(true);

        StartCoroutine(CoActivate());
    }

    public bool CanFire()
    {
        if (!isActive) return false;
        if (isMoving) return false;

        CleanupInvalidLoadedHeads();

        for (int i = 0; i < loadedHeads.Count; i++)
            if (IsUsableHead(loadedHeads[i])) return true;

        return false;
    }

    public void FireAt(Transform target)
    {
        TryFireAt(target);
    }

    internal bool TryFireAt(Transform target)
    {
        if (!CanFire()) return false;
        if (target == null || !target.gameObject.activeInHierarchy) return false;

        int idx = GetNextAmmoIndex();
        if (idx < 0) return false;

        // Prepare the shot before consuming its head, so pool failure cannot lose ammo.
        GameObject head = loadedHeads[idx];

        Vector3 spawnWorldPos = (head != null)
            ? head.transform.position
            : transform.TransformPoint(GetLocalPosSafe(idx));

        // 2) Bomb 스폰
        GameObject bombObj = null;

        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(bombPoolTag))
            bombObj = ObjectPool.Instance.SpawnFromPool(bombPoolTag, spawnWorldPos, Quaternion.identity);

        if (bombObj == null)
        {
            Debug.LogError("[BombLauncher] Bomb spawn failed. ObjectPool tag/prefab 확인 필요: " + bombPoolTag);
            return false;
        }

        Bomb bomb = bombObj.GetComponent<Bomb>();
        if (bomb == null)
        {
            ObjectPool.Instance.TryReturnActive(bombObj);
            Debug.LogError("[BombLauncher] Bomb pool prefab is missing its Bomb component.");
            return false;
        }
        bomb.SetTarget(target);
        SpawnLaunchSmoke(spawnWorldPos, head != null ? head.transform.rotation : transform.rotation);
        loadedHeads[idx] = null;
        if (head != null)
        {
            head.transform.SetParent(null);
            if (ObjectPool.Instance != null && !string.IsNullOrEmpty(bombHeadPoolTag))
                ObjectPool.Instance.ReturnToPool(bombHeadPoolTag, head);
            else
                Destroy(head);
        }

        // 3) 다 썼으면 복귀 + 재장전
        if (IsAmmoEmpty())
            returnAndReloadCoroutine = StartCoroutine(CoReturnAndReload(autoReturnDelay));
        return true;
    }

    // =========================
    // Coroutines
    // =========================
    private IEnumerator CoActivate(bool resetAmmo = true)
    {
        isMoving = true;

        // ✅ 활성화될 때마다 “장전 새로” (머리 안 달리는 문제 방지)
        if (resetAmmo) ResetAmmo();

        yield return Move(transform.position, activePos);

        isActive = true;
        isMoving = false;
    }

    private IEnumerator CoReturnAndReload(float delay)
    {
        yield return new WaitForSeconds(delay);

        StopForcedFireTimer();
        isMoving = true;
        isActive = false;

        yield return Move(activePos, startPos);

        // ✅ 복귀 후 재장전해서 다음 루프 준비
        ResetAmmo();

        isMoving = false;
        returnAndReloadCoroutine = null;
    }

    private IEnumerator Move(Vector3 from, Vector3 to)
    {
        float t = 0f;
        float dur = Mathf.Max(0.0001f, moveDuration);

        while (t < dur)
        {
            transform.position = Vector3.Lerp(from, to, t / dur);
            t += Time.deltaTime;
            yield return null;
        }
        transform.position = to;
    }

    // =========================
    // Ammo
    // =========================
    private void ClearHeads()
    {
        for (int i = 0; i < loadedHeads.Count; i++)
        {
            var h = loadedHeads[i];
            if (h == null) continue;

            h.transform.SetParent(null);

            if (ObjectPool.Instance != null && !string.IsNullOrEmpty(bombHeadPoolTag) && h.scene.IsValid())
                ObjectPool.Instance.ReturnToPool(bombHeadPoolTag, h);
            else
                DestroyIfSceneObject(h);
        }
        loadedHeads.Clear();
    }

    private void ResetAmmo()
    {
        ClearHeads();
        RefillMissingAmmo();
    }

    private void RefillMissingAmmo()
    {
        EnsurePoolCapacity();
        CleanupInvalidLoadedHeads();

        int maxByPos = (headLocalPositions != null) ? headLocalPositions.Length : 0;
        int count = Mathf.Min(ammoCount, maxByPos);

        if (count <= 0)
        {
            return;
        }

        while (loadedHeads.Count < count)
            loadedHeads.Add(null);

        for (int i = 0; i < count; i++)
        {
            if (IsUsableHead(loadedHeads[i])) continue;
            Vector3 localPos = headLocalPositions[i];

            GameObject headObj = SpawnHead();
            loadedHeads[i] = headObj;

            if (headObj != null)
            {
                headObj.transform.SetParent(transform, false); // 런쳐 자식으로
                headObj.transform.localPosition = localPos;
                headObj.transform.localRotation = Quaternion.identity;
                headObj.transform.localScale = Vector3.one;
            }
            else
            {
                Debug.LogError("[BombLauncher] Head spawn failed. Pool/prefab 확인 필요: " + bombHeadPoolTag);
            }
        }
    }

    private GameObject SpawnHead()
    {
        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(bombHeadPoolTag))
        {
            GameObject pooledHead = ObjectPool.Instance.SpawnFromPool(bombHeadPoolTag, transform.position, Quaternion.identity);
            if (pooledHead != null)
                return pooledHead;
        }

        if (bombHeadPrefab == null)
        {
            Debug.LogError("[BombLauncher] bombHeadPrefab is NULL");
            return null;
        }

        return Instantiate(bombHeadPrefab);
    }

    private void SpawnLaunchSmoke(Vector3 position, Quaternion rotation)
    {
        if (launchSmokePrefab == null || ObjectPool.Instance == null || string.IsNullOrEmpty(launchSmokePoolTag))
            return;

        GameObject smoke = ObjectPool.Instance.SpawnFromPool(launchSmokePoolTag, position,
            rotation * launchSmokePrefab.transform.rotation);
        if (smoke == null) return;

        BombLaunchSmoke effect = smoke.GetComponent<BombLaunchSmoke>();
        if (effect == null) effect = smoke.AddComponent<BombLaunchSmoke>();
        activeLaunchSmokes.Add(effect);
        effect.Play(this, launchSmokePoolTag);
    }

    internal void NotifyLaunchSmokeReturned(BombLaunchSmoke smoke)
    {
        activeLaunchSmokes.Remove(smoke);
    }

    private void ClearLaunchSmokes()
    {
        while (activeLaunchSmokes.Count > 0)
        {
            int last = activeLaunchSmokes.Count - 1;
            BombLaunchSmoke smoke = activeLaunchSmokes[last];
            activeLaunchSmokes.RemoveAt(last);
            if (smoke != null) smoke.ReturnToPool();
        }
    }

    private bool IsAmmoEmpty()
    {
        CleanupInvalidLoadedHeads();

        for (int i = 0; i < loadedHeads.Count; i++)
            if (IsUsableHead(loadedHeads[i])) return false;
        return true;
    }

    private int GetNextAmmoIndex()
    {
        if (loadedHeads.Count == 0) return -1;
        CleanupInvalidLoadedHeads();

        if (fireFromLast)
        {
            for (int i = loadedHeads.Count - 1; i >= 0; i--)
                if (IsUsableHead(loadedHeads[i])) return i;
        }
        else
        {
            for (int i = 0; i < loadedHeads.Count; i++)
                if (IsUsableHead(loadedHeads[i])) return i;
        }

        return -1;
    }

    private Vector3 GetLocalPosSafe(int idx)
    {
        if (headLocalPositions == null || headLocalPositions.Length == 0) return Vector3.zero;
        idx = Mathf.Clamp(idx, 0, headLocalPositions.Length - 1);
        return headLocalPositions[idx];
    }

    private void HandleRageStart()
    {
        // Only the current loaded volley participates. A launcher waiting at
        // home must not fire newly reloaded caps because of an earlier rage.
        if ((!isActive && !isMoving) || GetNextAmmoIndex() < 0) return;
        StopForcedFireTimer();

        forcedFireCoroutine = StartCoroutine(CoForceFireRemainingBombs());
    }

    private void StopForcedFireTimer()
    {
        if (forcedFireCoroutine == null) return;
        StopCoroutine(forcedFireCoroutine);
        forcedFireCoroutine = null;
    }

    private IEnumerator CoForceFireRemainingBombs()
    {
        float remaining = Mathf.Max(0f, rageForcedFireDelay);
        if (remaining > 0f) yield return new WaitForSeconds(remaining);
        // If rage begins while the launcher is entering, preserve its original
        // deadline and fire as soon as the already loaded caps are ready.
        while (isMoving) yield return null;
        forcedFireCoroutine = null;
        ForceFireRemainingBombs();
    }

    private void ForceFireRemainingBombs()
    {
        if (!isActive || isMoving || !CanFire()) return;

        UpdateForcedTargetPoints();
        if (forcedTargetPoints.Count == 0)
        {
            return;
        }

        int targetIdx = 0;
        while (CanFire())
        {
            Transform forcedTarget = forcedTargetPoints[targetIdx % forcedTargetPoints.Count];
            if (forcedTarget == null) break;

            if (!TryFireAt(forcedTarget)) break;
            targetIdx++;
        }
    }

    private void EnsureForcedTargetPoints()
    {
        EnsureForcedTargetArray();
        int desiredCount = forcedTargetWorldPositions != null ? forcedTargetWorldPositions.Length : 0;

        while (forcedTargetPoints.Count < desiredCount)
        {
            var point = new GameObject($"BombLauncherForcedTarget_{forcedTargetPoints.Count}");
            point.hideFlags = HideFlags.HideAndDontSave;
            forcedTargetPoints.Add(point.transform);
        }

        while (forcedTargetPoints.Count > desiredCount)
        {
            Transform point = forcedTargetPoints[forcedTargetPoints.Count - 1];
            forcedTargetPoints.RemoveAt(forcedTargetPoints.Count - 1);

            if (point != null)
            {
                if (Application.isPlaying) Destroy(point.gameObject);
                else DestroyImmediate(point.gameObject);
            }
        }

        for (int i = 0; i < forcedTargetPoints.Count; i++)
        {
            if (forcedTargetPoints[i] == null) continue;
            forcedTargetPoints[i].position = new Vector3(
                forcedTargetWorldPositions[i].x,
                forcedTargetWorldPositions[i].y,
                transform.position.z
            );
        }
    }

    private void UpdateForcedTargetPoints()
    {
        EnsureForcedTargetPoints();
    }

    private void EnsureForcedTargetArray()
    {
        if (forcedTargetWorldPositions == null)
            forcedTargetWorldPositions = new Vector2[0];
    }

    public void ForceStopAndReturnHome()
    {
        StopAllCoroutines();          // 이동/복귀/발사 관련 코루틴 전부 중단
        forcedFireCoroutine = null;
        returnAndReloadCoroutine = null;
        isMoving = false;
        isActive = false;

        // 장전된 머리 전부 회수
        ClearHeads();
        ClearLaunchSmokes();

        // 원래 자리로 순간이동
        transform.position = startPos;
    }
    public void ResetLauncherState()
    {
        StopAllCoroutines();
        forcedFireCoroutine = null;
        returnAndReloadCoroutine = null;
        isMoving = false;
        isActive = false;

        ClearHeads();
        ClearLaunchSmokes();
        transform.position = startPos;
    }

    private void OnDisable()
    {
        GameData.OnRageStart -= HandleRageStart;
        StopAllCoroutines();
        forcedFireCoroutine = null;
        returnAndReloadCoroutine = null;
        isMoving = false;
        isActive = false;
        // Unity forbids reparenting caps while their parent is being disabled.
        // OnEnable already clears and rebuilds them on the next activation.
        ClearLaunchSmokes();
    }

    private void OnDestroy()
    {
        GameData.OnRageStart -= HandleRageStart;
        ClearLaunchSmokes();
        for (int i = 0; i < forcedTargetPoints.Count; i++)
        {
            Transform point = forcedTargetPoints[i];
            if (point == null) continue;

            if (Application.isPlaying) Destroy(point.gameObject);
            else DestroyImmediate(point.gameObject);
        }

        forcedTargetPoints.Clear();
    }

    private void EnsurePoolCapacity()
    {
        if (ObjectPool.Instance == null)
            return;

        int headSlots = Mathf.Max(1, ammoCount);
        if (!string.IsNullOrEmpty(bombHeadPoolTag) && bombHeadPrefab != null)
            ObjectPool.Instance.EnsurePoolSize(bombHeadPoolTag, bombHeadPrefab, Mathf.Max(headSlots * 3, 18));

        int desiredBombPoolSize = Mathf.Max(headSlots * 2, 12);
        if (launchSmokePrefab != null && !string.IsNullOrEmpty(launchSmokePoolTag))
            ObjectPool.Instance.EnsurePoolSize(launchSmokePoolTag, launchSmokePrefab, desiredBombPoolSize);

        if (!string.IsNullOrEmpty(bombPoolTag))
        {
            GameObject bombPrefab = ObjectPool.Instance.GetRegisteredPrefab(bombPoolTag);
            if (bombPrefab != null)
                ObjectPool.Instance.EnsurePoolSize(bombPoolTag, bombPrefab, desiredBombPoolSize);
        }
    }

    private void CleanupInvalidLoadedHeads()
    {
        for (int i = 0; i < loadedHeads.Count; i++)
        {
            GameObject head = loadedHeads[i];
            if (IsUsableHead(head))
                continue;

            loadedHeads[i] = null;
        }
    }

    private static bool IsUsableHead(GameObject head)
    {
        return head != null && head.activeInHierarchy && head.scene.IsValid();
    }

    private static void DestroyIfSceneObject(GameObject obj)
    {
        if (obj == null)
            return;

        if (obj.scene.IsValid())
            Object.Destroy(obj);
    }

}
