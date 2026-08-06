using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shootable crystal that feeds a beam into <see cref="DragonBoss"/>.
/// Visual: solid purple dodecahedron spinning inside an opaque edge-cage
/// (Minecraft-inspired, CAVE-safe — no transparent glass).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class EnderCrystal : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DragonBoss dragon;
    [Tooltip("Where the beam starts. Defaults to this transform.")]
    [SerializeField] private Transform beamOrigin;
    [Tooltip("Built / assigned visual root. Auto-built if empty.")]
    [SerializeField] private GameObject crystalVisual;

    [Header("Look")]
    [SerializeField] private bool autoBuildVisual = true;
    [SerializeField] private Color innerColor = new Color(0.85f, 0.25f, 1f, 1f);
    [SerializeField] private Color innerCoreColor = new Color(1f, 0.55f, 0.95f, 1f);
    [SerializeField] private Color cageColor = new Color(1f, 0.75f, 1f, 1f);
    [SerializeField] private Color cageAccentColor = new Color(0.7f, 0.15f, 0.95f, 1f);
    [SerializeField, Min(0.2f)] private float visualScale = 0.55f;
    [SerializeField, Min(1f)] private float cageScale = 1.35f;
    [SerializeField, Min(0.01f)] private float cageBarThickness = 0.045f;
    [SerializeField] private float innerSpinSpeed = 55f;
    [SerializeField] private float outerSpinSpeed = -35f;
    [SerializeField] private Vector3 innerSpinAxis = new Vector3(0.35f, 1f, 0.15f);
    [SerializeField] private Vector3 outerSpinAxis = new Vector3(-0.2f, 1f, 0.4f);
    [SerializeField] private float bobAmplitude = 0.12f;
    [SerializeField] private float bobSpeed = 1.6f;

    [Header("Beam")]
    [SerializeField] private bool createBeamIfMissing = true;
    [SerializeField] private Color beamColor = new Color(1f, 0.35f, 1f, 0.95f);
    [SerializeField] private float beamWidth = 0.08f;
    [SerializeField] private float beamScrollSpeed = 1.5f;

    private LineRenderer beam;
    private bool destroyed;
    private Material beamMaterial;
    private Transform visualRoot;
    private Transform innerSpin;
    private Transform outerSpin;
    private Vector3 visualBaseLocalPos;
    private float bobPhase;
    private readonly List<Material> ownedMaterials = new List<Material>(8);

    public bool IsAlive => !destroyed;

    public void Bind(DragonBoss owner)
    {
        dragon = owner;
        if (dragon != null)
        {
            beamColor = dragon.CrystalEnergyColor;
            beamColor.a = Mathf.Max(0.75f, beamColor.a);
            Color energy = dragon.CrystalEnergyColor;
            energy.a = 1f;
            innerColor = Color.Lerp(energy, Color.white, 0.15f);
            innerCoreColor = Color.Lerp(energy, Color.white, 0.55f);
            cageAccentColor = energy;
            cageColor = Color.Lerp(energy, Color.white, 0.65f);
        }

        EnsureBeam();
        ApplyBeamColor(beamColor);
    }

    private void Awake()
    {
        if (beamOrigin == null)
        {
            beamOrigin = transform;
        }

        if (dragon == null)
        {
            dragon = FindObjectOfType<DragonBoss>();
        }

        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        EnsureBeam();
    }

    private void Start()
    {
        if (autoBuildVisual)
        {
            EnsureCrystalVisual();
        }

        if (dragon != null)
        {
            dragon.RegisterCrystal(this);
        }
        else
        {
            Debug.LogWarning("EnderCrystal: no DragonBoss found — crystal will still be shootable.", this);
        }
    }

    private void Update()
    {
        if (destroyed || visualRoot == null)
        {
            return;
        }

        float bob = Mathf.Sin((Time.time * bobSpeed) + bobPhase) * bobAmplitude;
        visualRoot.localPosition = visualBaseLocalPos + Vector3.up * bob;

        if (innerSpin != null)
        {
            innerSpin.Rotate(innerSpinAxis.normalized, innerSpinSpeed * Time.deltaTime, Space.Self);
        }

        if (outerSpin != null)
        {
            outerSpin.Rotate(outerSpinAxis.normalized, outerSpinSpeed * Time.deltaTime, Space.Self);
        }
    }

    private void LateUpdate()
    {
        if (destroyed || beam == null)
        {
            return;
        }

        bool showBeam = dragon == null || dragon.ShouldShowCrystalShieldVisual;
        if (beam.enabled != showBeam)
        {
            beam.enabled = showBeam;
        }

        if (!showBeam)
        {
            return;
        }

        Vector3 start = beamOrigin != null ? beamOrigin.position : transform.position;
        Vector3 end = dragon != null ? dragon.ShieldAttachPoint : start + Vector3.up * 2f;

        beam.positionCount = 2;
        beam.SetPosition(0, start);
        beam.SetPosition(1, end);

        if (beamMaterial != null && beamMaterial.HasProperty("_MainTex"))
        {
            Vector2 offset = beamMaterial.mainTextureOffset;
            offset.x -= beamScrollSpeed * Time.deltaTime;
            beamMaterial.mainTextureOffset = offset;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryHit(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    private void TryHit(Collider other)
    {
        if (destroyed || other == null)
        {
            return;
        }

        if (dragon != null && !dragon.IsFightActive)
        {
            return;
        }

        ArrowProjectile arrow = other.GetComponentInParent<ArrowProjectile>();
        if (arrow == null || (!arrow.IsInFlight && !arrow.HasStuck))
        {
            return;
        }

        destroyed = true;
        Destroy(arrow.gameObject);
        FightAudio.PlayCrystalExplode(transform.position);
        OpaqueBurstVfx.SpawnCrystal(transform.position, innerColor);

        if (beam != null)
        {
            beam.enabled = false;
        }

        if (dragon != null)
        {
            dragon.NotifyCrystalDestroyed(this);
        }

        SetCrystalActiveVisual(false);
    }

    public void Revive()
    {
        destroyed = false;
        SetCrystalActiveVisual(true);

        if (beam != null)
        {
            beam.enabled = true;
        }

        EnsureBeam();
        ApplyBeamColor(beamColor);

        if (dragon != null)
        {
            dragon.RegisterCrystal(this);
        }
    }

    private void SetCrystalActiveVisual(bool active)
    {
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
            {
                // Keep the root hit collider toggled with active; child visuals have no colliders.
                cols[i].enabled = active;
            }
        }

        if (crystalVisual != null)
        {
            crystalVisual.SetActive(active);
        }

        if (visualRoot != null)
        {
            visualRoot.gameObject.SetActive(active);
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            if (beam != null && renderers[i] == beam)
            {
                renderers[i].enabled = active;
                continue;
            }

            renderers[i].enabled = active;
        }
    }

    private void OnDestroy()
    {
        if (beamMaterial != null)
        {
            Destroy(beamMaterial);
            beamMaterial = null;
        }

        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }

        ownedMaterials.Clear();

        if (!destroyed && dragon != null)
        {
            dragon.NotifyCrystalDestroyed(this);
        }
    }

    private void EnsureCrystalVisual()
    {
        Transform existing = transform.Find("CrystalCageVisual");
        if (existing != null)
        {
            visualRoot = existing;
            crystalVisual = existing.gameObject;
            innerSpin = existing.Find("InnerSpin");
            outerSpin = existing.Find("OuterSpin");
            visualBaseLocalPos = visualRoot.localPosition;
            HideLegacyPlaceholders();
            return;
        }

        HideLegacyPlaceholders();
        BuildCageVisual();
    }

    private void HideLegacyPlaceholders()
    {
        // Old sphere / "lipstick" meshes.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            string n = child.name;
            if (n == "CrystalCageVisual" || n == "Beam" || child.GetComponent<LineRenderer>() != null)
            {
                continue;
            }

            if (n == "CrystalVisual" || n.IndexOf("lipstick", System.StringComparison.OrdinalIgnoreCase) >= 0
                || child.GetComponent<MeshFilter>() != null || child.GetComponent<MeshRenderer>() != null)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private void BuildCageVisual()
    {
        ClearOwnedMaterials();

        GameObject root = new GameObject("CrystalCageVisual");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.up * 0.05f;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * visualScale;
        visualRoot = root.transform;
        visualBaseLocalPos = visualRoot.localPosition;
        crystalVisual = root;

        Mesh dodeca = CrystalGeometry.CreateDodecahedronMesh();

        // --- Inner solid crystal ---
        GameObject inner = new GameObject("InnerSpin");
        inner.transform.SetParent(visualRoot, false);
        innerSpin = inner.transform;

        GameObject innerMesh = new GameObject("InnerCrystal");
        innerMesh.transform.SetParent(innerSpin, false);
        innerMesh.transform.localScale = Vector3.one * 0.92f;
        MeshFilter innerFilter = innerMesh.AddComponent<MeshFilter>();
        innerFilter.sharedMesh = dodeca;
        MeshRenderer innerRenderer = innerMesh.AddComponent<MeshRenderer>();
        Material innerMat = CreateOpaqueUnlit(innerColor, "CrystalInner");
        TrackMaterial(innerMat);
        innerRenderer.sharedMaterial = innerMat;
        innerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        innerRenderer.receiveShadows = false;

        // Bright core nugget.
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "InnerCore";
        core.transform.SetParent(innerSpin, false);
        core.transform.localScale = Vector3.one * 0.45f;
        DestroyCollider(core);
        Material coreMat = CreateOpaqueUnlit(innerCoreColor, "CrystalCore");
        TrackMaterial(coreMat);
        Renderer coreRenderer = core.GetComponent<Renderer>();
        coreRenderer.sharedMaterial = coreMat;
        coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        coreRenderer.receiveShadows = false;

        // Opaque outline on inner crystal (CAVE-safe rim).
        GameObject outlineGo = new GameObject("InnerOutline");
        outlineGo.transform.SetParent(innerMesh.transform, false);
        MeshFilter outlineFilter = outlineGo.AddComponent<MeshFilter>();
        outlineFilter.sharedMesh = dodeca;
        MeshRenderer outlineRenderer = outlineGo.AddComponent<MeshRenderer>();
        Material outlineMat = CreateOutlineMaterial(cageAccentColor, 0.04f);
        TrackMaterial(outlineMat);
        outlineRenderer.sharedMaterial = outlineMat;
        outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outlineRenderer.receiveShadows = false;

        // --- Outer cage (opaque edge bars, counter-spin) ---
        GameObject outer = new GameObject("OuterSpin");
        outer.transform.SetParent(visualRoot, false);
        outer.transform.localScale = Vector3.one * cageScale;
        outerSpin = outer.transform;

        Material cageMat = CreateOpaqueUnlit(cageColor, "CrystalCage");
        Material cageAccentMat = CreateOpaqueUnlit(cageAccentColor, "CrystalCageAccent");
        TrackMaterial(cageMat);
        TrackMaterial(cageAccentMat);

        Vector3[] verts = CrystalGeometry.DodecahedronVertices;
        int[,] edges = CrystalGeometry.DodecahedronEdges;
        for (int i = 0; i < edges.GetLength(0); i++)
        {
            Vector3 a = verts[edges[i, 0]];
            Vector3 b = verts[edges[i, 1]];
            bool accent = (i % 3) == 0;
            CreateCageBar(outerSpin, a, b, accent ? cageAccentMat : cageMat, i);
        }

        // Corner studs for extra “crystal tech” read.
        for (int i = 0; i < verts.Length; i++)
        {
            GameObject stud = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stud.name = "CageStud_" + i;
            stud.transform.SetParent(outerSpin, false);
            stud.transform.localPosition = verts[i];
            stud.transform.localScale = Vector3.one * (cageBarThickness * 1.8f);
            DestroyCollider(stud);
            Renderer studRenderer = stud.GetComponent<Renderer>();
            studRenderer.sharedMaterial = (i % 2 == 0) ? cageAccentMat : cageMat;
            studRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            studRenderer.receiveShadows = false;
        }
    }

    private static void DestroyCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(col);
        }
        else
        {
            DestroyImmediate(col);
        }
    }

    private void CreateCageBar(Transform parent, Vector3 a, Vector3 b, Material mat, int index)
    {
        Vector3 mid = (a + b) * 0.5f;
        Vector3 delta = b - a;
        float length = delta.magnitude;
        if (length < 1e-4f)
        {
            return;
        }

        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "CageBar_" + index;
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = mid;
        bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        bar.transform.localScale = new Vector3(cageBarThickness, length * 0.5f, cageBarThickness);
        DestroyCollider(bar);

        Renderer renderer = bar.GetComponent<Renderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void EnsureBeam()
    {
        beam = GetComponent<LineRenderer>();
        if (beam == null && createBeamIfMissing)
        {
            beam = gameObject.AddComponent<LineRenderer>();
        }

        if (beam == null)
        {
            return;
        }

        beam.positionCount = 2;
        beam.startWidth = beamWidth;
        beam.endWidth = beamWidth * 0.55f;
        beam.numCapVertices = 4;
        beam.numCornerVertices = 2;
        beam.useWorldSpace = true;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.receiveShadows = false;
        beam.alignment = LineAlignment.View;

        if (beam.sharedMaterial == null)
        {
            beamMaterial = CreateBeamMaterial(beamColor);
            beam.sharedMaterial = beamMaterial;
        }
        else
        {
            beamMaterial = beam.material;
        }

        ApplyBeamColor(beamColor);
        beam.enabled = true;
    }

    private void ApplyBeamColor(Color color)
    {
        if (beam == null)
        {
            return;
        }

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(Mathf.Max(0.75f, color.a), 0f),
                new GradientAlphaKey(Mathf.Max(0.5f, color.a * 0.85f), 1f)
            });
        beam.colorGradient = gradient;

        if (beamMaterial != null)
        {
            if (beamMaterial.HasProperty("_Color"))
            {
                beamMaterial.color = color;
            }

            if (beamMaterial.HasProperty("_BaseColor"))
            {
                beamMaterial.SetColor("_BaseColor", color);
            }
        }
    }

    private void TrackMaterial(Material mat)
    {
        if (mat != null && !ownedMaterials.Contains(mat))
        {
            ownedMaterials.Add(mat);
        }
    }

    private void ClearOwnedMaterials()
    {
        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }

        ownedMaterials.Clear();
    }

    private static Material CreateOpaqueUnlit(Color color, string name)
    {
        color.a = 1f;
        string[] shaders = { "Unlit/Color", "Universal Render Pipeline/Unlit" };
        for (int i = 0; i < shaders.Length; i++)
        {
            Shader shader = Shader.Find(shaders[i]);
            if (shader == null)
            {
                continue;
            }

            Material mat = new Material(shader);
            mat.name = name;
            if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            return mat;
        }

        return new Material(Shader.Find("Hidden/InternalErrorShader"));
    }

    private static Material CreateOutlineMaterial(Color color, float outlineWidth)
    {
        color.a = 1f;
        Material fromResources = Resources.Load<Material>("DragonShieldOutline");
        if (fromResources != null)
        {
            Material mat = new Material(fromResources);
            mat.name = "CrystalOutline";
            if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }

            if (mat.HasProperty("_OutlineWidth"))
            {
                mat.SetFloat("_OutlineWidth", outlineWidth);
            }

            return mat;
        }

        Shader outlineShader = Shader.Find("VotanicBow/CrystalShieldGlow");
        if (outlineShader != null)
        {
            Material mat = new Material(outlineShader);
            mat.name = "CrystalOutline";
            if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }

            if (mat.HasProperty("_OutlineWidth"))
            {
                mat.SetFloat("_OutlineWidth", outlineWidth);
            }

            return mat;
        }

        return CreateOpaqueUnlit(color, "CrystalOutlineFallback");
    }

    private static Material CreateBeamMaterial(Color color)
    {
        string[] shaderNames =
        {
            "Unlit/Color",
            "Sprites/Default",
            "Universal Render Pipeline/Unlit"
        };

        for (int i = 0; i < shaderNames.Length; i++)
        {
            Shader shader = Shader.Find(shaderNames[i]);
            if (shader == null)
            {
                continue;
            }

            Material mat = new Material(shader);
            mat.name = "CrystalBeam";
            if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            return mat;
        }

        return new Material(Shader.Find("Hidden/InternalErrorShader"));
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild Crystal Visual")]
    private void RebuildCrystalVisualMenu()
    {
        Transform existing = transform.Find("CrystalCageVisual");
        if (existing != null)
        {
            DestroyImmediate(existing.gameObject);
        }

        visualRoot = null;
        crystalVisual = null;
        BuildCageVisual();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("EnderCrystal: rebuilt cage visual.", this);
    }
#endif
}

