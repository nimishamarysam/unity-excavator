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
// Bucket = the WHOLE bucket: the teeth AND the bucket's box (its mesh bounds, oriented with the bucket), so a
// service that ends up inside or against the bucket body counts, not only one touched by the teeth (2026-11-01,
// buried pipes: the pipe could sit inside the bucket without a strike).
public enum HazardMeasure { WholeMachine, BucketTeethOnly, TracksOnly, Attachment, Bucket }

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
        return ColliderMath.ClosestPoint(col, p);
    }
}

// Closest point on a collider, computed from the collider's own Transform and shape settings instead of
// Unity's physics engine (Collider.ClosestPoint). The engine reads its internal copy of the collider, which
// lags behind when objects are moved by script (Auto Sync Transforms is off) or just switched on (a worker
// appearing), and it returns the query point itself when it cannot measure - which looks like distance 0
// and was logged as false hazard contacts ("dist=0.00") on some computers.
// Box, capsule and sphere colliders (all hazards, truck parts, tracks) are solved exactly. Other colliders
// (the machine's convex mesh colliders) use the engine, with the mesh's box as a fallback when the engine
// clearly failed.
public static class ColliderMath
{
    public static Vector3 ClosestPoint(Collider c, Vector3 p)
    {
        if (c == null) return p;
        switch (c)
        {
            case BoxCollider b: return OnBox(b.transform, b.center, b.size, p);
            case CapsuleCollider k: return OnCapsule(k, p);
            case SphereCollider sp: return OnSphere(sp, p);
        }
        Vector3 q = c.ClosestPoint(p);
        if ((q - p).sqrMagnitude < 1e-8F && c is MeshCollider mc && mc.sharedMesh != null)
        {
            // Engine says "touching": accept only if p really is inside the mesh's (oriented) box.
            Bounds mb = mc.sharedMesh.bounds;
            Vector3 onBox = OnBox(mc.transform, mb.center, mb.size, p);
            if ((onBox - p).sqrMagnitude > 1e-6F) return onBox;
        }
        return q;
    }

    // Box in the collider's local space: clamp each local axis (exact also for rotated and scaled boxes).
    public static Vector3 OnBox(Transform t, Vector3 center, Vector3 size, Vector3 p)
    {
        Vector3 l = t.InverseTransformPoint(p) - center, h = size * 0.5F;
        l = new Vector3(Mathf.Clamp(l.x, -h.x, h.x), Mathf.Clamp(l.y, -h.y, h.y), Mathf.Clamp(l.z, -h.z, h.z));
        return t.TransformPoint(l + center);
    }

    static Vector3 OnCapsule(CapsuleCollider k, Vector3 p)
    {
        Transform t = k.transform;
        Vector3 s = t.lossyScale; s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        Vector3 axisLocal; float axisScale, radScale;
        switch (k.direction)
        {
            case 0: axisLocal = Vector3.right; axisScale = s.x; radScale = Mathf.Max(s.y, s.z); break;
            case 2: axisLocal = Vector3.forward; axisScale = s.z; radScale = Mathf.Max(s.x, s.y); break;
            default: axisLocal = Vector3.up; axisScale = s.y; radScale = Mathf.Max(s.x, s.z); break;
        }
        float r = k.radius * radScale;
        float half = Mathf.Max(k.height * axisScale * 0.5F, r) - r;      // half length of the inner segment
        Vector3 c = t.TransformPoint(k.center);
        Vector3 axis = t.TransformDirection(axisLocal).normalized;
        float along = Mathf.Clamp(Vector3.Dot(p - c, axis), -half, half);
        Vector3 seg = c + axis * along;
        Vector3 d = p - seg;
        return d.magnitude <= r ? p : seg + d.normalized * r;
    }

