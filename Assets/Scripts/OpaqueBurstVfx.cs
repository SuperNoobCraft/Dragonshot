using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CAVE-safe opaque explosion: solid expanding core + mesh sparks (+ optional cube shards).
/// No transparent particles — sparks shrink instead of fading alpha.
/// </summary>
public class OpaqueBurstVfx : MonoBehaviour
{
    [System.Serializable]
    public struct Settings
    {
        public Color coreColor;
        public Color sparkColor;
        public Color shardColor;
        public float duration;
        public float radius;
        public float startSize;
        public int sparkCount;
        public int shardCount;
        public float lightIntensity;
        public float lightRange;
        public bool spawnCore;
        public float gravity;

        public static Settings CrystalDefault => new Settings
        {
            coreColor = new Color(1f, 0.45f, 1f, 1f),
            sparkColor = new Color(0.85f, 0.2f, 1f, 1f),
            shardColor = new Color(0.7f, 0.15f, 0.95f, 1f),
            duration = 0.65f,
            radius = 1.8f,
            startSize = 0.35f,
            sparkCount = 28,
            shardCount = 14,
            lightIntensity = 4.5f,
            lightRange = 5f,
            spawnCore = true,
            gravity = 0.35f
        };

        public static Settings DragonDeathDefault => new Settings
        {
            coreColor = new Color(1f, 0.35f, 0.9f, 1f),
            sparkColor = new Color(0.55f, 0.05f, 0.85f, 1f),
            shardColor = new Color(0.35f, 0.05f, 0.45f, 1f),
            duration = 1.5f,
            radius = 4.5f,
            startSize = 1.2f,
            sparkCount = 48,
            shardCount = 20,
            lightIntensity = 7f,
            lightRange = 10f,
            spawnCore = true,
            gravity = 0.2f
        };
    }

    private Settings settings;
    private float elapsed;
    private Material coreMaterial;
    private Material sparkMaterial;
    private Material shardMaterial;
    private Light flashLight;
    private Transform core;
    private Transform followTarget;
    private readonly List<Shard> shards = new List<Shard>(24);

    private struct Shard
    {
        public Transform Transform;
        public Vector3 Velocity;
        public Vector3 Angular;
    }

    public static void Spawn(Vector3 worldPosition, Settings settings, Transform follow = null)
    {
        GameObject go = new GameObject("OpaqueBurstVfx");
        go.transform.position = worldPosition;
        OpaqueBurstVfx vfx = go.AddComponent<OpaqueBurstVfx>();
        vfx.settings = settings;
        vfx.elapsed = 0f;
        vfx.followTarget = follow;
        vfx.Build();
    }

    public static void SpawnCrystal(Vector3 worldPosition, Color energyColor, float radius = -1f)
    {
        Settings s = Settings.CrystalDefault;
        if (radius > 0f)
        {
            s.radius = radius;
        }

        Color e = energyColor;
        e.a = 1f;
        s.coreColor = Color.Lerp(e, Color.white, 0.45f);
        s.sparkColor = e;
        s.shardColor = Color.Lerp(e, new Color(0.4f, 0.05f, 0.6f), 0.35f);
        Spawn(worldPosition, s);
    }

    public static void SpawnDragonDeath(Transform follow, Color energyColor)
    {
        Settings s = Settings.DragonDeathDefault;
        Color e = energyColor;
        e.a = 1f;
        s.coreColor = Color.Lerp(e, Color.white, 0.35f);
        s.sparkColor = e;
        s.shardColor = Color.Lerp(e, Color.black, 0.35f);
        Vector3 pos = follow != null ? follow.position : Vector3.zero;
        Spawn(pos, s, follow);
    }

