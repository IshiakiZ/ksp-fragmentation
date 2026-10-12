using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// The ground a wreck slides on: what it is (paving or soil), and what the wreck does to it.
    /// </summary>
    public static class Ground
    {
        /// <summary>
        /// What is drawn where a ray met a collider: the name of the material of the triangle it met, where the collider is a mesh that
        /// is also drawn (the space centre's runway, aprons and lawns are such meshes), or of the material drawn by the collider's own
        /// object. Empty where nothing could be read.
        /// </summary>
        public static string Surface(RaycastHit hit)
        {
            Collider c = hit.collider;
            if (c == null) return "";
            try
            {
                MeshCollider mc = c as MeshCollider;
                MeshFilter f = c.GetComponent<MeshFilter>();
                MeshRenderer r = c.GetComponent<MeshRenderer>();
                if (mc != null && f != null && r != null && mc.sharedMesh != null && mc.sharedMesh == f.sharedMesh && hit.triangleIndex >= 0 && mc.sharedMesh.isReadable)
                {
                    Mesh m = mc.sharedMesh;
                    int first = hit.triangleIndex * 3;
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        int start = (int)m.GetIndexStart(s), length = (int)m.GetIndexCount(s);
                        if (first >= start && first < start + length)
                        {
                            Material[] mats = r.sharedMaterials;
                            Material mat = mats != null && mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                            return Describe(mat, "triangle's");
                        }
                    }
                }
                if (r != null) return Describe(r.sharedMaterial, "object's");
                return "no drawing of its own" + (mc != null && mc.sharedMesh != null ? " (mesh '" + mc.sharedMesh.name + "'" + (mc.sharedMesh.isReadable ? "" : ", unreadable") + ")" : "");
            }
            catch (Exception ex) { return "(" + ex.Message + ")"; }
        }

        // ---- the space centre's lawns
        //
        // The space centre's ground is a few big meshes (one for each section of the runway, one round each building), each drawn with
        // grass and tarmac blended by a mask texture (the shader "KSP/Scenery/Diffuse Ground KSC": _BlendMaskTexture). Their colliders are
        // all tagged as the space centre's, lawns and aprons alike; what the ground is at a place is read from the mask there.

        sealed class Mask { public Color32[] px; public int w, h; public Vector2 scale, offset; }
        static readonly Dictionary<Material, Mask> masks = new Dictionary<Material, Mask>();
        static readonly RaycastHit[] under = new RaycastHit[8];
        static readonly int MaskId = Shader.PropertyToID("_BlendMaskTexture");

        /// <summary>The mask a material blends its grass and tarmac by, read once into memory (null: it has none, or it cannot be read).</summary>
        static Mask MaskOf(Material m)
        {
            if (m == null) return null;
            if (masks.TryGetValue(m, out Mask mask)) return mask;
            mask = null;
            try
            {
                Texture t = m.HasProperty(MaskId) ? m.GetTexture(MaskId) : null;
                if (t != null && t.width > 0 && t.height > 0)
                {
                    // (drawn into a small picture and read back: the game's own copy of a texture is not kept where a mod can read it)
                    int w = Mathf.Min(256, t.width), h = Mathf.Min(256, t.height);
                    RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    RenderTexture was = RenderTexture.active;
                    Graphics.Blit(t, rt);
                    RenderTexture.active = rt;
                    var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                    copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    copy.Apply(false);
                    RenderTexture.active = was;
                    RenderTexture.ReleaseTemporary(rt);
                    mask = new Mask { px = copy.GetPixels32(), w = w, h = h, scale = m.GetTextureScale(MaskId), offset = m.GetTextureOffset(MaskId) };
                    UnityEngine.Object.Destroy(copy);
                }
            }
            catch (Exception) { mask = null; }
            if (masks.Count > 64) masks.Clear();
            masks[m] = mask;
            return mask;
        }

        static Color32 At(Mask mask, Vector2 uv)
        {
            float u = uv.x * mask.scale.x + mask.offset.x, v = uv.y * mask.scale.y + mask.offset.y;
            u -= Mathf.Floor(u); v -= Mathf.Floor(v);
            int x = Mathf.Clamp((int)(u * mask.w), 0, mask.w - 1), y = Mathf.Clamp((int)(v * mask.h), 0, mask.h - 1);
            return mask.px[y * mask.w + x];
        }

        // (a collider that is also drawn: its materials and where each one's triangles begin, looked up once)
        sealed class Face { public Material[] mats; public int[] start, length; }
        static readonly Dictionary<Collider, Face> faces = new Dictionary<Collider, Face>();

        /// <summary>The material drawn where a ray met a mesh collider that is also drawn (null where it is not, or cannot be read).</summary>
        static Material Drawn(RaycastHit hit)
        {
            Collider c = hit.collider;
            if (c == null || hit.triangleIndex < 0) return null;
            if (!faces.TryGetValue(c, out Face face))
            {
                face = null;
                MeshCollider mc = c as MeshCollider;
                MeshFilter f = mc != null ? mc.GetComponent<MeshFilter>() : null;
                MeshRenderer r = mc != null ? mc.GetComponent<MeshRenderer>() : null;
                if (mc != null && mc.sharedMesh != null && f != null && r != null && mc.sharedMesh == f.sharedMesh && mc.sharedMesh.isReadable)
                {
                    Mesh m = mc.sharedMesh;
                    Material[] mats = r.sharedMaterials;
                    if (mats != null && mats.Length > 0)
                    {
                        face = new Face { mats = mats, start = new int[m.subMeshCount], length = new int[m.subMeshCount] };
                        for (int s = 0; s < m.subMeshCount; s++) { face.start[s] = (int)m.GetIndexStart(s); face.length[s] = (int)m.GetIndexCount(s); }
                    }
                }
                if (faces.Count > 400) faces.Clear();
                faces[c] = face;
            }
            if (face == null) return null;
            int first = hit.triangleIndex * 3;
            for (int s = 0; s < face.start.Length; s++)
                if (first >= face.start[s] && first < face.start[s] + face.length[s]) return face.mats[Mathf.Min(s, face.mats.Length - 1)];
            return face.mats[0];
        }

        /// <summary>
        /// Whether the space centre's ground at a place is lawn: what is drawn on top there is one of its ground meshes, and its mask says
        /// grass (the mask is laid by the mesh's second set of texture places: white where the tarmac is, black where the grass is; found out
        /// on 2026-10-10 on the runway, its painted lines, its shoulders and the lawn beyond). False for tarmac, and for whatever cannot be
        /// told (ground round a building whose drawn mesh is not its collider): that stays paving.
        /// </summary>
        public static bool Lawn(Vector3 at, Vector3 up)
        {
            int n = Physics.RaycastNonAlloc(at + up * 1.5f, -up, under, 4f, 1 << 15, QueryTriggerInteraction.Ignore);
            float top = float.MaxValue;
            for (int k = 0; k < n; k++) if (under[k].distance < top) top = under[k].distance;
            for (int k = 0; k < n; k++)
            {
                // (what is drawn on top: a ground mesh lying under another collider is not what is seen there)
                if (under[k].distance > top + 0.3f) continue;
                Material m = Drawn(under[k]);
                if (m == null) continue;
                Mask mask = MaskOf(m);
                if (mask == null) continue;
                return At(mask, under[k].textureCoord2).r < 100;
            }
            return false;
        }

        /// <summary>
        /// The space centre's ground masks read ahead of any crash near it (each is drawn small and read back from the graphics card once,
        /// a millisecond or two: better at a flight's start than in the step in which a wreck first touches that ground).
        /// </summary>
        public static void Prepare(Vector3 near)
        {
            SpaceCenter centre = SpaceCenter.Instance;
            Transform t = centre != null ? centre.SpaceCenterTransform : null;
            if (t == null || (t.position - near).sqrMagnitude > 15000f * 15000f) return;
            int read = 0;
            foreach (MeshCollider mc in t.GetComponentsInChildren<MeshCollider>(false))
            {
                MeshRenderer r = mc != null ? mc.GetComponent<MeshRenderer>() : null;
                if (r == null) continue;
                foreach (Material m in r.sharedMaterials)
                    if (m != null && read < 24 && !masks.ContainsKey(m) && m.HasProperty(MaskId)) { MaskOf(m); read++; }
            }
        }

