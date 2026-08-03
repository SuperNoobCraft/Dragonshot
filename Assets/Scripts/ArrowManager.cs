using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks shot arrows and culls them so the scene does not fill up forever.
/// Default: hard cap with FIFO, plus optional max age / distance from the player.
/// </summary>
[DefaultExecutionOrder(-50)]
public class ArrowManager : MonoBehaviour
{
    public static ArrowManager Instance { get; private set; }

    [SerializeField, Min(1)] private int maxArrows = 50;
    [Tooltip("Destroy arrows older than this many seconds (flying or stuck). 0 = ignore age.")]
    [SerializeField, Min(0f)] private float maxAgeSeconds = 45f;
    [Tooltip("Destroy arrows farther than this from the player. 0 = ignore distance.")]
    [SerializeField, Min(0f)] private float maxDistanceFromPlayer = 80f;
    [SerializeField, Min(0.1f)] private float cullInterval = 0.5f;

    private readonly List<TrackedArrow> tracked = new List<TrackedArrow>(64);
    private float nextCullTime;

    private struct TrackedArrow
    {
        public ArrowProjectile Arrow;
        public float SpawnTime;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple ArrowManager instances; using the newest.", this);
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCullTime)
        {
            return;
        }

        nextCullTime = Time.unscaledTime + cullInterval;
        Cull();
    }

    public static void Register(ArrowProjectile arrow)
    {
        if (arrow == null)
        {
            return;
        }

        EnsureInstance();
        Instance.RegisterInternal(arrow);
    }

    public static void Unregister(ArrowProjectile arrow)
    {
        if (Instance == null || arrow == null)
        {
            return;
        }

        Instance.UnregisterInternal(arrow);
    }

    private void RegisterInternal(ArrowProjectile arrow)
    {
        for (int i = 0; i < tracked.Count; i++)
        {
            if (tracked[i].Arrow == arrow)
            {
                return;
            }
        }

        tracked.Add(new TrackedArrow
        {
            Arrow = arrow,
            SpawnTime = Time.time
        });

        while (tracked.Count > maxArrows)
        {
            DestroyOldest();
        }
    }

    private void UnregisterInternal(ArrowProjectile arrow)
    {
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            if (tracked[i].Arrow == arrow)
            {
                tracked.RemoveAt(i);
            }
        }
    }

    private void Cull()
    {
        Vector3 playerPos = ResolvePlayerPosition();
        float now = Time.time;

        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            ArrowProjectile arrow = tracked[i].Arrow;
            if (arrow == null)
            {
                tracked.RemoveAt(i);
                continue;
            }

            bool tooOld = maxAgeSeconds > 0f && now - tracked[i].SpawnTime >= maxAgeSeconds;
            bool tooFar = maxDistanceFromPlayer > 0f
                          && (arrow.transform.position - playerPos).sqrMagnitude
                          >= maxDistanceFromPlayer * maxDistanceFromPlayer;

            if (tooOld || tooFar)
            {
                tracked.RemoveAt(i);
                Destroy(arrow.gameObject);
            }
        }

        while (tracked.Count > maxArrows)
        {
            DestroyOldest();
        }
    }

    private void DestroyOldest()
    {
        if (tracked.Count == 0)
        {
            return;
        }

        ArrowProjectile oldest = tracked[0].Arrow;
        tracked.RemoveAt(0);
        if (oldest != null)
        {
            Destroy(oldest.gameObject);
        }
    }

    private static Vector3 ResolvePlayerPosition()
    {
        Transform player = PlayEnvironment.ResolvePlayerTransform();
        if (player != null)
        {
            return player.position;
        }

        Camera cam = PlayEnvironment.ResolveViewCamera();
        return cam != null ? cam.transform.position : Vector3.zero;
    }

    private static void EnsureInstance()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject go = new GameObject("ArrowManager");
        Instance = go.AddComponent<ArrowManager>();
    }
}
