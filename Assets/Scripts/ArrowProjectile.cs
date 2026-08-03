using UnityEngine;

/// <summary>
/// Kinematic while held/nocked; rigidbody flight after <see cref="Fire"/>.
/// Optional <see cref="arrowTip"/> / <see cref="arrowRear"/> children define shaft aim and nock placement.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ArrowProjectile : MonoBehaviour
{
    [Header("Shaft Markers")]
    [Tooltip("Child at the tip. Auto-finds 'ArrowTip' if empty.")]
    [SerializeField] private Transform arrowTip;
    [Tooltip("Child at the nock / rear. Auto-finds 'ArrowRear' if empty.")]
    [SerializeField] private Transform arrowRear;

    [Header("Fallback Aim")]
    [SerializeField] private Vector3 pullAxis = Vector3.back;
    [Tooltip("Used only when tip/rear markers are missing.")]
    [SerializeField] private Vector3 tipAimEuler = new Vector3(0f, -90f, 0f);

    [Header("Impact")]
    [SerializeField] private bool stickOnHit = true;
    [SerializeField] private LayerMask stickLayers;
    [SerializeField] private float lifeSeconds = 12f;

    private Rigidbody body;
    private Collider[] selfColliders;
    private Vector3 restLocalPos;
    private Quaternion restLocalRot;
    private bool flying;
    private float killTime;
    private float clearIgnoreAt;
    private Collider[] temporarilyIgnored;
    private Vector3 shaftLocalDir = Vector3.forward;

    public Transform Tip => arrowTip;
    public Transform Rear => arrowRear;
    public float ShaftLength
    {
        get
        {
            if (arrowTip == null || arrowRear == null)
            {
                return 0.8f;
            }

            return Vector3.Distance(arrowTip.position, arrowRear.position);
        }
    }

    public Vector3 TipWorldDirection
    {
        get
        {
            if (arrowTip != null && arrowRear != null)
            {
                Vector3 d = arrowTip.position - arrowRear.position;
                if (d.sqrMagnitude > 1e-8f)
                {
                    return d.normalized;
                }
            }

            return transform.rotation * shaftLocalDir;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        selfColliders = GetComponentsInChildren<Collider>(true);
        restLocalPos = transform.localPosition;
        restLocalRot = transform.localRotation;
        ResolveMarkers();
        CacheShaftLocalDir();
        MakeKinematic();
    }

    private void ResolveMarkers()
    {
        if (arrowTip == null)
        {
            arrowTip = FindChildRecursive(transform, "ArrowTip");
        }

        if (arrowRear == null)
        {
            arrowRear = FindChildRecursive(transform, "ArrowRear");
        }
    }

    private void CacheShaftLocalDir()
    {
        if (arrowTip != null && arrowRear != null)
        {
            Vector3 local = transform.InverseTransformPoint(arrowTip.position)
                            - transform.InverseTransformPoint(arrowRear.position);
            if (local.sqrMagnitude > 1e-8f)
            {
                shaftLocalDir = local.normalized;
                return;
            }
        }

        shaftLocalDir = Quaternion.Euler(tipAimEuler) * Vector3.forward;
        if (shaftLocalDir.sqrMagnitude < 1e-8f)
        {
            shaftLocalDir = Vector3.forward;
        }
        else
        {
            shaftLocalDir.Normalize();
        }
    }

    private void Update()
    {
        if (!flying)
        {
            return;
        }

        if (temporarilyIgnored != null && Time.time >= clearIgnoreAt)
        {
            SetIgnore(temporarilyIgnored, false);
            temporarilyIgnored = null;
        }

        if (lifeSeconds > 0f && Time.time >= killTime)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// World rotation so ArrowTip − ArrowRear (or tipAimEuler fallback) points along <paramref name="direction"/>.
    /// </summary>
    public Quaternion RotationForDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude < 1e-6f)
        {
            direction = Vector3.forward;
        }

        direction.Normalize();
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.95f)
        {
            up = Vector3.right;
        }

        return Quaternion.LookRotation(direction, up)
               * Quaternion.Inverse(Quaternion.LookRotation(shaftLocalDir, Vector3.up));
    }

    /// <summary>
    /// Put ArrowRear at <paramref name="rearWorldPosition"/> with tip aimed along <paramref name="tipDirection"/>.
    /// </summary>
    public void PlaceRearAt(Vector3 rearWorldPosition, Vector3 tipDirection)
    {
        transform.SetParent(null, true);
        transform.rotation = RotationForDirection(tipDirection);

        if (arrowRear != null)
        {
            transform.position += rearWorldPosition - arrowRear.position;
        }
        else
        {
            transform.position = rearWorldPosition;
        }

        MakeKinematic();
    }

    /// <summary>
    /// Center the shaft on <paramref name="worldPosition"/> with tip along <paramref name="tipDirection"/>.
    /// </summary>
    public void PlaceCenterAt(Vector3 worldPosition, Vector3 tipDirection)
    {
        transform.SetParent(null, true);
        transform.rotation = RotationForDirection(tipDirection);

        if (arrowTip != null && arrowRear != null)
        {
            Vector3 mid = (arrowTip.position + arrowRear.position) * 0.5f;
            transform.position += worldPosition - mid;
        }
        else
        {
            transform.position = worldPosition;
        }

        MakeKinematic();
    }

    public void AttachTo(Transform parent)
    {
        flying = false;
        ClearIgnore();
        transform.SetParent(parent, false);
        transform.localPosition = restLocalPos;
        transform.localRotation = restLocalRot;
        MakeKinematic();
    }

    public void Nock(Transform rest)
    {
        AttachTo(rest);
    }

    public void PullBack(float t01, float distance)
    {
        if (flying)
        {
            return;
        }

        Vector3 axis = pullAxis.sqrMagnitude > 1e-6f ? pullAxis.normalized : Vector3.back;
        transform.localPosition = restLocalPos + axis * (distance * Mathf.Clamp01(t01));
        transform.localRotation = restLocalRot;
    }

    public void Fire(Vector3 direction, float speed, Collider[] ignore = null, float ignoreFor = 0.35f)
    {
        if (direction.sqrMagnitude < 1e-6f)
        {
            direction = TipWorldDirection;
        }

        direction.Normalize();
        transform.SetParent(null, true);
        transform.rotation = RotationForDirection(direction);

        if (ignore != null && ignore.Length > 0)
        {
            SetIgnore(ignore, true);
            temporarilyIgnored = ignore;
            clearIgnoreAt = Time.time + ignoreFor;
        }
        else
        {
            clearIgnoreAt = Time.time;
        }

        body.isKinematic = false;
        body.detectCollisions = true;
        body.useGravity = true;
        body.velocity = direction * speed;
        body.angularVelocity = Vector3.zero;

        flying = true;
        killTime = lifeSeconds > 0f ? Time.time + lifeSeconds : float.PositiveInfinity;
    }

    private void MakeKinematic()
    {
        body.isKinematic = true;
        body.detectCollisions = false;
        body.useGravity = false;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void SetIgnore(Collider[] others, bool ignore)
    {
        if (others == null || selfColliders == null)
        {
            return;
        }

        for (int i = 0; i < selfColliders.Length; i++)
        {
            Collider a = selfColliders[i];
            if (a == null)
            {
                continue;
            }

            for (int j = 0; j < others.Length; j++)
            {
                Collider b = others[j];
                if (b == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }

    private void ClearIgnore()
    {
        if (temporarilyIgnored != null)
        {
            SetIgnore(temporarilyIgnored, false);
            temporarilyIgnored = null;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!flying || !stickOnHit)
        {
            return;
        }

        if (Time.time < clearIgnoreAt)
        {
            return;
        }

        if (((1 << collision.gameObject.layer) & stickLayers) == 0)
        {
            return;
        }

        flying = false;
        ClearIgnore();

        body.isKinematic = true;
        body.detectCollisions = false;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        transform.SetParent(collision.transform, true);
    }

    private void OnDestroy()
    {
        ClearIgnore();
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
