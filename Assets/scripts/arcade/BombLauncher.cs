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

    [Header("Pool Tags")]
    public string bombHeadPoolTag = "BombHead";
    public string bombPoolTag = "Bomb";

    [Header("Fire Settings")]
    public bool fireFromLast = true;
    public float autoReturnDelay = 0.1f;

    [Header("Forced Fire")]
    public float forcedFireDelay = 10f;
    public Vector2[] forcedTargetWorldPositions = new Vector2[3];

    // runtime
    private Vector3 startPos;
    private List<GameObject> loadedHeads = new List<GameObject>();
    private bool isActive = false;
    private bool isMoving = false;
    private Coroutine forcedFireCoroutine;
    private readonly List<Transform> forcedTargetPoints = new List<Transform>();

    void Awake()
    {
        startPos = transform.position; // ✅ 원래 위치 저장
        EnsureForcedTargetPoints();
        EnsurePoolCapacity();
    }

    void OnEnable()
    {
        // 혹시 풀/재시작으로 Enable 될 때 기본값 복구
        transform.position = startPos;
        isActive = false;
        isMoving = false;

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
        if (isMoving) return;
        if (isActive) return;

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
        if (!CanFire()) return;
        if (target == null) return;

        int idx = GetNextAmmoIndex();
        if (idx < 0) return;

        // 1) head 제거/회수
        GameObject head = loadedHeads[idx];
        loadedHeads[idx] = null;

        Vector3 spawnWorldPos = (head != null)
            ? head.transform.position
            : transform.TransformPoint(GetLocalPosSafe(idx));

        if (head != null)
        {
            head.transform.SetParent(null);

            if (ObjectPool.Instance != null && !string.IsNullOrEmpty(bombHeadPoolTag))
                ObjectPool.Instance.ReturnToPool(bombHeadPoolTag, head);
            else
                Destroy(head);
        }

        // 2) Bomb 스폰
        GameObject bombObj = null;

        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(bombPoolTag))
            bombObj = ObjectPool.Instance.SpawnFromPool(bombPoolTag, spawnWorldPos, Quaternion.identity);

        if (bombObj == null)
        {
            Debug.LogError("[BombLauncher] Bomb spawn failed. ObjectPool tag/prefab 확인 필요: " + bombPoolTag);
            return;
        }

        Bomb bomb = bombObj.GetComponent<Bomb>();
        if (bomb != null) bomb.SetTarget(target);

        // 3) 다 썼으면 복귀 + 재장전
        if (IsAmmoEmpty())
            StartCoroutine(CoReturnAndReload(autoReturnDelay));
    }

    // =========================
    // Coroutines
    // =========================
    private IEnumerator CoActivate()
    {
        isMoving = true;

        // ✅ 활성화될 때마다 “장전 새로” (머리 안 달리는 문제 방지)
        ResetAmmo();

        yield return Move(startPos, activePos);

        isActive = true;
        isMoving = false;
        StartForcedFireTimer();
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
        EnsurePoolCapacity();
        ClearHeads();

        int maxByPos = (headLocalPositions != null) ? headLocalPositions.Length : 0;
        int count = Mathf.Min(ammoCount, maxByPos);

        if (count <= 0)
        {
            Debug.LogWarning("[BombLauncher] headLocalPositions size가 0임. 6개 넣어줘.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 localPos = headLocalPositions[i];

            GameObject headObj = SpawnHead();
            loadedHeads.Add(headObj);

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

    private void StartForcedFireTimer()
    {
        StopForcedFireTimer();

        if (forcedFireDelay <= 0f)
        {
            ForceFireRemainingBombs();
            return;
        }

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
        yield return new WaitForSeconds(forcedFireDelay);
        forcedFireCoroutine = null;
        ForceFireRemainingBombs();
    }

    private void ForceFireRemainingBombs()
    {
        if (!isActive || isMoving || !CanFire()) return;

        UpdateForcedTargetPoints();
        if (forcedTargetPoints.Count == 0)
        {
            Debug.LogWarning("[BombLauncher] forcedTargetWorldPositions가 비어있어서 남은 Bomb 강제 발사를 건너뜀.");
            return;
        }

        int targetIdx = 0;
        while (CanFire())
        {
            Transform forcedTarget = forcedTargetPoints[targetIdx % forcedTargetPoints.Count];
            if (forcedTarget == null) break;

            FireAt(forcedTarget);
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
        isMoving = false;
        isActive = false;

        // 장전된 머리 전부 회수
        ClearHeads();

        // 원래 자리로 순간이동
        transform.position = startPos;
    }
    public void ResetLauncherState()
    {
        StopAllCoroutines();
        forcedFireCoroutine = null;
        isMoving = false;
        isActive = false;

        ClearHeads();
        transform.position = startPos;
    }

    private void OnDestroy()
    {
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
