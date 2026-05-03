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

    // runtime
    private Vector3 startPos;
    private List<GameObject> loadedHeads = new List<GameObject>();
    private bool isActive = false;
    private bool isMoving = false;

    void Awake()
    {
        startPos = transform.position; // ✅ 원래 위치 저장
    }

    void OnEnable()
    {
        // 혹시 풀/재시작으로 Enable 될 때 기본값 복구
        transform.position = startPos;
        isActive = false;
        isMoving = false;

        // 여기서 바로 장전하면 “항상 보임”이라서,
        // Activate 때 장전하도록 둠.
        ClearHeads();
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

        for (int i = 0; i < loadedHeads.Count; i++)
            if (loadedHeads[i] != null) return true;

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
    }

    private IEnumerator CoReturnAndReload(float delay)
    {
        yield return new WaitForSeconds(delay);

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

            if (ObjectPool.Instance != null && !string.IsNullOrEmpty(bombHeadPoolTag))
                ObjectPool.Instance.ReturnToPool(bombHeadPoolTag, h);
            else
                Destroy(h);
        }
        loadedHeads.Clear();
    }

    private void ResetAmmo()
    {
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
            return ObjectPool.Instance.SpawnFromPool(bombHeadPoolTag, transform.position, Quaternion.identity);
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
        for (int i = 0; i < loadedHeads.Count; i++)
            if (loadedHeads[i] != null) return false;
        return true;
    }

    private int GetNextAmmoIndex()
    {
        if (loadedHeads.Count == 0) return -1;

        if (fireFromLast)
        {
            for (int i = loadedHeads.Count - 1; i >= 0; i--)
                if (loadedHeads[i] != null) return i;
        }
        else
        {
            for (int i = 0; i < loadedHeads.Count; i++)
                if (loadedHeads[i] != null) return i;
        }

        return -1;
    }

    private Vector3 GetLocalPosSafe(int idx)
    {
        if (headLocalPositions == null || headLocalPositions.Length == 0) return Vector3.zero;
        idx = Mathf.Clamp(idx, 0, headLocalPositions.Length - 1);
        return headLocalPositions[idx];
    }
    public void ForceStopAndReturnHome()
    {
        StopAllCoroutines();          // 이동/복귀/발사 관련 코루틴 전부 중단
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
        isMoving = false;
        isActive = false;

        ClearHeads();
        transform.position = startPos;
    }

}