/// <summary>Procedural regular dodecahedron mesh + edge list.</summary>
public static class CrystalGeometry
{
    public static readonly Vector3[] DodecahedronVertices;
    public static readonly int[,] DodecahedronEdges;
    private static Mesh cachedMesh;

    static CrystalGeometry()
    {
        float phi = (1f + Mathf.Sqrt(5f)) * 0.5f;
        float inv = 1f / phi;

        // Unit-ish dodecahedron (will be scaled by the crystal).
        List<Vector3> verts = new List<Vector3>(20);
        // Cube corners
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            verts.Add(new Vector3(x, y, z));
        }

        // Rectangles
        for (int i = -1; i <= 1; i += 2)
        for (int j = -1; j <= 1; j += 2)
        {
            verts.Add(new Vector3(0f, i * inv, j * phi));
            verts.Add(new Vector3(i * inv, j * phi, 0f));
            verts.Add(new Vector3(i * phi, 0f, j * inv));
        }

        // Normalize to fit roughly in a unit sphere.
        float maxMag = 0f;
        for (int i = 0; i < verts.Count; i++)
        {
            maxMag = Mathf.Max(maxMag, verts[i].magnitude);
        }

        for (int i = 0; i < verts.Count; i++)
        {
            verts[i] /= maxMag;
        }

