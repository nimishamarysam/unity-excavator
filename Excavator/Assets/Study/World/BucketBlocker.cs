using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// BUCKET BLOCKER
// The arm and bucket are moved by setting joint angles directly (not by physics), so they would pass
// through solid objects. This component stops that for the objects registered in Solids (the dump
// truck: chassis, cab, bed floor and the four bed walls).
//
// Every physics step, AFTER the ExcavatorController has applied the operator's input, it checks a set of
// probe spheres along the arm and across the bucket. If any probe is inside a solid, the joint angles
// (swing, boom, arm, bucket) are put back to the last pose that was free, so the bucket stops at the
// truck wall instead of going through it. Each new contact is logged as "bucket_blocked" with the part
// that was hit and the IMPACT SPEED (how fast the bucket/arm was moving into it). Only contacts faster than
// impactSpeed count as an incident ("hit"); slow touches (e.g. resting the bucket on the wall while
// dumping) are blocked and logged, but not counted.
// ---------------------------------------------------------------------------

[DefaultExecutionOrder(1000)]   // run after ExcavatorController.FixedUpdate
public class BucketBlocker : MonoBehaviour
{
    // Colliders the bucket and arm may not enter. Filled by WorldBuilder (truck), cleared at every trial start.
    public static readonly List<Collider> Solids = new List<Collider>();

    public float bucketProbeRadius = 0.25F;
    public float armProbeRadius = 0.25F;
    public float bucketHalfWidth = 0.45F;   // probes this far to each side of the bucket centre line
    public float impactSpeed = 0.5F;        // m/s: contacts at least this fast are incidents
    Vector3 safeTip, safeArm;

    Excavator m;
    bool hasSafe, blocked;
    float sSwing, sBoom, sArm, sBucket;
    int blocks;
    public int BlockCount => blocks;

    public static BucketBlocker Instance { get; private set; }
    public void Init(ExcavatorController ctl) { m = ctl.Machine; Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    // Smallest gap (m) between the bucket/arm probes and collider c (0 = touching or inside).
    public float Gap(Collider c)
    {
        if (m == null || c == null) return float.MaxValue;
        float best = float.MaxValue;
        foreach (var (p, r) in Probes())
            best = Mathf.Min(best, Mathf.Max(0F, (ColliderMath.ClosestPoint(c, p) - p).magnitude - r));
        return best;
    }

    System.Collections.Generic.IEnumerable<(Vector3, float)> Probes()
    {
        Vector3 piv = m.ArmBucketJointPosition, tip = m.BucketTipPosition, armTop = m.BoomArmJointPosition;
        Vector3 across = Lateral() * bucketHalfWidth;
        for (int i = 0; i < 4; i++)
            for (int k = -1; k <= 1; k++)
                yield return (Vector3.Lerp(piv, tip, i / 3F) + across * k, bucketProbeRadius);
        for (int i = 1; i <= 4; i++)
            yield return (Vector3.Lerp(piv, armTop, i * 0.15F), armProbeRadius);
    }

    // Across the bucket = horizontal, perpendicular to the arm's working plane (independent of the model's axes).
    Vector3 Lateral()
    {
        Vector3 reach = m.BucketTipPosition - m.SwingCenter; reach.y = 0F;
        reach = reach.sqrMagnitude > 0.01F ? reach.normalized : m.ForwardFlat;
        return Vector3.Cross(Vector3.up, reach);
    }

    Vector3 ArmPoint => Vector3.Lerp(m.ArmBucketJointPosition, m.BoomArmJointPosition, 0.3F);

    static string Part(Collider c)
    {
        string n = c.name.ToLowerInvariant();
        return n.Contains("cab") ? "cab" : n.Contains("wall") ? "wall" : n.Contains("floor") ? "bed_floor" : n.Contains("chassis") ? "chassis" : "truck";
    }

    void FixedUpdate()
    {
        var sm = StudyManager.Instance;
        if (m == null || Solids.Count == 0 || sm == null || !sm.IsTrialRunning) return;

        Collider hit = Penetrating();
        if (hit == null)
        {
            sSwing = m.swingAngle; sBoom = m.boomAngle; sArm = m.armAngle; sBucket = m.bucketAngle;
            safeTip = m.BucketTipPosition; safeArm = ArmPoint;
            hasSafe = true;
            blocked = false;
            return;
        }

        if (!blocked)
        {
            blocked = true;
            blocks++;
            float dt = Mathf.Max(1e-4F, Time.fixedDeltaTime);
            float speed = hasSafe ? Mathf.Max((m.BucketTipPosition - safeTip).magnitude, (ArmPoint - safeArm).magnitude) / dt : 0F;
            bool impact = speed >= impactSpeed;
            StudyLogger.Log("bucket_blocked", $"{hit.name};part={Part(hit)};count={blocks};speed={speed:F2};impact={(impact ? 1 : 0)}");
        }
        if (hasSafe)
        {
            m.swingAngle = sSwing;
            m.boomAngle = sBoom;
            m.armAngle = sArm;
            m.bucketAngle = sBucket;
        }
    }

    // First solid that a probe is inside of, or null.
    Collider Penetrating()
    {
        Vector3 piv = m.ArmBucketJointPosition, tip = m.BucketTipPosition, armTop = m.BoomArmJointPosition;
        Vector3 across = Lateral() * bucketHalfWidth;

        // Bucket: 4 points from hinge to teeth, each at the centre and both sides.
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = Vector3.Lerp(piv, tip, i / 3F);
            for (int k = -1; k <= 1; k++)
            {
                var c = Inside(p + across * k, bucketProbeRadius);
                if (c != null) return c;
            }
        }
        // Arm: 4 points along the arm (lower part, where it can reach the truck).
        for (int i = 1; i <= 4; i++)
        {
            var c = Inside(Vector3.Lerp(piv, armTop, i * 0.15F), armProbeRadius);
            if (c != null) return c;
        }
        return null;
    }

    static Collider Inside(Vector3 p, float r)
    {
        foreach (var c in Solids)
        {
            if (c == null || !c.enabled) continue;
            if (!c.bounds.Intersects(new Bounds(p, Vector3.one * (2F * r)))) continue;
            if ((ColliderMath.ClosestPoint(c, p) - p).sqrMagnitude < r * r) return c;
        }
        return null;
    }
}
