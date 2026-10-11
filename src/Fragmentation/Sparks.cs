using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>
    /// Sparks where steel is dragged along paving: an engine's bell and pumps, a heavy part's fittings. Few and short: a spark is a bit of
    /// hot metal torn off, white-yellow going orange and red in a fraction of a second, falling and skipping off the ground. Aluminium (most of
    /// a tank's skin) hardly sparks at all, so a tank scraping along makes grit and a dark scrape, and few sparks (see the Research page).
    /// </summary>
    public static class Sparks
    {
        static ParticleSystem system;
        static GameObject holder;
        static Texture2D dot;

        /// <summary>Throw so many sparks from a place, going as fast as v (they are thrown back off it and up), the ground's up being up.</summary>
        public static void Emit(Vector3 at, Vector3 v, Vector3 up, int n)
        {
            if (n <= 0) return;
            Ready();
            CelestialBody body = FlightGlobals.currentMainBody;
            Vector3 g = body != null ? (Vector3)FlightGlobals.getGeeForceAtPosition(at, body) : Vector3.down * 9.81f;
            ParticleSystem.ForceOverLifetimeModule force = system.forceOverLifetime;
            force.x = g.x; force.y = g.y; force.z = g.z;
            var e = new ParticleSystem.EmitParams();
            for (int k = 0; k < n; k++)
            {
                e.position = at + Random.insideUnitSphere * 0.15f;
                e.velocity = v * Random.Range(0.3f, 0.8f) + up * Random.Range(0.5f, 4f) + Random.insideUnitSphere * 3f;
                e.startLifetime = Random.Range(0.2f, 0.7f);
                e.startSize = Random.Range(0.02f, 0.05f);
                e.startColor = new Color(1f, Random.Range(0.75f, 0.95f), Random.Range(0.4f, 0.6f), 1f);
                system.Emit(e, 1);
            }
        }

        public static void Prepare() => Ready();

        static void Ready()
        {
            if (system != null) return;
            holder = new GameObject("Fragmentation sparks") { layer = 0 };
            system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.maxParticles = 400;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.ForceOverLifetimeModule force = system.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.45f), new GradientColorKey(new Color(0.7f, 0.12f, 0.02f), 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = grad;
            ParticleSystem.CollisionModule hit = system.collision;
            hit.enabled = true;
            hit.type = ParticleSystemCollisionType.World;
            hit.mode = ParticleSystemCollisionMode.Collision3D;
            hit.quality = ParticleSystemCollisionQuality.Low;
            hit.collidesWith = 1 << 15;
            hit.bounce = 0.35f;
            hit.dampen = 0.4f;
            hit.lifetimeLoss = 0.25f;
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.03f;
            renderer.lengthScale = 1.2f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Shader s = Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
            var m = new Material(s) { name = "Fragmentation sparks" };
            m.mainTexture = Dot();
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            renderer.sharedMaterial = m;
            system.Play(false);
            try { FloatingOrigin.RegisterParticleSystem(system); } catch (System.Exception) { }
        }

        static Texture2D Dot()
        {
            if (dot != null) return dot;
            dot = new Texture2D(16, 16, TextureFormat.RGBA32, false) { name = "Fragmentation spark", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[256];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float d = new Vector2(x - 7.5f, y - 7.5f).magnitude / 7.5f;
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    px[y * 16 + x] = new Color(1f, 1f, 1f, a);
                }
            dot.SetPixels(px);
            dot.Apply();
            return dot;
        }

        public static void Clear()
        {
            if (system != null) { try { FloatingOrigin.UnregisterParticleSystem(system); } catch (System.Exception) { } }
            if (holder != null) Object.Destroy(holder);
            system = null; holder = null;
        }
    }
}
