using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// What a crash sounds like (since 0.3.0). The game gives a crash the bang of each part that goes up, and nothing else: a wreck
    /// grinding a hundred metres down the runway did it in silence. Here what this mod works out is heard as it happens, from sounds
    /// the mod makes itself out of noise and ringing tones (no recordings: nothing to license, and nothing the same twice):
    ///
    ///   * the slide: thin aluminium dragged over concrete is a hiss of grit with a chatter in it (the skin skips as it catches) and a
    ///     crackle of small catches, over the deep rumble of the whole wreck; steel (an engine) dragged over concrete squeals as well,
    ///     and hisses brighter with its sparks; on soil there is no squeal, but a heavy rumble and the crunch of clods and turf. As loud
    ///     as the power the dragging takes (the force the ground holds the wreck back with times its speed: a few kilowatts a scuff, a
    ///     megawatt a big wreck at speed), higher as it goes faster;
    ///   * each blow: a thump, the crack of the skin giving way, the crumple of it folding (a run of small tearing bursts) and the ring
    ///     of the metal (a plate's modes, which are not a chord); on soil a duller thud and earth thrown; steel on paving a clang;
    ///   * metal tearing, where panels come away: the stick and slip of the tear running along the metal, a creak at a few hundred
    ///     catches a second, rung through the panel's own high tones, over the hiss of the rip.
    ///
    /// Each comes from where it happens, and from far off a little late, as sound does (343 metres a second). The sounds are made at the
    /// first crash watched, on another thread, in a second or so.
    ///
    /// Since 0.4.0, where the mod has its recordings (Keystone 0.10.0's Sounds: Sonniss's libraries, docs/sounds-plan.md), recordings of
    /// real crashes are heard in their place, each kind as soon as one of it is loaded: cars dropped on concrete and on steel plates and
    /// metal crashing for a blow on paving (with metal debris falling about), rock and earth thrown for one on soil, heavy steel struck
    /// for a clang, metal scraped and squealing for a tear, and a scrape, a grind and a rockslide for the three slides. A part striking
    /// the water keeps its made splash. The made sounds meanwhile, and wherever there are none.
    /// </summary>
    public static class Sounds
    {
        const int Rate = 44100;

        sealed class Bank { public float[][] crunch, thud, tear, clang, splash; public float[] paved, steel, soil; }
        static volatile Bank made;
        static int asked;
        static AudioClip[] crunch, thud, tear, clang, splash;
        static AudioClip pavedLoop, steelLoop, soilLoop;

        static GameObject holder;
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static int next;
        static float crunchAt = -1f, tearAt = -1f, clangAt = -1f, splashAt = -1f;
        static readonly System.Random luck = new System.Random(20261017);

        sealed class Loop
        {
            public AudioSource surface, steel;
            public bool paved;
            public float want, wantSteel, pitch, heard;
            public Vector3 at;
            public float last;
        }
        static readonly Dictionary<Wreck, Loop> loops = new Dictionary<Wreck, Loop>();
        static readonly List<Wreck> quiet = new List<Wreck>();

        static bool Wanted => Frag.On != null && Frag.On && Frag.Heard != null && Frag.Heard;

        /// <summary>The sounds made, on another thread, the first time a crash is watched.</summary>
        public static void Prepare()
        {
            if (!Wanted || Interlocked.Exchange(ref asked, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var b = new Bank { crunch = new float[6][], thud = new float[4][], tear = new float[6][], clang = new float[4][], splash = new float[4][] };
                    for (int k = 0; k < 6; k++) b.crunch[k] = Crunch(101 + k, false);
                    for (int k = 0; k < 4; k++) b.thud[k] = Crunch(201 + k, true);
                    for (int k = 0; k < 6; k++) b.tear[k] = Tear(301 + k);
                    for (int k = 0; k < 4; k++) b.clang[k] = Clang(401 + k);
                    for (int k = 0; k < 4; k++) b.splash[k] = Splash(601 + k);
                    b.paved = Grind(501, 0); b.steel = Grind(502, 1); b.soil = Grind(503, 2);
                    made = b;
                }
                catch (Exception ex) { Debug.LogError("[Fragmentation] could not make the crash's sounds: " + ex); }
            });
        }

        /// <summary>(Each frame.) The sounds made into clips once they are ready; each sliding wreck's grinding brought to how loud it should be.</summary>
        public static void Frame()
        {
            if (crunch == null && made != null) Clips(made);
            if (crunch != null && !recordedAsked) AskRecorded();
            if (loops.Count == 0) return;
            float now = Time.time, dt = Time.deltaTime;
            quiet.Clear();
            foreach (KeyValuePair<Wreck, Loop> one in loops)
            {
                Loop l = one.Value;
                bool sliding = now - l.last < 0.15f && Wanted;
                float to = sliding ? l.want : 0f, toSteel = sliding ? l.wantSteel : 0f;
                float k = 1f - Mathf.Exp(-dt / 0.08f);
                Level(l.surface, to, l.pitch, l.at, k);
                Level(l.steel, toSteel, l.pitch, l.at, k);
                if (!sliding && (l.surface == null || l.surface.volume < 0.002f) && (l.steel == null || l.steel.volume < 0.002f)) quiet.Add(one.Key);
            }
            foreach (Wreck w in quiet)
            {
                Loop l = loops[w];
                if (l.surface != null) UnityEngine.Object.Destroy(l.surface.gameObject);
                if (l.steel != null) UnityEngine.Object.Destroy(l.steel.gameObject);
                loops.Remove(w);
            }
        }

        static void Level(AudioSource s, float to, float pitch, Vector3 at, float k)
        {
            if (s == null) return;
            s.transform.position = at;
            s.volume += (to * GameSettings.SHIP_VOLUME - s.volume) * k;
            s.pitch = pitch;
            if (s.volume > 0.002f && !s.isPlaying) { s.timeSamples = luck.Next(0, Math.Max(1, s.clip.samples - 1)); s.Play(); }
        }

        /// <summary>
        /// (Each physics step a wreck slides.) Where it touches, how fast it goes, what power the ground's holding it back takes (watts),
        /// and how much of that is steel dragged (an engine's).
        /// </summary>
        public static void Slide(Wreck w, Vector3 at, float speed, float power, bool paved, float steelPower)
        {
            if (!Wanted || pavedLoop == null || w == null) return;
            if (!loops.TryGetValue(w, out Loop l))
            {
                if (loops.Count >= 3 || power < 3000f) return;
                l = new Loop();
                loops[w] = l;
            }
            l.last = Time.time;
            l.at = at;
            // (a few kilowatts is a scuff, faint; a megawatt, a big wreck at speed, as loud as these get)
            l.want = Mathf.Clamp01((Mathf.Log10(Mathf.Max(power, 1f)) - 3.4f) / 2.6f) * 0.9f;
            l.wantSteel = paved ? Mathf.Clamp01((Mathf.Log10(Mathf.Max(steelPower, 1f)) - 3.4f) / 2.4f) * 0.75f : 0f;
            l.pitch = Mathf.Lerp(0.72f, 1.25f, Mathf.Clamp01(speed / 70f));
            if (l.surface == null || l.paved != paved)
            {
                if (l.surface != null) UnityEngine.Object.Destroy(l.surface.gameObject);
                l.surface = Source(paved ? pavedLoop : soilLoop, true, "Fragmentation grinding");
                l.paved = paved;
            }
            if (l.steel == null && l.wantSteel > 0.01f) l.steel = Source(steelLoop, true, "Fragmentation steel dragged");
        }

        /// <summary>A blow: where, how fast into the ground (m/s), the mass of what struck (tonnes), on paving or not, steel or not.</summary>
        public static void Hit(Vector3 at, float into, float mass, bool paved, bool steel)
        {
            if (!Wanted || crunch == null) return;
            float energy = 500f * Mathf.Max(mass, 0.01f) * into * into;
            float loud = Mathf.Clamp01((Mathf.Log10(Mathf.Max(energy, 1f)) - 3f) / 3.3f);
            if (loud < 0.05f) return;
            float now = Time.time;
            if (steel && paved)
            {
                if (now - clangAt < 0.12f) return;
                clangAt = now;
                Play(clang[luck.Next(clang.Length)], at, loud, 0.9f + 0.2f * (float)luck.NextDouble());
                return;
            }
            if (now - crunchAt < 0.06f) return;
            crunchAt = now;
            AudioClip[] from = paved ? crunch : thud;
            Play(from[luck.Next(from.Length)], at, loud, 0.85f + 0.3f * (float)luck.NextDouble() - 0.1f * Mathf.Clamp01(mass / 5f));
        }

        /// <summary>A part striking the water: where, how fast into it (m/s), its mass (tonnes) and how much of it meets the water (m²).</summary>
        public static void Splash(Vector3 at, float into, float mass, float face)
        {
            if (!Wanted || splash == null) return;
            float energy = 500f * Mathf.Max(mass, 0.01f) * into * into;
            float loud = Mathf.Clamp01((Mathf.Log10(Mathf.Max(energy, 1f)) - 3f) / 3.3f) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(face / 6f));
            if (loud < 0.05f) return;
            float now = Time.time;
            if (now - splashAt < 0.1f) return;
            splashAt = now;
            Play(splash[luck.Next(splash.Length)], at, loud, 0.8f + 0.3f * (float)luck.NextDouble() - 0.15f * Mathf.Clamp01(face / 10f));
        }

        /// <summary>Metal torn: where, and how much of it (panels).</summary>
        public static void Tear(Vector3 at, float much)
        {
            if (!Wanted || tear == null || much <= 0f) return;
            float now = Time.time;
            if (now - tearAt < 0.08f) return;
            tearAt = now;
            Play(tear[luck.Next(tear.Length)], at, Mathf.Clamp01(0.35f + 0.18f * Mathf.Sqrt(much)), 0.85f + 0.35f * (float)luck.NextDouble());
        }

        static void Play(AudioClip clip, Vector3 at, float loud, float pitch)
        {
            if (clip == null) return;
            if (holder == null) holder = new GameObject("Fragmentation sounds");
            while (pool.Count < 10) pool.Add(Source(null, false, "Fragmentation sound"));
            AudioSource s = null;
            for (int k = 0; k < pool.Count; k++)
            {
                AudioSource c = pool[(next + k) % pool.Count];
                if (c != null && !c.isPlaying) { s = c; next = (next + k + 1) % pool.Count; break; }
            }
            if (s == null) { s = pool[next]; next = (next + 1) % pool.Count; }
            if (s == null) return;
            s.transform.position = at;
            s.clip = clip;
            s.volume = loud * GameSettings.SHIP_VOLUME;
            s.pitch = pitch;
            // (heard as late as sound takes to get from there to the camera)
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            float far = cam != null ? (cam.transform.position - at).magnitude : 0f;
            s.PlayDelayed(far / 343f);
        }

        static AudioSource Source(AudioClip clip, bool loop, string name)
        {
            if (holder == null) holder = new GameObject("Fragmentation sounds");
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = loop ? 8f : 6f;
            s.maxDistance = 3000f;
            s.dopplerLevel = 0f;
            s.spread = 60f;
            s.volume = 0f;
            return s;
        }

        // ---- the recordings (since 0.4.0): each kind's clips taken over by its recordings as they load
        static bool recordedAsked;
        static readonly Dictionary<string, List<AudioClip>> recordedKinds = new Dictionary<string, List<AudioClip>>();

        static void AskRecorded()
        {
            recordedAsked = true;
            try { AskRecordings(); }
            catch (Exception) { }      // (a Keystone older than 0.10.0: no Sounds there, the made sounds stay)
        }

        static void AskRecordings()
        {
            foreach (string name in Keystone.Sounds.Names("Fragmentation"))
            {
                string kind = name.StartsWith("crunch") || name.StartsWith("debris") ? "crunch" : name.StartsWith("thud") ? "thud" : name.StartsWith("tear") ? "tear"
                            : name.StartsWith("clang") ? "clang" : name == "slide-paved" || name == "slide-steel" || name == "slide-soil" ? name : null;
                if (kind == null) continue;
                Keystone.Sounds.Get("Fragmentation", name, clip =>
                {
                    if (clip == null) return;
                    if (!recordedKinds.TryGetValue(kind, out List<AudioClip> have)) recordedKinds[kind] = have = new List<AudioClip>();
                    have.Add(clip);
                    switch (kind)
                    {
                        case "crunch": crunch = have.ToArray(); break;
                        case "thud": thud = have.ToArray(); break;
                        case "tear": tear = have.ToArray(); break;
                        case "clang": clang = have.ToArray(); break;
                        case "slide-paved": pavedLoop = clip; break;
                        case "slide-steel": steelLoop = clip; break;
                        case "slide-soil": soilLoop = clip; break;
                    }
                });
            }
        }

        static void Clips(Bank b)
        {
            crunch = Make(b.crunch, "Fragmentation crunch");
            thud = Make(b.thud, "Fragmentation thud");
            tear = Make(b.tear, "Fragmentation tear");
            clang = Make(b.clang, "Fragmentation clang");
            splash = Make(b.splash, "Fragmentation splash");
            pavedLoop = Make(b.paved, "Fragmentation grinding on paving");
            steelLoop = Make(b.steel, "Fragmentation steel on paving");
            soilLoop = Make(b.soil, "Fragmentation ploughing soil");
        }

        static AudioClip[] Make(float[][] all, string name)
        {
            var clips = new AudioClip[all.Length];
            for (int k = 0; k < all.Length; k++) clips[k] = Make(all[k], name + " " + (k + 1));
            return clips;
        }

        static AudioClip Make(float[] data, string name)
        {
            AudioClip c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        public static void Clear()
        {
            foreach (Loop l in loops.Values)
            {
                if (l.surface != null) UnityEngine.Object.Destroy(l.surface.gameObject);
                if (l.steel != null) UnityEngine.Object.Destroy(l.steel.gameObject);
            }
            loops.Clear();
            pool.Clear();
            if (holder != null) UnityEngine.Object.Destroy(holder);
            holder = null;
        }

        // ================================================================== the sounds themselves

        /// <summary>A filter of the usual kind (Robert Bristow-Johnson's): low-pass, high-pass, or band-pass with its peak at 1.</summary>
        struct Filter
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            static Filter From(float b0, float b1, float b2, float a0, float a1, float a2) =>
                new Filter { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };

            public static Filter Low(float f, float q)
            {
                float w = 2f * Mathf.PI * f / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q);
                return From((1f - c) / 2f, 1f - c, (1f - c) / 2f, 1f + a, -2f * c, 1f - a);
            }

            public static Filter High(float f, float q)
            {
                float w = 2f * Mathf.PI * f / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q);
                return From((1f + c) / 2f, -(1f + c), (1f + c) / 2f, 1f + a, -2f * c, 1f - a);
            }

            public static Filter Band(float f, float q)
            {
                float w = 2f * Mathf.PI * f / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q);
                return From(a, 0f, -a, 1f + a, -2f * c, 1f - a);
            }

            public float Run(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        sealed class Luck
        {
            readonly System.Random r;
            public Luck(int seed) { r = new System.Random(seed); }
            public float N() => (float)(r.NextDouble() * 2.0 - 1.0);
            public float U(float a, float b) => a + (float)r.NextDouble() * (b - a);
            public bool Chance(float p) => r.NextDouble() < p;
        }

        /// <summary>
        /// A blow: a thump with its pitch falling, the crack of the skin giving way, the crumple of it folding (small tearing bursts over a
        /// third of a second) and the ring of the metal, a plate's modes (1, 2.76, 5.40, 8.93 times the lowest: not a chord). Soft, on soil:
        /// a deeper, longer thud, hardly a crack, no ring, and the patter of earth thrown and falling.
        /// </summary>
        static float[] Crunch(int seed, bool soft)
        {
            var r = new Luck(seed);
            int n = (int)(Rate * (soft ? 1.1f : 0.9f));
            var o = new float[n];
            float low = r.U(soft ? 42f : 55f, soft ? 70f : 95f), ring = r.U(210f, 480f);
            float[] modes = { 1f, 2.76f, 5.40f, 8.93f }, rings = { 0.3f, 0.19f, 0.12f, 0.075f }, amps = { 0.2f, 0.15f, 0.1f, 0.06f };
            float[] phase = new float[4];
            for (int k = 0; k < 4; k++) phase[k] = r.U(0f, 6.2832f);
            Filter crack = Filter.High(1300f, 0.7f), crumple = Filter.Band(r.U(650f, 1500f), 0.9f), crumple2 = Filter.Band(r.U(1800f, 3200f), 1.2f), earth = Filter.Low(700f, 0.7f);
            // (the crumple: bursts at random within its first third of a second, each dying away in tens of milliseconds)
            int bursts = soft ? 5 : 11;
            var at = new int[bursts]; var amp = new float[bursts]; var keep = new float[bursts]; var env = new float[bursts];
            for (int b = 0; b < bursts; b++)
            {
                at[b] = b == 0 ? 0 : (int)(Rate * r.U(0.005f, soft ? 0.25f : 0.34f));
                amp[b] = r.U(0.35f, 1f) * (b == 0 ? 1.4f : 1f);
                keep[b] = Mathf.Exp(-1f / (Rate * r.U(0.012f, 0.045f)));
            }
            // (earth thrown up falls back over most of a second: a patter of small thuds)
            float patterDue = 0f, patter = 0f, patterKeep = Mathf.Exp(-1f / (Rate * 0.01f));
            float thumpPhase = 0f, thump = 1f, thumpKeep = Mathf.Exp(-1f / (Rate * (soft ? 0.14f : 0.085f)));
            float crackEnv = 1f, crackKeep = Mathf.Exp(-1f / (Rate * 0.006f));
            var ringEnv = new float[4]; var ringKeep = new float[4];
            for (int k = 0; k < 4; k++) { ringEnv[k] = 1f; ringKeep[k] = Mathf.Exp(-1f / (Rate * rings[k])); }
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                thumpPhase += 2f * Mathf.PI * low * (1f + 0.7f * Mathf.Exp(-t / 0.02f)) / Rate;
                float y = 0.9f * Mathf.Sin(thumpPhase) * thump;
                thump *= thumpKeep;
                float noise = r.N();
                y += (soft ? 0.3f : 1.1f) * crack.Run(noise) * crackEnv;
                crackEnv *= crackKeep;
                float e = 0f;
                for (int b = 0; b < bursts; b++)
                {
                    if (i == at[b]) env[b] = amp[b];
                    e += env[b];
                    env[b] *= keep[b];
                }
                y += (soft ? 1.0f : 2.2f) * crumple.Run(noise) * e + (soft ? 0.2f : 1.0f) * crumple2.Run(r.N()) * e;
                if (!soft)
                    for (int k = 0; k < 4; k++) { y += amps[k] * Mathf.Sin(phase[k] + 2f * Mathf.PI * ring * modes[k] * t) * ringEnv[k]; ringEnv[k] *= ringKeep[k]; }
                else
                {
                    patterDue += r.U(0f, 2f) * 60f / Rate * Mathf.Clamp01(1f - t / 1f) * Mathf.Clamp01((t - 0.08f) * 10f);
                    if (patterDue >= 1f) { patterDue -= 1f; patter = r.U(0.2f, 0.7f); }
                    y += 1.6f * earth.Run(r.N()) * patter;
                    patter *= patterKeep;
                }
                o[i] = y;
            }
            Finish(o, 0.9f, 0.02f);
            return o;
        }

        /// <summary>
        /// Metal tearing: the tear runs along the panel in jerks (stick and slip), a creak at some tens to a couple of hundred catches a
        /// second that wanders as it goes, rung through two of the panel's high tones; over it the hiss of the rip itself; rising quickly,
        /// going on unevenly, dying away.
        /// </summary>
        static float[] Tear(int seed)
        {
            var r = new Luck(seed);
            float length = r.U(0.4f, 0.85f);
            int n = (int)(Rate * length);
            var o = new float[n];
            Filter one = Filter.Band(r.U(1200f, 2100f), r.U(3f, 6f)), two = Filter.Band(r.U(2300f, 3800f), r.U(4f, 8f)), rip = Filter.Band(r.U(3000f, 5500f), 0.8f);
            float rate = r.U(45f, 110f), nextCatch = 0f, pulse = 0f, pulseKeep = Mathf.Exp(-1f / (Rate * 0.0012f));
            float strength = 0.7f, strengthTo = 0.8f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                if (i % 441 == 0) strengthTo = r.U(0.35f, 1f);
                strength += (strengthTo - strength) * 0.0015f;
                if (t >= nextCatch)
                {
                    pulse = r.U(0.5f, 1f);
                    rate = Mathf.Clamp(rate + r.N() * 30f, 35f, 230f);
                    nextCatch = t + r.U(0.75f, 1.25f) / rate;
                }
                float p = pulse * r.N();
                pulse *= pulseKeep;
                float shape = Mathf.Clamp01(t / 0.025f) * Mathf.Clamp01((length - t) / 0.18f);
                o[i] = (3.2f * one.Run(p) + 2.4f * two.Run(p) + 0.5f * rip.Run(r.N())) * strength * shape;
            }
            Finish(o, 0.8f, 0.01f);
            return o;
        }

        /// <summary>
        /// Something striking water fast: a slap (the face of it meeting the water: a sharp burst and a deep thump), the roar of the water
        /// thrown up, and over a second and more the patter of it falling back, drop by drop, and the gulp of the hollow closing.
        /// </summary>
        static float[] Splash(int seed)
        {
            var r = new Luck(seed);
            int n = (int)(Rate * 2.2f);
            var o = new float[n];
            Filter slap = Filter.High(900f, 0.7f), roar = Filter.Band(r.U(500f, 900f), 0.6f), spray = Filter.Band(r.U(2500f, 4500f), 0.9f), drop = Filter.Band(r.U(1200f, 2200f), 6f), gulp = Filter.Low(220f, 0.8f);
            float slapEnv = 1f, slapKeep = Mathf.Exp(-1f / (Rate * 0.005f)), thump = 1f, thumpKeep = Mathf.Exp(-1f / (Rate * 0.07f)), low = r.U(60f, 95f);
            float rain = 0f, rainKeep = Mathf.Exp(-1f / (Rate * 0.006f)), gulpAt = r.U(0.25f, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float y = 1.3f * slap.Run(r.N()) * slapEnv + 0.9f * Mathf.Sin(2f * Mathf.PI * low * t) * thump;
                slapEnv *= slapKeep; thump *= thumpKeep;
                // (the water thrown up: a roar rising in a moment, dying over most of a second)
                float thrown = Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t / 0.35f);
                y += 2.0f * roar.Run(r.N()) * thrown + 1.1f * spray.Run(r.N()) * thrown;
                // (falling back: drops, each a little ringing click, thick at first and thinning out)
                float falling = Mathf.Clamp01((t - 0.15f) / 0.2f) * Mathf.Exp(-(t - 0.15f) / 0.7f);
                if (r.Chance(400f * falling / Rate)) rain = r.U(0.3f, 1f);
                y += 2.4f * drop.Run(rain * r.N()) * falling;
                rain *= rainKeep;
                // (the hollow it made closing: a deep gulp)
                float g = t > gulpAt ? Mathf.Exp(-(t - gulpAt) / 0.12f) * Mathf.Clamp01((t - gulpAt) / 0.02f) : 0f;
                y += 1.5f * gulp.Run(r.N()) * g;
                o[i] = y;
            }
            Finish(o, 0.9f, 0.1f);
            return o;
        }

        /// <summary>Steel struck on paving: a click, then a bell's ring of five not quite harmonic tones dying away over a second.</summary>
        static float[] Clang(int seed)
        {
            var r = new Luck(seed);
            int n = (int)(Rate * 1.5f);
            var o = new float[n];
            float f = r.U(300f, 700f);
            float[] modes = { 1f, 1.98f, 2.94f, 4.12f, 5.43f }, rings = { 0.95f, 0.65f, 0.45f, 0.3f, 0.2f }, amps = { 0.3f, 0.22f, 0.16f, 0.12f, 0.08f };
            var env = new float[5]; var keep = new float[5]; var phase = new float[5];
            for (int k = 0; k < 5; k++) { env[k] = 1f; keep[k] = Mathf.Exp(-1f / (Rate * rings[k] * r.U(0.8f, 1.2f))); phase[k] = r.U(0f, 6.2832f); modes[k] *= 1f + 0.01f * r.N(); }
            Filter click = Filter.High(2000f, 0.7f);
            float clickEnv = 1f, clickKeep = Mathf.Exp(-1f / (Rate * 0.003f)), thump = 1f, thumpKeep = Mathf.Exp(-1f / (Rate * 0.05f));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float y = 0.8f * click.Run(r.N()) * clickEnv + 0.5f * Mathf.Sin(2f * Mathf.PI * 90f * t) * thump;
                clickEnv *= clickKeep; thump *= thumpKeep;
                for (int k = 0; k < 5; k++) { y += amps[k] * Mathf.Sin(phase[k] + 2f * Mathf.PI * f * modes[k] * t) * env[k]; env[k] *= keep[k]; }
                o[i] = y;
            }
            Finish(o, 0.85f, 0.03f);
            return o;
        }

        /// <summary>
        /// A slide's grinding, three seconds that run on into themselves: 0, a skin dragged over paving (rumble, a hiss of grit that
        /// chatters, a crackle of catches); 1, steel over paving (squealing tones that wander and come and go, a bright hiss of sparks and
        /// grit, harsher catches); 2, ploughing soil (a heavy rumble, the crunch of clods coming and going, roots and turf torn).
        /// </summary>
        static float[] Grind(int seed, int kind)
        {
            var r = new Luck(seed);
            int n = Rate * 3, fade = Rate / 4;
            var o = new float[n + fade];
            Filter rumble = Filter.Low(kind == 2 ? 180f : 110f, 0.7f), hiss = Filter.Band(kind == 1 ? 5200f : kind == 2 ? 600f : 2600f, kind == 2 ? 0.8f : 0.6f);
            Filter catchRing = Filter.Band(kind == 2 ? 1500f : 3200f, kind == 2 ? 1.2f : 5f);
            float chatter = 0f, chatterRate = r.U(8f, 18f), grain = 0f, grainKeep = Mathf.Exp(-1f / (Rate * (kind == 2 ? 0.03f : 0.004f)));
            float catches = kind == 1 ? 160f : kind == 2 ? 28f : 90f;
            float squealA = r.U(1100f, 1500f), squealB = r.U(1500f, 1900f), wanderA = 0f, wanderB = 0f, squealPhaseA = 0f, squealPhaseB = 0f, squeal = 0f, squealTo = 0f;
            float swell = 0.8f, swellTo = 0.8f;
            for (int i = 0; i < o.Length; i++)
            {
                float t = i / (float)Rate;
                if (i % 2205 == 0) { swellTo = r.U(0.55f, 1f); squealTo = r.Chance(0.4f) ? r.U(0.3f, 1f) : 0f; chatterRate = Mathf.Clamp(chatterRate + r.N() * 3f, 6f, 22f); }
                swell += (swellTo - swell) * 0.0008f;
                squeal += (squealTo - squeal) * 0.0006f;
                chatter += 2f * Mathf.PI * chatterRate / Rate;
                float y = (kind == 2 ? 2.2f : 0.9f) * rumble.Run(r.N());
                float skip = kind == 2 ? 1f : 0.6f + 0.4f * Mathf.Sin(chatter + 0.6f * Mathf.Sin(chatter * 0.37f));
                y += (kind == 1 ? 1.8f : kind == 2 ? 0.9f : 1.7f) * hiss.Run(r.N()) * skip * swell;
                if (r.Chance(catches / Rate)) grain = r.U(0.3f, 1f);
                y += (kind == 2 ? 1.4f : 2.6f) * catchRing.Run(grain * r.N()) * (kind == 2 ? 1f : 1f);
                grain *= grainKeep;
                if (kind == 1)
                {
                    wanderA += (r.N() * 0.002f - wanderA * 0.0005f); wanderB += (r.N() * 0.002f - wanderB * 0.0005f);
                    squealPhaseA += 2f * Mathf.PI * squealA * (1f + Mathf.Clamp(wanderA, -0.08f, 0.08f)) / Rate;
                    squealPhaseB += 2f * Mathf.PI * squealB * (1f + Mathf.Clamp(wanderB, -0.08f, 0.08f)) / Rate;
                    y += 0.22f * squeal * (Mathf.Sin(squealPhaseA) + 0.6f * Mathf.Sin(squealPhaseB));
                }
                o[i] = y * swell;
            }
            // (the last quarter of a second laid over the first, so that it runs on into itself with no join to be heard)
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                o[i] = o[i] * w + o[n + i] * (1f - w);
            }
            Array.Resize(ref o, n);
            Finish(o, 0.75f, 0f);
            return o;
        }

        /// <summary>Brought to so much at its loudest, and its end faded over so many seconds.</summary>
        static void Finish(float[] o, float peak, float fadeOut)
        {
            float most = 1e-6f;
            for (int i = 0; i < o.Length; i++) most = Mathf.Max(most, Mathf.Abs(o[i]));
            float k = peak / most;
            int tail = (int)(fadeOut * Rate);
            for (int i = 0; i < o.Length; i++)
            {
                float f = tail > 0 && i > o.Length - tail ? (o.Length - i) / (float)tail : 1f;
                o[i] *= k * f;
            }
        }
    }
}
