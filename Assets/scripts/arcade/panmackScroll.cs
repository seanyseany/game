using UnityEngine;

public class pandmackScroll : MonoBehaviour
{   
    [Header("Settings")]
    [Tooltip("Scroll speed") ]
    public float scrollSpeed; 

    [Header("References")]
    public MeshRenderer meshRenderer;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Vector2 offset = meshRenderer.material.mainTextureOffset;
        offset.x -= scrollSpeed * Time.deltaTime;
        offset.x = Mathf.Repeat(offset.x, 1f);
        meshRenderer.material.mainTextureOffset = offset;
    }
}
