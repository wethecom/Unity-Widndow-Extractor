using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ProceduralShatter : MonoBehaviour
{
    [Header("Assets")]
    [Tooltip("Drag your Glass Material here. If left empty, it will use the object's current material.")]
    public Material glassMaterial;
    [Tooltip("Drag your Glass Break AudioClip here.")]
    public AudioClip breakSound;
    [Range(0f, 1f)]
    public float soundVolume = 1f;

    [Header("Fragment Settings")]
    public int fragmentCount = 30;          
    public float minShardSize = 0.1f;            
    public float maxShardSize = 0.3f;            

    [Header("Physics")]
    public float explosiveForce = 15f;       
    public float durability = 2f;
    [Tooltip("If true, the glass stays still until it breaks. If false, it will fall with gravity.")]
    public bool stayInPlace = true;

    [Header("Cleanup")]
    public float destroyFragmentsAfter = 3f;

    [Header("Effects")]
    public ParticleSystem breakParticles;
    public bool mouseClickBreaks = true;

    private bool isBroken = false;

    void Awake()
    {
        AutomatePhysicsSetup();
        
        // Fallback for material
        if (glassMaterial == null)
        {
            var rend = GetComponent<Renderer>();
            if (rend != null) glassMaterial = rend.material;
        }
    }

    private void AutomatePhysicsSetup()
    {
        // 1. Automate Rigidbody
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            Debug.Log($"[Shatter] Added missing Rigidbody to {gameObject.name}");
        }
        rb.isKinematic = stayInPlace;
        rb.useGravity = !stayInPlace;

        // 2. Automate Collider
        if (GetComponent<Collider>() == null)
        {
            // We add a BoxCollider as it's the most common for glass panes/windows
            BoxCollider boxCol = gameObject.AddComponent<BoxCollider>();
            
            // Try to size the collider to the mesh automatically
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null)
            {
                boxCol.center = mf.sharedMesh.bounds.center;
                boxCol.size = mf.sharedMesh.bounds.size;
            }
            Debug.Log($"[Shatter] Added missing BoxCollider to {gameObject.name}");
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isBroken && collision.relativeVelocity.magnitude > durability)
        {
            Shatter(collision.contacts[0].point);
        }
    }

    void OnMouseDown()
    {
        if (mouseClickBreaks && !isBroken)
            Shatter(transform.position);
    }

    public void Shatter(Vector3 impactPoint)
    {
        if (isBroken) return;
        isBroken = true;

        // Hide original glass
        var rend = GetComponent<Renderer>();
        if (rend != null) rend.enabled = false;
        
        // Play Sound
        if (breakSound != null)
        {
            AudioSource.PlayClipAtPoint(breakSound, transform.position, soundVolume);
        }

        // Play Particles
        if (breakParticles != null)
        {
            var ps = Instantiate(breakParticles, transform.position, Quaternion.identity);
            Destroy(ps.gameObject, ps.main.duration);
        }

        // Clean up physics on original object so it doesn't block the shards
        Destroy(GetComponent<Collider>());
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        StartCoroutine(SpawnShards(impactPoint));
    }

    private IEnumerator SpawnShards(Vector3 impactPoint)
    {
        Bounds bounds = GetBounds();
        List<GameObject> shards = new List<GameObject>();

        for (int i = 0; i < fragmentCount; i++)
        {
            Mesh shardMesh = GenerateShardMesh();

            GameObject shard = new GameObject("Shard_" + i);
            shard.transform.position = RandomPointInBounds(bounds);
            shard.transform.rotation = Random.rotation;
            
            float scale = Random.Range(minShardSize, maxShardSize);
            shard.transform.localScale = Vector3.one * scale;

            shard.AddComponent<MeshFilter>().mesh = shardMesh;
            shard.AddComponent<MeshRenderer>().material = glassMaterial;
            
            Rigidbody sRb = shard.AddComponent<Rigidbody>();
            MeshCollider sCol = shard.AddComponent<MeshCollider>();
            sCol.sharedMesh = shardMesh;
            sCol.convex = true;

            // Explosive "Pop" force
            Vector3 pushDir = (shard.transform.position - impactPoint).normalized;
            sRb.AddForce(pushDir * Random.Range(explosiveForce * 0.5f, explosiveForce), ForceMode.Impulse);
            sRb.AddTorque(Random.insideUnitSphere * explosiveForce, ForceMode.Impulse);

            shards.Add(shard);
        }

        yield return new WaitForSeconds(destroyFragmentsAfter);

        foreach (var s in shards)
        {
            if (s != null) Destroy(s);
        }

        Destroy(gameObject);
    }

    private Mesh GenerateShardMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[4];
        vertices[0] = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0);
        vertices[1] = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0);
        vertices[2] = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0);
        vertices[3] = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), Random.Range(0.5f, 1.5f));

        int[] triangles = new int[] { 0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3 };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

    private Vector3 RandomPointInBounds(Bounds b)
    {
        return new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
    }

    private Bounds GetBounds()
    {
        var rend = GetComponent<Renderer>();
        if (rend != null) return rend.bounds;
        return new Bounds(transform.position, Vector3.one);
    }
}