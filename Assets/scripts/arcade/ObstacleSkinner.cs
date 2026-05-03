using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ObstacleSkinner : MonoBehaviour
{
    public SpriteRenderer sr;

    void Reset()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
    }

    public void SetSprite(Sprite sprite)
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
    }
}
