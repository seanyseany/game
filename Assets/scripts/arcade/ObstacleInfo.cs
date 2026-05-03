using UnityEngine;

public enum ObstacleType
{
    Normal,
    Missile,
    Saw,
    Hill
}

public class ObstacleInfo : MonoBehaviour
{
    public ObstacleType type;
}
