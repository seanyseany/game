using System.Collections;
using UnityEngine;

public class MachineGun : MonoBehaviour
{
    [Header("Bullet")]
    public MachineGunBullet bulletPrefab;
    public Vector2 fireEffectLocalPos = Vector2.zero;
    public Vector2[] bulletSpawnLocalPositions = new Vector2[2];
    public float bulletSpawnTime = 0f;
    public float bulletSpawnInterval = 0.12f;
    public GameObject fireEffect;

    [Header("Aim")]
    public Vector2 rearCenterLocalPos = Vector2.zero;
    public Vector2 noseLocalPos = Vector2.right;
    public float upperMaxAngle = 35f;
    public float lowerMaxAngle = -35f;
    public float angleUpMoveSpeed = 90f;
    public float angleDownMoveSpeed = 90f;

    private const float aimResetDuration = 1f;
    private const int defaultBulletPoolSize = 24;

    private Coroutine aimResetRoutine;
    private float currentAimAngle;
    private float nextBulletTime;
    private int nextSpawnIndex;
    private bool fireHeld;
    private Player controllingPlayer;
    private Animator machineGunAnimator;
    private Animator fireEffectAnimator;
    private GameObject runtimeFireEffect;

    public bool IsPlayerControlActive { get; private set; }

    private void Awake()
    {
        EnsureFireEffectInstance();
        CacheAnimators();
        EnsureBulletPool();
        ReinitMountedState();
    }

    private void OnEnable()
    {
        EnsureFireEffectInstance();
        CacheAnimators();
        EnsureBulletPool();
        ReinitMountedState();
    }

    private void OnDisable()
    {
        StopAimResetRoutine();
        ReleasePlayerControl();
        SetFireAnimationPlaying(false);
    }

    public void ReinitMountedState()
    {
        StopAimResetRoutine();
        ReleasePlayerControl();
        fireHeld = false;
        currentAimAngle = lowerMaxAngle;
        ApplyPose();
        ApplyFireEffectLocalPosition();
        SetFireAnimationPlaying(false);
    }

    public void BeginActivation()
    {
        StopAimResetRoutine();
        ReleasePlayerControl();
        fireHeld = false;
        ApplyPose();
        ApplyFireEffectLocalPosition();
        SetFireAnimationPlaying(false);
    }

    public void BeginPlayerControl()
    {
        StopAimResetRoutine();
        ReleasePlayerControl();
        aimResetRoutine = StartCoroutine(CoResetAimAndEnableControl());
    }

    public void BeginPlayerControlImmediate()
    {
        StopAimResetRoutine();
        ReleasePlayerControl();
        currentAimAngle = lowerMaxAngle;
        ApplyPose();
        EnablePlayerControl();
    }

    public void BeginDeactivation()
    {
        StopAimResetRoutine();
        ReleasePlayerControl();
        fireHeld = false;
        ApplyPose();
        ApplyFireEffectLocalPosition();
        SetFireAnimationPlaying(false);
    }

    public void HandlePlayerControl()
    {
        if (!IsPlayerControlActive)
            return;

        float minAngle = Mathf.Min(lowerMaxAngle, upperMaxAngle);
        float maxAngle = Mathf.Max(lowerMaxAngle, upperMaxAngle);
        float targetAimAngle = Input.GetKey(KeyCode.Space) ? maxAngle : minAngle;
        float moveSpeed = targetAimAngle > currentAimAngle
            ? Mathf.Max(0f, angleUpMoveSpeed)
            : Mathf.Max(0f, angleDownMoveSpeed);

        currentAimAngle = Mathf.MoveTowards(currentAimAngle, targetAimAngle, moveSpeed * Time.deltaTime);
        ApplyPose();

        bool firePressed = Input.GetKey(KeyCode.DownArrow);
        if (!firePressed)
        {
            fireHeld = false;
            SetFireAnimationPlaying(false);
            return;
        }

        if (!fireHeld)
        {
            fireHeld = true;
            nextBulletTime = Time.time + Mathf.Max(0f, bulletSpawnTime);
            SetFireAnimationPlaying(true);
        }

        float interval = Mathf.Max(0.01f, bulletSpawnInterval);
        while (Time.time >= nextBulletTime)
        {
            FireOneBullet();
            nextBulletTime += interval;
        }
    }

    private IEnumerator CoResetAimAndEnableControl()
    {
        float startAimAngle = currentAimAngle;
        float elapsed = 0f;

        while (elapsed < aimResetDuration)
        {
            float t = elapsed / aimResetDuration;
            currentAimAngle = Mathf.LerpAngle(startAimAngle, lowerMaxAngle, t);
            ApplyPose();
            elapsed += Time.deltaTime;
            yield return null;
        }

        currentAimAngle = lowerMaxAngle;
        ApplyPose();
        EnablePlayerControl();
        aimResetRoutine = null;
    }