#if DEV
        /// <summary>(development build) What the masks of the space centre's ground say under a place: for finding out which way round they are.</summary>
        public static string Masks(Vector3 at, Vector3 up)
        {
            var say = new System.Text.StringBuilder();
            int n = Physics.RaycastNonAlloc(at, -up, under, 2000f, 1 << 15, QueryTriggerInteraction.Ignore);
            say.Append(n).Append(" things under there. ");
            for (int k = 0; k < n; k++)
            {
                RaycastHit hit = under[k];
                Material m = Drawn(hit);
                if (m == null) continue;
                Mask mask = MaskOf(m);
                say.Append(hit.collider.name).Append(" (").Append(m.name).Append("): ");
                if (mask == null) { say.Append("no mask\n"); continue; }
                Texture t = m.GetTexture(MaskId);
                say.Append("mask ").Append(t.name).Append(' ').Append(t.width).Append('x').Append(t.height).Append(", scale ").Append(mask.scale.ToString("F2")).Append(" offset ").Append(mask.offset.ToString("F2"))
                   .Append("; uv ").Append(hit.textureCoord.ToString("F3")).Append(" -> ").Append(At(mask, hit.textureCoord)).Append("; uv2 ").Append(hit.textureCoord2.ToString("F3")).Append(" -> ").Append(At(mask, hit.textureCoord2)).Append('\n');
            }
            return say.Length > 0 ? say.ToString() : "nothing drawn with a mask under there";
        }
