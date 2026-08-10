using System;
using UnityEngine;
using Votanic.vXR.vCast;
using Votanic.vXR.vGear;

public enum PlayEnvironmentMode
{
    Auto,
    DesktopPc,
    Cave,
    Hmd
}

/// <summary>
/// Resolves whether play runs in desktop test mode or a tracked Votanic XR environment (CAVE/HMD).
/// </summary>
[DefaultExecutionOrder(-150)]
public class PlayEnvironment : MonoBehaviour
{
    public static PlayEnvironment Instance { get; private set; }

    [Header("Play Environment")]
    [Tooltip("Auto reads vCast.environment after Votanic finishes starting (CAVE bat / ConfigCAVE). "
             + "Force Desktop PC for mouse testing. Force Cave/Hmd to skip autodetection.")]
    [SerializeField] private PlayEnvironmentMode playEnvironment = PlayEnvironmentMode.Auto;
    [Tooltip("Fallback eye height when no vision/sensor/head transform is available.")]
    [SerializeField, Min(0.5f)] private float caveFallbackEyeHeight = 1.6f;
    [SerializeField] private bool logResolvedTrackingTransform = true;
    [Tooltip("How long Auto mode keeps re-reading vCast.environment after play starts.")]
    [SerializeField, Min(0.5f)] private float autoDetectWindowSeconds = 8f;
    [SerializeField, Min(0.05f)] private float autoDetectPollInterval = 0.25f;

    private PlayEnvironmentMode resolvedMode = PlayEnvironmentMode.DesktopPc;
    private static string lastLoggedTrackingSource;
    private float autoDetectUntil;
    private float nextAutoDetectTime;
    private string lastDetectLog;

    public static event Action EnvironmentChanged;

