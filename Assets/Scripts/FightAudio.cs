using UnityEngine;

/// <summary>
/// Central SFX for the dragon fight / archery.
/// All playback is non-spatial (2D) — CAVE stereo/positional audio is unreliable.
/// Optional manual distance ducking is applied in code (not Unity 3D rolloff).
/// </summary>
public class FightAudio : MonoBehaviour
{
    public static FightAudio Instance { get; private set; }

    [Header("Clips")]
    [SerializeField] private AudioClip arrowShot;
    [SerializeField] private AudioClip shieldBounce;
    [Tooltip("One picked at random each time the dragon is hit.")]
    [SerializeField] private AudioClip[] dragonHurt;
    [SerializeField] private AudioClip dragonDeath;
    [SerializeField] private AudioClip fireballShoot;
    [SerializeField] private AudioClip fireballExplode;
    [SerializeField] private AudioClip crystalExplode;
    [Tooltip("Single-flap clips. One plays at random on each flap beat.")]
    [SerializeField] private AudioClip[] dragonFlaps;
    [Tooltip("Equip variants (bow / quiver / wear / later arrow draws). One picked at random each play.")]
    [SerializeField] private AudioClip[] equipSounds;
    [Tooltip("Plays once when a draw starts; stopped immediately on release/shot. One picked at random.")]
    [SerializeField] private AudioClip[] bowDrawSounds;

    [Header("Wing Flaps")]
    [Tooltip("Seconds between flap one-shots while the dragon is flying. Match your wing anim.")]
    [SerializeField, Min(0.05f)] private float flapInterval = 0.35f;
    [Tooltip("Random extra delay added each flap (0 = exact interval).")]
    [SerializeField, Min(0f)] private float flapIntervalJitter = 0.04f;

    [Header("Volumes")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float arrowShotVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] private float shieldBounceVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float dragonHurtVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float dragonDeathVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float fireballShootVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float fireballExplodeVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float crystalExplodeVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float dragonFlapVolume = 0.7f;
    [SerializeField, Range(0f, 2f)] private float equipVolume = 1.25f;
    [SerializeField, Range(0f, 1f)] private float bowDrawVolume = 0.85f;

    [Header("Manual Distance (no Unity spatial audio)")]
    [Tooltip("If on, volume is scaled by distance from the player using the curve below.")]
    [SerializeField] private bool useManualDistanceVolume = true;
    [Tooltip("Full volume within this distance (meters).")]
    [SerializeField, Min(0f)] private float fullVolumeDistance = 4f;
    [Tooltip("At/ beyond this distance, volume hits the floor.")]
    [SerializeField, Min(1f)] private float quietDistance = 25f;
    [Tooltip("Minimum volume multiplier at quietDistance+ (keeps far SFX audible in CAVE).")]
    [SerializeField, Range(0f, 1f)] private float minDistanceVolume = 0.55f;

    [Header("Source")]
    [Tooltip("Single non-spatial AudioSource used for everything. Auto-created if empty.")]
    [SerializeField] private AudioSource sfxSource;
    [Tooltip("Dedicated source so bow draw can be stopped without killing other one-shots.")]
    [SerializeField] private AudioSource bowDrawSource;

    private bool flyingActive;
    private float nextFlapTime;
    private Transform playbackHost;
    private bool playbackReparented;

    public static FightAudio Resolve()
    {
        if (Instance != null && Instance.isActiveAndEnabled)
        {
            return Instance;
        }

        if (Instance != null)
        {
            return Instance;
        }

#if UNITY_2023_1_OR_NEWER
        Instance = FindFirstObjectByType<FightAudio>(FindObjectsInactive.Include);
#else
        FightAudio[] all = Resources.FindObjectsOfTypeAll<FightAudio>();
        for (int i = 0; i < all.Length; i++)
        {
            FightAudio candidate = all[i];
            if (candidate == null || candidate.gameObject.scene.IsValid() == false)
            {
                continue;
            }

            Instance = candidate;
            break;
        }
#endif
        return Instance;
    }

