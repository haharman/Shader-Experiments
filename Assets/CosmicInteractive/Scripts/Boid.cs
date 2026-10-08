using UnityEngine;

public class Boid : MonoBehaviour
{
    public Vector3 velocity;
    public Transform model;
    
    void Start()
    {
        //var b = GetComponent<MeshFilter>().sharedMesh.bounds;
        //Debug.Log($"min.x={b.min.x}  max.x={b.max.x}  size.x={b.size.x}");
    }
}