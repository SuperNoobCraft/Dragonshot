using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Timed target practice: infinite quiver, random targets in a box, world-space start/timer/score UI.
/// </summary>
public class TargetPracticeGame : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Playing,
        Results
    }

    [Header("References")]
    [SerializeField] private ArrowQuiver quiver;
    [SerializeField] private TargetPracticeUI ui;
    [Tooltip("World-space box that defines where targets may spawn (uses collider bounds or transform scale).")]
    [SerializeField] private Transform spawnArea;
    [Tooltip("Optional prefab with ArcheryTarget + Collider. If empty, a red sphere is created at runtime.")]
    [SerializeField] private ArcheryTarget targetPrefab;

    [Header("Round")]
    [SerializeField] private float roundSeconds = 20f;
    [SerializeField, Min(1)] private int maxTargetsAtOnce = 3;
    [SerializeField] private Vector2 targetScaleRange = new Vector2(0.35f, 0.55f);
    [SerializeField] private bool enableInfiniteArrowsDuringGame = true;
    [SerializeField] private bool clearFlyingArrowsOnRoundEnd = true;

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.25f);

    private Phase phase = Phase.Idle;
    private float timeRemaining;
    private int score;
    private readonly List<ArcheryTarget> liveTargets = new List<ArcheryTarget>(8);

    public int Score => score;
    public float TimeRemaining => timeRemaining;
    public bool IsPlaying => phase == Phase.Playing;

    private void Awake()
    {
        if (quiver == null)
        {
            quiver = FindObjectOfType<ArrowQuiver>();
        }

        if (ui == null)
        {
            ui = GetComponentInChildren<TargetPracticeUI>(true);
        }

        if (ui == null)
        {
            ui = FindObjectOfType<TargetPracticeUI>();
        }

        if (spawnArea == null)
        {
            Transform found = transform.Find("SpawnArea");
            if (found != null)
            {
                spawnArea = found;
            }
        }
    }

    private void Start()
    {
        if (ui == null)
        {
            ui = GetComponentInChildren<TargetPracticeUI>(true);
        }

        if (spawnArea == null)
        {
            Transform found = transform.Find("SpawnArea");
            if (found != null)
            {
                spawnArea = found;
            }
        }

        if (ui == null || spawnArea == null)
        {
            Debug.LogError(
                "TargetPracticeGame: assign UI + SpawnArea in the Inspector, or right-click this component → "
                + "'Create Scene UI + Spawn Area', then move those children where you want.",
                this);
        }

        if (quiver != null && enableInfiniteArrowsDuringGame)
        {
            quiver.InfiniteArrows = true;
        }

        if (ui != null)
        {
            ui.Bind(this);
        }

        phase = Phase.Idle;
    }

    /// <summary>
    /// Editor helper: creates editable child objects you can move in the Scene view.
    /// Right-click the component header → Create Scene UI + Spawn Area.
    /// </summary>
    [ContextMenu("Create Scene UI + Spawn Area")]
    public void CreateSceneUiAndSpawnArea()
    {
        if (spawnArea == null)
        {
            Transform existing = transform.Find("SpawnArea");
            if (existing != null)
            {
                spawnArea = existing;
            }
            else
            {
                GameObject area = new GameObject("SpawnArea");
                area.transform.SetParent(transform, false);
                area.transform.localPosition = new Vector3(0f, 1.5f, 5f);
                area.transform.localRotation = Quaternion.identity;
                area.transform.localScale = new Vector3(4f, 2.5f, 2f);
                BoxCollider box = area.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one;
                spawnArea = area.transform;
            }
        }

        if (ui == null)
        {
            ui = GetComponentInChildren<TargetPracticeUI>(true);
        }

        if (ui == null)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "TargetPracticeUI";
            panel.transform.SetParent(transform, false);
            // Place in front of a typical standing player; move freely afterward.
            panel.transform.localPosition = new Vector3(0f, 1.6f, 2.5f);
            panel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            panel.transform.localScale = new Vector3(1.2f, 0.7f, 1f);

            Collider col = panel.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = false;
            }

            Renderer renderer = panel.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                Material mat = new Material(renderer.sharedMaterial);
                Color panelColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);
                if (mat.HasProperty("_Color"))
                {
                    mat.color = panelColor;
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", panelColor);
                }

                renderer.sharedMaterial = mat;
            }

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(panel.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = Vector3.one;

            TextMesh textMesh = textGo.AddComponent<TextMesh>();
            textMesh.text = "Click to Start";
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.08f;
            textMesh.fontSize = 64;
            textMesh.color = Color.white;

            TargetPracticeUI panelUi = panel.AddComponent<TargetPracticeUI>();
            panelUi.SetLabel(textMesh);
            ui = panelUi;
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (ui != null)
        {
            UnityEditor.EditorUtility.SetDirty(ui.gameObject);
        }

        if (spawnArea != null)
        {
            UnityEditor.EditorUtility.SetDirty(spawnArea.gameObject);
        }
#endif

        Debug.Log(
            "TargetPracticeGame: created/assigned SpawnArea + TargetPracticeUI as children. "
            + "Move them in the Scene view, then press Play.",
            this);
    }

    private void EndRound()
    {
        phase = Phase.Results;
        timeRemaining = 0f;
        ClearTargets();

        // Keep infinite arrows for the whole target-test session.
        if (quiver != null && enableInfiniteArrowsDuringGame)
        {
            quiver.InfiniteArrows = true;
        }

        if (clearFlyingArrowsOnRoundEnd)
        {
            ClearLooseArrows();
        }

        if (ui != null)
        {
            ui.ShowResults(score);
        }
    }

    private void Update()
    {
        if (phase != Phase.Playing)
        {
            return;
        }

        timeRemaining -= Time.deltaTime;
        if (ui != null)
        {
            ui.ShowTimer(timeRemaining, score);
        }

        liveTargets.RemoveAll(t => t == null);

        while (liveTargets.Count < maxTargetsAtOnce)
        {
            SpawnTarget();
        }

        if (timeRemaining <= 0f)
        {
            EndRound();
        }
    }

    public void StartRound()
    {
        if (phase == Phase.Playing)
        {
            return;
        }

        ClearTargets();
        score = 0;
        timeRemaining = roundSeconds;
        phase = Phase.Playing;

        if (quiver != null && enableInfiniteArrowsDuringGame)
        {
            quiver.InfiniteArrows = true;
        }

        if (ui != null)
        {
            ui.ShowTimer(timeRemaining, score);
        }

        for (int i = 0; i < maxTargetsAtOnce; i++)
        {
            SpawnTarget();
        }
    }

    public void NotifyTargetHit(ArcheryTarget target)
    {
        if (phase != Phase.Playing || target == null)
        {
            if (target != null)
            {
                Destroy(target.gameObject);
            }

            return;
        }

        score++;
        liveTargets.Remove(target);
        Destroy(target.gameObject);

        if (ui != null)
        {
            ui.ShowTimer(timeRemaining, score);
        }
    }

    private void SpawnTarget()
    {
        if (!TryRandomPointInSpawnArea(out Vector3 point))
        {
            return;
        }

        ArcheryTarget target;
        if (targetPrefab != null)
        {
            target = Instantiate(targetPrefab, point, Random.rotation, transform);
        }
        else
        {
            target = CreateDefaultTarget(point);
        }

        float scale = Random.Range(targetScaleRange.x, targetScaleRange.y);
        target.transform.localScale = Vector3.one * scale;
        target.Bind(this);
        liveTargets.Add(target);
    }

    private ArcheryTarget CreateDefaultTarget(Vector3 point)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ArcheryTarget";
        go.transform.SetParent(transform, true);
        go.transform.position = point;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Built-in / URP both accept a simple color via material property block when possible.
            Material mat = renderer.material;
            if (mat.HasProperty("_Color"))
            {
                mat.color = new Color(0.85f, 0.15f, 0.15f, 1f);
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", new Color(0.85f, 0.15f, 0.15f, 1f));
            }
        }

        // Ensure arrows can collide (not trigger-only).
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = false;
        }

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        return go.AddComponent<ArcheryTarget>();
    }

    private bool TryRandomPointInSpawnArea(out Vector3 point)
    {
        Bounds bounds = GetSpawnBounds();
        if (bounds.size.sqrMagnitude < 1e-6f)
        {
            point = transform.position + Vector3.forward * 3f + Vector3.up * 1.5f;
            return true;
        }

        point = new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y),
            Random.Range(bounds.min.z, bounds.max.z));
        return true;
    }

    private Bounds GetSpawnBounds()
    {
        if (spawnArea == null)
        {
            return new Bounds(transform.position + Vector3.forward * 4f + Vector3.up * 1.5f, new Vector3(4f, 2f, 2f));
        }

        Collider col = spawnArea.GetComponent<Collider>();
        if (col != null)
        {
            return col.bounds;
        }

        Vector3 center = spawnArea.position;
        Vector3 size = Vector3.Scale(Vector3.one, spawnArea.lossyScale);
        if (size.x < 0.1f) size.x = 2f;
        if (size.y < 0.1f) size.y = 2f;
        if (size.z < 0.1f) size.z = 2f;
        return new Bounds(center, size);
    }

    private void ClearTargets()
    {
        for (int i = 0; i < liveTargets.Count; i++)
        {
            if (liveTargets[i] != null)
            {
                Destroy(liveTargets[i].gameObject);
            }
        }

        liveTargets.Clear();
    }

    private static void ClearLooseArrows()
    {
#if UNITY_2023_1_OR_NEWER
        ArrowProjectile[] arrows = FindObjectsByType<ArrowProjectile>(FindObjectsSortMode.None);
#else
        ArrowProjectile[] arrows = FindObjectsOfType<ArrowProjectile>();
#endif
        for (int i = 0; i < arrows.Length; i++)
        {
            ArrowProjectile arrow = arrows[i];
            if (arrow == null)
            {
                continue;
            }

            // Keep the one currently held on the bow.
            if (!arrow.IsInFlight && !arrow.HasStuck)
            {
                continue;
            }

            Destroy(arrow.gameObject);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Bounds bounds = Application.isPlaying ? GetSpawnBounds() : PreviewSpawnBounds();
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(bounds.center, bounds.size);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.9f);
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }

    private Bounds PreviewSpawnBounds()
    {
        if (spawnArea == null)
        {
            return new Bounds(transform.position + Vector3.forward * 4f + Vector3.up * 1.5f, new Vector3(4f, 2f, 2f));
        }

        Collider col = spawnArea.GetComponent<Collider>();
        if (col != null)
        {
            return col.bounds;
        }

        return new Bounds(spawnArea.position, spawnArea.lossyScale);
    }
}