    static Vector3 OnSphere(SphereCollider sp, Vector3 p)
    {
        Transform t = sp.transform;
        Vector3 s = t.lossyScale;
        float r = sp.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        Vector3 c = t.TransformPoint(sp.center);
        Vector3 d = p - c;
        return d.magnitude <= r ? p : c + d.normalized * r;
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

    // No warnings/contacts are counted in the first SettleSeconds of a trial, or of a hazard becoming active
    // (e.g. a worker appearing): positions are still being set up then. Distances are still measured.
    public const float SettleSeconds = 0.5F;
    readonly Dictionary<Hazard, float> activeSince = new Dictionary<Hazard, float>();
    float lastTrialTime = -1F;

    public void Init(ExcavatorController ctl)
    {
        controller = ctl;
        machine = ctl.Machine;
        var list = new List<Collider>();
        foreach (var c in machine.AllColliders) if (c.enabled && !c.isTrigger) list.Add(c);
        all = list.ToArray();
        tracks = machine.TrackColliders;
        // Size of the bucket box used by HazardMeasure.Bucket (logged once per trial, for checking).
        var bmc = machine.BucketMeshCollider;
        if (bmc != null && bmc.sharedMesh != null)
        {
            Vector3 sz = Vector3.Scale(bmc.sharedMesh.bounds.size, bmc.transform.lossyScale);
            StudyLogger.Log("bucket_box", $"size={Mathf.Abs(sz.x):F2}x{Mathf.Abs(sz.y):F2}x{Mathf.Abs(sz.z):F2}");
        }
    }

    void FixedUpdate()
    {
        var sm = StudyManager.Instance;
        if (machine == null || sm == null || !sm.IsTrialRunning) return;
        if (Time.time < next) return;
        next = Time.time + 1F / rateHz;

        // New trial (trial time went back): forget the previous trial's episodes and activation times.
        if (sm.TrialTime < lastTrialTime) { episodes.Clear(); activeSince.Clear(); }
        lastTrialTime = sm.TrialTime;

        Physics.SyncTransforms();   // the machine's mesh colliders are still measured by the physics engine

        foreach (var h in Hazard.All.ToArray())
        {
            if (!h.active || !h.gameObject.activeInHierarchy)
            {
                activeSince.Remove(h);
                if (h.CurrentLevel != Hazard.Level.Clear) Clear(h, sm);
                continue;
            }
            if (!activeSince.TryGetValue(h, out float since)) { since = sm.TrialTime; activeSince[h] = since; }

            float d = Distance(h);
            h.CurrentDistance = d;
            if (sm.TrialTime < SettleSeconds || sm.TrialTime - since < SettleSeconds) continue;
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

    // Whole bucket: closest distance between the hazard and the bucket's oriented box (alternating projections,
    // exact for convex shapes; 0 when the hazard passes through the box). 'best' = the teeth distance.
    float BucketDistance(Hazard h, float best)
    {
        var mc = machine.BucketMeshCollider;
        if (mc == null || mc.sharedMesh == null) return best;
        Bounds b = mc.sharedMesh.bounds;
        Transform t = mc.transform;
        Vector3 q = t.TransformPoint(b.center), p = h.ClosestPointTo(q);
        for (int i = 0; i < 5; i++) { q = ColliderMath.OnBox(t, b.center, b.size, p); p = h.ClosestPointTo(q); }
        q = ColliderMath.OnBox(t, b.center, b.size, p);
        float d = (p - q).magnitude;
        if (d < best) { best = d; h.MachinePoint = q; h.HazardPoint = p; }
        return best;
    }

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
        if (h.measure == HazardMeasure.Bucket) return BucketDistance(h, best);
        if (h.measure == HazardMeasure.Attachment) return AttachmentDistance(h, best);

        var cols = h.measure == HazardMeasure.TracksOnly ? tracks : all;
        if (h.measure == HazardMeasure.TracksOnly) best = float.MaxValue;
        foreach (var c in cols)
        {
            if (c == null) continue;
            // two refinement steps: hazard point -> machine surface -> hazard point
            Vector3 p = h.ClosestPointTo(c.bounds.center);
            Vector3 q = ColliderMath.ClosestPoint(c, p);
            p = h.ClosestPointTo(q);
            q = ColliderMath.ClosestPoint(c, p);
            float d = (p - q).magnitude;
            if (d < best) { best = d; h.MachinePoint = q; h.HazardPoint = p; }
        }
        // Whole machine also includes the whole BUCKET body (its collider is switched off in the digging tasks, so
        // without this only the teeth tip of the bucket counted - a worker right in front of the bucket was measured
        // to the arm or the cab instead).
        if (h.measure == HazardMeasure.WholeMachine) best = BucketDistance(h, best);
        return best;
    }
}
