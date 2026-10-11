using System;
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
