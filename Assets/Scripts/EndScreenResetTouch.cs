using System.Collections;
using UnityEngine;

/// <summary>
/// Floating end-screen prop (assign a world TMP / mesh object, or put this on the TMP itself).
/// Appears on win / lose / timeout with a bow-style scale pop; hand touch resets like the panel.
/// </summary>
public class EndScreenResetTouch : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DragonFightUI fightUI;
    [Tooltip("World object to show at end screens. Leave empty to use this GameObject.")]
    [SerializeField] private GameObject resetProp;
    [Tooltip("Optional left-hand follow (bow). Used to resolve Hand2.")]
    [SerializeField] private LeftHandChild leftHandChild;

    [Header("Touch")]
    [Tooltip("Hand must come within this distance of the prop (meters).")]
    [SerializeField, Min(0.05f)] private float handTouchRadius = 0.28f;
    [Tooltip("Ignore repeated touches for this long after a successful reset.")]
    [SerializeField, Min(0f)] private float retriggerCooldown = 0.75f;
    [Tooltip("Desktop: also accept this key while the prop is visible.")]
    [SerializeField] private KeyCode desktopResetKey = KeyCode.R;

    [Header("Appear")]
    [SerializeField, Min(0.05f)] private float appearScaleSeconds = 0.28f;
    [SerializeField] private bool billboardTowardPlayer = true;

    private Vector3 propBaseScale = Vector3.one;
    private bool baseScaleCached;
    private bool visible;
    private bool handWasInside;
    private float nextAllowedTouchTime;
    private float nextHandSearchTime;
    private Transform cachedLeftHand;
    private Coroutine appearRoutine;

    private void Awake()
    {
        ResolveReferences();
        CacheBaseScale();
        // Hide without SetActive(false) on this object — that would kill this script forever.
        SetPropVisible(false, animate: false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        SyncVisibility(force: true);
    }

    private void Update()
    {
        ResolveReferences();
        if (fightUI == null || resetProp == null)
        {
            return;
        }

        SyncVisibility(force: false);

        if (!visible)
        {
            handWasInside = false;
            return;
        }

        if (billboardTowardPlayer)
        {
            BillboardProp();
        }

        if (PlayEnvironment.IsDesktopInput && Input.GetKeyDown(desktopResetKey))
        {
            TryTriggerReset();
            return;
        }

        bool handInside = IsAnyHandTouching();
        if (handInside && !handWasInside)
        {
            TryTriggerReset();
        }

        handWasInside = handInside;
    }

    /// <summary>Called from DragonFightUI when entering / leaving end screens.</summary>
    public void RefreshFromFightUI()
    {
        ResolveReferences();
        SyncVisibility(force: true);
    }

    private void ResolveReferences()
    {
        if (fightUI == null)
        {
            fightUI = FindObjectOfType<DragonFightUI>();
        }

        if (resetProp == null)
        {
            resetProp = gameObject;
        }

        if (leftHandChild == null)
        {
            leftHandChild = FindObjectOfType<LeftHandChild>();
        }
    }

    private void SyncVisibility(bool force)
    {
        if (fightUI == null || resetProp == null)
        {
            return;
        }

        bool wantVisible = fightUI.IsEndScreen;
        if (force || wantVisible != visible)
        {
            SetPropVisible(wantVisible, animate: wantVisible);
        }
    }

    private void TryTriggerReset()
    {
        if (Time.unscaledTime < nextAllowedTouchTime)
        {
            return;
        }

        if (fightUI == null || !fightUI.IsEndScreen)
        {
            return;
        }

        nextAllowedTouchTime = Time.unscaledTime + retriggerCooldown;
        fightUI.TriggerPanelAction();
    }

    private bool IsAnyHandTouching()
    {
        if (resetProp == null)
        {
            return false;
        }

        Vector3 propPos = resetProp.transform.position;
        float radiusSq = handTouchRadius * handTouchRadius;

        Transform right = PlayEnvironment.ResolveRightHandTransform();
        if (right != null && (right.position - propPos).sqrMagnitude <= radiusSq)
        {
            return true;
        }

        Transform left = ResolveLeftHandTransform();
        if (left != null && (left.position - propPos).sqrMagnitude <= radiusSq)
        {
            return true;
        }

        if (PlayEnvironment.IsDesktopInput)
        {
            Camera cam = PlayEnvironment.ResolveViewCamera();
            if (cam != null && (cam.transform.position - propPos).sqrMagnitude <= radiusSq)
            {
                return true;
            }
        }

        return false;
    }

    private Transform ResolveLeftHandTransform()
    {
        if (leftHandChild == null)
        {
            leftHandChild = FindObjectOfType<LeftHandChild>();
        }

        if (leftHandChild != null && leftHandChild.BoundHand != null)
        {
            cachedLeftHand = leftHandChild.BoundHand;
            return cachedLeftHand;
        }

        if (cachedLeftHand != null)
        {
            return cachedLeftHand;
        }

        if (Time.unscaledTime < nextHandSearchTime)
        {
            return null;
        }

        nextHandSearchTime = Time.unscaledTime + 0.5f;
        cachedLeftHand = FindNamedHand("Hand2") ?? FindNamedHand("LeftHand");
        return cachedLeftHand;
    }

    private static Transform FindNamedHand(string handName)
    {
        if (string.IsNullOrEmpty(handName))
        {
            return null;
        }

#if UNITY_2023_1_OR_NEWER
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        Transform[] all = Object.FindObjectsOfType<Transform>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null
                && string.Equals(all[i].name, handName, System.StringComparison.OrdinalIgnoreCase))
            {
                return all[i];
            }
        }

        return null;
    }

    private void SetPropVisible(bool show, bool animate)
    {
        visible = show;
        if (resetProp == null)
        {
            return;
        }

        if (appearRoutine != null)
        {
            StopCoroutine(appearRoutine);
            appearRoutine = null;
        }

        CacheBaseScale();

        if (!show)
        {
            // Never deactivate this component's GameObject — Awake hide would disable Update forever.
            if (CanSafelyDeactivateProp())
            {
                resetProp.SetActive(false);
            }
            else
            {
                resetProp.transform.localScale = Vector3.one * 0.001f;
                SetPropRenderersEnabled(false);
            }

            return;
        }

        if (!resetProp.activeSelf)
        {
            resetProp.SetActive(true);
        }

        SetPropRenderersEnabled(true);

        if (animate)
        {
            appearRoutine = StartCoroutine(AnimateAppear());
        }
        else
        {
            resetProp.transform.localScale = propBaseScale;
        }
    }

    /// <summary>
    /// True when resetProp is a separate object (not this GameObject and not an ancestor).
    /// </summary>
    private bool CanSafelyDeactivateProp()
    {
        if (resetProp == null || resetProp == gameObject)
        {
            return false;
        }

        Transform t = transform;
        while (t != null)
        {
            if (t.gameObject == resetProp)
            {
                return false;
            }

            t = t.parent;
        }

        return true;
    }

    private void SetPropRenderersEnabled(bool enabled)
    {
        if (resetProp == null)
        {
            return;
        }

        Renderer[] renderers = resetProp.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = enabled;
            }
        }
    }

    private IEnumerator AnimateAppear()
    {
        if (resetProp == null)
        {
            yield break;
        }

        Transform t = resetProp.transform;
        Vector3 target = propBaseScale;
        t.localScale = Vector3.one * 0.001f;

        float duration = Mathf.Max(0.05f, appearScaleSeconds);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (resetProp == null || !visible)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            u = 1f - (1f - u) * (1f - u);
            t.localScale = Vector3.LerpUnclamped(Vector3.one * 0.001f, target, u);
            yield return null;
        }

        if (resetProp != null)
        {
            t.localScale = target;
        }

        appearRoutine = null;
    }

    private void CacheBaseScale()
    {
        if (resetProp == null || baseScaleCached)
        {
            return;
        }

        Vector3 scale = resetProp.transform.localScale;
        // If already shrunk from a previous hide, don't treat that as the base.
        if (scale.sqrMagnitude < 1e-4f)
        {
            scale = Vector3.one;
        }

        propBaseScale = scale;
        baseScaleCached = true;
    }

    private void BillboardProp()
    {
        if (resetProp == null)
        {
            return;
        }

        Vector3 lookFrom = PlayEnvironment.ResolvePlayerAimPosition();
        Vector3 toPlayer = lookFrom - resetProp.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 1e-4f)
        {
            return;
        }

        resetProp.transform.rotation = Quaternion.LookRotation(-toPlayer.normalized, Vector3.up);
    }

    private void OnDrawGizmosSelected()
    {
        Transform t = resetProp != null ? resetProp.transform : transform;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
        Gizmos.DrawWireSphere(t.position, handTouchRadius);
    }
}
