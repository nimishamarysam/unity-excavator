using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// HAZARDS
// Every dangerous thing in a scenario (worker, obstacle, buried cable, power
// line, trench edge) gets a Hazard component. The HazardTracker measures the
// distance from the machine to each hazard and logs:
//   hazard_warning   machine came closer than warnDistance (this is the "onset")
//   hazard_response  first operator reaction after the onset (sticks released
//                    or direction reversed), with reaction time in seconds
//   hazard_contact   machine came closer than contactDistance (a strike)
//   hazard_clear     machine moved away again, with the closest distance reached
// Later, the context monitor and the attention cues read the same list.
// ---------------------------------------------------------------------------

public enum HazardKind { Worker, Obstacle, BuriedCable, PowerLine, TrenchEdge, Truck, BuriedPipe }

// Which part of the machine counts when measuring distance.
// Attachment = boom, arm and bucket measured as thick line segments along their real centre lines
// (not the colliders' convex hulls, which are much larger than the visible parts). Used for the power line.
public enum HazardMeasure { WholeMachine, BucketTeethOnly, TracksOnly, Attachment }

public abstract class Hazard : MonoBehaviour
{
    public static readonly List<Hazard> All = new List<Hazard>();

    public string hazardId = "hazard";
    public HazardKind kind;
    public HazardMeasure measure = HazardMeasure.WholeMachine;
    public float warnDistance = 3F;
    public float contactDistance = 0.3F;
    public bool active = true;           // inactive hazards are ignored (e.g. a worker not yet walking)

    public enum Level { Clear, Warning, Contact }
    public Level CurrentLevel { get; set; } = Level.Clear;
    public float CurrentDistance { get; set; } = float.MaxValue;
    // The two closest points (on the machine and on the hazard), updated with CurrentDistance.
    public Vector3 MachinePoint { get; set; }
    public Vector3 HazardPoint { get; set; }

    // Closest point of this hazard to a world position.
    public abstract Vector3 ClosestPointTo(Vector3 p);

    protected virtual void OnEnable() { All.Add(this); }
    protected virtual void OnDisable() { All.Remove(this); }
}

// A hazard that is a solid object (worker, barrel, rock, truck): uses its collider.
public class ColliderHazard : Hazard
{
    Collider col;
    void Awake() { col = GetComponent<Collider>(); }
    public override Vector3 ClosestPointTo(Vector3 p)
    {
        if (col == null) return transform.position;
        return col.ClosestPoint(p);
    }
}

// A hazard that is a line (buried cable, overhead power line).
public class SegmentHazard : Hazard
{
    public Vector3 a, b;
    public override Vector3 ClosestPointTo(Vector3 p)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-5F, ab.sqrMagnitude));
        return a + ab * t;
    }
}

// A hazard that is a ground rectangle (trench edge): closest point on the rectangle's outline.
public class RectEdgeHazard : Hazard
{
    public Vector3 center, axisU;
    public float halfU, halfV;
    public override Vector3 ClosestPointTo(Vector3 p)
    {
        Vector3 axisV = Vector3.Cross(Vector3.up, axisU);
        Vector3 d = p - center;
        float u = Mathf.Clamp(Vector3.Dot(d, axisU), -halfU, halfU);
        float v = Mathf.Clamp(Vector3.Dot(d, axisV), -halfV, halfV);
        // push onto the nearest edge
        if (halfU - Mathf.Abs(u) < halfV - Mathf.Abs(v)) u = Mathf.Sign(u == 0 ? 1 : u) * halfU;
        else v = Mathf.Sign(v == 0 ? 1 : v) * halfV;
        Vector3 q = center + axisU * u + axisV * v;
        q.y = TerrainDigger.ReferenceGroundHeight(q);
        return q;
    }
}

// Measures machine-to-hazard distances 10 times per second and logs changes.
public class HazardTracker : MonoBehaviour
{
    public float rateHz = 10F;
    Excavator machine;
    ExcavatorController controller;
    Collider[] all, tracks;
    float next;

    class Episode { public float onset; public bool responded; public float minDist; public InputSnapshot inputAtOnset; }
    readonly Dictionary<Hazard, Episode> episodes = new Dictionary<Hazard, Episode>();

    public void Init(ExcavatorController ctl)
    {
        controller = ctl;
        machine = ctl.Machine;
        var list = new List<Collider>();
        foreach (var c in machine.AllColliders) if (c.enabled && !c.isTrigger) list.Add(c);
        all = list.ToArray();
        tracks = machine.TrackColliders;
    }

