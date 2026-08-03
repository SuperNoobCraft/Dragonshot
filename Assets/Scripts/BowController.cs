using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// Desktop PC: bow follows the view camera every frame (camera is never modified).
/// Hold RMB to draw, release to shoot along camera forward.
/// Tracked XR: bow on left hand, hold any axis (trigger) to draw, release to shoot.
/// </summary>
[DefaultExecutionOrder(100)]
public class BowController : MonoBehaviour
{
    [Header("Arrow")]
    [SerializeField] private ArrowProjectile arrowPrefab;
    [SerializeField] private Transform arrowRest;
    [Tooltip("Bow model string control (moves the string curve). Follows right hand / arrow rear while drawing.")]
    [SerializeField] private Transform bowString;

    [Header("Shot")]
    [SerializeField] private float minSpeed = 2f;
    [SerializeField] private float maxSpeed = 40f;
    [Tooltip("Tracked: release below this draw cancels instead of firing. Keep low so a tiny pull still flop-fires.")]
    [SerializeField, Range(0f, 1f)] private float minDrawToShoot = 0.02f;

    [Header("Desktop")]
    [Tooltip("Leave empty to use PlayEnvironment.ResolveViewCamera(). Assign if the wrong camera is picked.")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Vector3 holdOffset = new Vector3(0.25f, -0.2f, 0.55f);
    [SerializeField] private Vector3 holdEuler;
    [SerializeField] private float fullDrawTime = 0.75f;
    [SerializeField] private float pullDistance = 0.35f;

    [Header("Tracked XR")]
    [SerializeField] private LeftHandChild leftHandChild;
    [Tooltip("Primary right-hand name. Also tries Head/Hand path under vGear.")]
    [SerializeField] private string rightHandName = "Hand1";
    [Tooltip("Start draw when right hand is within this distance of the bow / left hand (meters). Generous on purpose.")]
    [SerializeField] private float nockStartDistance = 0.5f;
    [Tooltip("Hand separation at full draw (meters). Power scales from nock → this.")]
    [SerializeField] private float maxDrawDistance = 1.0f;
    [SerializeField, Range(0.01f, 0.5f)] private float axisDeadzone = 0.08f;
    [SerializeField] private int maxControllersToScan = 4;
    [SerializeField] private int maxAxesToScan = 16;
    [SerializeField] private bool logInputDetection = true;
    [Tooltip("Visual only: keep idle arrow parented to the right hand when found.")]
    [SerializeField] private bool idleArrowOnRightHand = true;
    [SerializeField] private Vector3 rightHandArrowLocalPosition;
    [Tooltip("Idle arrow on right hand. (0,-90,0) matches imported tip-along-+X meshes.")]
    [SerializeField] private Vector3 rightHandArrowLocalEuler = new Vector3(0f, -90f, 0f);

    private enum State { Idle, Drawing }

    private State state;
    private float draw;
    private bool desktop;
    private ArrowProjectile arrow;
    private Transform rightHand;
    private float nextHandSearch;
    private Collider[] bowColliders;
    private bool wasInputHeld;
    private float nextNockDebugTime;
    private Vector3 bowStringRestLocalPos;
    private bool hasBowStringRest;

    private void Awake()
    {
        if (arrowRest == null)
        {
            arrowRest = transform;
        }

        if (leftHandChild == null)
        {
            leftHandChild = GetComponent<LeftHandChild>();
        }

        bowColliders = GetComponentsInChildren<Collider>(true);
        CacheBowStringRest();
        SpawnArrow();
    }

    private void CacheBowStringRest()
    {
        if (bowString == null)
        {
            hasBowStringRest = false;
            return;
        }

        bowStringRestLocalPos = bowString.localPosition;
        hasBowStringRest = true;
    }

    private void OnEnable()
    {
        PlayEnvironment.EnvironmentChanged += RefreshMode;
        RefreshMode();
    }

    private void OnDisable()
    {
        PlayEnvironment.EnvironmentChanged -= RefreshMode;
        CancelDraw();
    }

    private void Update()
    {
        if (desktop)
        {
            if (state == State.Idle && Input.GetMouseButtonDown(1))
            {
                BeginDraw();
            }
            else if (state == State.Drawing && Input.GetMouseButtonUp(1))
            {
                Release();
            }
            else if (state == State.Drawing && Input.GetMouseButton(1))
            {
                draw = Mathf.Clamp01(draw + Time.deltaTime / fullDrawTime);
            }
        }
        else
        {
            TrackedInput();
        }
    }

    private void LateUpdate()
    {
        if (!desktop)
        {
            if (leftHandChild != null && leftHandChild.isActiveAndEnabled)
            {
                leftHandChild.FollowBoundHand();
            }

            FindRightHand();

            if (state == State.Idle)
            {
                UpdateIdleArrowVisual();
                ResetBowString();
            }
            else if (state == State.Drawing)
            {
                TrackedDrawVisual();
            }

            return;
        }

        FollowCamera();

        if (state == State.Drawing && arrow != null)
        {
            arrow.PullBack(draw, pullDistance);
        }
    }

    private void RefreshMode()
    {
        CancelDraw();
        desktop = PlayEnvironment.IsDesktopInput;

        if (leftHandChild != null)
        {
            leftHandChild.enabled = !desktop;
        }

        if (desktop)
        {
            transform.SetParent(null, true);
            FollowCamera();
        }
        else if (leftHandChild != null)
        {
            leftHandChild.FollowBoundHand();
        }

        SpawnArrow();

        if (!desktop)
        {
            Debug.Log(
                "BowController: CAVE mode — hands within "
                + nockStartDistance.ToString("0.00")
                + "m + trigger to nock, then pull for power.",
                this);
        }
    }

    private Camera GetViewCamera()
    {
        if (viewCamera != null && viewCamera.isActiveAndEnabled)
        {
            return viewCamera;
        }

        return PlayEnvironment.ResolveViewCamera();
    }

    private void FollowCamera()
    {
        Camera cam = GetViewCamera();
        if (cam == null)
        {
            return;
        }

        Transform t = cam.transform;
        transform.SetPositionAndRotation(
            t.TransformPoint(holdOffset),
            t.rotation * Quaternion.Euler(holdEuler));
    }

    private void BeginDraw()
    {
        SpawnArrow();
        if (arrow == null)
        {
            return;
        }

        state = State.Drawing;
        draw = 0f;

        if (desktop)
        {
            arrow.Nock(arrowRest);
            arrow.PullBack(0f, pullDistance);
        }
        else
        {
            // Keep arrow free so LateUpdate can place nock on the right hand.
            arrow.transform.SetParent(null, true);
        }
    }

    private void Release()
    {
        if (state != State.Drawing)
        {
            return;
        }

        float power = draw;
        Vector3 dir = ShotDirection();
        state = State.Idle;
        draw = 0f;

        if (arrow == null)
        {
            return;
        }

        if (power < minDrawToShoot)
        {
            if (logInputDetection)
            {
                Debug.Log($"Bow CAVE: release ignored (draw={power:0.00}).", this);
            }

            ResetBowString();
            UpdateIdleArrowVisual();
            return;
        }

        float speed = Mathf.Lerp(minSpeed, maxSpeed, power);
        ArrowProjectile shot = arrow;
        arrow = null;
        if (logInputDetection)
        {
            Debug.Log($"Bow CAVE: shot draw={power:0.00} speed={speed:0.0} dir={dir}.", this);
        }

        shot.Fire(dir, speed, bowColliders);
        ResetBowString();
        SpawnArrow();
        UpdateIdleArrowVisual();
    }

    private void CancelDraw()
    {
        state = State.Idle;
        draw = 0f;
        ResetBowString();
        if (arrow == null)
        {
            return;
        }

        if (desktop)
        {
            arrow.Nock(arrowRest);
        }
        else
        {
            UpdateIdleArrowVisual();
        }
    }

    private Vector3 ShotDirection()
    {
        if (desktop)
        {
            Camera cam = GetViewCamera();
            return cam != null ? cam.transform.forward : transform.forward;
        }

        Vector3 restPos = GetArrowRestPosition();
        if (rightHand != null)
        {
            Vector3 d = restPos - rightHand.position;
            if (d.sqrMagnitude > 0.0001f)
            {
                return d.normalized;
            }
        }

        return transform.forward;
    }

    private Vector3 GetArrowRestPosition()
    {
        if (arrowRest != null)
        {
            return arrowRest.position;
        }

        return transform.position;
    }

    private void TrackedInput()
    {
        FindRightHand();

        bool holding = IsAnyAxisHeld(out string source);
        float handDist = GetHandsDistance();

        if (holding != wasInputHeld)
        {
            wasInputHeld = holding;
            if (logInputDetection)
            {
                Debug.Log(holding ? $"Bow CAVE: axis held ({source})" : "Bow CAVE: axis released.", this);
            }
        }

        if (state == State.Idle)
        {
            bool handsClose = rightHand != null && handDist <= nockStartDistance;
            if (holding && logInputDetection && Time.unscaledTime >= nextNockDebugTime)
            {
                nextNockDebugTime = Time.unscaledTime + 0.5f;
                Debug.Log(
                    $"Bow CAVE: waiting to nock — handsClose={handsClose} "
                    + $"dist={handDist:0.00} need<={nockStartDistance:0.00} "
                    + $"rightHand={(rightHand != null ? rightHand.name : "null")}",
                    this);
            }

            if (holding && handsClose)
            {
                BeginDraw();
            }
        }
        else if (state == State.Drawing)
        {
            if (!holding)
            {
                Release();
                return;
            }

            // Pull power: 0 at nock range, 1 when rear→rest ≈ arrow shaft length (tip at rest).
            float fullPull = maxDrawDistance;
            if (arrow != null)
            {
                fullPull = Mathf.Max(maxDrawDistance, arrow.ShaftLength);
            }

            float span = Mathf.Max(0.01f, fullPull - nockStartDistance);
            draw = Mathf.Clamp01((handDist - nockStartDistance) / span);
        }
    }

    /// <summary>
    /// Gap between right-hand tracker and bow / left hand.
    /// Do NOT include the arrow — when the arrow is parented to the right hand that
    /// distance is ~0 and breaks both nock gating and draw power.
    /// </summary>
    private float GetHandsDistance()
    {
        if (rightHand == null)
        {
            return float.MaxValue;
        }

        float best = float.MaxValue;
        best = MinDist(best, rightHand.position, transform.position);
        best = MinDist(best, rightHand.position, GetArrowRestPosition());

        if (leftHandChild != null && leftHandChild.BoundHand != null)
        {
            best = MinDist(best, rightHand.position, leftHandChild.BoundHand.position);
        }

        return best;
    }

    private static float MinDist(float current, Vector3 a, Vector3 b)
    {
        return Mathf.Min(current, Vector3.Distance(a, b));
    }

    private void TrackedDrawVisual()
    {
        if (arrow == null || rightHand == null)
        {
            return;
        }

        // ArrowRear on right hand, tip aimed at the bow rest.
        Vector3 restPos = GetArrowRestPosition();
        Vector3 rearPos = rightHand.position;
        Vector3 toRest = restPos - rearPos;
        Vector3 dir = toRest.sqrMagnitude > 0.0001f ? toRest.normalized : transform.forward;

        arrow.PlaceRearAt(rearPos, dir);

        // String control point = nock (arrow rear / right hand).
        UpdateBowString(arrow.Rear != null ? arrow.Rear.position : rearPos);
    }

    private void UpdateBowString(Vector3 worldNockPosition)
    {
        if (bowString == null)
        {
            return;
        }

        bowString.position = worldNockPosition;
    }

    private void ResetBowString()
    {
        if (bowString == null || !hasBowStringRest)
        {
            return;
        }

        bowString.localPosition = bowStringRestLocalPos;
    }

    /// <summary>
    /// Idle: shaft centered on right hand, tip pointing hand-forward.
    /// </summary>
    private void UpdateIdleArrowVisual()
    {
        if (arrow == null || !idleArrowOnRightHand || rightHand == null)
        {
            return;
        }

        Vector3 holdPos = rightHand.TransformPoint(rightHandArrowLocalPosition);
        arrow.PlaceCenterAt(holdPos, rightHand.forward);
    }

    private void SpawnArrow()
    {
        if (arrow != null || arrowPrefab == null || arrowRest == null)
        {
            return;
        }

        arrow = Instantiate(arrowPrefab, arrowRest);
        arrow.Nock(arrowRest);
    }

    private void FindRightHand()
    {
        if (rightHand != null)
        {
            return;
        }

        if (Time.unscaledTime < nextHandSearch)
        {
            return;
        }

        nextHandSearch = Time.unscaledTime + 0.25f;
        rightHand = ResolveRightHandTransform();
        if (rightHand != null && logInputDetection)
        {
            Debug.Log("BowController: right hand bound to '" + GetPath(rightHand) + "'.", this);
        }
    }

    private Transform ResolveRightHandTransform()
    {
        Transform byName = FindByName(rightHandName);
        if (byName != null)
        {
            return byName;
        }

        byName = FindByName("Hand1") ?? FindByName("hand1") ?? FindByName("Hand");
        if (byName != null)
        {
            return byName;
        }

        // Playtime hierarchy: vGear/Frame/User/Head/Hand
        Transform vGear = PlayEnvironment.ResolveVGearTransform();
        if (vGear != null)
        {
            Transform byPath = FindChildPathIgnoreCase(vGear, "Frame", "User", "Head", "Hand");
            if (byPath != null)
            {
                return byPath;
            }

            Transform head = FindChildRecursiveIgnoreCase(vGear, "Head");
            if (head != null)
            {
                Transform handUnderHead = FindChildRecursiveIgnoreCase(head, "Hand");
                if (handUnderHead != null)
                {
                    return handUnderHead;
                }
            }
        }

        try
        {
            if (vCast.hand != null)
            {
                return vCast.hand.transform;
            }
        }
        catch
        {
        }

        return null;
    }

    private bool IsAnyAxisHeld(out string source)
    {
        source = null;
        int controllers = Mathf.Max(1, maxControllersToScan);

        for (int c = 0; c < controllers; c++)
        {
            int axisCount = maxAxesToScan;
            try
            {
                int reported = vCast.Ctrl.NumberOfAxis(c);
                if (reported > 0)
                {
                    axisCount = Mathf.Max(reported, maxAxesToScan);
                }
            }
            catch
            {
            }

            for (int a = 0; a < axisCount; a++)
            {
                float value = 0f;
                try
                {
                    value = vCast.Ctrl.AxisValue(a, c);
                }
                catch
                {
                    continue;
                }

                if (Mathf.Abs(value) > axisDeadzone)
                {
                    source = $"ctrl={c} axis={a} value={value:0.000}";
                    return true;
                }
            }
        }

        return false;
    }

    private static Transform FindByName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

#if UNITY_2023_1_OR_NEWER
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && t.name == name && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded)
            {
                return t;
            }
        }

        return null;
    }

    private static Transform FindChildPathIgnoreCase(Transform root, params string[] path)
    {
        Transform current = root;
        for (int i = 0; i < path.Length; i++)
        {
            if (current == null)
            {
                return null;
            }

            current = FindDirectChildIgnoreCase(current, path[i]);
        }

        return current;
    }

    private static Transform FindDirectChildIgnoreCase(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static Transform FindChildRecursiveIgnoreCase(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform nested = FindChildRecursiveIgnoreCase(child, childName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }

        return path;
    }
}
