using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BossSlimeCanon : MonoBehaviour, IReinitializable
{
    [Header("Animator")]
    public Animator canonAnimator;
    public string fireTrigger = "Fire";

    [Header("Fire Point (LOCAL)")]
    public Vector3 canonBallStartLocalOffset = Vector3.zero;

    [Header("Canon Ball")]
    public string canonBallPoolTag = "BossSlimeCanonBall";
    public GameObject canonBallPrefab;

    [Header("Pool")]
    public bool usePool = true;

    [Header("Idle Pose")]
    public float idleZ = -90f;
    public string idleStateName = "Idle";
    public float fireAnimReturnDelay = 0.15f;

    private Coroutine fireRoutine;
    private Coroutine flashRoutine;
    private Renderer[] cachedRenderers;
    private List<Color[]> originalColors = new List<Color[]>();

    void Awake()
    {
        if (canonAnimator == null)
            canonAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        CacheRendererColors();
    }

    public void Reinit()
    {
        if (fireRoutine != null)
        {
            StopCoroutine(fireRoutine);
            fireRoutine = null;
        }

        if (canonAnimator != null && !string.IsNullOrEmpty(fireTrigger))
            canonAnimator.ResetTrigger(fireTrigger);

        // 풀에서 다시 나올 때 발사 상태가 남아있지 않게 기본 상태로 복귀
        if (canonAnimator != null)
            TryPlayState(canonAnimator, idleStateName, 0f);

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        RestoreRendererColors();

        SetZRotation(idleZ);
    }

    public void AddZRotation(float deltaZ)
    {
        transform.localRotation = Quaternion.Euler(0f, 0f, transform.localEulerAngles.z + deltaZ);
    }

    public void SetZRotation(float zDeg)
    {
        transform.localRotation = Quaternion.Euler(0f, 0f, zDeg);
    }

    public void PlayFireAnimation()
    {
        if (canonAnimator == null || string.IsNullOrEmpty(fireTrigger)) return;
        if (fireRoutine != null) StopCoroutine(fireRoutine);
        fireRoutine = StartCoroutine(CoPlayFireThenIdle());
    }

    private System.Collections.IEnumerator CoPlayFireThenIdle()
    {
        canonAnimator.SetTrigger(fireTrigger);

        float wait = Mathf.Max(0.01f, fireAnimReturnDelay);
        yield return new WaitForSeconds(wait);

        TryPlayState(canonAnimator, idleStateName, 0f);

        fireRoutine = null;
    }

    public GameObject SpawnCanonBall(float shotAngleDeg, int forcedVersionIndex = -1)
    {
        Vector3 worldPos = transform.TransformPoint(canonBallStartLocalOffset);

        GameObject ball = null;
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(canonBallPoolTag) && ObjectPool.Instance.HasPool(canonBallPoolTag))
            ball = ObjectPool.Instance.SpawnFromPool(canonBallPoolTag, worldPos, Quaternion.identity);
        else if (canonBallPrefab != null)
            ball = Instantiate(canonBallPrefab, worldPos, Quaternion.identity);

        if (ball == null) return null;

        var cs = ball.GetComponent<BossSlimeCanonBall>();
        if (cs != null)
        {
            cs.usePool = usePool;
            cs.poolTag = canonBallPoolTag;
            int vCount = cs.GetVersionCount();
            if (vCount > 0)
            {
                if (forcedVersionIndex >= 0)
                    cs.ApplyVersion(Mathf.Clamp(forcedVersionIndex, 0, vCount - 1));
                else
                    cs.ApplyVersion(Random.Range(0, vCount));
            }
            cs.LaunchAtAngle(shotAngleDeg);
        }

        return ball;
    }

    public void TriggerHitFlash(Color color, float duration)
    {
        if (flashRoutine != null)
            StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(CoFlash(color, duration));
    }

    private IEnumerator CoFlash(Color color, float duration)
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0 || originalColors.Count != cachedRenderers.Length)
            CacheRendererColors();

        SetAllRenderersColor(color);
        yield return new WaitForSeconds(Mathf.Max(0.01f, duration));
        RestoreRendererColors();
        flashRoutine = null;
    }

    private void CacheRendererColors()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        originalColors.Clear();
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            var r = cachedRenderers[i];
            if (r == null)
            {
                originalColors.Add(null);
                continue;
            }

            var mats = r.materials;
            Color[] cols = new Color[mats.Length];
            for (int m = 0; m < mats.Length; m++)
                cols[m] = (mats[m] != null && mats[m].HasProperty("_Color")) ? mats[m].color : Color.white;
            originalColors.Add(cols);
        }
    }

    private void SetAllRenderersColor(Color c)
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0)
            cachedRenderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            var r = cachedRenderers[i];
            if (r == null) continue;
            var mats = r.materials;
            for (int m = 0; m < mats.Length; m++)
                if (mats[m] != null && mats[m].HasProperty("_Color"))
                    mats[m].color = c;
        }
    }

    private void RestoreRendererColors()
    {
        if (cachedRenderers == null || originalColors == null) return;
        if (originalColors.Count != cachedRenderers.Length) return;

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            var r = cachedRenderers[i];
            var cols = originalColors[i];
            if (r == null || cols == null) continue;

            var mats = r.materials;
            for (int m = 0; m < mats.Length && m < cols.Length; m++)
                if (mats[m] != null && mats[m].HasProperty("_Color"))
                    mats[m].color = cols[m];
        }
    }

    private static bool TryPlayState(Animator animator, string stateName, float normalizedTime)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return false;
        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash)) return false;
        animator.Play(hash, 0, normalizedTime);
        return true;
    }
}
