using UnityEngine;

/// <summary>
/// Per-pillar rise range, measured from the <see cref="EnderCrystal"/> rest pose
/// (not the pillar root pivot). Gizmos stay fixed in world space at that rest pose
/// so they do not sink when the pillar buries at runtime.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class CrystalPillarRiseSettings : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnderCrystal crystal;
    [Tooltip("Optional marker for height gizmos if crystal is empty.")]
    [SerializeField] private Transform heightReference;

    [Header("Crystal Heights (world Y relative to crystal REST pose)")]
    [Tooltip("Lowest random peak: crystal world Y = rest Y + this.")]
    [SerializeField] private float minHeightOffset = -0.5f;
    [Tooltip("Highest random peak: crystal world Y = rest Y + this.")]
    [SerializeField] private float maxHeightOffset = 2.5f;
    [Tooltip("When buried, crystal world Y = rest Y − this.")]
    [SerializeField, Min(0.1f)] private float buriedDepthBelowCrystal = 8f;

    [Header("Rest Pose (scene placement — gizmos lock here)")]
    [SerializeField] private bool hasRestPose;
    [SerializeField] private Vector3 restPillarPosition;
    [SerializeField] private float crystalOffsetFromPillarY;
    [SerializeField] private Vector3 restCrystalWorldPosition;

    [Header("Gizmos")]
    [SerializeField] private bool drawGizmosAlways = true;
    [SerializeField] private float gizmoRadius = 0.35f;
    [SerializeField] private Color buriedGizmoColor = new Color(0.45f, 0.45f, 0.45f, 0.9f);
    [SerializeField] private Color minGizmoColor = new Color(0.25f, 0.95f, 0.35f, 0.95f);
    [SerializeField] private Color maxGizmoColor = new Color(0.2f, 0.75f, 1f, 0.95f);
    [SerializeField] private Color rangeLineColor = new Color(1f, 0.9f, 0.2f, 0.85f);

    public EnderCrystal Crystal => ResolveCrystal();

    public float CrystalRestWorldY =>
        hasRestPose ? restCrystalWorldPosition.y : GetHeightReferenceWorldY();

    public float MinCrystalPeakWorldY =>
        CrystalRestWorldY + Mathf.Min(minHeightOffset, maxHeightOffset);

    public float MaxCrystalPeakWorldY =>
        CrystalRestWorldY + Mathf.Max(minHeightOffset, maxHeightOffset);

    public float BuriedCrystalWorldY =>
        CrystalRestWorldY - Mathf.Abs(buriedDepthBelowCrystal);

    public Vector3 RestPillarPosition
    {
        get
        {
            EnsureRestPose();
            return restPillarPosition;
        }
    }

    public float PillarYForCrystalY(float crystalWorldY)
    {
        EnsureRestPose();
        return crystalWorldY - crystalOffsetFromPillarY;
    }

    /// <summary>Crystal world Y − pillar pivot Y at the captured rest pose.</summary>
    public float CrystalOffsetFromPillarY
    {
        get
        {
            EnsureRestPose();
            return crystalOffsetFromPillarY;
        }
    }

    public float BuriedDepthBelowCrystal => Mathf.Abs(buriedDepthBelowCrystal);

    public float BuriedPillarWorldY => PillarYForCrystalY(BuriedCrystalWorldY);

    public float PickRandomPeakPillarWorldY()
    {
        float crystalPeak = Random.Range(MinCrystalPeakWorldY, MaxCrystalPeakWorldY);
        return PillarYForCrystalY(crystalPeak);
    }

    private void OnEnable()
    {
        // Edit mode only: keep rest pose synced while you place pillars in the scene.
        if (!Application.isPlaying)
        {
            CaptureRestPoseFromScene();
        }
    }

    private void Awake()
    {
        ResolveCrystal();
        // Do NOT capture here during play — the pillar may already be buried by then.
        if (!Application.isPlaying)
        {
            CaptureRestPoseFromScene();
        }
    }

    [ContextMenu("Capture Rest Pose From Scene")]
    public void CaptureRestPoseFromScene()
    {
        ResolveCrystal();
        Transform reference = ResolveHeightReference();

        restPillarPosition = transform.position;
        restCrystalWorldPosition = reference != null
            ? reference.position
            : transform.position;
        crystalOffsetFromPillarY = restCrystalWorldPosition.y - restPillarPosition.y;
        hasRestPose = true;
    }

    /// <summary>
    /// Call once before burying — captures current scene placement if not already set in editor.
    /// </summary>
    public void CaptureRestPoseIfNeeded()
    {
        if (!hasRestPose)
        {
            CaptureRestPoseFromScene();
        }
    }

    public void EnsureRestPose()
    {
        CaptureRestPoseIfNeeded();
    }

    private EnderCrystal ResolveCrystal()
    {
        if (crystal == null)
        {
            crystal = GetComponentInChildren<EnderCrystal>(true);
        }

        return crystal;
    }

    private Transform ResolveHeightReference()
    {
        if (heightReference != null)
        {
            return heightReference;
        }

        EnderCrystal c = ResolveCrystal();
        if (c != null)
        {
            return c.transform;
        }

        return transform;
    }

    private float GetHeightReferenceWorldY()
    {
        Transform reference = ResolveHeightReference();
        return reference != null ? reference.position.y : transform.position.y;
    }

    private void OnDrawGizmos()
    {
        if (drawGizmosAlways)
        {
            DrawHeightGizmos();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmosAlways)
        {
            DrawHeightGizmos();
        }
    }

    private void DrawHeightGizmos()
    {
        // Edit mode: follow scene placement. Play mode: keep locked rest pose (never follow burial).
        if (!Application.isPlaying)
        {
            CaptureRestPoseFromScene();
        }
        else if (!hasRestPose)
        {
            return;
        }

        float x = restCrystalWorldPosition.x;
        float z = restCrystalWorldPosition.z;

        Vector3 buried = new Vector3(x, BuriedCrystalWorldY, z);
        Vector3 minPeak = new Vector3(x, MinCrystalPeakWorldY, z);
        Vector3 maxPeak = new Vector3(x, MaxCrystalPeakWorldY, z);
        Vector3 rest = restCrystalWorldPosition;

        Gizmos.color = rangeLineColor;
        Gizmos.DrawLine(buried, maxPeak);

        DrawGizmoDisc(buried, buriedGizmoColor, gizmoRadius);
        DrawGizmoDisc(minPeak, minGizmoColor, gizmoRadius);
        DrawGizmoDisc(maxPeak, maxGizmoColor, gizmoRadius);

        Gizmos.color = new Color(1f, 1f, 1f, 0.55f);
        Gizmos.DrawWireSphere(rest, gizmoRadius * 0.55f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = buriedGizmoColor;
        UnityEditor.Handles.Label(buried + Vector3.right * gizmoRadius, "Buried");
        UnityEditor.Handles.color = minGizmoColor;
        UnityEditor.Handles.Label(minPeak + Vector3.right * gizmoRadius, "Min peak");
        UnityEditor.Handles.color = maxGizmoColor;
        UnityEditor.Handles.Label(maxPeak + Vector3.right * gizmoRadius, "Max peak");
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(rest + Vector3.right * gizmoRadius, "Rest");
#endif
    }

    private static void DrawGizmoDisc(Vector3 center, Color color, float radius)
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(center, radius);

        const int segments = 24;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