    private void Build()
    {
        float start = Mathf.Max(0.1f, settings.startSize);

        if (settings.spawnCore)
        {
            GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coreGo.name = "BurstCore";
            coreGo.transform.SetParent(transform, false);
            coreGo.transform.localScale = Vector3.one * start;
            Destroy(coreGo.GetComponent<Collider>());
            core = coreGo.transform;

            coreMaterial = DragonFireball.CreateOpaqueUnlit(settings.coreColor, "BurstCoreMat");
            Renderer coreRenderer = coreGo.GetComponent<Renderer>();
            coreRenderer.sharedMaterial = coreMaterial;
            coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
        }

        GameObject lightGo = new GameObject("FlashLight");
        lightGo.transform.SetParent(transform, false);
        flashLight = lightGo.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = settings.coreColor;
        flashLight.intensity = settings.lightIntensity;
        flashLight.range = settings.lightRange;
        flashLight.shadows = LightShadows.None;

        if (settings.sparkCount > 0)
        {
            ParticleSystem sparks = DragonFireball.CreateOpaqueMeshParticleSystem(
                transform,
                "BurstSparks",
                settings.sparkColor,
                out sparkMaterial);

            var main = sparks.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Mathf.Max(0.05f, settings.duration);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                settings.radius * 1.2f,
                settings.radius * 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(start * 0.1f, start * 0.28f);
            Color spark = settings.sparkColor;
            spark.a = 1f;
            main.startColor = spark;
            main.gravityModifier = settings.gravity;
            main.simulationSpace = followTarget != null
                ? ParticleSystemSimulationSpace.Local
                : ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(8, settings.sparkCount);

            var emission = sparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)Mathf.Clamp(settings.sparkCount, 1, 64))
            });

            var shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = start * 0.25f;

            var colorOver = sparks.colorOverLifetime;
            colorOver.enabled = true;
            Gradient g = new Gradient();
            Color a = settings.coreColor;
            a.a = 1f;
            Color b = settings.sparkColor;
            b.a = 1f;
            g.SetKeys(
                new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            colorOver.color = g;

            var sizeOver = sparks.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            sparks.Play(true);
        }

        if (settings.shardCount > 0)
        {
            shardMaterial = DragonFireball.CreateOpaqueUnlit(settings.shardColor, "BurstShardMat");
            for (int i = 0; i < settings.shardCount; i++)
            {
                GameObject shardGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shardGo.name = "Shard_" + i;
                shardGo.transform.SetParent(transform, false);
                shardGo.transform.position = transform.position
                    + Random.onUnitSphere * (start * 0.2f);
                float s = Random.Range(start * 0.12f, start * 0.32f);
                shardGo.transform.localScale = new Vector3(
                    s * Random.Range(0.6f, 1.4f),
                    s * Random.Range(0.6f, 1.4f),
                    s * Random.Range(0.6f, 1.4f));
                shardGo.transform.rotation = Random.rotation;
                Destroy(shardGo.GetComponent<Collider>());

                Renderer renderer = shardGo.GetComponent<Renderer>();
                renderer.sharedMaterial = shardMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.65f + 0.35f;
                shards.Add(new Shard
                {
                    Transform = shardGo.transform,
                    Velocity = dir.normalized * Random.Range(settings.radius * 1.5f, settings.radius * 4f),
                    Angular = Random.insideUnitSphere * Random.Range(180f, 540f)
                });
            }
        }
    }

    private void LateUpdate()
    {
        if (followTarget != null)
        {
            transform.position = followTarget.position;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        elapsed += dt;
        float duration = Mathf.Max(0.05f, settings.duration);
        float t = Mathf.Clamp01(elapsed / duration);
        float pop = 1f - (1f - t) * (1f - t);

        if (core != null)
        {
            float end = Mathf.Max(settings.startSize, settings.radius * 1.6f);
            core.localScale = Vector3.one * Mathf.Lerp(settings.startSize, end, pop);
            if (coreMaterial != null)
            {
                Color c = Color.Lerp(settings.coreColor, settings.sparkColor, pop * 0.5f);
                c.a = 1f;
                DragonFireball.ApplyColor(coreMaterial, c);
            }
        }

        Vector3 gravity = Physics.gravity * settings.gravity;
        for (int i = 0; i < shards.Count; i++)
        {
            Shard shard = shards[i];
            if (shard.Transform == null)
            {
                continue;
            }

            shard.Velocity += gravity * dt;
            shard.Transform.position += shard.Velocity * dt;
            shard.Transform.Rotate(shard.Angular * dt, Space.World);
            float shrink = Mathf.Lerp(1f, 0.05f, t * t);
            shard.Transform.localScale *= Mathf.Pow(shrink, dt * 2f);
            shards[i] = shard;
        }

        if (flashLight != null)
        {
            flashLight.intensity = Mathf.Lerp(settings.lightIntensity, 0f, t);
            flashLight.range = Mathf.Lerp(settings.lightRange, settings.lightRange * 0.3f, t);
        }

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (coreMaterial != null)
        {
            Destroy(coreMaterial);
        }

        if (sparkMaterial != null)
        {
            Destroy(sparkMaterial);
        }

        if (shardMaterial != null)
        {
            Destroy(shardMaterial);
        }
    }
}