    void FixedUpdate()
    {
        var sm = StudyManager.Instance;
        if (machine == null || sm == null || !sm.IsTrialRunning) return;
        if (Time.time < next) return;
        next = Time.time + 1F / rateHz;

        foreach (var h in Hazard.All.ToArray())
        {
            if (!h.active) { if (h.CurrentLevel != Hazard.Level.Clear) Clear(h, sm); continue; }

            float d = Distance(h);
            h.CurrentDistance = d;
            var level = d <= h.contactDistance ? Hazard.Level.Contact : d <= h.warnDistance ? Hazard.Level.Warning : Hazard.Level.Clear;

            if (h.CurrentLevel == Hazard.Level.Clear && level != Hazard.Level.Clear)
            {
                episodes[h] = new Episode { onset = sm.TrialTime, minDist = d, inputAtOnset = controller.LastInput };
                StudyLogger.Log("hazard_warning", $"{h.hazardId};{h.kind};dist={d:F2}");
            }
            if (level == Hazard.Level.Contact && h.CurrentLevel != Hazard.Level.Contact)
                StudyLogger.Log("hazard_contact", $"{h.hazardId};{h.kind};dist={d:F2}");

            if (level != Hazard.Level.Clear && episodes.TryGetValue(h, out var ep))
            {
                ep.minDist = Mathf.Min(ep.minDist, d);
                if (!ep.responded && Responded(ep.inputAtOnset, controller.LastInput))
                {
                    ep.responded = true;
                    StudyLogger.Log("hazard_response", $"{h.hazardId};{h.kind};latency={sm.TrialTime - ep.onset:F2}");
                }
            }

            if (level == Hazard.Level.Clear && h.CurrentLevel != Hazard.Level.Clear) { h.CurrentLevel = level; Clear(h, sm); continue; }
            h.CurrentLevel = level;
        }
    }

    void Clear(Hazard h, StudyManager sm)
    {
        h.CurrentLevel = Hazard.Level.Clear;
        if (episodes.TryGetValue(h, out var ep))
        {
            StudyLogger.Log("hazard_clear", $"{h.hazardId};{h.kind};min_dist={ep.minDist:F2};duration={sm.TrialTime - ep.onset:F2}");
            episodes.Remove(h);
        }
    }

    // A reaction = the operator lets go of the sticks, or reverses the main movement.
    static bool Responded(InputSnapshot before, InputSnapshot now)
    {
        if (!now.Any) return true;
        return Flipped(before.swing, now.swing) || Flipped(before.boom, now.boom) || Flipped(before.arm, now.arm)
            || Flipped(before.bucket, now.bucket) || Flipped(before.trackLeft, now.trackLeft) || Flipped(before.trackRight, now.trackRight);
    }
    static bool Flipped(float a, float b) => a * b < 0F;

    // Boom (radius 0.45 m), arm (0.35 m) and bucket (0.6 m around the hinge->teeth line), sampled every ~0.25 m.
    float AttachmentDistance(Hazard h, float best)
    {
        var m = machine;
        Seg(h, m.BoomBasePosition, m.BoomArmJointPosition, 0.45F, ref best);
        Seg(h, m.BoomArmJointPosition, m.ArmBucketJointPosition, 0.35F, ref best);
        Seg(h, m.BucketPivotPosition, m.BucketTipPosition, 0.6F, ref best);
        return Mathf.Max(0F, best);
    }

    static void Seg(Hazard h, Vector3 a, Vector3 b, float radius, ref float best)
    {
        int n = Mathf.Clamp(Mathf.CeilToInt((b - a).magnitude / 0.25F), 2, 40);
        for (int i = 0; i <= n; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)n);
            Vector3 q = h.ClosestPointTo(p);
            float d = (q - p).magnitude - radius;
            if (d < best) { best = d; h.MachinePoint = p; h.HazardPoint = q; }
        }
    }

    float Distance(Hazard h)
    {
        Vector3 tip = machine.BucketTipPosition;
        Vector3 hp0 = h.ClosestPointTo(tip);
        float best = (hp0 - tip).magnitude;
        h.MachinePoint = tip; h.HazardPoint = hp0;
        if (h.measure == HazardMeasure.BucketTeethOnly) return best;
        if (h.measure == HazardMeasure.Attachment) return AttachmentDistance(h, best);

        var cols = h.measure == HazardMeasure.TracksOnly ? tracks : all;
        if (h.measure == HazardMeasure.TracksOnly) best = float.MaxValue;
        foreach (var c in cols)
        {
            if (c == null) continue;
            // two refinement steps: hazard point -> machine surface -> hazard point
            Vector3 p = h.ClosestPointTo(c.bounds.center);
            Vector3 q = c.ClosestPoint(p);
            p = h.ClosestPointTo(q);
            q = c.ClosestPoint(p);
            float d = (p - q).magnitude;
            if (d < best) { best = d; h.MachinePoint = q; h.HazardPoint = p; }
        }
        return best;
    }
}
