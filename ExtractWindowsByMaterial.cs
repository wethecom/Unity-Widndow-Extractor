using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ExtractWindowsByMaterial : EditorWindow
{
    private static string[] _materialNames;
    private static int _selectedIndex = 0;
    private static System.Action<int> _onConfirm;

    [MenuItem("Tools/Extract Windows By Material")]
    static void Extract()
    {
        GameObject[] candidates = Selection.gameObjects
            .Where(g => g.GetComponent<MeshFilter>() != null && g.GetComponent<MeshRenderer>() != null)
            .ToArray();

        if (candidates.Length == 0)
        {
            Debug.LogError("Select one or more GameObjects with a MeshFilter and MeshRenderer.");
            return;
        }

        HashSet<string> seen = new HashSet<string>();
        List<string> allMatNames = new List<string>();
        foreach (GameObject g in candidates)
            foreach (Material m in g.GetComponent<MeshRenderer>().sharedMaterials)
                if (m != null && seen.Add(m.name))
                    allMatNames.Add(m.name);

        _materialNames = allMatNames.ToArray();
        _selectedIndex = 0;

        _onConfirm = (chosenIndex) =>
        {
            string chosenMatName = _materialNames[chosenIndex];
            int processed = 0;
            foreach (GameObject g in candidates)
            {
                MeshRenderer mr = g.GetComponent<MeshRenderer>();
                int matIndex = System.Array.FindIndex(mr.sharedMaterials, m => m != null && m.name == chosenMatName);
                if (matIndex == -1) continue;
                RunExtraction(g, matIndex);
                processed++;
            }
            Debug.Log($"Extraction complete. Processed {processed} object(s).");
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
        if (GUILayout.Button("Cancel")) Close();
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
            Debug.LogWarning($"{selected.name}: No triangles found for material index {windowMatIndex}.");
            return;
        }

        // Vertices stay in local space. The window children inherit the parent's
        // transform so they sit correctly no matter the parent's rotation or position.
        Vector3[] verts = mesh.vertices;

        List<HashSet<int>> adjacency = BuildAdjacency(windowTriangles, verts);
        List<List<int>> islands = FindConnectedComponents(adjacency);

        Debug.Log($"{selected.name}: Found {islands.Count} window island(s).");

        // Parent container is a child of the original object so windows move with the building.
        Transform parent = new GameObject(selected.name + "_Windows").transform;
        parent.SetParent(selected.transform);
        parent.localPosition = Vector3.zero;
        parent.localRotation = Quaternion.identity;
        parent.localScale = Vector3.one;

        Material windowMat = mr.sharedMaterials[windowMatIndex];

        for (int i = 0; i < islands.Count; i++)
        {
            Mesh islandMesh = ExtractMeshFromTriangles(windowTriangles, islands[i], verts, mesh);

            GameObject windowObj = new GameObject($"Window_{i}");
            windowObj.transform.SetParent(parent);
            windowObj.transform.localPosition = Vector3.zero;
            windowObj.transform.localRotation = Quaternion.identity;
            windowObj.transform.localScale = Vector3.one;
            windowObj.AddComponent<MeshFilter>().sharedMesh = islandMesh;
            windowObj.AddComponent<MeshRenderer>().sharedMaterial = windowMat;

            // windowObj.AddComponent<BreakWindow>();
        }

        // Remove window submesh from original so it doesn't render twice.
        Mesh editableMesh = Object.Instantiate(mesh);
        editableMesh.name = mesh.name + "_NoWindows";
        editableMesh.SetTriangles(new int[0], windowMatIndex);
        mf.sharedMesh = editableMesh;

        Material[] mats = mr.sharedMaterials;
        mats[windowMatIndex] = null;
        mr.sharedMaterials = mats;
    }

    static List<HashSet<int>> BuildAdjacency(int[] tris, Vector3[] verts)
    {
        int triCount = tris.Length / 3;
        List<HashSet<int>> adj = new List<HashSet<int>>();
        for (int i = 0; i < triCount; i++) adj.Add(new HashSet<int>());

        Dictionary<int, List<int>> vertexToTriangles = new Dictionary<int, List<int>>();
        for (int t = 0; t < triCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int v = tris[t * 3 + k];
                if (!vertexToTriangles.ContainsKey(v))
                    vertexToTriangles[v] = new List<int>();
                vertexToTriangles[v].Add(t);
            }

        for (int t = 0; t < triCount; t++)
        {
            int v0 = tris[t * 3 + 0], v1 = tris[t * 3 + 1], v2 = tris[t * 3 + 2];

            HashSet<int> candidates = new HashSet<int>();
            foreach (int v in new[] { v0, v1, v2 })
                if (vertexToTriangles.ContainsKey(v))
                    foreach (int nt in vertexToTriangles[v])
                        if (nt != t) candidates.Add(nt);

            foreach (int nt in candidates)
            {
                int n0 = tris[nt * 3 + 0], n1 = tris[nt * 3 + 1], n2 = tris[nt * 3 + 2];
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

    static Mesh ExtractMeshFromTriangles(int[] allWindowTris, List<int> triangleIndices, Vector3[] verts, Mesh sourceMesh)
    {
        Dictionary<int, int> remap = new Dictionary<int, int>();
        List<Vector3> newVerts = new List<Vector3>();
        List<int> newTris = new List<int>();

        foreach (int triIdx in triangleIndices)
            for (int k = 0; k < 3; k++)
            {
                int oldVert = allWindowTris[triIdx * 3 + k];
                if (!remap.ContainsKey(oldVert))
                {
                    remap[oldVert] = newVerts.Count;
                    newVerts.Add(verts[oldVert]);
                }
                newTris.Add(remap[oldVert]);
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