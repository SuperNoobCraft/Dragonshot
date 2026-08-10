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
    [Tooltip("Gravity scale while in flight. 1 = Earth gravity (−9.81 m/s²). "
             + "0.1 = very floaty; 1.35 = snappier arc for room-scale.")]
    [SerializeField, Min(0f)] private float gravityMultiplier = 1.35f;
    [Tooltip("Light air drag (Rigidbody.drag). ~0.05–0.15 bleeds speed over distance.")]
    [SerializeField, Min(0f)] private float airDrag = 0.08f;

    [Header("Impact")]
    [Tooltip("Damage applied when this arrow hits a dragon (set from BowController on fire).")]
    [SerializeField, Min(1)] private int damage = 1;
    [SerializeField] private bool stickOnHit = true;
    [Tooltip("Layers the arrow can stick into. Empty / Nothing = stick to everything.")]
    [SerializeField] private LayerMask stickLayers;
    [SerializeField] private float lifeSeconds = 12f;
    [Tooltip("If true, arrow stays in world space when stuck (avoids ground scale twisting the shaft).")]
    [SerializeField] private bool stickInWorldSpace = true;
    [Tooltip("Sphere-cast radius used to pin the tip into solids (more reliable than collision alone).")]
    [SerializeField, Min(0.005f)] private float stickProbeRadius = 0.025f;

    [Header("Flight Trail")]
    [Tooltip("Leave a visible path while the arrow is in the air (helps at distance).")]
    [SerializeField] private bool showFlightTrail = true;
    [SerializeField] private float trailTime = 0.85f;
    [SerializeField] private float trailStartWidth = 0.035f;
    [SerializeField] private float trailEndWidth = 0.005f;
    [SerializeField] private Color trailStartColor = new Color(1f, 0.85f, 0.25f, 0.95f);
    [SerializeField] private Color trailEndColor = new Color(1f, 0.45f, 0.05f, 0f);
    [SerializeField, Min(0.001f)] private float trailMinVertexDistance = 0.04f;

    private Rigidbody body;
    private Collider[] selfColliders;
    private TrailRenderer trail;
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
    private bool dragonHitHandled;
    private static PhysicMaterial sharedNoBounceMaterial;

    /// <summary>
    /// Prevents the same arrow from registering twice (arrow collider + dragon mesh relay).
    /// </summary>
    public bool TryHandleDragonHit()
    {
        if (dragonHitHandled)
        {
            return false;
        }

        dragonHitHandled = true;
        return true;
    }

    public Transform Tip => arrowTip;
    public int Damage => damage;
    public Transform Rear => arrowRear;
    public bool IsInFlight => flying;
    public bool HasStuck => stuck;
    public float GravityMultiplier => gravityMultiplier;
    public float AirDrag => airDrag;
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
        ApplyNoBounceMaterial();
        // Prefab had stickLayers=128 (layer 7 only), so Default ground/pillars never stuck.
        // Nothing (0) and Everything (~0) both mean "stick to any solid".
        if (stickLayers.value != 0 && stickLayers.value != ~0)
        {
            stickLayers = ~0;
        }

        restLocalPos = transform.localPosition;
        restLocalRot = transform.localRotation;
        ResolveMarkers();
        CacheShaftLocalDir();
        EnsureTrail();
        SetTrailEmitting(false, clear: true);
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

        // Apply scaled gravity ourselves — Rigidbody.useGravity is always 1× and ignores this field.
        if (gravityMultiplier > 0f)
        {
            body.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
        }

        Vector3 velocity = body.velocity;
        float speedSqr = velocity.sqrMagnitude;
        float minSqr = alignMinSpeed * alignMinSpeed;
        if (speedSqr >= minSqr)
        {
            // Cache free-flight velocity only — once we scrape the ground, velocity
            // goes nearly horizontal and would bake a flat stick pose.
            lastAirVelocity = velocity;

            if (alignToVelocity)
            {
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
        }

        TryHitDragonPrecise(velocity);
        TryStickViaSweep(velocity);
    }

    /// <summary>
    /// Triangle-accurate dragon hit (avoids PhysX convex hull filling gaps under wings).
    /// </summary>
    private void TryHitDragonPrecise(Vector3 velocity)
    {
        if (stuck || !flying || dragonHitHandled)
        {
            return;
        }

        float speed = velocity.magnitude;
        if (speed < 0.15f)
        {
            return;
        }

        Vector3 direction = velocity / speed;
        Vector3 tip = arrowTip != null ? arrowTip.position : body.position;
        float castDistance = speed * Time.fixedDeltaTime + 0.08f;
        Ray ray = new Ray(tip - direction * 0.02f, direction);
        DragonBoss dragon = DragonBoss.Resolve();
        if (dragon == null || !dragon.IsFightActive)
        {
            return;
        }

        if (dragon.RaycastBody(ray, castDistance, out _, out _))
        {
            dragon.HandleArrowCollision(this);
        }
    }

    /// <summary>
    /// Pin into solids along flight path. More reliable than OnCollisionEnter alone
    /// (fast arrows / glancing ground hits often bounce or slide without a stick message).
    /// </summary>
    private void TryStickViaSweep(Vector3 velocity)
    {
        if (!stickOnHit || stuck || !flying)
        {
            return;
        }

        float speed = velocity.magnitude;
        if (speed < 0.15f)
        {
            return;
        }

        Vector3 direction = velocity / speed;
        Vector3 tip = arrowTip != null ? arrowTip.position : body.position;
        float castDistance = speed * Time.fixedDeltaTime + stickProbeRadius * 2f;

        int mask = StickLayerMask;
        if (!Physics.SphereCast(
                tip - direction * stickProbeRadius,
                stickProbeRadius,
                direction,
                out RaycastHit hit,
                castDistance,
                mask,
                QueryTriggerInteraction.Ignore))
        {
            return;
        }

        if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
        {
            return;
        }

        if (IsTemporarilyIgnoredCollider(hit.collider))
        {
            return;
        }

        // Dragon body is handled by TryHitDragonPrecise (triangle mesh) — ignore PhysX hulls.
        if (hit.collider.GetComponentInParent<DragonBoss>() != null)
        {
            return;
        }

        if (hit.collider.GetComponentInParent<EnderCrystal>() != null)
        {
            // Crystal destroys the arrow on its own hit callback.
            return;
        }

        Stick(hit.point, direction, hit.collider.transform);
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
        dragonHitHandled = false;
        ClearIgnore();
        SetTrailEmitting(false, clear: true);
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
        Fire(direction, speed, damage, ignore, ignoreFor);
    }

    public void Fire(Vector3 direction, float speed, int shotDamage, Collider[] ignore = null, float ignoreFor = 0.35f)
    {
        if (direction.sqrMagnitude < 1e-6f)
        {
            direction = TipWorldDirection;
        }

        damage = Mathf.Max(1, shotDamage);
        dragonHitHandled = false;
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
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.None;
        // Rotation is driven to follow velocity; freeze random physics spin.
        body.freezeRotation = alignToVelocity;
        body.drag = airDrag;
        body.angularDrag = 0.05f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.velocity = direction * speed;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();

        lastAirVelocity = direction * speed;
        stuck = false;
        flying = true;
        killTime = lifeSeconds > 0f ? Time.time + lifeSeconds : float.PositiveInfinity;
        SetTrailEmitting(showFlightTrail, clear: true);
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
        if (!flying || stuck)
        {
            return;
        }

        if (TryHandleDragonHit(collision))
        {
            return;
        }

        if (!stickOnHit)
        {
            return;
        }

        if (!IsStickLayer(collision.gameObject.layer))
        {
            return;
        }

        // Only skip the bow/hand colliders we temporarily ignore — do NOT skip
        // all world hits during that window (that made nearby solids bounce).
        if (IsTemporarilyIgnoredCollider(collision.collider))
        {
            return;
        }

        Stick(collision);
    }

    private bool IsTemporarilyIgnoredCollider(Collider other)
    {
        if (other == null || temporarilyIgnored == null || Time.time >= clearIgnoreAt)
        {
            return false;
        }

        for (int i = 0; i < temporarilyIgnored.Length; i++)
        {
            if (temporarilyIgnored[i] == other)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryHandleDragonHit(Collision collision)
    {
        if (collision == null)
        {
            return false;
        }

        DragonBoss dragon = collision.collider.GetComponentInParent<DragonBoss>();
        if (dragon == null)
        {
            return false;
        }

        return dragon.HandleArrowCollision(this);
    }

    private int StickLayerMask
    {
        get
        {
            // 0 (Nothing) = stick to everything. Restrictive prefab masks are cleared in Awake.
            return stickLayers.value == 0 ? ~0 : stickLayers.value;
        }
    }

    private bool IsStickLayer(int layer)
    {
        return ((1 << layer) & StickLayerMask) != 0;
    }

    private void Stick(Collision collision)
    {
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

        Vector3 contactPoint = body != null ? body.position : transform.position;
        Transform hitParent = null;
        if (collision != null && collision.contactCount > 0)
        {
            ContactPoint contact = collision.GetContact(0);
            contactPoint = contact.point;
            hitParent = collision.transform;
        }

        Stick(contactPoint, impactDir, hitParent);
    }

    private void Stick(Vector3 contactPoint, Vector3 impactDir, Transform hitParent)
    {
        if (stuck || !flying)
        {
            return;
        }

        if (impactDir.sqrMagnitude < 1e-6f)
        {
            impactDir = TipWorldDirection;
        }

        impactDir.Normalize();

        Quaternion impactRotation = RotationForDirection(impactDir);
        stuckRotation = impactRotation;

        flying = false;
        stuck = true;
        ClearIgnore();
        SetTrailEmitting(false, clear: false);

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

        transform.SetParent(null, true);
        transform.rotation = impactRotation;

        Vector3 impactPosition = contactPoint;
        if (arrowTip != null)
        {
            // Move whole arrow so the tip lands on the contact, then embed slightly.
            impactPosition = transform.position + (contactPoint - arrowTip.position);
            impactPosition += impactDir * 0.04f;
        }

        transform.position = impactPosition;
        body.position = impactPosition;
        body.rotation = impactRotation;

        if (!stickInWorldSpace && hitParent != null)
        {
            transform.SetParent(hitParent, true);
            transform.SetPositionAndRotation(impactPosition, impactRotation);
            body.position = impactPosition;
            body.rotation = impactRotation;
        }
    }

    private void ApplyNoBounceMaterial()
    {
        if (selfColliders == null || selfColliders.Length == 0)
        {
            return;
        }

        if (sharedNoBounceMaterial == null)
        {
            sharedNoBounceMaterial = new PhysicMaterial("ArrowNoBounce")
            {
                dynamicFriction = 0.4f,
                staticFriction = 0.4f,
                bounciness = 0f,
                frictionCombine = PhysicMaterialCombine.Average,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
        }

        for (int i = 0; i < selfColliders.Length; i++)
        {
            if (selfColliders[i] != null)
            {
                selfColliders[i].sharedMaterial = sharedNoBounceMaterial;
            }
        }
    }

    private void OnDestroy()
    {
        ClearIgnore();
        ArrowManager.Unregister(this);
    }

    private void EnsureTrail()
    {
        trail = GetComponent<TrailRenderer>();
        if (trail == null)
        {
            trail = gameObject.AddComponent<TrailRenderer>();
        }

        trail.time = trailTime;
        trail.startWidth = trailStartWidth;
        trail.endWidth = trailEndWidth;
        trail.minVertexDistance = trailMinVertexDistance;
        trail.autodestruct = false;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.numCapVertices = 2;
        trail.numCornerVertices = 2;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(trailStartColor, 0f),
                new GradientColorKey(trailEndColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(trailStartColor.a, 0f),
                new GradientAlphaKey(trailEndColor.a, 1f)
            });
        trail.colorGradient = gradient;

        if (trail.sharedMaterial == null)
        {
            trail.sharedMaterial = CreateTrailMaterial();
        }
    }

    private static Material CreateTrailMaterial()
    {
        string[] shaderNames =
        {
            "Sprites/Default",
            "Unlit/Color",
            "Universal Render Pipeline/Unlit",
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended Premultiply"
        };

        for (int i = 0; i < shaderNames.Length; i++)
        {
            Shader shader = Shader.Find(shaderNames[i]);
            if (shader != null)
            {
                Material mat = new Material(shader);
                mat.name = "ArrowFlightTrail";
                if (mat.HasProperty("_Color"))
                {
                    mat.color = Color.white;
                }

                return mat;
            }
        }

        return new Material(Shader.Find("Hidden/InternalErrorShader"));
    }

    private void SetTrailEmitting(bool emitting, bool clear)
    {
        if (!showFlightTrail && emitting)
        {
            emitting = false;
        }

        if (trail == null)
        {
            if (!emitting && !clear)
            {
                return;
            }

            EnsureTrail();
        }

        if (trail == null)
        {
            return;
        }

        trail.emitting = emitting;
        if (clear)
        {
            trail.Clear();
        }
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
