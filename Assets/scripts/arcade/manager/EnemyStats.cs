[System.Serializable]   // ✅ Inspector에서 배열로 보이게
public class EnemyStats
{
    public int level;
    public int attack;
    public int health;

    public EnemyStats(int level, int attack, int health)
    {
        this.level = level;
        this.attack = attack;
        this.health = health;
    }
}