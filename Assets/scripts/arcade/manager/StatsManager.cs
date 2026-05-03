using UnityEngine;

public class StatsManager
{
    // 기본 Player 데이터
    private static readonly int[] baseAtk   = { 0, 1, 2, 2, 3, 4 };
    private static readonly int[] baseHp    = { 0, 1, 1, 2, 2, 3 };
    private static readonly int[] manaCost  = { 0, 1, 2, 3, 4, 5 };

    // ---- Player ----
    public static PlayerStats GetPlayerStats(int type, int playerLevel)
    {
        if (type < 1 || type > 5) return null;
        if (playerLevel <= 0) playerLevel = 1;

        // ✅ 기본 공격력 + (레벨 - 1)
        int atk = baseAtk[type] + (playerLevel - 1);

        // HP는 지금처럼 유지 (곱셈이든 +든 따로 조정 가능)
        int hp  = baseHp[type] * playerLevel;

        int cost = manaCost[type];

        return new PlayerStats(type, playerLevel, atk, hp, cost);
    }

    // ---- Mana ----
    public static int GetManaMax(int manaValue)
    {
        return manaValue; // Inspector에서 준 값 그대로 최대 마나로 사용
    }
}
