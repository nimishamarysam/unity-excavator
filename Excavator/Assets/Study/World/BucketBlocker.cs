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
// truck wall instead of going through it. Each new contact is logged as "bucket_blocked".
// ---------------------------------------------------------------------------

[DefaultExecutionOrder(1000)]   // run after ExcavatorController.FixedUpdate
public class BucketBlocker : MonoBehaviour
{
    // Colliders the bucket and arm may not enter. Filled by WorldBuilder (truck), cleared at every trial start.
    public static readonly List<Collider> Solids = new List<Collider>();

    public float bucketProbeRadius = 0.25F;
    public float armProbeRadius = 0.25F;
    public float bucketHalfWidth = 0.45F;   // probes this far to each side of the bucket centre line

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
            best = Mathf.Min(best, Mathf.Max(0F, (c.ClosestPoint(p) - p).magnitude - r));
        return best;
    }

    System.Collections.Generic.IEnumerable<(Vector3, float)> Probes()
    {
        Vector3 piv = m.ArmBucketJointPosition, tip = m.BucketTipPosition, armTop = m.BoomArmJointPosition;
        Vector3 across = m.BucketTransform.right * bucketHalfWidth;
        for (int i = 0; i < 4; i++)
            for (int k = -1; k <= 1; k++)
                yield return (Vector3.Lerp(piv, tip, i / 3F) + across * k, bucketProbeRadius);
        for (int i = 1; i <= 4; i++)
            yield return (Vector3.Lerp(piv, armTop, i * 0.15F), armProbeRadius);
    }

    void FixedUpdate()
    {
        var sm = StudyManager.Instance;
        if (m == null || Solids.Count == 0 || sm == null || !sm.IsTrialRunning) return;

        Collider hit = Penetrating();
        if (hit == null)
        {
            sSwing = m.swingAngle; sBoom = m.boomAngle; sArm = m.armAngle; sBucket = m.bucketAngle;
            hasSafe = true;
            blocked = false;
            return;
        }

        if (!blocked)
        {
            blocked = true;
            blocks++;
            StudyLogger.Log("bucket_blocked", $"{hit.name};count={blocks}");
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
        Vector3 across = m.BucketTransform.right * bucketHalfWidth;

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
            if ((c.ClosestPoint(p) - p).sqrMagnitude < r * r) return c;
        }
        return null;
    }
}