    private void EnablePlayerControl()
    {
        controllingPlayer = Player.Instance != null ? Player.Instance : Object.FindFirstObjectByType<Player>();
        if (controllingPlayer != null)
            controllingPlayer.SetMachineGunController(this);

        nextBulletTime = Time.time + Mathf.Max(0f, bulletSpawnTime);
        nextSpawnIndex = 0;
        IsPlayerControlActive = true;
    }

    private void FireOneBullet()
    {
        if (bulletPrefab == null)
            return;

        int spawnCount = bulletSpawnLocalPositions != null ? bulletSpawnLocalPositions.Length : 0;
        Vector2 spawnLocal = spawnCount > 0
            ? bulletSpawnLocalPositions[nextSpawnIndex % spawnCount]
            : Vector2.zero;

        Vector3 spawnWorld = transform.TransformPoint(new Vector3(spawnLocal.x, spawnLocal.y, 0f));
        Vector3 rearWorld = transform.TransformPoint(new Vector3(rearCenterLocalPos.x, rearCenterLocalPos.y, 0f));
        Vector3 noseWorld = transform.TransformPoint(new Vector3(noseLocalPos.x, noseLocalPos.y, 0f));
        Vector2 direction = (noseWorld - rearWorld).sqrMagnitude > 0.0001f
            ? (noseWorld - rearWorld).normalized
            : Vector2.right;

        MachineGunBullet bullet = SpawnBullet(spawnWorld);
        if (bullet == null)
            return;

        bullet.Launch(direction);
        nextSpawnIndex++;
    }

    private MachineGunBullet SpawnBullet(Vector3 spawnWorld)
    {
        string poolTag = bulletPrefab.poolTag;
        GameObject bulletObject = null;

        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            bulletObject = ObjectPool.Instance.SpawnFromPool(poolTag, spawnWorld, Quaternion.identity);

        if (bulletObject == null)
            bulletObject = Instantiate(bulletPrefab.gameObject, spawnWorld, Quaternion.identity);

        return bulletObject != null ? bulletObject.GetComponent<MachineGunBullet>() : null;
    }

    private void ApplyPose()
    {
        transform.localRotation = Quaternion.Euler(0f, 0f, currentAimAngle);
    }

    private void CacheAnimators()
    {
        machineGunAnimator = GetComponent<Animator>();
        GameObject effectObject = GetFireEffectObject();
        fireEffectAnimator = effectObject != null ? effectObject.GetComponent<Animator>() : null;
    }

    private void ApplyFireEffectLocalPosition()
    {
        GameObject effectObject = GetFireEffectObject();
        if (effectObject == null)
            return;

        effectObject.transform.localPosition = new Vector3(
            fireEffectLocalPos.x,
            fireEffectLocalPos.y,
            effectObject.transform.localPosition.z
        );
    }

    private void SetFireAnimationPlaying(bool isPlaying)
    {
        GameObject effectObject = GetFireEffectObject();
        if (effectObject != null && effectObject.activeSelf != isPlaying)
            effectObject.SetActive(isPlaying);

        SetAnimatorPlaying(machineGunAnimator, isPlaying);
        SetAnimatorPlaying(fireEffectAnimator, isPlaying);
    }

    private void EnsureFireEffectInstance()
    {
        if (runtimeFireEffect != null)
            return;

        if (fireEffect == null)
            return;

        if (fireEffect.scene.IsValid())
        {
            runtimeFireEffect = fireEffect;
            return;
        }

        runtimeFireEffect = Instantiate(fireEffect, transform);
        runtimeFireEffect.name = fireEffect.name;
        runtimeFireEffect.SetActive(false);
    }

    private GameObject GetFireEffectObject()
    {
        return runtimeFireEffect != null ? runtimeFireEffect : fireEffect;
    }

    private static void SetAnimatorPlaying(Animator animator, bool isPlaying)
    {
        if (animator == null)
            return;

        animator.speed = isPlaying ? 1f : 0f;
    }

    private void EnsureBulletPool()
    {
        if (bulletPrefab == null || ObjectPool.Instance == null)
            return;

        string poolTag = bulletPrefab.poolTag;
        if (string.IsNullOrEmpty(poolTag))
            return;

        if (!ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.RegisterPool(poolTag, bulletPrefab.gameObject, defaultBulletPoolSize);
        else
            ObjectPool.Instance.EnsurePoolSize(poolTag, bulletPrefab.gameObject, defaultBulletPoolSize);
    }

    private void StopAimResetRoutine()
    {
        if (aimResetRoutine == null)
            return;

        StopCoroutine(aimResetRoutine);
        aimResetRoutine = null;
    }

    private void ReleasePlayerControl()
    {
        IsPlayerControlActive = false;

        if (controllingPlayer != null)
        {
            controllingPlayer.ClearMachineGunController(this);
            controllingPlayer = null;
        }
    }
}
