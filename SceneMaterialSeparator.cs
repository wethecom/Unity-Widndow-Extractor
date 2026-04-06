using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class SceneMaterialSeparator : EditorWindow
{
    // ── Scene scan results ──────────────────────────────────────────────────
    private List<string> _matNames = new List<string>();
    private List<Material> _matObjects = new List<Material>();

    // ── Target materials to extract (multi-select) ──────────────────────────
    private HashSet<int> _selectedIndices = new HashSet<int>();

    // ── User config ─────────────────────────────────────────────────────────
    private Material _replacementMat;
    private MonoScript _componentScript;

    // ── State ────────────────────────────────────────────────────────────────
    private bool _scanned = false;
    private Vector2 _matScroll;
    private Vector2 _scroll;
    private string _filter = "";

    [MenuItem("Tools/Scene Material Separator")]
    static void Open() => GetWindow<SceneMaterialSeparator>("Scene Material Separator").minSize = new Vector2(380, 480);

    // ────────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Scene Material Separator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Scan the scene, tick every material you want to extract, assign a single replacement material and an optional component.", MessageType.None);
        EditorGUILayout.Space(6);

        // ── Step 1: Scan ────────────────────────────────────────────────────
        if (GUILayout.Button("1. Scan Scene for Materials"))
            ScanScene();

        if (!_scanned)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Press Scan to begin.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.Space(8);

        // ── Step 2: Pick target materials ───────────────────────────────────
        EditorGUILayout.LabelField("2. Target Materials  (tick all you want extracted)", EditorStyles.boldLabel);

        if (_matNames.Count == 0)
        {
            EditorGUILayout.HelpBox("No materials found in scene.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        // Filter bar + bulk buttons
        EditorGUILayout.BeginHorizontal();
        _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
        if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(36)))
            for (int i = 0; i < _matNames.Count; i++) _selectedIndices.Add(i);
        if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(40)))
            _selectedIndices.Clear();
        EditorGUILayout.EndHorizontal();

        // Scrollable checkbox list
        _matScroll = EditorGUILayout.BeginScrollView(_matScroll, GUILayout.Height(160));
        for (int i = 0; i < _matNames.Count; i++)
        {
            if (!string.IsNullOrEmpty(_filter) &&
                !_matNames[i].ToLower().Contains(_filter.ToLower())) continue;

            bool was = _selectedIndices.Contains(i);
            bool toggled = EditorGUILayout.ToggleLeft(_matNames[i], was);
            if (toggled != was)
            {
                if (toggled) _selectedIndices.Add(i);
                else _selectedIndices.Remove(i);
            }
        }
        EditorGUILayout.EndScrollView();

        int selCount = _selectedIndices.Count;
        EditorGUILayout.LabelField($"{selCount} material(s) selected", EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.Space(8);

        // ── Step 3: Replacement material ────────────────────────────────────
        EditorGUILayout.LabelField("3. Replacement Material (applied to all extracted islands)", EditorStyles.boldLabel);
        _replacementMat = (Material)EditorGUILayout.ObjectField("Replacement Material", _replacementMat, typeof(Material), false);
        EditorGUILayout.HelpBox("Leave blank to keep each island's original material.", MessageType.None);

        EditorGUILayout.Space(8);

        // ── Step 4: Component to add ─────────────────────────────────────────
        EditorGUILayout.LabelField("4. Component to Add to Each Island", EditorStyles.boldLabel);
        _componentScript = (MonoScript)EditorGUILayout.ObjectField("Component Script", _componentScript, typeof(MonoScript), false);

        if (_componentScript != null)
        {
            System.Type t = _componentScript.GetClass();
            if (t == null || !t.IsSubclassOf(typeof(MonoBehaviour)))
            {
                EditorGUILayout.HelpBox("That script is not a MonoBehaviour — it can't be added as a component.", MessageType.Warning);
                _componentScript = null;
            }
            else
            {
                EditorGUILayout.HelpBox($"Will add:  {t.Name}", MessageType.Info);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Drag a script here from the Project window, or click the circle to browse.", MessageType.None);
        }

        EditorGUILayout.Space(12);

        // ── Step 5: Run ──────────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = selCount > 0 ? new Color(0.4f, 0.85f, 0.4f) : Color.grey;
        if (GUILayout.Button($"5. Extract {selCount} Material(s)", GUILayout.Height(32)) && selCount > 0)
        {
            string matList = string.Join("\n  • ", _selectedIndices.Select(i => _matNames[i]));
            bool confirmed = EditorUtility.DisplayDialog(
                "Confirm Extraction",
                $"Extract the following material(s) from every object in the scene?\n\n  • {matList}\n\nThis will modify meshes. Make sure you have a backup.",
                "Extract",
                "Cancel"
            );
            if (confirmed)
                RunExtraction();
        }
        GUI.backgroundColor = Color.white;

        GUI.backgroundColor = new Color(0.85f, 0.4f, 0.4f);
        if (GUILayout.Button("Cancel", GUILayout.Height(32)))
            Close();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    // ────────────────────────────────────────────────────────────────────────
    void ScanScene()
    {
        _matNames.Clear();
        _matObjects.Clear();
        _selectedIndices.Clear();

        HashSet<string> seen = new HashSet<string>();
        foreach (MeshRenderer mr in FindObjectsOfType<MeshRenderer>())
            foreach (Material m in mr.sharedMaterials)
                if (m != null && seen.Add(m.name))
                {
                    _matNames.Add(m.name);
                    _matObjects.Add(m);
                }

        // Sort alphabetically so the list is easy to read
        var sorted = _matNames
            .Select((name, i) => (name, mat: _matObjects[i]))
            .OrderBy(x => x.name)
            .ToList();

        _matNames = sorted.Select(x => x.name).ToList();
        _matObjects = sorted.Select(x => x.mat).ToList();

        _scanned = true;
        Repaint();
        Debug.Log($"Scene Material Separator: found {_matNames.Count} material(s).");
    }

    // ────────────────────────────────────────────────────────────────────────
    void RunExtraction()
    {
        if (_selectedIndices.Count == 0) { Debug.LogError("No materials selected."); return; }

        System.Type compType = _componentScript != null ? _componentScript.GetClass() : null;

        // Build lookup: material name → its Material object
        Dictionary<string, Material> targetMats = new Dictionary<string, Material>();
        foreach (int i in _selectedIndices)
            targetMats[_matNames[i]] = _matObjects[i];

        int totalIslands = 0;
        int totalObjects = 0;

        foreach (MeshRenderer mr in FindObjectsOfType<MeshRenderer>())
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            // Collect every submesh index that matches any selected material.
            // Process highest index first so stripping one submesh doesn't shift others.
            List<int> matchingSubMeshes = new List<int>();
            for (int s = 0; s < mr.sharedMaterials.Length; s++)
            {
                Material m = mr.sharedMaterials[s];
                if (m != null && targetMats.ContainsKey(m.name))
                    matchingSubMeshes.Add(s);
            }

            if (matchingSubMeshes.Count == 0) continue;

            matchingSubMeshes.Sort((a, b) => b.CompareTo(a)); // descending

            foreach (int subIdx in matchingSubMeshes)
            {
                Material originalMat = mr.sharedMaterials[subIdx];
                Material useMat = _replacementMat != null ? _replacementMat : originalMat;

                int islands = ExtractFromObject(mf, mr, subIdx, useMat, compType);
                totalIslands += islands;
            }

            totalObjects++;
        }

        Debug.Log($"Scene Material Separator: processed {totalObjects} object(s), created {totalIslands} island(s).");
        Close();
    }

    // ────────────────────────────────────────────────────────────────────────
    static int ExtractFromObject(MeshFilter mf, MeshRenderer mr, int subMatIndex, Material islandMat, System.Type compType)
    {
        Mesh mesh = mf.sharedMesh;
        int[] subTris = mesh.GetTriangles(subMatIndex);
        if (subTris.Length == 0)
        {
            Debug.LogWarning($"{mr.gameObject.name}: submesh {subMatIndex} has no triangles.");
            return 0;
        }

        Vector3[] verts = mesh.vertices;

        List<HashSet<int>> adj = BuildAdjacency(subTris, verts);
        List<List<int>> islands = FindIslands(adj);

        Transform container = new GameObject(mr.gameObject.name + "_Extracted").transform;
        container.SetParent(mr.transform);
        container.localPosition = Vector3.zero;
        container.localRotation = Quaternion.identity;
        container.localScale = Vector3.one;

        for (int i = 0; i < islands.Count; i++)
        {
            Mesh islandMesh = BuildIslandMesh(subTris, islands[i], verts, mesh);

            GameObject go = new GameObject($"Island_{i}");
            go.transform.SetParent(container);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = islandMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = islandMat;

            if (compType != null)
                go.AddComponent(compType);
        }

        // Hollow out the submesh on the original so nothing renders twice
        Mesh stripped = Object.Instantiate(mesh);
        stripped.name = mesh.name + "_Stripped";
        stripped.SetTriangles(new int[0], subMatIndex);
        mf.sharedMesh = stripped;

        Material[] mats = mr.sharedMaterials;
        mats[subMatIndex] = null;
        mr.sharedMaterials = mats;

        return islands.Count;
    }

    // ── Mesh helpers ─────────────────────────────────────────────────────────

    static List<HashSet<int>> BuildAdjacency(int[] tris, Vector3[] verts)
    {
        int triCount = tris.Length / 3;
        List<HashSet<int>> adj = new List<HashSet<int>>(triCount);
        for (int i = 0; i < triCount; i++) adj.Add(new HashSet<int>());

        Dictionary<int, List<int>> vertToTris = new Dictionary<int, List<int>>();
        for (int t = 0; t < triCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int v = tris[t * 3 + k];
                if (!vertToTris.ContainsKey(v)) vertToTris[v] = new List<int>();
                vertToTris[v].Add(t);
            }

        for (int t = 0; t < triCount; t++)
        {
            int v0 = tris[t * 3], v1 = tris[t * 3 + 1], v2 = tris[t * 3 + 2];
            HashSet<int> candidates = new HashSet<int>();
            foreach (int v in new[] { v0, v1, v2 })
                if (vertToTris.ContainsKey(v))
                    foreach (int nt in vertToTris[v])
                        if (nt != t) candidates.Add(nt);

            foreach (int nt in candidates)
            {
                int n0 = tris[nt * 3], n1 = tris[nt * 3 + 1], n2 = tris[nt * 3 + 2];
                int shared = 0;
                if (n0 == v0 || n0 == v1 || n0 == v2) shared++;
                if (n1 == v0 || n1 == v1 || n1 == v2) shared++;
                if (n2 == v0 || n2 == v1 || n2 == v2) shared++;
                if (shared >= 2) adj[t].Add(nt);
            }
        }
        return adj;
    }

    static List<List<int>> FindIslands(List<HashSet<int>> adj)
    {
        bool[] visited = new bool[adj.Count];
        List<List<int>> result = new List<List<int>>();
        for (int i = 0; i < adj.Count; i++)
        {
            if (visited[i]) continue;
            List<int> island = new List<int>();
            Queue<int> q = new Queue<int>();
            q.Enqueue(i); visited[i] = true;
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                island.Add(cur);
                foreach (int nb in adj[cur])
                    if (!visited[nb]) { visited[nb] = true; q.Enqueue(nb); }
            }
            result.Add(island);
        }
        return result;
    }

    static Mesh BuildIslandMesh(int[] allTris, List<int> triIndices, Vector3[] srcVerts, Mesh src)
    {
        Dictionary<int, int> remap = new Dictionary<int, int>();
        List<Vector3> newVerts = new List<Vector3>();
        List<int> newTris = new List<int>();

        foreach (int ti in triIndices)
            for (int k = 0; k < 3; k++)
            {
                int oldV = allTris[ti * 3 + k];
                if (!remap.ContainsKey(oldV)) { remap[oldV] = newVerts.Count; newVerts.Add(srcVerts[oldV]); }
                newTris.Add(remap[oldV]);
            }

        Mesh m = new Mesh();
        m.vertices = newVerts.ToArray();
        m.triangles = newTris.ToArray();
        m.RecalculateNormals();
        m.RecalculateBounds();

        if (src.uv.Length > 0)
        {
            Vector2[] uvs = new Vector2[newVerts.Count];
            foreach (var kv in remap) uvs[kv.Value] = src.uv[kv.Key];
            m.uv = uvs;
        }
        return m;
    }
}