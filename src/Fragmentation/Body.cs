using UnityEngine;

namespace Fragmentation
{
    /// <summary>A three by three matrix: enough for the turning of a rigid body.</summary>
    public struct Mat3
    {
        public float a, b, c, d, e, f, g, h, i;      // rows: (a b c) (d e f) (g h i)

        public Vector3 Mul(Vector3 v) => new Vector3(a * v.x + b * v.y + c * v.z, d * v.x + e * v.y + f * v.z, g * v.x + h * v.y + i * v.z);

        /// <summary>A point mass at r from the middle: m (r.r - r r).</summary>
        public void AddPoint(float m, Vector3 r)
        {
            float rr = r.sqrMagnitude;
            a += m * (rr - r.x * r.x); b -= m * r.x * r.y; c -= m * r.x * r.z;
            d -= m * r.y * r.x; e += m * (rr - r.y * r.y); f -= m * r.y * r.z;
            g -= m * r.z * r.x; h -= m * r.z * r.y; i += m * (rr - r.z * r.z);
        }

        /// <summary>A body's own turning, given along its own axes, turned into the scene's.</summary>
        public void AddTurned(Quaternion q, Vector3 own)
        {
            Outer(q * Vector3.right, own.x); Outer(q * Vector3.up, own.y); Outer(q * Vector3.forward, own.z);
        }

        void Outer(Vector3 u, float s)
        {
            a += s * u.x * u.x; b += s * u.x * u.y; c += s * u.x * u.z;
            d += s * u.y * u.x; e += s * u.y * u.y; f += s * u.y * u.z;
            g += s * u.z * u.x; h += s * u.z * u.y; i += s * u.z * u.z;
        }

        public Mat3 Inverse()
        {
            float det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            if (Mathf.Abs(det) < 1e-12f) return new Mat3 { a = 1e-3f, e = 1e-3f, i = 1e-3f };
            float k = 1f / det;
            return new Mat3
            {
                a = (e * i - f * h) * k, b = (c * h - b * i) * k, c = (b * f - c * e) * k,
                d = (f * g - d * i) * k, e = (a * i - c * g) * k, f = (c * d - a * f) * k,
                g = (d * h - e * g) * k, h = (b * g - a * h) * k, i = (a * e - b * d) * k,
            };
        }
    }

    /// <summary>
    /// A craft taken as one rigid body: its mass, where its middle is, how it is moving and turning, and how hard it
    /// is to turn. The game holds a craft together part by part with springs; for a blow that lasts one step of the
    /// physics, and for the drag of the ground along it, the craft is as good as rigid, and pushing it as one body
    /// (every part given the motion it would have as part of that body) leaves no strain in the springs to shake it
    /// apart or set it bouncing.
    /// </summary>
    public sealed class Body
    {
        public float M;
        public Vector3 C, V, W;
        public Mat3 Iinv;

        /// <summary>Work it out from the parts as they are now. False if the craft has nothing to push.</summary>
        public bool Of(Vessel v)
        {
            M = 0f;
            Vector3 middle = Vector3.zero, momentum = Vector3.zero;
            foreach (Part p in v.parts)
            {
                Rigidbody rb = p != null ? p.rb : null;
                if (rb == null) continue;
                float m = rb.mass;
                M += m;
                middle += rb.worldCenterOfMass * m;
                momentum += rb.velocity * m;
            }
            if (M <= 1e-5f) return false;
            C = middle / M;
            V = momentum / M;
            var I = new Mat3();
            Vector3 spin = Vector3.zero;
            foreach (Part p in v.parts)
            {
                Rigidbody rb = p != null ? p.rb : null;
                if (rb == null) continue;
                Vector3 r = rb.worldCenterOfMass - C;
                I.AddPoint(rb.mass, r);
                I.AddTurned(rb.rotation * rb.inertiaTensorRotation, rb.inertiaTensor);
                spin += Vector3.Cross(r, rb.velocity - V) * rb.mass;
                // (the part's own turning, about its own middle)
                Quaternion q = rb.rotation * rb.inertiaTensorRotation;
                Vector3 local = Quaternion.Inverse(q) * rb.angularVelocity;
                spin += q * Vector3.Scale(rb.inertiaTensor, local);
            }
            Iinv = I.Inverse();
            W = Iinv.Mul(spin);
            return true;
        }

        public Vector3 At(Vector3 point) => V + Vector3.Cross(W, point - C);

        /// <summary>How much mass a push at that point, that way, has to move: less than the whole far out at the end of a long craft, which turns as well as goes.</summary>
        public float MassAlong(Vector3 point, Vector3 way)
        {
            Vector3 r = point - C;
            float k = 1f / M + Vector3.Dot(way, Vector3.Cross(Iinv.Mul(Vector3.Cross(r, way)), r));
            return k > 1e-6f ? 1f / k : M;
        }

        /// <summary>A push (tonne metres a second) at a point.</summary>
        public void Push(Vector3 point, Vector3 impulse)
        {
            V += impulse / M;
            W += Iinv.Mul(Vector3.Cross(point - C, impulse));
        }

        /// <summary>Give the parts the change in motion since the body was worked out (it was then going at V0 and turning at W0).</summary>
        public void Commit(Vessel v, Vector3 V0, Vector3 W0)
        {
            Vector3 dV = V - V0, dW = W - W0;
            if (dV.sqrMagnitude < 1e-10f && dW.sqrMagnitude < 1e-10f) return;
            foreach (Part p in v.parts)
            {
                Rigidbody rb = p != null ? p.rb : null;
                if (rb == null || rb.isKinematic) continue;
                rb.velocity += dV + Vector3.Cross(dW, rb.worldCenterOfMass - C);
                rb.angularVelocity += dW;
            }
        }
    }
}
