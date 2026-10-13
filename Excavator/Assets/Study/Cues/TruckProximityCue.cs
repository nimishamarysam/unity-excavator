using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// TRUCK WALL PROXIMITY  (C3, profile option truckWallProximity)
// When the bucket or arm comes close to a wall of the truck bed, only the part of that wall's TOP EDGE
// (rim) nearest to the bucket is highlighted: a short bar (rimLength) on the rim, drawn on top and visible
// in the driver view and all context views. It slides along the rim with the bucket and gets warmer as
// the gap shrinks:
//   < 1.2 m yellow,  < 0.6 m orange,  < 0.25 m red (blinking) - at 0 the BucketBlocker stops the bucket.
// The gap is measured from the same bucket/arm probes the BucketBlocker uses. Logged as cue_on/cue_off
// "truck_wall;<wall>;gap=..".
// ---------------------------------------------------------------------------

public class TruckProximityCue : MonoBehaviour
{
    public float showGap = 1.2F, warnGap = 0.6F, dangerGap = 0.25F;
    public float rimLength = 1.6F;   // length of the highlighted piece of the rim (m)

    AttentionCues root;
    readonly List<(Collider col, LineRenderer lr)> walls = new List<(Collider, LineRenderer)>();
    readonly Dictionary<Collider, bool> shown = new Dictionary<Collider, bool>();

    public void Init(AttentionCues r)
    {
        root = r;
        foreach (var c in BucketBlocker.Solids)
            if (c != null && c.name.Contains("_wall"))
                walls.Add((c, CueKit.Line(r.WorldRoot, "CueTruckWall_" + c.name, 0.06F, true, false)));
    }

    void LateUpdate()
    {
        var bb = BucketBlocker.Instance;
        bool on = root != null && root.Visible && root.Profile.truckWallProximity && bb != null && !root.Ctl.IsTravelMode;
        foreach (var (col, lr) in walls)
        {
            float gap = on && col != null ? bb.Gap(col) : float.MaxValue;
            bool want = gap < showGap;
            shown.TryGetValue(col, out bool was);
            if (want != was) { shown[col] = want; AttentionCues.LogCue(want, "truck_wall", $"{col.name};gap={gap:F2}"); }
            if (!want) { CueKit.Hide(lr); continue; }

            RimSegment(lr, col.transform);
            Color c = gap < dangerGap ? CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.5F))
                    : gap < warnGap ? CueKit.Warn
                    : CueKit.Alpha(CueKit.Look, Mathf.Lerp(1F, 0.5F, (gap - warnGap) / (showGap - warnGap)));
            CueKit.Paint(lr, c);
            lr.widthMultiplier = gap < warnGap ? 0.14F : 0.09F;
        }
    }

    // The piece of the wall's top edge nearest to the bucket.
    void RimSegment(LineRenderer lr, Transform t)
    {
        // The wall is a scaled unit cube: its rim runs along the longer horizontal local axis, on top.
        Vector3 s = t.lossyScale;
        bool alongZ = s.z >= s.x;
        Vector3 a = t.TransformPoint(alongZ ? new Vector3(0, 0.5F, -0.5F) : new Vector3(-0.5F, 0.5F, 0));
        Vector3 b = t.TransformPoint(alongZ ? new Vector3(0, 0.5F, 0.5F) : new Vector3(0.5F, 0.5F, 0));
        Vector3 ab = b - a;
        float len = ab.magnitude;
        var m = root.M;
        Vector3 q = (m.BucketPivotPosition + m.BucketTipPosition) / 2F;
        float u = Mathf.Clamp(Vector3.Dot(q - a, ab) / Mathf.Max(1e-4F, len * len), 0F, 1F) * len;   // along the rim
        float half = Mathf.Min(rimLength, len) / 2F;
        u = Mathf.Clamp(u, half, len - half);
        Vector3 dir = ab / Mathf.Max(1e-4F, len), lift = Vector3.up * 0.04F;
        lr.loop = false;
        lr.positionCount = 2;
        lr.SetPosition(0, a + dir * (u - half) + lift);
        lr.SetPosition(1, a + dir * (u + half) + lift);
    }
}
