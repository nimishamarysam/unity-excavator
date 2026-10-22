using UnityEngine;

// ---------------------------------------------------------------------------
// BUCKET SIDE VIEW - LATERAL (SWING) FRAME ALERT  (C3, profile option bucketSideLateralGauge)
//
// The bucket side view is a section in the arm's plane: it shows depth and reach, but NOT how far the
// bucket has drifted sideways (by swinging). While the operator watches that view, sideways drift out of
// the dig area goes unnoticed. The FRAME of the BUCKET SIDE VIEW therefore turns:
//     orange (pulsing)         = bucket edge closer than warnClearance to a trench edge
//     red (thicker, pulsing)   = bucket over a trench edge, or the teeth outside the dig area
//                                (also when cutting outside the area along the reach)
// A peripheral cue: it is seen without reading anything. (An L-R gauge bar was tried and removed.)
//
// Active while the bucket is near the dig area and low (digging), not while carrying a load up and away.
// State changes are logged as  cue_lateral_gauge  <state>;left=<m>;right=<m>  (metric: time near/outside).
// ---------------------------------------------------------------------------

public class BucketSideLateralGauge : MonoBehaviour
{
    public float warnClearance = 0.2F;    // bucket edge closer than this to a trench edge = orange (m)
    public float overEdge = 0.1F;         // bucket more than this over a trench edge = red (m)
    public float showMargin = 2.5F;       // shown when the teeth are within this of the dig area (m)
    public float showAboveGround = 1.5F;  // ...and lower than this above the original ground (m)

    AttentionCues root;
    BucketLoad load;
    Renderer edgeRenderer;
    string state = "off";

    TerrainDigger Digger => TerrainDigger.Instance;

    public void Init(AttentionCues r)
    {
        root = r;
        load = r.Scenario != null ? r.Scenario.GetComponent<BucketLoad>() : null;
    }

    void OnDisable() { ContextViews.BucketSideAlert = null; }
    void OnDestroy() { ContextViews.BucketSideAlert = null; }

    void LateUpdate()
    {
        if (root == null || root.M == null) return;
        var m = root.M;
        var d = Digger;
        Camera cam = ContextViews.BucketSideCamera;
        bool can = root.Visible && root.Profile.bucketSideLateralGauge && !root.Ctl.IsTravelMode
                   && d != null && d.HasZone && cam != null;
        if (!can) { Off(); return; }

        Vector3 tip = m.BucketTipPosition;
        float refY = TerrainDigger.ReferenceGroundHeight(tip);
        bool loadedAndUp = load != null && load.IsLoaded && tip.y > refY + 0.3F;
        bool cuttingOutside = refY - tip.y > 0.1F && !d.Contains(tip, 0.3F);   // same rule as the dig-area outline
        bool near = d.Contains(tip, showMargin) && tip.y < refY + showAboveGround && !loadedAndUp;
        if (!near) { Off(); return; }

        // Lateral axis = operator's right, perpendicular to the arm (the direction a swing moves the bucket).
        Vector3 reach = CueKit.Flat(tip - m.SwingCenter);
        reach = reach.sqrMagnitude > 0.01F ? reach.normalized : m.ForwardFlat;
        Vector3 side = Vector3.Cross(Vector3.up, reach);
        Vector3 ec = EdgeCentre(m, side, out float hw);

        if (!Interval(d, CueKit.Flat(ec), side, out float t0, out float t1))
        {
            // Beyond the end of the trench along the reach: alert only when cutting there.
            SetState(cuttingOutside ? "outside_reach" : "beyond_end", 0F, 0F);
            ContextViews.BucketSideAlert = cuttingOutside ? CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.5F)) : (Color?)null;
            return;
        }

        float lc = -hw - t0;   // bucket's left edge to the trench's left edge (m, negative = over the edge)
        float rc = t1 - hw;    // bucket's right edge to the trench's right edge
        bool tipInside = t0 <= 0F && t1 >= 0F;
        bool rightSide = rc < lc;   // the closer edge is on the right -> swing left to get back
        float closest = Mathf.Min(lc, rc);
        string st; Color col;
        if (!tipInside)                   { st = "outside";   col = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.5F)); }
        else if (closest < -overEdge)     { st = "over_edge"; col = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(8F, 0.55F)); }
        else if (closest < warnClearance) { st = "near_edge"; col = CueKit.Warn; }
        else                              { st = "inside";    col = CueKit.Good; }
        if (cuttingOutside && st == "inside") { st = "outside_reach"; col = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.5F)); }
        SetState(st + (st == "inside" ? "" : rightSide ? ";edge=right" : ";edge=left"), lc, rc);

        // ---- frame alert (peripheral) ----
        ContextViews.BucketSideAlert = st == "inside" ? (Color?)null
                                     : st == "near_edge" ? CueKit.Alpha(CueKit.Warn, CueKit.Pulse(5F, 0.6F)) : col;
    }

    void Off()
    {
        ContextViews.BucketSideAlert = null;
        SetState("off", 0F, 0F);
    }

    void SetState(string s, float lc, float rc)
    {
        if (s == state) return;
        state = s;
        StudyLogger.Log("cue_lateral_gauge", $"{s};left={lc:F2};right={rc:F2}");
    }

    // Where the line p + dir * t crosses the dig-zone rectangle (flat). false = it misses the rectangle.
    static bool Interval(TerrainDigger d, Vector3 p, Vector3 dir, out float t0, out float t1)
    {
        t0 = float.NegativeInfinity; t1 = float.PositiveInfinity;
        Vector3 rel = p - CueKit.Flat(d.ZoneCenter), u = d.ZoneAxisU, v = Vector3.Cross(Vector3.up, u);
        return Slab(Vector3.Dot(rel, u), Vector3.Dot(dir, u), d.ZoneHalfLength, ref t0, ref t1)
            && Slab(Vector3.Dot(rel, v), Vector3.Dot(dir, v), d.ZoneHalfWidth, ref t0, ref t1);
    }

    static bool Slab(float p, float dd, float h, ref float t0, ref float t1)
    {
        if (Mathf.Abs(dd) < 1e-5F) return Mathf.Abs(p) <= h;
        float a = (-h - p) / dd, b = (h - p) / dd;
        if (a > b) { float tmp = a; a = b; b = tmp; }
        t0 = Mathf.Max(t0, a); t1 = Mathf.Min(t1, b);
        return t0 <= t1;
    }

    // Cutting edge centre and half-width across the arm (same mesh as DiggingCues' teeth line).
    Vector3 EdgeCentre(Excavator m, Vector3 s, out float halfWidth)
    {
        if (edgeRenderer == null && m.BucketTransform != null)
            foreach (var r in m.BucketTransform.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Cube.019") { edgeRenderer = r; break; }
        if (edgeRenderer == null) { halfWidth = 0.6F; return m.BucketTipPosition; }
        Bounds b = edgeRenderer.bounds;
        Vector3 e = b.extents;
        halfWidth = Mathf.Clamp(Mathf.Abs(e.x * s.x) + Mathf.Abs(e.y * s.y) + Mathf.Abs(e.z * s.z), 0.3F, 1.2F);
        return b.center;
    }
}
