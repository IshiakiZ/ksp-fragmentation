using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// One part of a craft that may crash: the crash tolerance the game gave it (kept here while the game's is raised),
    /// how badly it has been hurt, and what touched it in the last step of the physics.
    ///
    /// The physics tells the object that carries a rigid body what it ran into. A small part that has no body of its own
    /// (the game hangs it on its parent) is told through its parent's; each point of contact is handed to the part whose
    /// collider it is (see Crashes.OwnerOf).
    /// </summary>
    public sealed class Hits : MonoBehaviour
    {
        public Part part;
        public float original = -1f;        // the part's own crash tolerance, metres a second
        public bool armed;                  // while it is, the game's tolerance is raised and this mod decides what a blow does
        public float damage;                // 0 whole; 1 broken up
        public float hardest;               // the hardest blow it has taken, m/s into whatever it hit
        public bool leaking, gone, tearing, doomed;
        public int shed;                    // pieces it has lost while scraping along
        public float stiffness = 1f;        // what is left of the stiffness of what holds it to its parent (see Crashes.Weaken)
        public float shedDue;
        public float lastTouch = -100f;     // when it last touched anything (Time.fixedTime)
        public float firstTouch = -100f;
        public string why = "";             // what last hurt it, for the log
        public HullPlan plan;               // its skin, cut into panels (asked for when it was armed; ready a moment later: see HullPlan)
        public bool planAsked;
        public Hull hull;                   // its skin as it is being torn (made the first time it is hurt)
        public float crushed;               // how much of its length blows have crushed, 0 to 1
        public float bite;                  // how deep it has dug into soft ground, metres
        public float onSoil;                // when it last slid on soil (Time.fixedTime)
        public bool sever;                  // something was torn off it: see whether what holds it to the parts beside it still does
        public bool hullMissed;             // (said in the log once: its skin could not be torn)
        public float slidOn, groundWork;    // (for the readouts) metres it slid along the ground touching it, and the work done grinding its skin there, kJ

        // ---- what touched it in the last step of the physics (summed; see Crashes.Step, which empties it)
        public int touches;
        public Vector3 point, normal;       // sums: divide by touches
        public float slide;                 // the fastest it slid along what it touched, m/s
        public float into;                  // the fastest it ran into it, m/s (from the physics' own figure)
        public float pushed;                // what the physics pushed back with, tonne metres a second
        public bool fresh;                  // a contact began in that step
        public bool ground, paved, punched; // on the ground or a building; on paving; through it (the game's own guard against going through the ground put it back)
        public Part other;                  // a part of another craft it ran into
        public Vector3 otherVelocity;

        // ---- how it was moving going into that step (set by Crashes at the end of each step)
        public Vector3 lastVelocity, lastSpin, lastCentre;
        public bool lastKnown;

        public void Arm(Part p)
        {
            part = p;
            if (armed) return;
            original = p.crashTolerance;
            // (high enough that nothing the game measures reaches it: the game then leaves every blow to this mod)
            p.crashTolerance = Mathf.Max(p.crashTolerance, Raised);
            armed = true;
        }

        public void Disarm()
        {
            if (!armed) return;
            armed = false;
            if (part != null && original >= 0f) part.crashTolerance = original;
            Clear();
        }

        public const float Raised = 1e6f;

        /// <summary>The part's own tolerance whether or not it is raised just now.</summary>
        public float Tolerance => armed || original >= 0f ? Mathf.Max(0.5f, original) : (part != null ? Mathf.Max(0.5f, part.crashTolerance) : 6f);

        public void Clear()
        {
            touches = 0; point = normal = Vector3.zero; slide = into = pushed = 0f;
            fresh = ground = paved = punched = false; other = null;
        }

        void OnCollisionEnter(Collision c) => Take(c, true);
        void OnCollisionStay(Collision c) => Take(c, false);

#if DEV
        // (development build: what was heard, for finding out why something was not)
        public static int HeardGround, HeardCraft, HeardOther, HeardUnarmed, HeardPieces, HeardSameCraft;
        static Collider lastOther;
        static Part lastOtherPart;
        /// <summary>What was heard last that was not the ground (made into words only when asked: a crash hears hundreds a second).</summary>
        public static string LastOther => lastOther == null ? "" : lastOther.name + " on layer " + lastOther.gameObject.layer + (lastOtherPart != null ? " (" + (lastOtherPart.partInfo != null ? lastOtherPart.partInfo.name : lastOtherPart.name) + " of " + (lastOtherPart.vessel != null ? lastOtherPart.vessel.GetDisplayName() : "no craft") + ")" : " (not a part)");
#endif

        void Take(Collision c, bool enter)
        {
            if (c == null || part == null) return;
            if (!armed)
            {
#if DEV
                HeardUnarmed++;
#endif
                return;
            }
            Collider them = c.collider;
            if (them == null || them.isTrigger) return;
            if (Piece.Is(them))                                            // (our own wreckage hurts nothing: see Piece)
            {
#if DEV
                HeardPieces++;
#endif
                return;
            }
            int layer = them.gameObject.layer;
            bool ground = layer == 15;
            Part other = null;
            if (!ground)
            {
                other = FlightGlobals.GetPartUpwardsCached(them.gameObject);
#if DEV
                lastOther = them; lastOtherPart = other;
                if (other != null && other.vessel == part.vessel) HeardSameCraft++;
                else if (other != null) HeardCraft++;
                else HeardOther++;
#endif
                // (kerbals, flags and anything else that is not a part of a craft are left as the game has them)
                if (other == null || other.vessel == null || other.vessel == part.vessel || other.vessel.isEVA) return;
            }
#if DEV
            else HeardGround++;
#endif
            int n = Mathf.Min(c.contactCount, 6);
            if (n <= 0) return;
            Vector3 rel = c.relativeVelocity;
            float each = c.impulse.magnitude / n;
            bool paved = ground && Crashes.Paved(them);
            for (int k = 0; k < n; k++)
            {
                ContactPoint cp = c.GetContact(k);
                Hits h = Crashes.OwnerOf(cp.thisCollider) ?? this;
                if (!h.armed) h = this;
                Vector3 normal = cp.normal;
                // (the normal is wanted pointing out of what was hit, towards the part)
                if (Vector3.Dot(normal, h.part.transform.position - cp.point) < 0f) normal = -normal;
                float along = Vector3.Dot(rel, normal);
                h.touches++;
                h.point += cp.point;
                h.normal += normal;
                h.into = Mathf.Max(h.into, Mathf.Abs(along));
                h.slide = Mathf.Max(h.slide, (rel - normal * along).magnitude);
                h.pushed += each;
                h.fresh |= enter;
                h.ground |= ground;
                h.paved |= paved;
                if (other != null) { h.other = other; h.otherVelocity = other.rb != null ? other.rb.velocity : Vector3.zero; }
            }
        }
    }
}
