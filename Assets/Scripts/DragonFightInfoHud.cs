using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

/// <summary>
/// Runtime HUD parented under vGear → Frame → Info → InfoUI (Votanic creates InfoUI at play).
/// Primary copy: top-left. Optional front copy: same parent, shifted to the left edge of the
/// right-panel canvas, rotated on Y so it faces the front wall in a multi-wall CAVE.
/// </summary>
public class DragonFightInfoHud : MonoBehaviour
{
    [Header("Find")]
    [SerializeField] private string infoUiObjectName = "InfoUI";
    [SerializeField] private string hudObjectName = "DragonFightHud";
    [SerializeField] private bool logDebug = true;

    [Header("Primary Layout (top-left)")]
    [Tooltip("Off = big-cave export: hide the original top-left HUD and use the front copy only.")]
    [SerializeField] private bool enablePrimaryCopy = true;
    [SerializeField] private Vector2 anchoredPosition = new Vector2(24f, -24f);
    [SerializeField] private Vector2 sizeDelta = new Vector2(480f, 160f);
    [SerializeField] private int fontSize = 36;
    [SerializeField] private Color textColor = Color.white;

    [Header("Front Copy (same InfoUI / right panel)")]
    [Tooltip("Duplicate HUD shifted to the left edge of the right panel (front screen in big CAVE).")]
    [SerializeField] private bool enableFrontCopy = true;
    [SerializeField] private string frontHudObjectName = "DragonFightHudFront";
    [Tooltip("Top-left anchor on the right-panel canvas — left edge ≈ front wall.")]
    [SerializeField] private Vector2 frontAnchoredPosition = new Vector2(12f, -24f);
    [Tooltip("Depth offset (local Z). Negative pulls the copy toward the front wall.")]
    [SerializeField] private float frontAnchoredPositionZ = -48f;
    [Tooltip("Local euler angles. Y ≈ 90° swings the copy off the right panel toward the front wall "
             + "(not Z — that only spins text vertically on the same panel).")]
    [SerializeField] private Vector3 frontLocalEulerAngles = new Vector3(0f, 90f, 0f);
    [SerializeField, Min(0.1f)] private float frontSizeMultiplier = 2f;

    private RectTransform hudRoot;
    private Text hudText;
    private RectTransform frontHudRoot;
    private Text frontHudText;
    private bool visible;
    private string pendingText = string.Empty;
    private float nextFindAttempt;
    private float nextMissingLog;
    private bool loggedCreated;
    private Transform cachedInfoUi;

    public void ShowText(string text)
    {
        pendingText = text ?? string.Empty;
        visible = true;
        Apply();
    }

    public void Hide()
    {
        visible = false;
        pendingText = string.Empty;
        if (hudRoot != null)
        {
            hudRoot.gameObject.SetActive(false);
        }

        if (frontHudRoot != null)
        {
            frontHudRoot.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        nextFindAttempt = 0f;
        TryEnsureHudObjects();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextFindAttempt)
        {
            nextFindAttempt = Time.unscaledTime + 0.25f;
            TryEnsureHudObjects();
        }

        if (!visible)
        {
            return;
        }

        if (enablePrimaryCopy)
        {
            SyncText(hudRoot, hudText);
        }
        else if (hudRoot != null)
        {
            hudRoot.gameObject.SetActive(false);
        }

        if (enableFrontCopy)
        {
            SyncText(frontHudRoot, frontHudText);
        }
        else if (frontHudRoot != null)
        {
            frontHudRoot.gameObject.SetActive(false);
        }
    }

    private void Apply()
    {
        TryEnsureHudObjects();
        if (enablePrimaryCopy)
        {
            SetHudActive(hudRoot, hudText);
        }
        else if (hudRoot != null)
        {
            hudRoot.gameObject.SetActive(false);
        }

        if (enableFrontCopy)
        {
            SetHudActive(frontHudRoot, frontHudText);
        }
        else if (frontHudRoot != null)
        {
            frontHudRoot.gameObject.SetActive(false);
        }
    }

    private void SyncText(RectTransform root, Text text)
    {
        if (root == null || text == null)
        {
            return;
        }

        if (!root.gameObject.activeSelf)
        {
            root.gameObject.SetActive(true);
        }

        if (text.text != pendingText)
        {
            text.text = pendingText;
        }
    }

    private void SetHudActive(RectTransform root, Text text)
    {
        if (root == null || text == null)
        {
            return;
        }

        root.gameObject.SetActive(visible);
        if (visible)
        {
            text.text = pendingText;
        }
    }

