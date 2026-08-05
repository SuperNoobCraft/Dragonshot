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

    [Header("Flight")]
    [Tooltip("Keep the tip pointed along velocity so the shaft follows the ballistic arc.")]
    [SerializeField] private bool alignToVelocity = true;
    [Tooltip("Ignore tiny velocities (m/s) to avoid jitter near the apex / landing.")]
    [SerializeField, Min(0.01f)] private float alignMinSpeed = 0.5f;
    [Tooltip("0 = snap to velocity each physics step; higher = smoother (less snappy).")]
    [SerializeField, Range(0f, 30f)] private float alignSmoothing = 12f;
    [Tooltip("1 = Unity gravity (−9.81). 1.3–1.6 shortens the apex so room-scale shots feel less floaty "
             + "without needing unrealistically slow launches.")]
    [SerializeField, Min(0.1f)] private float gravityMultiplier = 1.35f;
    [Tooltip("Light air drag (Rigidbody.drag). ~0.05–0.15 bleeds speed over distance.")]
    [SerializeField, Min(0f)] private float airDrag = 0.08f;

    [Header("Impact")]
    [SerializeField] private bool stickOnHit = true;
    [Tooltip("Layers that stop and pin the arrow. Leave empty to stick on any layer.")]
    [SerializeField] private LayerMask stickLayers;
    [SerializeField] private float lifeSeconds = 12f;
    [Tooltip("If true, arrow stays in world space when stuck (avoids ground scale twisting the shaft).")]
    [SerializeField] private bool stickInWorldSpace = true;

    private Rigidbody body;
    private Collider[] selfColliders;
    private Vector3 restLocalPos;
    private Quaternion restLocalRot;
    private bool flying;
    private bool stuck;
    private float killTime;
    private float clearIgnoreAt;
    private Collider[] temporarilyIgnored;
    private Vector3 shaftLocalDir = Vector3.forward;
    private Vector3 lastAirVelocity = Vector3.forward;
    private Quaternion stuckRotation = Quaternion.identity;

    public Transform Tip => arrowTip;
    public Transform Rear => arrowRear;
    public bool IsInFlight => flying;
    public bool HasStuck => stuck;
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
        if (stuck)
        {
            // Re-assert pose in case parenting / physics touched it.
            transform.rotation = stuckRotation;
            if (body != null && body.isKinematic)
            {
                body.rotation = stuckRotation;
            }

            return;
        }

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

    private void FixedUpdate()
    {
        if (!flying || stuck || body == null || body.isKinematic)
        {
            return;
        }

        // Extra gravity beyond Physics.gravity (useGravity already applies 1×).
        if (gravityMultiplier > 1.001f)
        {
            body.AddForce(Physics.gravity * (gravityMultiplier - 1f), ForceMode.Acceleration);
        }

        Vector3 velocity = body.velocity;
        float speedSqr = velocity.sqrMagnitude;
        float minSqr = alignMinSpeed * alignMinSpeed;
        if (speedSqr < minSqr)
        {
            return;
        }

        // Cache free-flight velocity only — once we scrape the ground, velocity
        // goes nearly horizontal and would bake a flat stick pose.
        lastAirVelocity = velocity;

        if (!alignToVelocity)
        {
            return;
        }

        Quaternion target = RotationForDirection(velocity);
        if (alignSmoothing <= 0.01f)
        {
            body.MoveRotation(target);
        }
        else
        {
            float t = 1f - Mathf.Exp(-alignSmoothing * Time.fixedDeltaTime);
            body.MoveRotation(Quaternion.Slerp(body.rotation, target, t));
        }

        body.angularVelocity = Vector3.zero;
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

    public void PrepareHeld()
    {
        flying = false;
        stuck = false;
        ClearIgnore();
        MakeKinematic();
    }

    public void AttachTo(Transform parent)
    {
        PrepareHeld();
        transform.SetParent(parent, false);
        transform.localPosition = restLocalPos;
        transform.localRotation = restLocalRot;
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
            temporarilyIgnored = null;
            clearIgnoreAt = Time.time;
        }

        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        body.isKinematic = false;
        body.detectCollisions = true;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
        // Rotation is driven to follow velocity; freeze random physics spin.
        body.freezeRotation = alignToVelocity;
        body.drag = airDrag;
        body.angularDrag = 0.05f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.velocity = direction * speed;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();

        lastAirVelocity = direction * speed;
        stuck = false;
        flying = true;
        killTime = lifeSeconds > 0f ? Time.time + lifeSeconds : float.PositiveInfinity;
        ArrowManager.Register(this);
    }

    private void MakeKinematic()
    {
        body.isKinematic = true;
        body.detectCollisions = false;
        body.useGravity = false;
        body.freezeRotation = false;
        body.drag = 0f;
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
        if (!flying || stuck || !stickOnHit)
        {
            return;
        }

        if (Time.time < clearIgnoreAt)
        {
            return;
        }

        if (!IsStickLayer(collision.gameObject.layer))
        {
            return;
        }

        Stick(collision);
    }

    private bool IsStickLayer(int layer)
    {
        // Empty mask = stick to anything.
        if (stickLayers.value == 0)
        {
            return true;
        }

        return ((1 << layer) & stickLayers) != 0;
    }

    private void Stick(Collision collision)
    {
        // Incident direction from last free-flight sample (pre-scrape), not the
        // post-contact horizontal velocity that makes the shaft look flat.
        Vector3 impactDir = lastAirVelocity;
        if (impactDir.sqrMagnitude < 1e-4f && collision != null)
        {
            impactDir = collision.relativeVelocity;
        }

        if (impactDir.sqrMagnitude < 1e-4f)
        {
            impactDir = TipWorldDirection;
        }

        impactDir.Normalize();

        Quaternion impactRotation = RotationForDirection(impactDir);
        stuckRotation = impactRotation;

        flying = false;
        stuck = true;
        ClearIgnore();

        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        body.interpolation = RigidbodyInterpolation.None;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.detectCollisions = false;
        body.freezeRotation = true;
        body.isKinematic = true;

        Vector3 impactPosition = body.position;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(impactPosition, impactRotation);

        // After rotation is correct, slide so the tip sits on the contact point.
        if (collision != null && collision.contactCount > 0 && arrowTip != null)
        {
            ContactPoint contact = collision.GetContact(0);
            impactPosition += contact.point - arrowTip.position;
            transform.position = impactPosition;
        }

        body.position = transform.position;
        body.rotation = impactRotation;

        if (!stickInWorldSpace && collision != null)
        {
            transform.SetParent(collision.transform, true);
            transform.SetPositionAndRotation(impactPosition, impactRotation);
            body.position = impactPosition;
            body.rotation = impactRotation;
        }
    }

    private void OnDestroy()
    {
        ClearIgnore();
        ArrowManager.Unregister(this);
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
