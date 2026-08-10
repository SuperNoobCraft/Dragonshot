using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pre-fight: crystal pillars sit underground with crystals off (dragon unshielded).
/// On fight start: pillars rise at a shared speed to per-pillar random heights
/// (see <see cref="CrystalPillarRiseSettings"/>); crystals grow (shell then orb)
/// with the rise and enable + beam at peak.
/// On reset: crystals disable and pillars retreat underground.
/// </summary>
[DefaultExecutionOrder(-200)]
public class CrystalPillarRiseController : MonoBehaviour
{
    private enum PillarMotion
    {
        Buried,
        Rising,
        Raised,
        Retreating
    }

    private class PillarSlot
    {
        public Transform pillar;
        public CrystalPillarRiseSettings settings;
        public EnderCrystal crystal;
        public float buriedPillarY;
        public float peakPillarY;
        public bool crystalEnabled;
        public PillarMotion motion;
    }

    [Header("References")]
    [SerializeField] private DragonBoss dragon;
    [Tooltip("Optional. Empty = auto-find CrystalPillar_1..N.")]
    [SerializeField] private Transform[] pillars;

    [Header("Rise / Retreat")]
    [Tooltip("Shared vertical speed (m/s). Taller peaks take longer to arrive.")]
    [SerializeField, Min(0.1f)] private float riseSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float retreatSpeed = 4.5f;
    [SerializeField] private bool snapUndergroundOnAwake = true;

    private readonly List<PillarSlot> slots = new List<PillarSlot>(8);
    private bool cached;

