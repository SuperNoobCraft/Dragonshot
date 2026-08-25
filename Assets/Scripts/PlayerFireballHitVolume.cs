using UnityEngine;

/// <summary>
/// Player fireball hurtbox: child of glasses (sensor.vision), not wand head / user root / wall cameras.
/// Hit tests use a vertical cylinder — do not rely on trigger ClosestPoint (broken on triggers).
/// </summary>
[DisallowMultipleComponent]
public class PlayerFireballHitVolume : MonoBehaviour
{
    public const string VolumeObjectName = "PlayerTrackedHeadHitVolume";

    [Header("Hurtbox")]
    [Tooltip("Horizontal body radius around the glasses (meters).")]
    [SerializeField, Min(0.05f)] private float headRadius = 0.22f;
    [Tooltip("Hurtbox extends this far below the glasses (standing body).")]
    [SerializeField, Min(0.1f)] private float heightBelowHead = 1.75f;
    [Tooltip("Hurtbox extends this far above the glasses.")]
    [SerializeField, Min(0f)] private float heightAboveHead = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool drawGroundRing = true;
    [SerializeField] private Color groundRingColor = new Color(1f, 0.2f, 0.2f, 0.85f);
    [SerializeField, Min(8)] private int groundRingSegments = 24;
    [SerializeField] private bool logFollowTarget = true;

    private SphereCollider markerCollider;
    private Transform followTarget;
    private static PlayerFireballHitVolume instance;
    private static string lastLoggedFollow;

    public float HeadRadius => headRadius;
    public float HeightBelowHead => heightBelowHead;
    public float HeightAboveHead => heightAboveHead;
    public Vector3 HurtboxCenter => transform.position;
    public Transform FollowTarget => followTarget;
    public static PlayerFireballHitVolume Instance => instance;

