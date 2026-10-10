using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// DIGGING CUES  (C3, operation mode, part of AttentionCues)
//
// Cue 3 - dig area
//   - Bright outline over the boundary of the pit / trench (drawn on the original ground).
//     Cyan normally; orange/red when the tracks come close to the trench edge (trench_edge hazard)
//     or blinking red when the teeth cut outside the marked area.
//   - Marker on the ground straight below the bucket teeth (ring + cross, plus a drop line when
//     the bucket is in the air). It shows the dig state at that spot:
//         green  = inside the area, more soil to remove here
//         white  = target depth reached here (double ring)
//         red    = deeper than the target (over-dig) or close to the buried cable
//         orange = outside the marked area
//
// Cue 5 - bucket trajectory
//   Suggested path of the teeth for the NEXT scoop, drawn on top of everything (also below the
//   ground surface): enter at the far end of the strip with the most soil left, cut down about
//   0.45 m (never below the target depth), drag towards the machine about 1.6 m (= one bucket fill),
//   curl out and lift. A thin line shows how to get from the bucket's position to the entry point.
//   The plan is frozen while a scoop is in progress and recalculated when the bucket is empty.
//   It is hidden while the bucket is loaded (carrying/dumping).
// ---------------------------------------------------------------------------

public class DiggingCues : MonoBehaviour
{
    public float cutDepthPerPass = 0.45F;
    public float dragLength = 1.6F;
    public float replanSeconds = 0.6F;
    public float reachFraction = 0.85F;   // do not plan scoops further out than this share of max reach

    AttentionCues root;
    BucketLoad load;

    LineRenderer boundary, ring, ring2, cross, drop;
    LineRenderer approach, cut, entryRing;
    readonly List<LineRenderer> arrows = new List<LineRenderer>();

    bool areaShown, trajShown;
    bool hasPlan;
    float nextPlan;
    readonly List<Vector3> cutPts = new List<Vector3>();
    Vector3 entry, aboveEntry, lateral;
    Vector3 lastLoggedEntry = Vector3.one * 1e6F;
    string tipState = "";

    public void Init(AttentionCues r)
    {
        root = r;
        load = r.Scenario != null ? r.Scenario.GetComponent<BucketLoad>() : null;
        var w = r.WorldRoot;
        boundary  = CueKit.Line(w, "CueDigBoundary", 0.14F, false, true);
        ring      = CueKit.Line(w, "CueTeethRing", 0.07F, true, true);
        ring2     = CueKit.Line(w, "CueTeethRing2", 0.07F, true, true);
        cross     = CueKit.Line(w, "CueTeethCross", 0.05F, true, true);
        drop      = CueKit.Line(w, "CueTeethDrop", 0.03F, true, false);
        approach  = CueKit.Line(w, "CueTrajApproach", 0.035F, true, false);
        cut       = CueKit.Line(w, "CueTrajCut", 0.09F, true, false);
        entryRing = CueKit.Line(w, "CueTrajEntry", 0.06F, true, true);
        for (int i = 0; i < 8; i++) arrows.Add(CueKit.Line(w, "CueTrajArrow", 0.06F, true, false));
    }

    TerrainDigger Digger => TerrainDigger.Instance;

    float TargetDepth
    {
        get
        {
            float? t = root.Scenario != null ? root.Scenario.DigTargetDepth : null;
            return t ?? (Digger != null ? Digger.ZoneMaxDepth : 1F);
        }
    }

    void LateUpdate()
    {
        if (root == null || root.M == null) return;
        bool op = root.Visible && !root.Ctl.IsTravelMode && Digger != null && Digger.HasZone;

        bool wantArea = op && AttentionCues.DigAreaCues;
        if (wantArea != areaShown) { areaShown = wantArea; AttentionCues.LogCue(wantArea, "dig_area", ""); }
        if (wantArea) DrawArea(); else HideArea();

        bool loaded = load != null && load.IsLoaded;
        bool wantTraj = op && AttentionCues.BucketTrajectoryCues && !loaded && UpdatePlan();
        if (wantTraj != trajShown) { trajShown = wantTraj; AttentionCues.LogCue(wantTraj, "bucket_trajectory", wantTraj ? EntryText() : ""); }
        if (wantTraj) DrawTrajectory(); else HideTrajectory();
    }