    /// <summary>
    /// Keep SFX sources on an active transform while the dragon GameObject is disabled
    /// (e.g. secret crystal target practice).
    /// </summary>
    public void ReparentPlaybackTo(Transform host)
    {
        if (host == null)
        {
            return;
        }

        EnsureSource();
        playbackHost = host;
        playbackReparented = true;

        if (sfxSource != null)
        {
            sfxSource.transform.SetParent(host, true);
        }

        if (bowDrawSource != null)
        {
            bowDrawSource.transform.SetParent(host, true);
        }
    }

    public void RestorePlaybackParent()
    {
        if (!playbackReparented)
        {
            return;
        }

        if (sfxSource != null)
        {
            sfxSource.transform.SetParent(transform, false);
        }

        if (bowDrawSource != null)
        {
            bowDrawSource.transform.SetParent(transform, false);
        }

        playbackHost = null;
        playbackReparented = false;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            return;
        }

        Instance = this;
        EnsureSource();
    }

    private void Update()
    {
        if (!flyingActive)
        {
            return;
        }

        if (Time.time < nextFlapTime)
        {
            return;
        }

        ScheduleNextFlap();
        PlayRandomFlap();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void EnsureSource()
    {
        if (sfxSource == null)
        {
            GameObject sfxGo = new GameObject("FightAudio_SFX");
            sfxGo.transform.SetParent(transform, false);
            sfxSource = sfxGo.AddComponent<AudioSource>();
        }

        ConfigureNonSpatial(sfxSource);
        EnsureBowDrawSource();
    }

    private void EnsureBowDrawSource()
    {
        if (bowDrawSource == null)
        {
            GameObject drawGo = new GameObject("FightAudio_BowDraw");
            drawGo.transform.SetParent(transform, false);
            bowDrawSource = drawGo.AddComponent<AudioSource>();
        }

        ConfigureNonSpatial(bowDrawSource);
    }

    private static void ConfigureNonSpatial(AudioSource source)
    {
        // Force non-spatial — CAVE does not handle Unity 3D audio reliably.
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.spatialize = false;
        source.bypassListenerEffects = false;
        source.volume = 1f;
    }

    private void PlayBowDrawInternal(Vector3 worldPosition)
    {
        AudioClip clip = PickRandom(bowDrawSounds);
        if (clip == null || masterVolume <= 0.001f || bowDrawVolume <= 0.001f)
        {
            return;
        }

        EnsureBowDrawSource();
        StopBowDrawInternal();

        float distMul = useManualDistanceVolume
            ? DistanceVolumeMultiplier(worldPosition)
            : 1f;
        float vol = Mathf.Clamp01(bowDrawVolume * masterVolume * distMul);
        if (vol <= 0.001f)
        {
            return;
        }

        bowDrawSource.clip = clip;
        bowDrawSource.volume = vol;
        bowDrawSource.loop = false;
        bowDrawSource.Play();
    }

    private void StopBowDrawInternal()
    {
        if (bowDrawSource != null && bowDrawSource.isPlaying)
        {
            bowDrawSource.Stop();
        }
    }

    public static void PlayArrowShot(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.Play(audio.arrowShot, worldPosition, audio.arrowShotVolume);
        }
    }

    public static void PlayShieldBounce(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.Play(audio.shieldBounce, worldPosition, audio.shieldBounceVolume);
        }
    }

    public static void PlayDragonHurt(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.Play(PickRandom(audio.dragonHurt), worldPosition, audio.dragonHurtVolume);
        }
    }

    public static void PlayDragonDeath(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.SetFlyingInternal(false);
            audio.Play(audio.dragonDeath, worldPosition, audio.dragonDeathVolume);
        }
    }

    /// <summary>
    /// Same clip as the death cry, without stopping the flying loop (overtime roar).
    /// </summary>
    public static void PlayDragonRoar(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.Play(audio.dragonDeath, worldPosition, audio.dragonDeathVolume);
        }
    }

    public static void PlayFireballShoot(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.Play(audio.fireballShoot, worldPosition, audio.fireballShootVolume);
        }
    }

    public static void PlayFireballExplode(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.Play(audio.fireballExplode, worldPosition, audio.fireballExplodeVolume);
        }
    }

    public static void PlayCrystalExplode(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.Play(audio.crystalExplode, worldPosition, audio.crystalExplodeVolume);
        }
    }

    public static void PlayEquipBow(Vector3 worldPosition)
    {
        PlayEquip(worldPosition);
    }

    public static void PlayEquipQuiverPickup(Vector3 worldPosition)
    {
        PlayEquip(worldPosition);
    }

    public static void PlayEquipQuiverWear(Vector3 worldPosition)
    {
        PlayEquip(worldPosition);
    }

    public static void PlayEquipScope(Vector3 worldPosition)
    {
        PlayEquip(worldPosition);
    }

    /// <summary>Back-quiver arrow grab.</summary>
    public static void PlayEquipArrowFromQuiver(Vector3 worldPosition)
    {
        PlayEquip(worldPosition);
    }

    public static void PlayEquip(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.Play(PickRandom(audio.equipSounds), worldPosition, audio.equipVolume);
        }
    }

    /// <summary>Start draw creak once per load. Call StopBowDraw on shot/cancel.</summary>
    public static void PlayBowDraw(Vector3 worldPosition)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.PlayBowDrawInternal(worldPosition);
        }
    }

    public static void StopBowDraw()
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.StopBowDrawInternal();
        }
    }

    public static void SetDragonFlying(bool flying)
    {
        FightAudio audio = Resolve();
        if (audio != null)
        {
            audio.SetFlyingInternal(flying);
        }
    }

    private void SetFlyingInternal(bool flying)
    {
        if (flying == flyingActive)
        {
            return;
        }

        flyingActive = flying;
        if (flying)
        {
            nextFlapTime = Time.time + Random.Range(0f, Mathf.Max(0.02f, flapInterval * 0.25f));
        }
    }

    private void ScheduleNextFlap()
    {
        float interval = Mathf.Max(0.05f, flapInterval);
        float jitter = flapIntervalJitter > 0f
            ? Random.Range(-flapIntervalJitter, flapIntervalJitter)
            : 0f;
        nextFlapTime = Time.time + Mathf.Max(0.05f, interval + jitter);
    }

    private void PlayRandomFlap()
    {
        // Flaps use the dragon's position for optional manual distance ducking.
        Play(PickRandom(dragonFlaps), transform.position, dragonFlapVolume);
    }

    private void Play(AudioClip clip, Vector3 worldPosition, float volume)
    {
        if (clip == null || masterVolume <= 0.001f || volume <= 0.001f)
        {
            return;
        }

        EnsureSource();

        float distMul = useManualDistanceVolume
            ? DistanceVolumeMultiplier(worldPosition)
            : 1f;
        float vol = Mathf.Max(0f, volume * masterVolume * distMul);
        if (vol <= 0.001f)
        {
            return;
        }

        sfxSource.spatialBlend = 0f;
        sfxSource.spatialize = false;
        sfxSource.PlayOneShot(clip, vol);
    }

    private float DistanceVolumeMultiplier(Vector3 worldPosition)
    {
        Vector3 listener = ResolveListenerPosition();
        float dist = Vector3.Distance(listener, worldPosition);
        float near = Mathf.Max(0f, fullVolumeDistance);
        float far = Mathf.Max(near + 0.01f, quietDistance);
        float t = Mathf.InverseLerp(near, far, dist);
        // Soft falloff that never drops below minDistanceVolume.
        float shaped = t * t;
        return Mathf.Lerp(1f, Mathf.Clamp01(minDistanceVolume), shaped);
    }

    private static Vector3 ResolveListenerPosition()
    {
        Vector3 aim = PlayEnvironment.ResolvePlayerAimPosition();
        if (aim.sqrMagnitude > 1e-6f || PlayEnvironment.ResolvePlayerTransform() != null)
        {
            return aim;
        }

        Camera cam = PlayEnvironment.ResolveViewCamera();
        if (cam != null)
        {
            return cam.transform.position;
        }

        AudioListener listener = FindObjectOfType<AudioListener>();
        if (listener != null)
        {
            return listener.transform.position;
        }

        return Vector3.zero;
    }

    private static AudioClip PickRandom(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        int usable = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
            {
                usable++;
            }
        }

        if (usable == 0)
        {
            return null;
        }

        int pick = Random.Range(0, usable);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null)
            {
                continue;
            }

            if (pick == 0)
            {
                return clips[i];
            }

            pick--;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (sfxSource != null)
        {
            sfxSource.spatialBlend = 0f;
            sfxSource.spatialize = false;
        }

        quietDistance = Mathf.Max(fullVolumeDistance + 0.01f, quietDistance);
    }
#endif
}
