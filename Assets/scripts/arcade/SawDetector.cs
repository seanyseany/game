using UnityEngine;

public class SawDetector : MonoBehaviour
{
    public BombLauncher launcher; // 인스펙터에 런쳐 연결

    private void OnTriggerEnter2D(Collider2D other)
    {
        var info = other.GetComponent<ObstacleInfo>();
        if (info != null && info.type == ObstacleType.Saw)
        {
            if (launcher != null)
                launcher.FireAt(other.transform);
        }
    }
}