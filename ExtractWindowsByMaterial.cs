using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ExtractWindowsByMaterial : EditorWindow
{
    [MenuItem("Tools/Extract Windows By Material")]
    static void Extract()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null || selected.GetComponent<MeshFilter>() == null)
        {
            Debug.LogError("Select a GameObject with MeshFilter.");
            return;
        }

        MeshFilter mf = selected.GetComponent<MeshFilter>();
        MeshRenderer mr = selected.GetComponent<MeshRenderer>();
        Mesh mesh = mf.sharedMesh;

        // Ask user which material index is the window material
        string[] materialNames = mr.sharedMaterials.Select(m => m.name).ToArray();
        int windowMatIndex = EditorUtility.DisplayDialogComplex(
            "Select Window Material",
            "Which material is used for the windows?",
            materialNames[0], materialNames.Length > 1 ? materialNames[1] : null, "Cancel"
        );

        if (windowMatIndex == 1 && materialNames.Length > 1) windowMatIndex = 1;
        else if (windowMatIndex == 2) return;

        // Get all triangles of the window submesh
        int[] windowTriangles = mesh.GetTriangles(windowMatIndex);
        if (windowTriangles.Length == 0)
        {
            Debug.LogError("No triangles found for selected material.");
            return;
        }

        // Build adjacency for these triangles only
        Vector3[] verts = mesh.vertices;
        List<HashSet<int>> adjacency = BuildAdjacency(windowTriangles, verts);

        // Find connected components (islands)
        List<List<int>> islands = FindConnectedComponents(adjacency);

        Debug.Log($"Found {islands.Count} window islands.");

        // Create parent
        Transform parent = new GameObject(selected.name + "_Windows").transform;
        parent.SetParent(selected.transform);
        parent.localPosition = Vector3.zero;

        Material windowMat = mr.sharedMaterials[windowMatIndex];

        // Create one GameObject per island
        for (int i = 0; i < islands.Count; i++)
        {
            Mesh islandMesh = ExtractMeshFromTriangles(windowTriangles, islands[i], verts, mesh);
            GameObject windowObj = new GameObject($"Window_{i}");
            windowObj.transform.SetParent(parent);
            windowObj.transform.localPosition = Vector3.zero;
            windowObj.AddComponent<MeshFilter>().sharedMesh = islandMesh;
            windowObj.AddComponent<MeshRenderer>().sharedMaterial = windowMat;

            // Add your break script here
            // windowObj.AddComponent<BreakWindow>();
        }

        // Optional: hide original windows by disabling the material or renderer
        // mr.sharedMaterials[windowMatIndex] = null; // careful with this
        Debug.Log("Done.");
    }

    static List<HashSet<int>> BuildAdjacency(int[] tris, Vector3[] verts)
    {
        int triCount = tris.Length / 3;
        List<HashSet<int>> adj = new List<HashSet<int>>();
        for (int i = 0; i < triCount; i++) adj.Add(new HashSet<int>());

        // Map each vertex to triangles that use it
        Dictionary<int, List<int>> vertexToTriangles = new Dictionary<int, List<int>>();
        for (int t = 0; t < triCount; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = tris[t * 3 + k];
                if (!vertexToTriangles.ContainsKey(v))
                    vertexToTriangles[v] = new List<int>();
                vertexToTriangles[v].Add(t);
            }
        }

        // Two triangles are adjacent if they share an edge (2 common vertices)
        for (int t = 0; t < triCount; t++)
        {
            int v0 = tris[t * 3 + 0];
            int v1 = tris[t * 3 + 1];
            int v2 = tris[t * 3 + 2];

            // For each vertex, check triangles that share it and test edge sharing
            HashSet<int> candidates = new HashSet<int>();
            foreach (int v in new[] { v0, v1, v2 })
                if (vertexToTriangles.ContainsKey(v))
                    foreach (int nt in vertexToTriangles[v])
                        if (nt != t) candidates.Add(nt);

            foreach (int nt in candidates)
            {
                int n0 = tris[nt * 3 + 0];
                int n1 = tris[nt * 3 + 1];
                int n2 = tris[nt * 3 + 2];
                int shared = 0;
                if (n0 == v0 || n0 == v1 || n0 == v2) shared++;
                if (n1 == v0 || n1 == v1 || n1 == v2) shared++;
                if (n2 == v0 || n2 == v1 || n2 == v2) shared++;
                if (shared >= 2) adj[t].Add(nt);
            }
        }
        return adj;
    }

    static List<List<int>> FindConnectedComponents(List<HashSet<int>> adj)
    {
        bool[] visited = new bool[adj.Count];
        List<List<int>> components = new List<List<int>>();

        for (int i = 0; i < adj.Count; i++)
        {
            if (!visited[i])
            {
                List<int> comp = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    comp.Add(cur);
                    foreach (int n in adj[cur])
                        if (!visited[n]) { visited[n] = true; queue.Enqueue(n); }
                }
                components.Add(comp);
            }
        }
        return components;
    }

    static Mesh ExtractMeshFromTriangles(int[] allWindowTris, List<int> triangleIndices, Vector3[] sourceVerts, Mesh sourceMesh)
    {
        Dictionary<int, int> remap = new Dictionary<int, int>();
        List<Vector3> newVerts = new List<Vector3>();
        List<int> newTris = new List<int>();

        foreach (int triIdx in triangleIndices)
        {
            for (int k = 0; k < 3; k++)
            {
                int oldVert = allWindowTris[triIdx * 3 + k];
                if (!remap.ContainsKey(oldVert))
                {
                    remap[oldVert] = newVerts.Count;
                    newVerts.Add(sourceVerts[oldVert]);
                }
                newTris.Add(remap[oldVert]);
            }
        }

        Mesh newMesh = new Mesh();
        newMesh.vertices = newVerts.ToArray();
        newMesh.triangles = newTris.ToArray();
        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();

        // Copy UVs if needed
        if (sourceMesh.uv.Length > 0)
        {
            Vector2[] newUVs = new Vector2[newVerts.Count];
            foreach (var kv in remap)
                newUVs[kv.Value] = sourceMesh.uv[kv.Key];
            newMesh.uv = newUVs;
        }

        return newMesh;
    }
}