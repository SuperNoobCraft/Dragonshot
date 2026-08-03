using UnityEngine;

/// <summary>
/// Keeps this object glued to a tracked hand (default Hand2) every frame.
/// Prefers parenting; always copies pose so follow still works if parenting is blocked.
/// </summary>
[DefaultExecutionOrder(50)]
public class LeftHandChild : MonoBehaviour
{
    [SerializeField] private string entityName = "Hand2";
    [SerializeField] private string[] fallbackNames = { "Hand2", "hand2", "LeftHand", "Hand_L", "Controller (Left)" };
    [SerializeField] private Vector3 localPosition;
    [SerializeField] private Vector3 localEulerAngles;
    [Tooltip("Bow grip vs left-hand tracker. "
             + "Tuned so a natural thumbs-up hold sits upright facing away "
             + "(thumb axis up → right → forward relative to the hand).")]
    [SerializeField] private Vector3 bowGripEuler = new Vector3(180f, -90f, 0f);
    [SerializeField, Min(0.05f)] private float retryInterval = 0.25f;
    [SerializeField] private bool logAttach = true;

    private Transform boundParent;
    private float nextRetryTime;
    private bool loggedMissing;
    private bool loggedBound;

    public Transform BoundHand => boundParent;
    public Vector3 LocalPosition => localPosition;
    public Vector3 LocalEulerAngles => localEulerAngles;
    public bool HasHand => boundParent != null;

    private void OnEnable()
    {
        nextRetryTime = 0f;
        loggedMissing = false;
        loggedBound = false;
    }

    private void LateUpdate()
    {
        if (boundParent == null)
        {
            TryResolveParent();
        }

        if (boundParent == null)
        {
            return;
        }

        FollowBoundHand();
    }

    /// <summary>
    /// Call from other scripts (e.g. BowController) to force a pose sync this frame.
    /// </summary>
    public void FollowBoundHand()
    {
        if (boundParent == null)
        {
            return;
        }

        if (transform.parent != boundParent)
        {
            transform.SetParent(boundParent, true);
        }

        Quaternion localRot = Quaternion.Euler(localEulerAngles) * Quaternion.Euler(bowGripEuler);
        transform.localPosition = localPosition;
        transform.localRotation = localRot;

        // If parenting was rejected, still hard-follow in world space.
        if (transform.parent != boundParent)
        {
            transform.SetPositionAndRotation(
                boundParent.TransformPoint(localPosition),
                boundParent.rotation * localRot);
        }
    }

    private void TryResolveParent()
    {
        if (Time.unscaledTime < nextRetryTime)
        {
            return;
        }

        nextRetryTime = Time.unscaledTime + retryInterval;

        Transform found = FindHand();
        if (found == null)
        {
            if (logAttach && !loggedMissing)
            {
                loggedMissing = true;
                Debug.LogWarning(
                    $"LeftHandChild on '{name}': waiting for hand named '{entityName}' (or fallbacks).",
                    this);
            }

            return;
        }

        boundParent = found;
        FollowBoundHand();

        if (logAttach && !loggedBound)
        {
            loggedBound = true;
            Debug.Log(
                $"LeftHandChild on '{name}': following '{boundParent.name}' "
                + $"(parent={(transform.parent != null ? transform.parent.name : "null")}).",
                this);
        }
    }

    private Transform FindHand()
    {
        Transform byPrimary = FindByName(entityName);
        if (byPrimary != null)
        {
            return byPrimary;
        }

        if (fallbackNames == null)
        {
            return null;
        }

        for (int i = 0; i < fallbackNames.Length; i++)
        {
            string n = fallbackNames[i];
            if (string.IsNullOrEmpty(n) || n == entityName)
            {
                continue;
            }

            Transform found = FindByName(n);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindByName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return null;
        }

#if UNITY_2023_1_OR_NEWER
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
}
