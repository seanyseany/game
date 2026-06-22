using System.Collections;
using UnityEngine;

public class Turret1 : BaseTurret
{
    [Header("Turret1")]
    [SerializeField] private Transform bulletSpawnPoint;
    [SerializeField] private Bullet bulletPrefab;

    protected override Bullet GetBulletPrefab() => bulletPrefab;

    protected override IEnumerator FireRoutine()
    {
        while (currentTarget != null && HasAmmo())
        {
            SpawnBullet(bulletPrefab, bulletSpawnPoint);
            float wait = bulletPrefab != null ? bulletPrefab.SpawnSpeed : 0.5f;
            yield return new WaitForSeconds(wait);
        }

        firingRoutine = null;
    }
}