    private void TryEnsureHudObjects()
    {
        bool primaryReady = !enablePrimaryCopy
            || (hudRoot != null && hudText != null && hudRoot.parent != null);
        bool frontReady = !enableFrontCopy
            || (frontHudRoot != null && frontHudText != null && frontHudRoot.parent != null);
        if (primaryReady && frontReady)
        {
            return;
        }

        if (!primaryReady && enablePrimaryCopy)
        {
            hudRoot = null;
            hudText = null;
        }

        if (!frontReady && enableFrontCopy)
        {
            frontHudRoot = null;
            frontHudText = null;
        }

        if (!enablePrimaryCopy && !enableFrontCopy)
        {
            return;
        }

        cachedInfoUi = null;

        Transform infoUi = ResolveInfoUi();
        if (infoUi == null)
        {
            if (logDebug && Time.unscaledTime >= nextMissingLog)
            {
                nextMissingLog = Time.unscaledTime + 3f;
                Debug.LogWarning(
                    "DragonFightInfoHud: waiting for '" + infoUiObjectName
                    + "'. Check Console after play — will keep retrying.",
                    this);
            }

            return;
        }

        cachedInfoUi = infoUi;

        if (enablePrimaryCopy && !primaryReady)
        {
            EnsureHudInstance(
                infoUi,
                hudObjectName,
                primaryLayout: true,
                out hudRoot,
                out hudText);
        }

        if (enableFrontCopy && !frontReady)
        {
            EnsureHudInstance(
                infoUi,
                frontHudObjectName,
                primaryLayout: false,
                out frontHudRoot,
                out frontHudText);
        }
    }

    private void EnsureHudInstance(
        Transform infoUi,
        string objectName,
        bool primaryLayout,
        out RectTransform root,
        out Text text)
    {
        root = null;
        text = null;

        Transform existing = infoUi.Find(objectName);
        if (existing != null)
        {
            root = existing as RectTransform;
            if (root == null)
            {
                Destroy(existing.gameObject);
            }
            else
            {
                text = existing.GetComponent<Text>();
                if (text == null)
                {
                    if (existing.GetComponent<CanvasRenderer>() == null)
                    {
                        existing.gameObject.AddComponent<CanvasRenderer>();
                    }

                    text = existing.gameObject.AddComponent<Text>();
                    StyleText(text, infoUi, primaryLayout);
                }

                ApplyLayout(root, primaryLayout);
                root.gameObject.SetActive(visible);
                LogCreated(infoUi, objectName, rebound: true);
                return;
            }
        }

        try
        {
            CreateHudUnder(infoUi, objectName, primaryLayout, out root, out text);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("DragonFightInfoHud: create failed — " + ex, this);
        }
    }

    private void CreateHudUnder(
        Transform infoUi,
        string objectName,
        bool primaryLayout,
        out RectTransform root,
        out Text text)
    {
        EnsureCanvas(infoUi);

        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = infoUi.gameObject.layer;
        go.transform.SetParent(infoUi, false);

        root = go.GetComponent<RectTransform>();
        ApplyLayout(root, primaryLayout);

        text = go.AddComponent<Text>();
        StyleText(text, infoUi, primaryLayout);
        text.text = string.IsNullOrEmpty(pendingText) ? "TIME --\nHP --/--" : pendingText;
        go.SetActive(visible);

        LogCreated(infoUi, objectName, rebound: false);
    }

    private static void EnsureCanvas(Transform infoUi)
    {
        Canvas canvas = infoUi.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = infoUi.GetComponentInParent<Canvas>();
        }

        if (canvas != null)
        {
            return;
        }

        canvas = infoUi.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        if (infoUi.GetComponent<CanvasScaler>() == null)
        {
            infoUi.gameObject.AddComponent<CanvasScaler>();
        }

