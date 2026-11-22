using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fragmentation
{
    /// <summary>
    /// (Since 2026-11-19.) The colours of the parts' own paint, read small from their textures: so that the shreds of skin a crash
    /// throws are the colour of the skin they tore from, white off a white tank, orange off an orange one, black off its black bands
    /// (see Hull.SkinAt, Bits.Skin). NASA's photographs of the Morpheus lander's crash of 9 August 2012 (KSC-2012-4345 and -4346, in
    /// `pictures/reference-crash-wreckage/`) and the NTSB's of Atlas Air 3591's field of wreckage show the ground strewn with white and
    /// light grey shreds and a few dark ones; ours were all one grey, among darker metal and black.
    /// Read without stopping the game: each texture is drawn into a small picture of its own and read back when the graphics card
    /// has done it (AsyncGPUReadback), a few frames later; the craft's own are asked for when it is first flown or watched. Until a
    /// texture is read, and where it cannot be, there is no colour, and a shred is the grey it always was.
    /// </summary>
    internal static class Paints
    {
        // (128 across: at 64 each colour took in the panel lines and trim round it, and a shred of the Kerbal X's white was
        // 0.61 where its clean paint is 0.70; at 16, its black bands and white were one grey. 64 kB a texture kept.)
        const int Side = 128;
        sealed class Read { public Color32[] px; }
        static readonly Dictionary<Texture, Read> read = new Dictionary<Texture, Read>();
        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        // (which way up the pictures come back: the graphics card's own order of rows is not the same on every system, so a picture
        // of known halves is read back first, and the rest wait for it. 0 not known yet, 1 as a texture's rows, -1 the other way up)
        static int order;
        static bool testing, failed;
        public static int Asked, Got;

        /// <summary>Every texture of the craft's parts asked to be read now, so that a crash finds them read.</summary>
        public static void Warm(Vessel v)
        {
            if (v == null || v.parts == null) return;
            try
            {
                foreach (Part p in v.parts)
                {
                    if (p == null) continue;
                    foreach (Renderer r in p.FindModelComponents<Renderer>())
                    {
                        if (r == null || r is ParticleSystemRenderer) continue;
                        foreach (Material m in r.sharedMaterials) Ask(m);
                    }
                }
            }
            catch (Exception ex) { Frag.Log("could not read the paint of " + v.GetDisplayName() + ": " + ex.Message); }
        }

        /// <summary>A material's texture asked to be read, if it is not yet.</summary>
        public static void Ask(Material m)
        {
            if (failed || m == null || !m.HasProperty(MainTex)) return;
            Texture t = m.GetTexture(MainTex);
            if (t == null || read.ContainsKey(t)) return;
            if (order == 0 && !testing) Test();
            if (failed) return;
            var entry = new Read();
            read[t] = entry;
            Draw(t, px => entry.px = px);
        }

        /// <summary>
        /// The colour of a material's texture at a place on it (texture coordinates, as its mesh has them), its own colour left out (see
        /// Hull.SkinAt). False: it is not read, not yet or not ever.
        /// </summary>
        public static bool At(Material m, Vector2 uv, out Color c)
        {
            c = Color.white;
            if (order == 0 || m == null || !m.HasProperty(MainTex)) return false;
            Texture t = m.GetTexture(MainTex);
            if (t == null) return false;
            if (!read.TryGetValue(t, out Read entry)) { Ask(m); return false; }
            if (entry.px == null) return false;
            Vector2 scale = m.GetTextureScale(MainTex), offset = m.GetTextureOffset(MainTex);
            float u = uv.x * scale.x + offset.x, w = uv.y * scale.y + offset.y;
            u -= Mathf.Floor(u); w -= Mathf.Floor(w);
            int x = Mathf.Min(Side - 1, (int)(u * Side)), y = Mathf.Min(Side - 1, (int)(w * Side));
            if (order < 0) y = Side - 1 - y;
            c = entry.px[y * Side + x];
            return true;
        }

        /// <summary>
        /// A texture drawn small (the graphics card takes the mipmap that fits, an average of each 128th across) and read back;
        /// null to got if it could not be. Only a system that cannot read back at all stops the reading.
        /// </summary>
        static void Draw(Texture t, Action<Color32[]> got)
        {
            if (!SystemInfo.supportsAsyncGPUReadback) { failed = true; got(null); return; }
            RenderTexture rt = null;
            try
            {
                rt = RenderTexture.GetTemporary(Side, Side, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(t, rt);
                Asked++;
                RenderTexture held = rt;
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                {
                    Color32[] px = null;
                    try { if (!req.hasError && req.done) { px = req.GetData<Color32>().ToArray(); Got++; } }
                    catch (Exception) { px = null; }
                    RenderTexture.ReleaseTemporary(held);
                    got(px != null && px.Length >= Side * Side ? px : null);
                });
            }
            catch (Exception ex)
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                Frag.Log("could not read the paint of " + (t != null ? t.name : "a texture") + ": " + ex.Message);
                got(null);
            }
        }

        /// <summary>A picture black below and white above, read back to learn which way up the rows come.</summary>
        static void Test()
        {
            testing = true;
            var tex = new Texture2D(Side, Side, TextureFormat.RGBA32, false) { name = "Fragmentation paint test", filterMode = FilterMode.Point };
            var px = new Color32[Side * Side];
            for (int k = 0; k < px.Length; k++) px[k] = k / Side < Side / 2 ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false);
            Draw(tex, back =>
            {
                if (back != null) order = back[0].r < 128 && back[back.Length - 1].r >= 128 ? 1 : back[0].r >= 128 && back[back.Length - 1].r < 128 ? -1 : 0;
                if (order == 0) { failed = true; Frag.Log("cannot read the parts' paint: the test picture " + (back == null ? "could not be read back" : "came back neither way up")); }
                testing = false;
                UnityEngine.Object.Destroy(tex);
            });
        }

        /// <summary>For the development build's readouts.</summary>
        public static string Say() => "paint: " + read.Count + " textures, " + Asked + " asked, " + Got + " read, rows " + (order > 0 ? "as a texture's" : order < 0 ? "the other way up" : "not known") + (failed ? ", failed" : "");
    }
}