#endif

        static string Describe(Material m, string whose)
        {
            if (m == null) return whose + " material: none";
            Texture t = m.HasProperty("_MainTex") ? m.mainTexture : null;
            var say = new System.Text.StringBuilder(whose + " material '" + m.name + "', shader '" + (m.shader != null ? m.shader.name : "?") + "', texture '" + (t != null ? t.name : "-") + "'");
#if DEV
            // (what the shader is given: for telling the space centre's lawns from its concrete)
            try
            {
                Shader s = m.shader;
                for (int k = 0; s != null && k < s.GetPropertyCount(); k++)
                {
                    string name = s.GetPropertyName(k);
                    if (s.GetPropertyType(k) == UnityEngine.Rendering.ShaderPropertyType.Texture) { Texture tx = m.GetTexture(name); say.Append(' ').Append(name).Append('=').Append(tx != null ? tx.name : "-"); }
                }
            }
            catch (Exception) { }
#endif
            return say.ToString();
        }

        /// <summary>(development build) The colour the mesh a ray met gives its corners there (the space centre's ground blends its grass and its concrete by it, it may be), as the ray met it between them.</summary>
        public static string Corners(RaycastHit hit)
        {
            MeshCollider mc = hit.collider as MeshCollider;
            if (mc == null || mc.sharedMesh == null || hit.triangleIndex < 0) return "";
            try
            {
                Mesh m = mc.sharedMesh;
                if (!m.isReadable) return "unreadable";
                Color[] c = m.colors;
                if (c == null || c.Length == 0) return "no corner colours";
                int[] tris = m.triangles;
                int i = hit.triangleIndex * 3;
                Vector3 b = hit.barycentricCoordinate;
                Color mix = c[tris[i]] * b.x + c[tris[i + 1]] * b.y + c[tris[i + 2]] * b.z;
                return "corner colour " + mix.ToString("F2");
            }
            catch (Exception ex) { return "(" + ex.Message + ")"; }
        }
    }
}