        if (infoUi.GetComponent<GraphicRaycaster>() == null)
        {
            infoUi.gameObject.AddComponent<GraphicRaycaster>();
        }
    }

    private void ApplyLayout(RectTransform rt, bool primaryLayout)
    {
        if (primaryLayout)
        {
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
        }
        else
        {
            rt.localScale = Vector3.one * frontSizeMultiplier;
            rt.localRotation = Quaternion.Euler(frontLocalEulerAngles);
            // Left edge of the right-panel canvas ≈ front wall in a wide CAVE.
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition3D = new Vector3(
                frontAnchoredPosition.x,
                frontAnchoredPosition.y,
                frontAnchoredPositionZ);
            rt.sizeDelta = sizeDelta;
        }

        rt.SetAsLastSibling();
    }

    private void StyleText(Text text, Transform infoUi, bool primaryLayout)
    {
        Font font = null;
        int size = fontSize;

        if (infoUi != null)
        {
            Text[] texts = infoUi.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i] != text && texts[i].font != null)
                {
                    font = texts[i].font;
                    if (size <= 0)
                    {
                        size = texts[i].fontSize;
                    }

                    break;
                }
            }
        }

        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        text.font = font;
        text.fontSize = Mathf.Max(18, size);
        text.color = textColor;
        text.alignment = primaryLayout ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = false;
    }

    private Transform ResolveInfoUi()
    {
        if (cachedInfoUi != null)
        {
            return cachedInfoUi;
        }

        // A) vGear.uiScreen (Votanic Info screen host) — reflect so SDK version diffs don't break compile.
        try
        {
            Transform uiRoot = ResolveVGearUiScreenTransform();
            if (uiRoot != null)
            {
                Transform underUi = FindChildRecursiveIgnoreCase(uiRoot, infoUiObjectName);
                if (underUi != null)
                {
                    return underUi;
                }

                if (string.Equals(uiRoot.name, infoUiObjectName, System.StringComparison.OrdinalIgnoreCase)
                    || uiRoot.GetComponent<Canvas>() != null)
                {
                    return uiRoot;
                }

                Transform info = FindChildRecursiveIgnoreCase(uiRoot, "Info");
                if (info != null)
                {
                    Transform nested = FindChildRecursiveIgnoreCase(info, infoUiObjectName);
                    if (nested != null)
                    {
                        return nested;
                    }

                    if (info.GetComponent<Canvas>() != null)
                    {
                        return info;
                    }
                }
            }
        }
        catch (System.Exception)
        {
        }

        // B) Hierarchy path vGear/Frame/Info/InfoUI
        Transform vGearRoot = PlayEnvironment.ResolveVGearTransform();
        if (vGearRoot != null)
        {
            Transform byPath = FindChildPathIgnoreCase(vGearRoot, "Frame", "Info", infoUiObjectName);
            if (byPath != null)
            {
                return byPath;
            }

            Transform recursive = FindChildRecursiveIgnoreCase(vGearRoot, infoUiObjectName);
            if (recursive != null)
            {
                return recursive;
            }

            Transform info = FindChildPathIgnoreCase(vGearRoot, "Frame", "Info");
            if (info == null)
            {
                info = FindChildRecursiveIgnoreCase(vGearRoot, "Info");
            }

            if (info != null)
            {
                Transform nested = FindChildRecursiveIgnoreCase(info, infoUiObjectName);
                if (nested != null)
                {
                    return nested;
                }

                if (info.GetComponent<Canvas>() != null)
                {
                    return info;
                }
            }
        }

        // C) Active GameObject.Find
        GameObject found = GameObject.Find(infoUiObjectName);
        if (found != null)
        {
            return found.transform;
        }

        found = GameObject.Find("vGear/Frame/Info/" + infoUiObjectName);
        if (found != null)
        {
            return found.transform;
        }

        // D) Any loaded-scene InfoUI (prefer live Canvas with children)
        return FindBestInfoUiInScenes();
    }

    private static Transform ResolveVGearUiScreenTransform()
    {
        // Prefer the public static vGear.uiScreen when present.
        System.Type vGearType = System.Type.GetType("Votanic.vXR.vGear.vGear, Votanic.vXR");
        if (vGearType == null)
        {
            foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                vGearType = assembly.GetType("Votanic.vXR.vGear.vGear");
                if (vGearType != null)
                {
                    break;
                }
            }
        }

        if (vGearType == null)
        {
            return null;
        }

        PropertyInfo prop = vGearType.GetProperty(
            "uiScreen",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        object screen = prop != null ? prop.GetValue(null, null) : null;
        if (screen == null)
        {
            FieldInfo field = vGearType.GetField(
                "uiScreen",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            screen = field != null ? field.GetValue(null) : null;
        }

        if (screen == null)
        {
            return null;
        }

        Component asComponent = screen as Component;
        if (asComponent != null)
        {
            return asComponent.transform;
        }

        GameObject asGo = screen as GameObject;
        if (asGo != null)
        {
            return asGo.transform;
        }

        // Fallback: duck-typed .transform property.
        PropertyInfo transformProp = screen.GetType().GetProperty("transform");
        if (transformProp != null)
        {
            return transformProp.GetValue(screen, null) as Transform;
        }

        return null;
    }

    private Transform FindBestInfoUiInScenes()
    {
#if UNITY_2023_1_OR_NEWER
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
#endif
        Transform best = null;
        int bestScore = -1;
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.name != infoUiObjectName)
            {
                continue;
            }

            if (!t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded)
            {
                continue;
            }

            int score = 0;
            if (t.GetComponent<Canvas>() != null)
            {
                score += 5;
            }

            if (t.GetComponent<RectTransform>() != null)
            {
                score += 2;
            }

            if (t.gameObject.activeInHierarchy)
            {
                score += 3;
            }

            score += Mathf.Min(t.childCount, 5);
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    private void LogCreated(Transform infoUi, string objectName, bool rebound)
    {
        if (loggedCreated || !logDebug)
        {
            return;
        }

        loggedCreated = true;
        Debug.Log(
            "DragonFightInfoHud: "
            + (rebound ? "bound" : "created")
            + " '" + objectName + "' under " + GetPath(infoUi)
            + " (children=" + infoUi.childCount + ").",
            hudRoot != null ? hudRoot.gameObject : infoUi.gameObject);
    }

    private static Transform FindChildPathIgnoreCase(Transform root, params string[] names)
    {
        if (root == null || names == null || names.Length == 0)
        {
            return null;
        }

        Transform current = root;
        for (int i = 0; i < names.Length; i++)
        {
            Transform next = null;
            for (int c = 0; c < current.childCount; c++)
            {
                Transform child = current.GetChild(c);
                if (child != null
                    && string.Equals(child.name, names[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next == null)
            {
                return null;
            }

            current = next;
        }

        return current;
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
            if (child != null
                && string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
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
        if (t == null)
        {
            return "(null)";
        }

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