    public bool IsIntroPlaying
    {
        get
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].motion == PillarMotion.Rising || slots[i].motion == PillarMotion.Retreating)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void Awake()
    {
        if (dragon == null)
        {
            dragon = GetComponent<DragonBoss>();
            if (dragon == null)
            {
                dragon = FindObjectOfType<DragonBoss>();
            }
        }

        // Cache rest poses while pillars are still at scene height, then bury.
        CachePillars();
        if (snapUndergroundOnAwake)
        {
            SnapBuriedAndDisableCrystals();
        }
    }

    private void Update()
    {
        if (slots.Count == 0)
        {
            return;
        }

        float dt = Time.deltaTime;
        for (int i = 0; i < slots.Count; i++)
        {
            PillarSlot slot = slots[i];
            if (slot.pillar == null)
            {
                continue;
            }

            if (slot.motion == PillarMotion.Rising)
            {
                TickRise(slot, dt);
            }
            else if (slot.motion == PillarMotion.Retreating)
            {
                TickRetreat(slot, dt);
            }
        }
    }

    public void CachePillars()
    {
        slots.Clear();
        cached = false;

        List<Transform> found = new List<Transform>(8);
        if (pillars != null)
        {
            for (int i = 0; i < pillars.Length; i++)
            {
                if (pillars[i] != null)
                {
                    found.Add(pillars[i]);
                }
            }
        }

        if (found.Count == 0)
        {
            for (int i = 1; i <= 12; i++)
            {
                GameObject go = GameObject.Find("CrystalPillar_" + i);
                if (go != null)
                {
                    found.Add(go.transform);
                }
            }
        }

        // Also pick up any CrystalPillarRiseSettings in the scene.
        CrystalPillarRiseSettings[] settingsAll = FindObjectsOfType<CrystalPillarRiseSettings>();
        for (int i = 0; i < settingsAll.Length; i++)
        {
            if (settingsAll[i] != null && !found.Contains(settingsAll[i].transform))
            {
                found.Add(settingsAll[i].transform);
            }
        }

        for (int i = 0; i < found.Count; i++)
        {
            Transform pillar = found[i];
            CrystalPillarRiseSettings settings = pillar.GetComponent<CrystalPillarRiseSettings>();
            if (settings == null)
            {
                settings = pillar.gameObject.AddComponent<CrystalPillarRiseSettings>();
            }

            // Prefer editor-saved rest pose. Only capture now if missing (still at scene height).
            settings.CaptureRestPoseIfNeeded();

            EnderCrystal crystal = settings.Crystal;

            slots.Add(new PillarSlot
            {
                pillar = pillar,
                settings = settings,
                crystal = crystal,
                buriedPillarY = settings.BuriedPillarWorldY,
                peakPillarY = settings.RestPillarPosition.y,
                crystalEnabled = false,
                motion = PillarMotion.Buried
            });
        }

        cached = slots.Count > 0;
    }

    public void CaptureAllRestPosesFromScene()
    {
        EnsureCached();
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].settings != null)
            {
                slots[i].settings.CaptureRestPoseFromScene();
            }
        }
    }

    public void SnapBuriedAndDisableCrystals()
    {
        EnsureCached();
        for (int i = 0; i < slots.Count; i++)
        {
            PillarSlot slot = slots[i];
            if (slot.pillar == null || slot.settings == null)
            {
                continue;
            }

            slot.settings.EnsureRestPose();
            SetCrystalEnabled(slot, false);
            Vector3 p = slot.settings.RestPillarPosition;
            p.y = slot.settings.BuriedPillarWorldY;
            slot.pillar.position = p;
            slot.buriedPillarY = p.y;
            slot.motion = PillarMotion.Buried;
            slot.crystalEnabled = false;
        }
    }

    public void BeginRetreat()
    {
        EnsureCached();
        for (int i = 0; i < slots.Count; i++)
        {
            PillarSlot slot = slots[i];
            if (slot.pillar == null || slot.settings == null)
            {
                continue;
            }

            SetCrystalEnabled(slot, false);
            slot.crystalEnabled = false;
            slot.buriedPillarY = slot.settings.BuriedPillarWorldY;
            slot.motion = PillarMotion.Retreating;
        }
    }

    public void BeginRiseIntro()
    {
        BeginRiseIntro(-1);
    }

    /// <param name="maxRising">
    /// How many pillars rise this fight. Negative or larger than slot count = all.
    /// Extra pillars stay buried with crystals off (Easy mode).
    /// </param>
    public void BeginRiseIntro(int maxRising)
    {
        EnsureCached();

        int riseTarget = maxRising < 0 ? slots.Count : Mathf.Min(maxRising, slots.Count);
        bool[] willRise = PickRisingMask(slots.Count, riseTarget);

        for (int i = 0; i < slots.Count; i++)
        {
            PillarSlot slot = slots[i];
            if (slot.pillar == null || slot.settings == null)
            {
                continue;
            }

            slot.settings.EnsureRestPose();
            SetCrystalEnabled(slot, false);
            slot.crystalEnabled = false;

            Vector3 p = slot.settings.RestPillarPosition;
            slot.buriedPillarY = slot.settings.BuriedPillarWorldY;
            p.y = slot.buriedPillarY;
            slot.pillar.position = p;

            if (!willRise[i])
            {
                slot.motion = PillarMotion.Buried;
                // Keep these crystals fully out of the fight (Easy mode leftovers).
                SetCrystalEnabled(slot, false);
                continue;
            }

            slot.peakPillarY = slot.settings.PickRandomPeakPillarWorldY();
            slot.motion = PillarMotion.Rising;
            BeginCrystalRiseEmerge(slot);
        }

        SuppressCrystalsNotRising();
    }

    /// <summary>
    /// Easy mode: any crystal whose pillar is not rising must not beam or shield.
    /// </summary>
    public void SuppressCrystalsNotRising()
    {
        EnsureCached();

        HashSet<EnderCrystal> keepActive = new HashSet<EnderCrystal>();
        for (int i = 0; i < slots.Count; i++)
        {
            PillarSlot slot = slots[i];
            if (slot.crystal == null && slot.settings != null)
            {
                slot.crystal = slot.settings.Crystal;
            }

            if (slot.crystal == null)
            {
                continue;
            }

            if (slot.motion == PillarMotion.Rising || slot.motion == PillarMotion.Raised)
            {
                keepActive.Add(slot.crystal);
            }
        }

        EnderCrystal[] all = FindObjectsOfType<EnderCrystal>();
        for (int i = 0; i < all.Length; i++)
        {
            EnderCrystal crystal = all[i];
            if (crystal == null || keepActive.Contains(crystal))
            {
                continue;
            }

            crystal.CancelGrow();
            crystal.SetCombatActive(false);
            crystal.SuppressBeam();
        }
    }

    private void BeginCrystalRiseEmerge(PillarSlot slot)
    {
        if (slot.crystal == null && slot.settings != null)
        {
            slot.crystal = slot.settings.Crystal;
        }

        if (slot.crystal != null)
        {
            slot.crystal.BeginRiseEmerge();
        }
    }

    private static bool[] PickRisingMask(int count, int riseTarget)
    {
        bool[] mask = new bool[count];
        if (count <= 0)
        {
            return mask;
        }

        if (riseTarget >= count)
        {
            for (int i = 0; i < count; i++)
            {
                mask[i] = true;
            }

            return mask;
        }

        // Stable shuffle of indices, then enable the first riseTarget.
        int[] order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = order[i];
            order[i] = order[j];
            order[j] = tmp;
        }

        for (int i = 0; i < riseTarget; i++)
        {
            mask[order[i]] = true;
        }

        return mask;
    }

    private void TickRise(PillarSlot slot, float dt)
    {
        Vector3 p = slot.pillar.position;
        float nextY = Mathf.MoveTowards(p.y, slot.peakPillarY, riseSpeed * dt);
        p.y = nextY;
        slot.pillar.position = p;

        float span = Mathf.Max(0.01f, slot.peakPillarY - slot.buriedPillarY);
        float progress = Mathf.Clamp01((nextY - slot.buriedPillarY) / span);
        if (slot.crystal != null)
        {
            slot.crystal.SetRiseEmergeProgress(progress);
        }

        if (Mathf.Abs(nextY - slot.peakPillarY) <= 0.01f)
        {
            p.y = slot.peakPillarY;
            slot.pillar.position = p;
            slot.motion = PillarMotion.Raised;
            if (!slot.crystalEnabled)
            {
                CompleteCrystalRiseEmerge(slot);
                slot.crystalEnabled = true;
            }
        }
    }

    private void CompleteCrystalRiseEmerge(PillarSlot slot)
    {
        if (slot.crystal == null && slot.settings != null)
        {
            slot.crystal = slot.settings.Crystal;
        }

        if (slot.crystal != null)
        {
            slot.crystal.SetRiseEmergeProgress(1f);
            slot.crystal.CompleteRiseEmerge();
        }
        else
        {
            SetCrystalEnabled(slot, true);
        }
    }

    private void TickRetreat(PillarSlot slot, float dt)
    {
        Vector3 p = slot.pillar.position;
        float nextY = Mathf.MoveTowards(p.y, slot.buriedPillarY, retreatSpeed * dt);
        p.y = nextY;
        slot.pillar.position = p;

        if (Mathf.Abs(nextY - slot.buriedPillarY) <= 0.01f)
        {
            p.y = slot.buriedPillarY;
            slot.pillar.position = p;
            slot.motion = PillarMotion.Buried;
        }
    }

    private void SetCrystalEnabled(PillarSlot slot, bool enabled)
    {
        if (slot.crystal == null && slot.settings != null)
        {
            slot.crystal = slot.settings.Crystal;
        }

        if (slot.crystal == null)
        {
            return;
        }

        if (enabled)
        {
            if (slot.crystal.IsDestroyed)
            {
                slot.crystal.Revive();
            }

            slot.crystal.SetCombatActive(true);
        }
        else
        {
            slot.crystal.SetCombatActive(false);
        }
    }

    private void EnsureCached()
    {
        if (!cached || slots.Count == 0)
        {
            CachePillars();
        }
    }
}