    // ---------- cue 3: dig area ----------

    void DrawArea()
    {
        var d = Digger;
        var m = root.M;
        Vector3 c = d.ZoneCenter, u = d.ZoneAxisU, v = Vector3.Cross(Vector3.up, u);
        float hu = d.ZoneHalfLength, hv = d.ZoneHalfWidth;

        // Outline along the four edges, every 0.5 m, on the ORIGINAL ground (the edge itself is not dug).
        var pts = new List<Vector3>();
        Vector3[] corners = { c - u * hu - v * hv, c + u * hu - v * hv, c + u * hu + v * hv, c - u * hu + v * hv };
        for (int e = 0; e < 4; e++)
        {
            Vector3 a = corners[e], b = corners[(e + 1) % 4];
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 0.5F));
            for (int k = 0; k < n; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, k / (float)n);
                p.y = TerrainDigger.ReferenceGroundHeight(p) + 0.06F;
                pts.Add(p);
            }
        }
        boundary.loop = true;
        boundary.positionCount = pts.Count;
        boundary.SetPositions(pts.ToArray());

        Vector3 tip = m.BucketTipPosition;
        float refY = TerrainDigger.ReferenceGroundHeight(tip);
        bool cuttingOutside = refY - tip.y > 0.1F && !d.Contains(tip, 0.3F);
        Hazard edge = Find(HazardKind.TrenchEdge);
        Color bc = CueKit.Guide;
        if (cuttingOutside) bc = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.5F));
        else if (edge != null && edge.CurrentLevel == Hazard.Level.Contact) bc = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.6F));
        else if (edge != null && edge.CurrentLevel == Hazard.Level.Warning) bc = CueKit.Alpha(CueKit.Warn, CueKit.Pulse(6F, 0.5F));
        CueKit.Paint(boundary, bc);

        // Teeth projection on the ground (current, possibly dug, surface).
        float groundY = TerrainDigger.CurrentGroundHeight(tip);
        Vector3 g = new Vector3(tip.x, Mathf.Min(groundY, tip.y) + 0.04F, tip.z);
        float depthHere = refY - Mathf.Min(tip.y, groundY);   // how deep the ground (or the teeth) is at this spot
        float target = TargetDepth;
        bool inside = d.Contains(tip, 0.1F);
        Hazard cable = Find(HazardKind.BuriedCable);
        bool nearCable = cable != null && cable.CurrentLevel != Hazard.Level.Clear;

        string state; Color col; bool atGrade = false;
        if (nearCable) { state = "near_cable"; col = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.5F)); }
        else if (!inside) { state = "outside"; col = CueKit.Warn; }
        else if (depthHere > target + 0.25F) { state = "over_dig"; col = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(8F, 0.5F)); }
        else if (depthHere >= target - 0.12F) { state = "at_grade"; col = CueKit.Bright; atGrade = true; }
        else { state = "dig_here"; col = CueKit.Good; }
        if (state != tipState) { tipState = state; StudyLogger.Log("cue_teeth_marker", $"{state};depth={depthHere:F2}"); }

        CueKit.FlatCircle(ring, g, 0.45F);
        CueKit.Paint(ring, col);
        if (atGrade) { CueKit.FlatCircle(ring2, g, 0.65F); CueKit.Paint(ring2, col); } else CueKit.Hide(ring2);

        Vector3 f = CueKit.Flat(tip - m.SwingCenter).normalized, s = Vector3.Cross(Vector3.up, f);
        cross.loop = false;
        cross.positionCount = 5;
        cross.SetPosition(0, g - f * 0.3F);
        cross.SetPosition(1, g + f * 0.3F);
        cross.SetPosition(2, g);
        cross.SetPosition(3, g - s * 0.3F);
        cross.SetPosition(4, g + s * 0.3F);
        CueKit.Paint(cross, col);

        if (tip.y - g.y > 0.15F)
        {
            drop.positionCount = 2;
            drop.SetPosition(0, tip);
            drop.SetPosition(1, g);
            CueKit.Paint(drop, CueKit.Alpha(col, 0.6F));
        }
        else CueKit.Hide(drop);
    }

    static Hazard Find(HazardKind kind)
    {
        foreach (var h in Hazard.All) if (h.kind == kind && h.active) return h;
        return null;
    }

    void HideArea()
    {
        CueKit.Hide(boundary); CueKit.Hide(ring); CueKit.Hide(ring2); CueKit.Hide(cross); CueKit.Hide(drop);
        tipState = "";
    }

    // ---------- cue 5: bucket trajectory ----------

    float Remaining(Vector3 p)
    {
        float bottom = TerrainDigger.ReferenceGroundHeight(p) - TargetDepth;
        return Mathf.Max(0F, TerrainDigger.CurrentGroundHeight(p) - bottom);
    }

    bool Scooping()
    {
        var m = root.M;
        Vector3 tip = m.BucketTipPosition;
        return (load != null && load.Fill > 0F) || tip.y < TerrainDigger.CurrentGroundHeight(tip) + 0.15F;
    }

    // Returns true if there is a valid plan to show.
    bool UpdatePlan()
    {
        if (hasPlan && (Scooping() || Time.unscaledTime < nextPlan)) return true;
        nextPlan = Time.unscaledTime + replanSeconds;
        hasPlan = Plan();
        if (hasPlan && (entry - lastLoggedEntry).magnitude > 0.3F)
        {
            lastLoggedEntry = entry;
            StudyLogger.Log("cue_trajectory_plan", EntryText());
        }
        return hasPlan;
    }

    string EntryText() => $"entry_x={entry.x:F1};entry_z={entry.z:F1};depth={entry.y - (cutPts.Count > 2 ? cutPts[2].y : entry.y):F2}";

    bool Plan()
    {
        var d = Digger;
        var m = root.M;
        Vector3 c = CueKit.Flat(m.SwingCenter);
        Vector3 zc = CueKit.Flat(d.ZoneCenter);
        Vector3 r = (zc - c).sqrMagnitude > 0.01F ? (zc - c).normalized : m.ForwardFlat;   // reach direction
        Vector3 l = Vector3.Cross(Vector3.up, r);                                          // across
        lateral = l;

        // Extent of the zone in (along reach, across) coordinates relative to the swing centre.
        Vector3 u = d.ZoneAxisU, v = Vector3.Cross(Vector3.up, u);
        float rMin = float.MaxValue, rMax = float.MinValue, lMin = float.MaxValue, lMax = float.MinValue;
        foreach (var k in new[] { u * d.ZoneHalfLength + v * d.ZoneHalfWidth, u * d.ZoneHalfLength - v * d.ZoneHalfWidth,
                                  -u * d.ZoneHalfLength + v * d.ZoneHalfWidth, -u * d.ZoneHalfLength - v * d.ZoneHalfWidth })
        {
            Vector3 p = zc + k - c;
            rMin = Mathf.Min(rMin, Vector3.Dot(p, r)); rMax = Mathf.Max(rMax, Vector3.Dot(p, r));
            lMin = Mathf.Min(lMin, Vector3.Dot(p, l)); lMax = Mathf.Max(lMax, Vector3.Dot(p, l));
        }
        float far = Mathf.Min(rMax - 0.3F, m.MaxReach * reachFraction);
        float near = rMin + 0.3F;
        if (far - near < 0.6F) return false;

        // Choose the strip (along the reach direction) with the most soil left; prefer strips close
        // to where the bucket is now, so the operator swings less.
        float tipL = Vector3.Dot(CueKit.Flat(m.BucketTipPosition) - c, l);
        float best = 0F, bestL = 0F;
        float l0 = lMin + 0.45F, l1 = lMax - 0.45F;
        if (l1 < l0) { l0 = l1 = (lMin + lMax) / 2F; }
        for (float s = l0; s <= l1 + 1e-3F; s += 0.4F)
        {
            float score = 0F;
            for (float t = near; t <= far; t += 0.3F)
            {
                Vector3 p = c + r * t + l * s;
                if (d.Contains(p)) score += Remaining(p);
            }
            score -= 0.05F * Mathf.Abs(s - tipL);
            if (score > best) { best = score; bestL = s; }
        }
        if (best < 0.15F) return false;   // the area is finished

        // Entry: the farthest point of that strip that still has soil.
        float tFar = -1F;
        for (float t = far; t >= near; t -= 0.2F)
        {
            Vector3 p = c + r * t + l * bestL;
            if (d.Contains(p) && Remaining(p) > 0.08F) { tFar = t; break; }
        }
        if (tFar < 0F) return false;
        float tNear = Mathf.Max(near, tFar - dragLength);

        Vector3 P(float t) => c + r * t + l * bestL;
        Vector3 e = P(tFar);
        float cut0 = Mathf.Min(cutDepthPerPass, Remaining(e));

        cutPts.Clear();
        entry = new Vector3(e.x, TerrainDigger.CurrentGroundHeight(e), e.z);
        aboveEntry = entry + Vector3.up * 0.9F;
        cutPts.Add(aboveEntry);
        cutPts.Add(entry);
        for (float t = tFar - 0.2F; t >= tNear - 1e-3F; t -= 0.2F)
        {
            Vector3 p = P(t);
            float surf = TerrainDigger.CurrentGroundHeight(p);
            float bottom = TerrainDigger.ReferenceGroundHeight(p) - TargetDepth;
            float ramp = Mathf.Clamp01((tFar - t) / 0.4F);          // penetrate over the first 0.4 m
            float y = Mathf.Max(surf - cut0 * ramp, bottom);
            cutPts.Add(new Vector3(p.x, Mathf.Min(y, surf), p.z));
        }
        Vector3 exitP = P(tNear - 0.35F);
        cutPts.Add(new Vector3(exitP.x, TerrainDigger.CurrentGroundHeight(exitP) + 0.15F, exitP.z));   // curl out
        Vector3 liftP = P(tNear - 0.6F);
        cutPts.Add(new Vector3(liftP.x, TerrainDigger.CurrentGroundHeight(liftP) + 1.4F, liftP.z));    // lift
        return true;
    }

    void DrawTrajectory()
    {
        var m = root.M;
        Vector3 tip = m.BucketTipPosition;

        // Main path of the scoop.
        cut.loop = false;
        cut.positionCount = cutPts.Count;
        cut.SetPositions(cutPts.ToArray());
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(CueKit.Guide, 0F), new GradientColorKey(CueKit.Bright, 1F) },
                  new[] { new GradientAlphaKey(0.95F, 0F), new GradientAlphaKey(0.7F, 1F) });
        cut.colorGradient = g;

        // Entry marker on the ground.
        CueKit.FlatCircle(entryRing, entry + Vector3.up * 0.04F, 0.4F);
        CueKit.Paint(entryRing, CueKit.Alpha(CueKit.Guide, CueKit.Pulse(4F, 0.5F)));

        // How to get there: only while the bucket is not yet scooping and still away from the entry.
        if (!Scooping() && (tip - aboveEntry).magnitude > 0.6F)
        {
            approach.positionCount = 2;
            approach.SetPosition(0, tip);
            approach.SetPosition(1, aboveEntry);
            CueKit.Paint(approach, CueKit.Alpha(CueKit.Guide, 0.45F));
        }
        else CueKit.Hide(approach);

        // Arrows moving along the scoop path.
        float total = 0F;
        var cum = new float[cutPts.Count];
        for (int i = 1; i < cutPts.Count; i++) { total += (cutPts[i] - cutPts[i - 1]).magnitude; cum[i] = total; }
        float spacing = 0.7F;
        float phase = Mathf.Repeat(Time.unscaledTime * 0.8F, spacing);
        int used = 0;
        for (float s = phase + 0.2F; s < total - 0.1F && used < arrows.Count; s += spacing)
        {
            int i = 1;
            while (i < cum.Length - 1 && cum[i] < s) i++;
            float t = Mathf.InverseLerp(cum[i - 1], cum[i], s);
            Vector3 p = Vector3.Lerp(cutPts[i - 1], cutPts[i], t);
            Vector3 dir = (cutPts[i] - cutPts[i - 1]).normalized;
            var lr = arrows[used++];
            CueKit.Chevron(lr, p, dir, lateral, 0.22F, 0.2F);
            CueKit.Paint(lr, CueKit.Alpha(CueKit.Bright, 0.9F));
        }
        for (int i = used; i < arrows.Count; i++) CueKit.Hide(arrows[i]);
    }

    void HideTrajectory()
    {
        CueKit.Hide(cut); CueKit.Hide(approach); CueKit.Hide(entryRing);
        foreach (var a in arrows) CueKit.Hide(a);
        if (load != null && load.IsLoaded) hasPlan = false;   // plan a fresh scoop after dumping
    }
}
