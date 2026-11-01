using UnityEngine;

// ---------------------------------------------------------------------------
// SOIL SIDE BLOCKER
// A buried bucket is held by the soil around it, as on a real excavator:
//   * SIDEWAYS: no swinging towards undug soil beside the bucket. Logged once per episode as "swing_blocked_soil".
//   * OUTWARDS: no pushing the bucket away from the cab (arm out or opening the bucket) into undug soil
//     beyond it. Logged once per episode as "push_blocked_soil".
// Moving towards the part of the hole that is already dug, pulling the bucket in, curling it and lifting it
// out all work as normal. The cut reaches CutRadius (0.6 m) around the teeth, so the checks look just beyond
// that (sideGap / frontGap). Soil counts when it is more than embedTolerance above the bucket's lower edge.
//
// Every physics step, after the ExcavatorController (and BucketBlocker) applied the input, the new pose is
// compared with the last one and the blocked movement is put back. Works in every digging task. Added by Scenario.
// ---------------------------------------------------------------------------

[DefaultExecutionOrder(1001)]   // after ExcavatorController and BucketBlocker, before BucketLoad
public class SoilSideBlocker : MonoBehaviour
{
    public static bool Enabled = true;          // sideways (swing) blocking
    public static bool BlockPushOut = true;     // outwards (arm out / boom / bucket open) blocking
    public float sideGap = TerrainDigger.CutRadius + 0.1F;    // m from the bucket centre line to the soil that blocks a swing
    public float frontGap = TerrainDigger.CutRadius + 0.1F;   // m beyond the bucket (away from the cab) to the soil that blocks pushing out
    public float embedTolerance = 0.15F;                      // soil must be at least this much above the bucket edge
    const float MinMove = 1e-4F;
    // Swing change (deg) that counts as swinging. Setting swingAngle back is not exact (~0.001 deg off), so a smaller
    // threshold made the machine creep and re-trigger the block every step with no swing input.
    const float MinSwing = 0.01F;

    Excavator m;
    bool hasPrev, swingBlocked, pushBlocked;
    float pSwing, pBoom, pArm, pBucket;
    Vector3 pTip, pMid;
    int swingCount, pushCount;

    public void Init(ExcavatorController ctl) { m = ctl.Machine; }

    void FixedUpdate()
    {
        var sm = StudyManager.Instance;
        if (m == null || sm == null || !sm.IsTrialRunning || TerrainDigger.Instance == null) { hasPrev = false; return; }

        if (hasPrev)
        {
            // SIDEWAYS
            if (Enabled && Mathf.Abs(Mathf.DeltaAngle(pSwing, m.swingAngle)) > MinSwing)
            {
                Vector3 lat = Lateral(pTip), tip = m.BucketTipPosition;
                float side = Vector3.Dot(new Vector3(tip.x - pTip.x, 0F, tip.z - pTip.z), lat);
                if (Mathf.Abs(side) > 1e-5F && SoilAt(lat * Mathf.Sign(side), sideGap))
                {
                    m.swingAngle = pSwing;
                    if (!swingBlocked) { swingBlocked = true; swingCount++; StudyLogger.Log("swing_blocked_soil", $"count={swingCount};depth={Depth():F2}"); }
                }
                else swingBlocked = false;
            }

            // OUTWARDS (away from the cab)
            // Only the ARM and the BUCKET are held back. The boom is never blocked: raising it is how the bucket gets
            // out of the hole (and with a wide-open bucket, lifting also moves the bucket slightly outwards).
            bool pushing = false;
            if (BlockPushOut && MovedOut() && SoilAt(Outward(pTip), frontGap))
            {
                float arm = m.armAngle, bucket = m.bucketAngle;
                m.armAngle = pArm; m.bucketAngle = pBucket;
                if (MovedOut()) { m.armAngle = arm; m.bucketAngle = bucket; }   // the boom moved it out, not arm/bucket: allow
                else pushing = true;
            }
            if (pushing && !pushBlocked) { pushBlocked = true; pushCount++; StudyLogger.Log("push_blocked_soil", $"count={pushCount};depth={Depth():F2}"); }
            if (!pushing) pushBlocked = false;
        }

        pSwing = m.swingAngle; pBoom = m.boomAngle; pArm = m.armAngle; pBucket = m.bucketAngle;
        pTip = m.BucketTipPosition; pMid = Mid();
        hasPrev = true;
    }

    Vector3 Mid() => Vector3.Lerp(m.ArmBucketJointPosition, m.BucketTipPosition, 0.5F);
    float Depth() => TerrainDigger.ReferenceGroundHeight(m.BucketTipPosition) - m.BucketTipPosition.y;

    // Did the teeth or the middle of the bucket move away from the cab since the last step?
    // Lifting out (rising more than moving out) is always allowed, so the bucket cannot get stuck against a trench wall.
    bool MovedOut() => Out(m.BucketTipPosition, pTip) || Out(Mid(), pMid);
    bool Out(Vector3 now, Vector3 before) { float d = Radial(now) - Radial(before); return d > MinMove && d > now.y - before.y; }
    float Radial(Vector3 p) => new Vector2(p.x - m.SwingCenter.x, p.z - m.SwingCenter.z).magnitude;

    // Undug soil next to the bucket (teeth and middle of the bucket), 'gap' metres away in direction dir?
    bool SoilAt(Vector3 dir, float gap)
    {
        Vector3 tip = m.BucketTipPosition;
        foreach (var p in new[] { tip, Mid() })
        {
            Vector3 s = p + dir * gap;
            if (TerrainDigger.CurrentGroundHeight(s) > p.y + embedTolerance) return true;
        }
        return false;
    }

    // Horizontal direction away from the cab, through the bucket.
    Vector3 Outward(Vector3 tip)
    {
        Vector3 reach = tip - m.SwingCenter; reach.y = 0F;
        return reach.sqrMagnitude > 0.01F ? reach.normalized : m.ForwardFlat;
    }

    // Horizontal, perpendicular to the arm (the direction the bucket moves when swinging).
    Vector3 Lateral(Vector3 tip) => Vector3.Cross(Vector3.up, Outward(tip));
}