    public PlayEnvironmentMode ConfiguredMode => playEnvironment;
    public static PlayEnvironmentMode ActiveMode =>
        Instance != null ? Instance.resolvedMode : ResolveMode(PlayEnvironmentMode.Auto);
    public static bool IsDesktopInput => ActiveMode == PlayEnvironmentMode.DesktopPc;
    public static bool IsTrackedXr =>
        ActiveMode == PlayEnvironmentMode.Cave || ActiveMode == PlayEnvironmentMode.Hmd;
    public static bool IsCaveMode => ActiveMode == PlayEnvironmentMode.Cave;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple PlayEnvironment instances found. Using the most recent one.", this);
        }

        Instance = this;
        autoDetectUntil = Time.unscaledTime + autoDetectWindowSeconds;
        LogCommandLineOnce();
        RefreshResolvedMode(forceLog: true);
    }

    private void Start()
    {
        // vCast often finishes config load after other Awakes — resolve again.
        RefreshResolvedMode(forceLog: true);
    }

    private void Update()
    {
        if (playEnvironment != PlayEnvironmentMode.Auto)
        {
            return;
        }

        if (Time.unscaledTime > autoDetectUntil)
        {
            return;
        }

        if (Time.unscaledTime < nextAutoDetectTime)
        {
            return;
        }

        nextAutoDetectTime = Time.unscaledTime + autoDetectPollInterval;
        RefreshResolvedMode(forceLog: false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void RefreshResolvedMode(bool forceLog)
    {
        ApplyResolvedMode(ResolveMode(playEnvironment), forceLog);
    }

    private static bool loggedCommandLine;

    private static void LogCommandLineOnce()
    {
        if (loggedCommandLine)
        {
            return;
        }

        loggedCommandLine = true;
        try
        {
            Debug.Log("PlayEnvironment command line: " + string.Join(" | ", Environment.GetCommandLineArgs()));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("PlayEnvironment could not read command line: " + exception.Message);
        }
    }

    /// <summary>
    /// Transform used for player tracking (physical glasses in CAVE).
    /// Prefer <c>sensor.vision</c> — <c>vCast.head</c> is the synchronizer and often follows the wand.
    /// </summary>
    public static Transform ResolvePlayerTransform()
    {
        Transform vision = ResolveVisionTransform();
        if (vision != null)
        {
            LogTrackingSourceOnce("vision/glasses", vision);
            return vision;
        }

        Transform sensor = ResolveSensorTransform();
        if (sensor != null)
        {
            LogTrackingSourceOnce("sensor", sensor);
            return sensor;
        }

        Transform head = ResolveHeadTransform(allowSynchronizerFallback: !IsCaveMode);
        if (head != null)
        {
            LogTrackingSourceOnce("head", head);
            return head;
        }

        Transform user = ResolveUserTransform();
        if (user != null)
        {
            LogTrackingSourceOnce("user", user);
            return user;
        }

        LogTrackingSourceOnce("none", null);
        return null;
    }

    /// <summary>
    /// World aim/eye position — glasses / vision pose when available.
    /// </summary>
    public static Vector3 ResolvePlayerAimPosition()
    {
        Transform tracking = ResolvePlayerTransform();
        if (tracking != null)
        {
            return tracking.position;
        }

        Transform user = ResolveUserTransform();
        if (user != null)
        {
            return user.position + Vector3.up * GetCaveFallbackEyeHeight();
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform.position : Vector3.zero;
    }

    /// <summary>
    /// Horizontal view forward for the player (CAVE front wall / glasses look).
    /// Used so threats approach from the visible side.
    /// </summary>
    public static Vector3 ResolvePlayerViewForward()
    {
        Vector3 forward = Vector3.forward;

        Transform tracking = ResolvePlayerTransform();
        if (tracking != null)
        {
            forward = tracking.forward;
        }
        else
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                forward = mainCamera.transform.forward;
            }
        }

        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    /// <summary>
    /// Glasses / eye tracking part of the Votanic sensor.
    /// </summary>
    public static Transform ResolveVisionTransform()
    {
        try
        {
            if (vGear.sensor != null && vGear.sensor.vision != null)
            {
                Transform vision = vGear.sensor.vision.transform;
                if (IsUsableTrackingTransform(vision))
                {
                    return vision;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.sensor != null && vCast.sensor.vision != null)
            {
                Transform vision = vCast.sensor.vision.transform;
                if (IsUsableTrackingTransform(vision))
                {
                    return vision;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    public static Transform ResolveSensorTransform()
    {
        try
        {
            if (vGear.sensor != null)
            {
                Transform sensor = vGear.sensor.transform;
                if (IsUsableTrackingTransform(sensor))
                {
                    return sensor;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.sensor != null)
            {
                Transform sensor = vCast.sensor.transform;
                if (IsUsableTrackingTransform(sensor))
                {
                    return sensor;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    /// <summary>
    /// Legacy head / synchronizer. In CAVE this often tracks the controller wand — prefer vision instead.
    /// </summary>
    public static Transform ResolveHeadTransform()
    {
        return ResolveHeadTransform(allowSynchronizerFallback: true);
    }

    /// <summary>
    /// CAVE right-hand / wand host: vGear → Frame → User → Head → Hand → Controller.
    /// </summary>
    public static Transform ResolveRightHandTransform()
    {
        Transform vGear = ResolveVGearTransform();
        if (vGear != null)
        {
            Transform controller = FindChildPathIgnoreCase(
                vGear, "Frame", "User", "Head", "Hand", "Controller");
            if (controller != null)
            {
                return controller;
            }

            Transform headHand = FindChildPathIgnoreCase(vGear, "Frame", "User", "Head", "Hand");
            if (headHand != null)
            {
                return headHand;
            }

            controller = FindChildPathIgnoreCase(vGear, "Frame", "User", "Hand", "Controller");
            if (controller != null)
            {
                return controller;
            }
        }

        Transform hand1 = FindSceneTransformByName("Hand1");
        if (hand1 != null)
        {
            return hand1;
        }

        try
        {
            if (vCast.hand != null)
            {
                return vCast.hand.transform;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    /// <summary>
    /// Keep the Votanic wand laser off (reapplied every frame by vCast).
    /// </summary>
    public static void SuppressWandRay()
    {
        try
        {
            if (vCast.controller != null)
            {
                vCast.controller.DisplayWandRay(false);
                vCast.controller.EnableWandRay(false);
            }
        }
        catch (Exception)
        {
        }

        Transform vGear = ResolveVGearTransform();
        if (vGear == null)
        {
            return;
        }

        Transform wand = FindChildPathIgnoreCase(
            vGear, "Frame", "User", "Head", "Hand", "Controller", "Wand");
        if (wand == null)
        {
            wand = FindChildPathIgnoreCase(vGear, "Frame", "User", "Hand", "Controller", "Wand");
        }

        if (wand == null)
        {
            wand = FindSceneTransformByName("Wand");
        }

        if (wand == null)
        {
            return;
        }

        for (int i = 0; i < wand.childCount; i++)
        {
            Transform child = wand.GetChild(i);
            if (!IsWandVisualName(child.name))
            {
                continue;
            }

            if (child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static bool IsWandVisualName(string name)
    {
        return string.Equals(name, "Beam", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Point", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Ring", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Cursor", StringComparison.OrdinalIgnoreCase);
    }

    private static Transform ResolveHeadTransform(bool allowSynchronizerFallback)
    {
        Transform vision = ResolveVisionTransform();
        if (vision != null)
        {
            return vision;
        }

        Transform sensor = ResolveSensorTransform();
        if (sensor != null)
        {
            return sensor;
        }

        if (!allowSynchronizerFallback)
        {
            Camera mainCamera = Camera.main;
            return mainCamera != null ? mainCamera.transform : null;
        }

        try
        {
            if (vGear.head != null)
            {
                Transform head = vGear.head.transform;
                if (IsUsableTrackingTransform(head) && !IsControllerTransform(head))
                {
                    return head;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.head != null)
            {
                Transform head = vCast.head.transform;
                if (IsUsableTrackingTransform(head) && !IsControllerTransform(head))
                {
                    return head;
                }
            }
        }
        catch (Exception)
        {
        }

        Camera fallbackCamera = Camera.main;
        return fallbackCamera != null ? fallbackCamera.transform : null;
    }

    public static Transform ResolveUserTransform()
    {
        try
        {
            if (vGear.user != null)
            {
                return vGear.user.transform;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.user != null)
            {
                return vCast.user.transform;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    /// <summary>
    /// Root vGear transform in the loaded scene (not the disabled Main Camera).
    /// </summary>
    public static Transform ResolveVGearTransform()
    {
        Transform byName = FindSceneTransformByName("vGear");
        if (byName != null)
        {
            return byName;
        }

        Transform user = ResolveUserTransform();
        if (user != null)
        {
            Transform current = user;
            while (current != null)
            {
                if (current.name == "vGear")
                {
                    return current;
                }

                current = current.parent;
            }

            return user;
        }

        return FindSceneTransformByName("vCast");
    }

    /// <summary>
    /// Desktop bow hold parent: prefer the live view camera, then Head.
    /// </summary>
    public static Transform ResolveDesktopBowParent()
    {
        Camera cam = ResolveViewCamera();
        if (cam != null)
        {
            return cam.transform;
        }

        try
        {
            if (vGear.head != null)
            {
                Transform head = vGear.head.transform;
                if (head != null)
                {
                    return head;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.head != null)
            {
                Transform head = vCast.head.transform;
                if (head != null)
                {
                    return head;
                }
            }
        }
        catch (Exception)
        {
        }

        Transform vGearRoot = ResolveVGearTransform();
        if (vGearRoot != null)
        {
            Transform byPath = FindChildPathIgnoreCase(vGearRoot, "Frame", "User", "Head");
            if (byPath != null)
            {
                return byPath;
            }

            Transform headOnly = FindChildRecursiveIgnoreCase(vGearRoot, "Head");
            if (headOnly != null)
            {
                return headOnly;
            }
        }

        return FindSceneTransformByName("Head");
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
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
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
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
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

    private static Transform FindSceneTransformByName(string targetName)
    {
#if UNITY_2023_1_OR_NEWER
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
#endif
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.name != targetName)
            {
                continue;
            }

            if (!candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    public static Transform ResolveControllerTransform()
    {
        try
        {
            if (vGear.controller != null)
            {
                return vGear.controller.transform;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.controller != null)
            {
                return vCast.controller.transform;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    public static Camera ResolveViewCamera()
    {
        Transform vision = ResolveVisionTransform();
        Camera fromVision = FindUsableCameraOn(vision);
        if (fromVision != null)
        {
            return fromVision;
        }

        Transform headTransform = ResolveHeadTransform();
        Camera fromHead = FindUsableCameraOn(headTransform);
        if (fromHead != null)
        {
            return fromHead;
        }

        Transform user = ResolveUserTransform();
        Camera fromUser = FindUsableCameraOn(user);
        if (fromUser != null)
        {
            return fromUser;
        }

        if (IsUsableCamera(Camera.main))
        {
            return Camera.main;
        }

        return FindBestActiveCamera();
    }

    /// <summary>
    /// True when the camera can actually render (component on, hierarchy active).
    /// Skips the disabled scene Main Camera under VotanicXR.
    /// </summary>
    public static bool IsUsableCamera(Camera camera)
    {
        return camera != null
            && camera.enabled
            && camera.gameObject.activeInHierarchy;
    }

    private static Camera FindUsableCameraOn(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        Camera onRoot = root.GetComponent<Camera>();
        if (IsUsableCamera(onRoot))
        {
            return onRoot;
        }

        Camera[] children = root.GetComponentsInChildren<Camera>(true);
        Camera best = null;
        for (int i = 0; i < children.Length; i++)
        {
            Camera candidate = children[i];
            if (!IsUsableCamera(candidate))
            {
                continue;
            }

            if (best == null || candidate.depth > best.depth)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static Camera FindBestActiveCamera()
    {
#if UNITY_2023_1_OR_NEWER
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
#endif
        Camera best = null;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (!IsUsableCamera(candidate))
            {
                continue;
            }

            if (best == null || candidate.depth > best.depth)
            {
                best = candidate;
            }
        }

        return best;
    }

    public static string GetRestartPrompt(string desktopPrompt, string trackedPrompt)
    {
        return IsDesktopInput ? desktopPrompt : trackedPrompt;
    }

    private static bool IsControllerTransform(Transform candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        Transform controller = ResolveControllerTransform();
        if (controller == null)
        {
            return false;
        }

        return candidate == controller
            || candidate.IsChildOf(controller)
            || controller.IsChildOf(candidate);
    }

    private static bool IsUsableTrackingTransform(Transform candidate)
    {
        return candidate != null && candidate.gameObject.activeInHierarchy;
    }

    private static void LogTrackingSourceOnce(string source, Transform transform)
    {
        if (Instance == null || !Instance.logResolvedTrackingTransform)
        {
            return;
        }

        string key = source + ":" + (transform != null ? transform.name : "null");
        if (lastLoggedTrackingSource == key)
        {
            return;
        }

        lastLoggedTrackingSource = key;
        Debug.Log(
            "Tracking source: " + source
            + (transform != null ? " ('" + GetHierarchyPath(transform) + "')" : " (null)"),
            Instance);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        string path = transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private static float GetCaveFallbackEyeHeight()
    {
        return Instance != null ? Mathf.Max(0.5f, Instance.caveFallbackEyeHeight) : 1.6f;
    }

    private static PlayEnvironmentMode ResolveMode(PlayEnvironmentMode mode)
    {
        if (mode != PlayEnvironmentMode.Auto)
        {
            return mode;
        }

        // 1) Command line / bat hints (Votanic CAVE bat often includes "CAVE" or ConfigCAVE).
        PlayEnvironmentMode fromArgs = ResolveModeFromCommandLine();
        if (fromArgs != PlayEnvironmentMode.Auto)
        {
            return fromArgs;
        }

        // 2) Live Votanic environment (may be PC until config finishes loading).
        try
        {
            switch (vCast.environment)
            {
                case Votanic.vXR.vCast.Core.SystemType.CAVE:
                    return PlayEnvironmentMode.Cave;
                case Votanic.vXR.vCast.Core.SystemType.HMD:
                    return PlayEnvironmentMode.Hmd;
                case Votanic.vXR.vCast.Core.SystemType.PC:
                    return PlayEnvironmentMode.DesktopPc;
                default:
                    return PlayEnvironmentMode.DesktopPc;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Could not read vCast.environment; defaulting to desktop PC mode. "
                + exception.Message);
            return PlayEnvironmentMode.DesktopPc;
        }
    }

    private static PlayEnvironmentMode ResolveModeFromCommandLine()
    {
        string[] args;
        try
        {
            args = Environment.GetCommandLineArgs();
        }
        catch
        {
            return PlayEnvironmentMode.Auto;
        }

        if (args == null)
        {
            return PlayEnvironmentMode.Auto;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.IsNullOrEmpty(arg))
            {
                continue;
            }

            string upper = arg.ToUpperInvariant();
            if (upper.Contains("CONFIGCAVE") || upper.Contains("\\CAVE") || upper.Contains("/CAVE")
                || upper == "CAVE" || upper.EndsWith("_CAVE") || upper.Contains(".CAVE"))
            {
                return PlayEnvironmentMode.Cave;
            }

            if (upper.Contains("CONFIGHMD") || upper == "HMD" || upper.EndsWith("_HMD"))
            {
                return PlayEnvironmentMode.Hmd;
            }

            if (upper.Contains("CONFIGPC") || upper.EndsWith("PC.VXRC") || upper.EndsWith("_PC") || upper == "PC")
            {
                return PlayEnvironmentMode.DesktopPc;
            }
        }

        // Joined command line fallback (bat wrappers sometimes pack tokens oddly).
        string joined = string.Join(" ", args).ToUpperInvariant();
        if (joined.Contains("CONFIGCAVE") || joined.Contains("BATCHTYPE.CAVE") || joined.Contains(" TYPE=CAVE"))
        {
            return PlayEnvironmentMode.Cave;
        }

        if (joined.Contains("CONFIGHMD"))
        {
            return PlayEnvironmentMode.Hmd;
        }

        return PlayEnvironmentMode.Auto;
    }

    private void ApplyResolvedMode(PlayEnvironmentMode mode, bool forceLog = false)
    {
        PlayEnvironmentMode previousMode = resolvedMode;
        resolvedMode = mode;

        string votanicEnv = "?";
        try
        {
            votanicEnv = vCast.environment.ToString();
        }
        catch (Exception exception)
        {
            votanicEnv = "error:" + exception.Message;
        }

        string log = "Play environment: " + resolvedMode
                     + " (configured as " + playEnvironment
                     + ", vCast.environment=" + votanicEnv + ").";

        if (previousMode != resolvedMode)
        {
            EnvironmentChanged?.Invoke();
            Debug.Log(log + " [changed from " + previousMode + "]", this);
            lastDetectLog = log;
        }
        else if (forceLog || lastDetectLog != log)
        {
            Debug.Log(log, this);
            lastDetectLog = log;
        }

        if (playEnvironment == PlayEnvironmentMode.DesktopPc
            && votanicEnv.IndexOf("CAVE", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Debug.LogWarning(
                "PlayEnvironment is forced to DesktopPc while vCast reports CAVE. "
                + "Set Play Environment to Auto or Cave so left-hand follow / controller input turn on.",
                this);
        }
    }
}
