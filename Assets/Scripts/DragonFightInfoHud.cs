using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

/// <summary>
/// Runtime HUD parented under vGear → Frame → Info → InfoUI (Votanic creates InfoUI at play).
/// Anchored top-left; shows fight time + HP only while the dragon fight is playing.
/// </summary>
public class DragonFightInfoHud : MonoBehaviour
{
    [Header("Find")]
    [SerializeField] private string infoUiObjectName = "InfoUI";
    [SerializeField] private string hudObjectName = "DragonFightHud";
    [SerializeField] private bool logDebug = true;

    [Header("Layout")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(24f, -24f);
    [SerializeField] private Vector2 sizeDelta = new Vector2(480f, 160f);
    [SerializeField] private int fontSize = 36;
    [SerializeField] private Color textColor = Color.white;

    private RectTransform hudRoot;
    private Text hudText;
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
    }

    private void Start()
    {
        nextFindAttempt = 0f;
        TryEnsureHudObject();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextFindAttempt)
        {
            nextFindAttempt = Time.unscaledTime + 0.25f;
            TryEnsureHudObject();
        }

        if (!visible || hudText == null || hudRoot == null)
        {
            return;
        }

        if (!hudRoot.gameObject.activeSelf)
        {
            hudRoot.gameObject.SetActive(true);
        }

        if (hudText.text != pendingText)
        {
            hudText.text = pendingText;
        }
    }

    private void Apply()
    {
        TryEnsureHudObject();
        if (hudRoot == null || hudText == null)
        {
            return;
        }

        hudRoot.gameObject.SetActive(visible);
        if (visible)
        {
            hudText.text = pendingText;
        }
    }

    private void TryEnsureHudObject()
    {
        if (hudRoot != null && hudText != null && hudRoot.parent != null)
        {
            return;
        }

        hudRoot = null;
        hudText = null;
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

        Transform existing = infoUi.Find(hudObjectName);
        if (existing != null)
        {
            BindExisting(existing, infoUi);
            return;
        }

        try
        {
            CreateHudUnder(infoUi);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("DragonFightInfoHud: create failed — " + ex, this);
        }
    }

    private void BindExisting(Transform existing, Transform infoUi)
    {
        hudRoot = existing as RectTransform;
        if (hudRoot == null)
        {
            Destroy(existing.gameObject);
            CreateHudUnder(infoUi);
            return;
        }

        hudText = existing.GetComponent<Text>();
        if (hudText == null)
        {
            if (existing.GetComponent<CanvasRenderer>() == null)
            {
                existing.gameObject.AddComponent<CanvasRenderer>();
            }

            hudText = existing.gameObject.AddComponent<Text>();
            StyleText(hudText, infoUi);
        }

        Layout(hudRoot);
        hudRoot.gameObject.SetActive(visible);
        LogCreated(infoUi, rebound: true);
    }

    private void CreateHudUnder(Transform infoUi)
    {
        EnsureCanvas(infoUi);

        GameObject go = new GameObject(hudObjectName, typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = infoUi.gameObject.layer;
        go.transform.SetParent(infoUi, false);

        hudRoot = go.GetComponent<RectTransform>();
        Layout(hudRoot);

        hudText = go.AddComponent<Text>();
        StyleText(hudText, infoUi);
        hudText.text = string.IsNullOrEmpty(pendingText) ? "TIME --\nHP --/--" : pendingText;
        go.SetActive(visible);

        LogCreated(infoUi, rebound: false);
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

    private void Layout(RectTransform rt)
    {
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
        rt.SetAsLastSibling();
    }

    private void StyleText(Text text, Transform infoUi)
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
        text.alignment = TextAnchor.UpperLeft;
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

    private void LogCreated(Transform infoUi, bool rebound)
    {
        if (loggedCreated || !logDebug)
        {
            return;
        }

        loggedCreated = true;
        Debug.Log(
            "DragonFightInfoHud: "
            + (rebound ? "bound" : "created")
            + " '" + hudObjectName + "' under " + GetPath(infoUi)
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
