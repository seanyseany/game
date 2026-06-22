using System.Collections;
using UnityEngine;

public class Turret2 : BaseTurret
{
    [Header("Turret2")]
    [SerializeField] private Transform bulletSpawnPoint1;
    [SerializeField] private Transform bulletSpawnPoint2;
    [SerializeField] private Bullet bulletPrefab;
    [SerializeField] private GameObject bulletCap;

    protected override Bullet GetBulletPrefab() => bulletPrefab;

    protected override IEnumerator FireRoutine()
    {
        bool useFirst = true;
        while (currentTarget != null && HasAmmo())
        {
            if (bulletCap != null)
                bulletCap.SetActive(false);

            Transform spawnPoint = useFirst ? bulletSpawnPoint1 : bulletSpawnPoint2;
            SpawnBullet(bulletPrefab, spawnPoint);
            useFirst = !useFirst;

            if (bulletCap != null)
                bulletCap.SetActive(HasAmmo());

            float wait = bulletPrefab != null ? Mathf.Max(0.05f, bulletPrefab.SpawnSpeed * 0.5f) : 0.25f;
            yield return new WaitForSeconds(wait);
        }

        if (bulletCap != null)
            bulletCap.SetActive(HasAmmo());

        firingRoutine = null;
    }
}
