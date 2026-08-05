using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Timed target practice: infinite quiver, random targets in a box, world-space start/timer/score UI.
/// </summary>
public class TargetPracticeGame : MonoBehaviour
{
    private enum Phase
    {
        WaitingToStart,
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
    [Tooltip("How many next-target ghosts to show while solid targets are active. "
             + "Lets players pre-aim before the current target is cleared.")]
    [SerializeField, Min(0)] private int previewTargetCount = 1;
    [Tooltip("Color for live / hittable targets.")]
    [SerializeField] private Color activeTargetColor = new Color(0.85f, 0.15f, 0.15f, 1f);
    [Tooltip("Color for next-target ghosts (not hittable until promoted).")]
    [SerializeField] private Color previewTargetColor = new Color(0.2f, 0.75f, 1f, 1f);
    [Tooltip("Random multiplier applied on top of the target prefab's scale. "
             + "Use (1,1) to keep the prefab at exactly the size you authored (e.g. 3,3,3). "
             + "Only used as absolute size when no prefab is set (built-in spheres).")]
    [SerializeField] private Vector2 targetScaleRange = new Vector2(1f, 1f);
    [SerializeField] private bool enableInfiniteArrowsDuringGame = true;
    [SerializeField] private bool clearFlyingArrowsOnRoundEnd = true;
    [Tooltip("After a round ends, wait this long showing only the score before the retry target/text appear.")]
    [SerializeField, Min(0f)] private float retryPromptDelay = 1f;

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.25f);

    private Phase phase = Phase.WaitingToStart;
    private float timeRemaining;
    private int score;
    private readonly List<ArcheryTarget> liveTargets = new List<ArcheryTarget>(8);
    private readonly List<ArcheryTarget> previewTargets = new List<ArcheryTarget>(8);

    public int Score => score;
    public float TimeRemaining => timeRemaining;
    public bool IsPlaying => phase == Phase.Playing;
    public Color ActiveTargetColor => activeTargetColor;
    public Color PreviewTargetColor => previewTargetColor;

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

        EnterWaitingToStart();
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
            textMesh.text = "Shoot the center\ntarget to start";
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
        StopAllCoroutines();

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
            ui.ShowResults(score, showRetryPrompt: false);
        }

        StartCoroutine(ShowRetryAfterDelay());
    }

    private IEnumerator ShowRetryAfterDelay()
    {
        if (retryPromptDelay > 0f)
        {
            yield return new WaitForSeconds(retryPromptDelay);
        }

        if (phase != Phase.Results)
        {
            yield break;
        }

        if (ui != null)
        {
            ui.ShowResults(score, showRetryPrompt: true);
        }

        SpawnStarterTarget();
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
        previewTargets.RemoveAll(t => t == null);
        MaintainTargetCounts();

        if (timeRemaining <= 0f)
        {
            EndRound();
        }
    }

    private void MaintainTargetCounts()
    {
        // Promote ghost previews into solid targets first so their positions stay put.
        while (liveTargets.Count < maxTargetsAtOnce && previewTargets.Count > 0)
        {
            PromotePreview();
        }

        while (liveTargets.Count < maxTargetsAtOnce)
        {
            SpawnTarget(preview: false);
        }

        while (previewTargets.Count < previewTargetCount)
        {
            SpawnTarget(preview: true);
        }
    }

    private void PromotePreview()
    {
        if (previewTargets.Count == 0)
        {
            return;
        }

        ArcheryTarget next = previewTargets[0];
        previewTargets.RemoveAt(0);
        if (next == null)
        {
            return;
        }

        next.SetPreview(false);
        liveTargets.Add(next);
    }

    private void EnterWaitingToStart()
    {
        phase = Phase.WaitingToStart;
        score = 0;
        timeRemaining = 0f;
        ClearTargets();

        if (quiver != null && enableInfiniteArrowsDuringGame)
        {
            quiver.InfiniteArrows = true;
        }

        if (ui != null)
        {
            ui.ShowStart();
        }

        SpawnStarterTarget();
    }

    private void StartRoundFromStarterHit()
    {
        if (phase == Phase.Playing)
        {
            return;
        }

        StopAllCoroutines();
        ClearTargets();
        score = 1;
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

        MaintainTargetCounts();
    }

    public void NotifyTargetHit(ArcheryTarget target)
    {
        if (target == null)
        {
            return;
        }

        if (target.IsStarter)
        {
            liveTargets.Remove(target);
            Destroy(target.gameObject);

            if (phase == Phase.WaitingToStart || phase == Phase.Results)
            {
                StartRoundFromStarterHit();
            }

            return;
        }

        if (phase != Phase.Playing)
        {
            Destroy(target.gameObject);
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

    private void SpawnStarterTarget()
    {
        Bounds bounds = GetSpawnBounds();
        ArcheryTarget target = CreateTargetAt(bounds.center, starter: true, preview: false);
        ApplySpawnScale(target, starter: true);
        liveTargets.Add(target);
    }

    private void SpawnTarget(bool preview)
    {
        if (!TryRandomPointInSpawnArea(out Vector3 point))
        {
            return;
        }

        ArcheryTarget target = CreateTargetAt(point, starter: false, preview: preview);
        ApplySpawnScale(target, starter: false);
        if (preview)
        {
            previewTargets.Add(target);
        }
        else
        {
            liveTargets.Add(target);
        }
    }

    private void ApplySpawnScale(ArcheryTarget target, bool starter)
    {
        if (target == null)
        {
            return;
        }

        // Prefab: keep authored scale, optionally multiply by range.
        // No prefab (procedural sphere): treat range as absolute uniform scale.
        Vector3 baseScale = targetPrefab != null
            ? targetPrefab.transform.localScale
            : Vector3.one;

        float min = Mathf.Min(targetScaleRange.x, targetScaleRange.y);
        float max = Mathf.Max(targetScaleRange.x, targetScaleRange.y);
        float factor = starter ? max * 1.15f : Random.Range(min, max);

        if (targetPrefab != null)
        {
            target.transform.localScale = baseScale * factor;
        }
        else
        {
            // Absolute size for built-in spheres (defaults were ~0.35–0.55 before).
            float absolute = factor > 0.001f ? factor : 0.45f;
            target.transform.localScale = Vector3.one * absolute;
        }
    }

    private ArcheryTarget CreateTargetAt(Vector3 point, bool starter, bool preview)
    {
        ArcheryTarget target;
        if (targetPrefab != null)
        {
            target = Instantiate(targetPrefab, point, starter ? Quaternion.identity : Random.rotation, transform);
        }
        else
        {
            target = CreateDefaultTarget(point);
        }

        target.Bind(this, starter, preview);
        return target;
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
            Material mat = renderer.material;
            Color color = activeTargetColor;
            if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
        }

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

        for (int i = 0; i < previewTargets.Count; i++)
        {
            if (previewTargets[i] != null)
            {
                Destroy(previewTargets[i].gameObject);
            }
        }

        previewTargets.Clear();
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