        DodecahedronVertices = verts.ToArray();
        DodecahedronEdges = BuildEdges(DodecahedronVertices);
    }

    public static Mesh CreateDodecahedronMesh()
    {
        if (cachedMesh != null)
        {
            return cachedMesh;
        }

        cachedMesh = BuildConvexMeshFromVertices(DodecahedronVertices);
        cachedMesh.name = "Dodecahedron";
        return cachedMesh;
    }

    private static Mesh BuildConvexMeshFromVertices(Vector3[] verts)
    {
        float edgeLen = EstimateEdgeLength(verts);
        List<int[]> pentagons = FindPentagonFaces(verts, edgeLen);
        List<Vector3> meshVerts = new List<Vector3>();
        List<int> meshTris = new List<int>();
        List<Vector3> normals = new List<Vector3>();

        for (int f = 0; f < pentagons.Count; f++)
        {
            int[] face = pentagons[f];
            Vector3 center = Vector3.zero;
            for (int i = 0; i < face.Length; i++)
            {
                center += verts[face[i]];
            }

            center /= face.Length;
            Vector3 n = Vector3.Cross(
                verts[face[1]] - verts[face[0]],
                verts[face[2]] - verts[face[0]]).normalized;
            if (Vector3.Dot(n, center) < 0f)
            {
                n = -n;
                System.Array.Reverse(face);
            }

            int centerIndex = meshVerts.Count;
            meshVerts.Add(center);
            normals.Add(n);
            int[] ring = new int[face.Length];
            for (int i = 0; i < face.Length; i++)
            {
                ring[i] = meshVerts.Count;
                meshVerts.Add(verts[face[i]]);
                normals.Add(n);
            }

            for (int i = 0; i < face.Length; i++)
            {
                int next = (i + 1) % face.Length;
                meshTris.Add(centerIndex);
                meshTris.Add(ring[i]);
                meshTris.Add(ring[next]);
            }
        }

        Mesh mesh = new Mesh();
        mesh.SetVertices(meshVerts);
        mesh.SetTriangles(meshTris, 0);
        mesh.SetNormals(normals);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float EstimateEdgeLength(Vector3[] verts)
    {
        float min = float.MaxValue;
        for (int i = 0; i < verts.Length; i++)
        {
            for (int j = i + 1; j < verts.Length; j++)
            {
                float d = Vector3.Distance(verts[i], verts[j]);
                if (d > 0.01f)
                {
                    min = Mathf.Min(min, d);
                }
            }
        }

        return min;
    }

    private static List<int[]> FindPentagonFaces(Vector3[] verts, float edgeLen)
    {
        float tol = edgeLen * 0.2f;
        List<int>[] adj = new List<int>[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            adj[i] = new List<int>(3);
        }

        for (int i = 0; i < verts.Length; i++)
        {
            for (int j = i + 1; j < verts.Length; j++)
            {
                float d = Vector3.Distance(verts[i], verts[j]);
                if (Mathf.Abs(d - edgeLen) <= tol)
                {
                    adj[i].Add(j);
                    adj[j].Add(i);
                }
            }
        }

        HashSet<string> seen = new HashSet<string>();
        List<int[]> faces = new List<int[]>();

        for (int start = 0; start < verts.Length; start++)
        {
            for (int a = 0; a < adj[start].Count; a++)
            {
                int second = adj[start][a];
                TryWalkPentagon(start, second, adj, verts, seen, faces);
            }
        }

        return faces;
    }

    private static void TryWalkPentagon(
        int start,
        int second,
        List<int>[] adj,
        Vector3[] verts,
        HashSet<string> seen,
        List<int[]> faces)
    {
        // DFS cycles of length 5.
        int[] path = new int[5];
        path[0] = start;
        path[1] = second;
        Walk(2, path, adj, verts, seen, faces, start, second);
    }

    private static void Walk(
        int depth,
        int[] path,
        List<int>[] adj,
        Vector3[] verts,
        HashSet<string> seen,
        List<int[]> faces,
        int start,
        int prev)
    {
        if (depth == 5)
        {
            if (!adj[path[4]].Contains(start))
            {
                return;
            }

            int[] face = (int[])path.Clone();
            // Canonical key.
            int min = face[0];
            int minIdx = 0;
            for (int i = 1; i < 5; i++)
            {
                if (face[i] < min)
                {
                    min = face[i];
                    minIdx = i;
                }
            }

            int[] rot = new int[5];
            for (int i = 0; i < 5; i++)
            {
                rot[i] = face[(minIdx + i) % 5];
            }

            string key = string.Join(",", rot);
            int[] rotRev = new int[5];
            rotRev[0] = rot[0];
            for (int i = 1; i < 5; i++)
            {
                rotRev[i] = rot[5 - i];
            }

            string keyRev = string.Join(",", rotRev);
            if (seen.Contains(key) || seen.Contains(keyRev))
            {
                return;
            }

            // Flatness check — all points near plane.
            Vector3 n = Vector3.Cross(
                verts[rot[1]] - verts[rot[0]],
                verts[rot[2]] - verts[rot[0]]);
            if (n.sqrMagnitude < 1e-8f)
            {
                return;
            }

            n.Normalize();
            float plane = Vector3.Dot(n, verts[rot[0]]);
            for (int i = 1; i < 5; i++)
            {
                if (Mathf.Abs(Vector3.Dot(n, verts[rot[i]]) - plane) > 0.08f)
                {
                    return;
                }
            }

            seen.Add(key);
            faces.Add(rot);
            return;
        }

        int current = path[depth - 1];
        for (int i = 0; i < adj[current].Count; i++)
        {
            int next = adj[current][i];
            if (next == prev)
            {
                continue;
            }

            bool used = false;
            for (int p = 0; p < depth; p++)
            {
                if (path[p] == next)
                {
                    used = true;
                    break;
                }
            }

            if (used)
            {
                continue;
            }

            path[depth] = next;
            Walk(depth + 1, path, adj, verts, seen, faces, start, current);
        }
    }

    private static int[,] BuildEdges(Vector3[] verts)
    {
        float edgeLen = EstimateEdgeLength(verts);
        float tol = edgeLen * 0.2f;
        List<Vector2Int> edges = new List<Vector2Int>(30);
        for (int i = 0; i < verts.Length; i++)
        {
            for (int j = i + 1; j < verts.Length; j++)
            {
                float d = Vector3.Distance(verts[i], verts[j]);
                if (Mathf.Abs(d - edgeLen) <= tol)
                {
                    edges.Add(new Vector2Int(i, j));
                }
            }
        }

        int[,] result = new int[edges.Count, 2];
        for (int i = 0; i < edges.Count; i++)
        {
            result[i, 0] = edges[i].x;
            result[i, 1] = edges[i].y;
        }

        return result;
    }
}
