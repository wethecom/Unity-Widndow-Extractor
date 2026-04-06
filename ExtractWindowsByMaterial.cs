using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ExtractWindowsByMaterial : EditorWindow
{
    // --- Material picker popup ---
    private static string[] _materialNames;
    private static int _selectedIndex = 0;
    private static System.Action<int> _onConfirm;

    [MenuItem("Tools/Extract Windows By Material")]
    static void Extract()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null || selected.GetComponent<MeshFilter>() == null)
        {
            Debug.LogError("Select a GameObject with a MeshFilter.");
            return;
        }

        MeshRenderer mr = selected.GetComponent<MeshRenderer>();
        if (mr == null || mr.sharedMaterials.Length == 0)
        {
            Debug.LogError("Selected object has no MeshRenderer or materials.");
            return;
        }

        _materialNames = mr.sharedMaterials.Select((m, i) => $"{i}: {(m != null ? m.name : "null")}").ToArray();
        _selectedIndex = 0;

        _onConfirm = (chosenIndex) =>
        {
            RunExtraction(selected, chosenIndex);
        };

        ExtractWindowsByMaterial window = GetWindow<ExtractWindowsByMaterial>("Select Window Material");
        window.minSize = new Vector2(300, 120);
        window.ShowUtility();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Which material is used for the windows?", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space();

        _selectedIndex = EditorGUILayout.Popup("Window Material", _selectedIndex, _materialNames);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Extract"))
        {
            int chosen = _selectedIndex;
            Close();
            _onConfirm?.Invoke(chosen);
        }

        if (GUILayout.Button("Cancel"))
        {
            Close();
        }

        EditorGUILayout.EndHorizontal();
    }

    static void RunExtraction(GameObject selected, int windowMatIndex)
    {
        MeshFilter mf = selected.GetComponent<MeshFilter>();
        MeshRenderer mr = selected.GetComponent<MeshRenderer>();
        Mesh mesh = mf.sharedMesh;

        int[] windowTriangles = mesh.GetTriangles(windowMatIndex);
        if (windowTriangles.Length == 0)
        {
            Debug.LogError($"No triangles found for material index {windowMatIndex}.");
            return;
        }

        Vector3[] verts = mesh.vertices;
        List<HashSet<int>> adjacency = BuildAdjacency(windowTriangles, verts);
        List<List<int>> islands = FindConnectedComponents(adjacency);

        Debug.Log($"Found {islands.Count} window island(s).");

        Transform parent = new GameObject(selected.name + "_Windows").transform;
        parent.SetParent(selected.transform);
        parent.localPosition = Vector3.zero;

        Material windowMat = mr.sharedMaterials[windowMatIndex];

        for (int i = 0; i < islands.Count; i++)
        {
            Mesh islandMesh = ExtractMeshFromTriangles(windowTriangles, islands[i], verts, mesh);

            GameObject windowObj = new GameObject($"Window_{i}");
            windowObj.transform.SetParent(parent);
            windowObj.transform.localPosition = Vector3.zero;
            windowObj.AddComponent<MeshFilter>().sharedMesh = islandMesh;
            windowObj.AddComponent<MeshRenderer>().sharedMaterial = windowMat;

            // windowObj.AddComponent<BreakWindow>();
        }

        Debug.Log("Extraction complete.");
    }

    static List<HashSet<int>> BuildAdjacency(int[] tris, Vector3[] verts)
    {
        int triCount = tris.Length / 3;
        List<HashSet<int>> adj = new List<HashSet<int>>();
        for (int i = 0; i < triCount; i++) adj.Add(new HashSet<int>());

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

        for (int t = 0; t < triCount; t++)
        {
            int v0 = tris[t * 3 + 0];
            int v1 = tris[t * 3 + 1];
            int v2 = tris[t * 3 + 2];

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