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
        meshRenderer.material.mainTextureOffset -= new Vector2(scrollSpeed * Time.deltaTime, 0);
    }
}