    private void Awake()
    {
        EnsureMarkerCollider();
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void LateUpdate()
    {
        Transform target = ResolveGlassesOrFallback();
        if (target == null)
        {
            return;
        }

        if (followTarget != target)
        {
            followTarget = target;
            transform.SetParent(target, false);
            if (logFollowTarget)
            {
                string key = target.name + "@" + GetTransformPath(target);
                if (lastLoggedFollow != key)
                {
                    lastLoggedFollow = key;
                    Debug.Log(
                        "PlayerFireballHitVolume following: " + key
                        + " (desktop=" + PlayEnvironment.IsDesktopInput
                        + ", cave=" + PlayEnvironment.IsCaveMode + ")",
                        this);
                }
            }
        }

        // Parent and leave — never write world position (CAVE tracking owns the parent).
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        if (drawGroundRing)
        {
            DrawGroundRing();
        }
    }

    /// <summary>
    /// Vertical cylinder around glasses: horizontal radius = headRadius + projectileRadius,
    /// Y from center.y - heightBelowHead to center.y + heightAboveHead.
    /// Samples the travel segment so fast projectiles cannot tunnel.
    /// </summary>
    public bool TryEvaluateSegmentHit(
        Vector3 segmentStart,
        Vector3 segmentEnd,
        float projectileRadius,
        float sampleStepMeters = 0.2f,
        Vector3? overrideCenter = null)
    {
        Vector3 center = overrideCenter ?? HurtboxCenter;
        float combinedRadius = Mathf.Max(0.01f, headRadius + Mathf.Max(0f, projectileRadius));
        float combinedRadiusSq = combinedRadius * combinedRadius;
        float yMin = center.y - heightBelowHead;
        float yMax = center.y + heightAboveHead;

        Vector3 delta = segmentEnd - segmentStart;
        float length = delta.magnitude;
        int samples = 1;
        if (length > 1e-5f && sampleStepMeters > 1e-4f)
        {
            samples = Mathf.Max(1, Mathf.CeilToInt(length / sampleStepMeters));
        }

        for (int i = 0; i <= samples; i++)
        {
            float t = samples == 0 ? 1f : i / (float)samples;
            Vector3 p = Vector3.Lerp(segmentStart, segmentEnd, t);
            if (p.y < yMin || p.y > yMax)
            {
                continue;
            }

            float dx = p.x - center.x;
            float dz = p.z - center.z;
            if (dx * dx + dz * dz <= combinedRadiusSq)
            {
                return true;
            }
        }

        // Closest XZ approach on the segment (between sample steps).
        if (length > 1e-6f)
        {
            Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
            float flatLenSq = flatDelta.sqrMagnitude;
            if (flatLenSq > 1e-8f)
            {
                Vector3 flatStart = new Vector3(segmentStart.x, 0f, segmentStart.z);
                Vector3 flatCenter = new Vector3(center.x, 0f, center.z);
                float tClosest = Mathf.Clamp01(
                    Vector3.Dot(flatCenter - flatStart, flatDelta) / flatLenSq);
                Vector3 p = Vector3.Lerp(segmentStart, segmentEnd, tClosest);
                if (p.y >= yMin && p.y <= yMax)
                {
                    float dx = p.x - center.x;
                    float dz = p.z - center.z;
                    if (dx * dx + dz * dz <= combinedRadiusSq)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool ContainsPoint(Vector3 worldPoint, float projectileRadius)
    {
        return TryEvaluateSegmentHit(worldPoint, worldPoint, projectileRadius, sampleStepMeters: 1f);
    }

    public static bool IsOurCollider(Collider other)
    {
        return other != null && other.GetComponentInParent<PlayerFireballHitVolume>() != null;
    }

    public static bool TryGetHurtboxAimPoint(out Vector3 worldPoint)
    {
        PlayerFireballHitVolume volume = Ensure();
        if (volume == null || !volume.IsFollowingReliableTarget())
        {
            worldPoint = default;
            return false;
        }

        worldPoint = volume.HurtboxCenter;
        return true;
    }

    public static PlayerFireballHitVolume Ensure()
    {
        if (instance != null)
        {
            return instance;
        }

#if UNITY_2023_1_OR_NEWER
        instance = Object.FindFirstObjectByType<PlayerFireballHitVolume>();
#else
        instance = Object.FindObjectOfType<PlayerFireballHitVolume>();
#endif
        if (instance != null)
        {
            return instance;
        }

        Transform parent = ResolveGlassesOrFallback();
        if (parent == null)
        {
            return null;
        }

        Transform existing = parent.Find(VolumeObjectName);
        GameObject go = existing != null
            ? existing.gameObject
            : new GameObject(VolumeObjectName);

        if (existing == null)
        {
            go.transform.SetParent(parent, false);
        }

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        PlayerFireballHitVolume volume = go.GetComponent<PlayerFireballHitVolume>();
        if (volume == null)
        {
            volume = go.AddComponent<PlayerFireballHitVolume>();
        }

        instance = volume;
        return volume;
    }

    private bool IsFollowingReliableTarget()
    {
        if (followTarget == null)
        {
            return false;
        }

        if (PlayEnvironment.IsDesktopInput)
        {
            return true;
        }

        // Tracked: only trust glasses / sensor parents — not wall cameras or wand head.
        Transform vision = PlayEnvironment.ResolveVisionTransform();
        if (vision != null && followTarget == vision)
        {
            return true;
        }

        Transform sensor = PlayEnvironment.ResolveSensorTransform();
        return sensor != null && followTarget == sensor;
    }

    private void EnsureMarkerCollider()
    {
        markerCollider = GetComponent<SphereCollider>();
        if (markerCollider == null)
        {
            markerCollider = gameObject.AddComponent<SphereCollider>();
        }

        markerCollider.isTrigger = true;
        markerCollider.radius = headRadius;
        markerCollider.center = Vector3.zero;
    }

    /// <summary>
    /// Desktop: mouse-look view camera.
    /// CAVE: vision glasses → sensor only. Never Camera.main (wall cameras) or wand head.
    /// </summary>
    private static Transform ResolveGlassesOrFallback()
    {
        bool useGlasses = PlayEnvironment.PreferTrackedGlassesTracking();

        if (!useGlasses && PlayEnvironment.IsDesktopInput)
        {
            Camera desktopCam = PlayEnvironment.ResolveViewCamera();
            if (desktopCam != null && desktopCam.gameObject.activeInHierarchy)
            {
                return desktopCam.transform;
            }

            Transform desktopParent = PlayEnvironment.ResolveDesktopBowParent();
            if (desktopParent != null && desktopParent.gameObject.activeInHierarchy)
            {
                return desktopParent;
            }

            return null;
        }

        // Tracked XR / CAVE — glasses first.
        Transform vision = PlayEnvironment.ResolveVisionTransform();
        if (vision != null)
        {
            return vision;
        }

        Transform sensor = PlayEnvironment.ResolveSensorTransform();
        if (sensor != null)
        {
            return sensor;
        }

        // Never parent to Camera.main in CAVE (wall / compositing cameras sit "behind" the player).
        return null;
    }

    private static bool IsControllerOrChild(Transform candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        Transform controller = PlayEnvironment.ResolveControllerTransform();
        if (controller == null)
        {
            string n = candidate.name;
            return n.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Wand", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return candidate == controller
            || candidate.IsChildOf(controller)
            || controller.IsChildOf(candidate);
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null)
        {
            return "null";
        }

        string path = t.name;
        Transform p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 12)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }

        return path;
    }

    private void DrawGroundRing()
    {
        Vector3 center = HurtboxCenter;
        float y = center.y - heightBelowHead + 0.02f;
        Vector3 prev = new Vector3(center.x + headRadius, y, center.z);
        int segs = Mathf.Max(8, groundRingSegments);
        for (int i = 1; i <= segs; i++)
        {
            float ang = (i / (float)segs) * Mathf.PI * 2f;
            Vector3 next = new Vector3(
                center.x + Mathf.Cos(ang) * headRadius,
                y,
                center.z + Mathf.Sin(ang) * headRadius);
            Debug.DrawLine(prev, next, groundRingColor, 0f, false);
            prev = next;
        }
    }
}
