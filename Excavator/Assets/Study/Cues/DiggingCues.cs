using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// DIGGING CUES  (C3, operation mode, part of AttentionCues)
//
// Cue 3 - dig area
//   - Bright outline over the boundary of the pit / trench (drawn on the original ground).
//     Cyan normally; orange/red when the tracks come close to the trench edge (trench_edge hazard)
//     or blinking red when the teeth cut outside the marked area.
//   - TEETH LINE (driver view only): a bold horizontal line as wide as the cutting edge, with a dark outline,
//     lying on the ground straight below the teeth (on the trench floor when digging). In the BUCKET SIDE VIEW
//     a line along the arm marks the teeth. Its colour shows the dig state:
//         green  = inside the area, more soil to remove here
//         white  = target depth reached here (thicker line)
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
//   The path never goes below the target depth and keeps clear of buried services (cable, crossing pipe):
//   near a service the bottom of the path is lifted so the teeth stay outside its warning distance.
//   Profile option (digging): the path is drawn ONLY in the BUCKET SIDE VIEW (layer BucketSideOnlyLayer),
//   together with the current bucket angle next to the bucket. The angle label is green when the
//   bucket's curl matches the ideal curl for that point of the path (upright at entry, curling during
//   the drag, fully curled at the exit), orange with "CURL MORE" / "OPEN MORE" otherwise.
//
// Bucket side view height arrow (profile option): a vertical double arrow between the teeth and the
// ground below them with the height in metres; inside the trench it runs from the original ground
// down to the teeth, shows the depth and a short tick at the target depth.
//
// The teeth marker (cue 3) is only shown in digging mode and when the BUCKET angle (as on the
// measurement bar) is over markerMinBucketAngle, i.e. not while the bucket is curled up carrying soil.
// ---------------------------------------------------------------------------

public class DiggingCues : MonoBehaviour
{
    public float cutDepthPerPass = 0.45F;
    public float dragLength = 1.6F;
    public float replanSeconds = 0.6F;
    public float reachFraction = 0.85F;   // do not plan scoops further out than this share of max reach
    public float markerMinBucketAngle = 20F;   // teeth marker only when the bucket angle is above this
    public float serviceMargin = 0.05F;   // extra clearance kept above the warning distance of buried services
    public float curlMatchDegrees = 15F;  // real curl within this of the ideal = green

    AttentionCues root;
    BucketLoad load;

    LineRenderer boundary, ring, ring2, cross, drop;
    LineRenderer teethLine, teethBack, teethGround, sideBack;   // teethGround = bucket side view line
    Renderer edgeRenderer;
    LineRenderer approach, cut, entryRing;
    readonly List<LineRenderer> arrows = new List<LineRenderer>();
    LineRenderer heightArrow, targetTick;
    ScreenBox heightText, heightTextDriver, angleText;
    // Ideal curl keyframes: index into cutPts and angle of the teeth->pivot line from vertical,
    // tipped towards "out" (away from the machine). 20 = bucket upright, 105 = fully curled.
    readonly List<(int idx, float phi)> curlKeys = new List<(int, float)>();
    Vector3 outDir = Vector3.right;

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
        // Teeth line: driver view (across the cutting edge, with a dark outline) and bucket side view (along the arm).
        teethBack   = CueKit.Line(w, "CueTeethLineOutline", 0.24F, true, true, CueKit.DriverOnlyLayer);   // flat on the ground
        teethLine   = CueKit.Line(w, "CueTeethLine", 0.15F, true, true, CueKit.DriverOnlyLayer);
        sideBack    = CueKit.Line(w, "CueTeethSideOutline", 0.16F, true, false, CueKit.BucketSideOnlyLayer);
        teethGround = CueKit.Line(w, "CueTeethSide", 0.1F, true, false, CueKit.BucketSideOnlyLayer);
        teethBack.sortingOrder = -1; sideBack.sortingOrder = -1;
        int side = CueKit.BucketSideOnlyLayer;
        int trajLayer = r.Profile.trajectoryInBucketSideViewOnly ? side : 0;
        approach  = CueKit.Line(w, "CueTrajApproach", 0.035F, true, false, trajLayer);
        cut       = CueKit.Line(w, "CueTrajCut", 0.09F, true, false, trajLayer);
        entryRing = CueKit.Line(w, "CueTrajEntry", 0.06F, true, true, trajLayer);
        for (int i = 0; i < 8; i++) arrows.Add(CueKit.Line(w, "CueTrajArrow", 0.06F, true, false, trajLayer));

        // Height arrow: bucket side view only, or (profile option) also in the driver view (default layer).
        int arrowLayer = r.Profile.heightArrowInDriverView ? 0 : side;
        heightArrow = CueKit.Line(w, "CueHeightArrow", 0.05F, true, false, arrowLayer);
        targetTick  = CueKit.Line(w, "CueTargetTick", 0.05F, true, false, arrowLayer);
        heightText       = new ScreenBox(r.CanvasRoot, r.Font, "CueHeightLabel", 17);
        heightTextDriver = new ScreenBox(r.CanvasRoot, r.Font, "CueHeightLabelDriver", 17);
        angleText        = new ScreenBox(r.CanvasRoot, r.Font, "CueAngleLabel", 17);
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

        bool wantArea = op && root.DigAreaOn;
        if (wantArea != areaShown) { areaShown = wantArea; AttentionCues.LogCue(wantArea, "dig_area", ""); }
        if (wantArea) DrawArea(); else HideArea();

        bool loaded = load != null && load.IsLoaded;
        bool wantTraj = op && root.TrajectoryOn && !loaded && UpdatePlan();
        if (wantTraj != trajShown) { trajShown = wantTraj; AttentionCues.LogCue(wantTraj, "bucket_trajectory", wantTraj ? EntryText() : ""); }
        if (wantTraj) DrawTrajectory(); else HideTrajectory();

        bool overDump = OverDumpArea();
        if (op && root.Profile.bucketSideHeightArrow && ContextViews.BucketSideCamera != null && (BucketInDigArea() || overDump))
            DrawHeightArrow(overDump && !BucketInDigArea());
        else HideHeightArrow();
    }

    // ---------- bucket side view: height / depth arrow ----------

    // Only while digging: teeth over the dig area (0.3 m margin) and lower than 1.5 m above the original
    // ground (same rule as the "Digging" phase of the ContextMonitor, without its 0.5 s delay).
    // Teeth over the dump area (truck bed / spoil square), with 1 m margin.
    bool OverDumpArea()
    {
        if (root.Scenario == null || !root.Scenario.DumpArea(out Vector3 c, out Vector3 fw, out float len, out float wid)) return false;
        fw = CueKit.Flat(fw).normalized;
        Vector3 rel = CueKit.Flat(root.M.BucketTipPosition - c), sd = Vector3.Cross(Vector3.up, fw);
        return Mathf.Abs(Vector3.Dot(rel, fw)) < len / 2F + 1F && Mathf.Abs(Vector3.Dot(rel, sd)) < wid / 2F + 1F;
    }

    // First surface straight below the teeth that is not the machine (truck bed floor, wall top or ground).
    Vector3 SurfaceBelow(Vector3 tip, out string what)
    {
        what = "ground";
        Vector3 g = new Vector3(tip.x, TerrainDigger.CurrentGroundHeight(tip), tip.z);
        float best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(tip + Vector3.up * 0.05F, Vector3.down, 30F, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(root.M.transform) || h.distance >= best) continue;
            best = h.distance; g = h.point;
            what = h.collider.name.Contains("_bedFloor") ? "truck bed" : h.collider.name.Contains("_wall") ? "truck wall" : "ground";
        }
        return g;
    }

    bool BucketInDigArea()
    {
        Vector3 tip = root.M.BucketTipPosition;
        return Digger.Contains(tip, 0.3F) && tip.y - TerrainDigger.ReferenceGroundHeight(tip) < 1.5F;
    }

    void DrawHeightArrow(bool overDump)
    {
        var m = root.M;
        var cam = ContextViews.BucketSideCamera;
        Vector3 tip = m.BucketTipPosition;
        float refY = TerrainDigger.ReferenceGroundHeight(tip);
        float curY = TerrainDigger.CurrentGroundHeight(tip);

        // Draw the arrow just in front of the teeth (towards the camera) so the bucket does not hide it.
        Vector3 toCam = cam.transform.position - tip; toCam.y = 0F;
        Vector3 off = toCam.sqrMagnitude > 0.01F ? toCam.normalized * 0.5F : Vector3.zero;
        Vector3 top, bottom;
        string txt; Color c;
        if (overDump)
        {   // over the truck / spoil square: arrow from the teeth down to the nearest surface below
            // (bed floor, wall top or ground)
            Vector3 s = SurfaceBelow(tip, out string what);
            top = tip + off;
            bottom = s + off;
            float gap = tip.y - s.y;
            txt = $"{gap:0.00} m to {what}";
            c = what == "truck wall" ? CueKit.Warn : CueKit.Guide;
            CueKit.Hide(targetTick);
        }
        else if (tip.y >= refY)
        {   // above the ground: arrow from the teeth down to the ground below them
            top = tip + off;
            bottom = new Vector3(tip.x, Mathf.Min(curY, tip.y), tip.z) + off;
            txt = $"{tip.y - curY:0.00} m";
            c = CueKit.Guide;
            CueKit.Hide(targetTick);
        }
        else
        {   // in the trench: arrow from the original ground down to the teeth, with the target depth tick
            float depth = refY - tip.y, target = TargetDepth;
            top = new Vector3(tip.x, refY, tip.z) + off;
            bottom = tip + off;
            c = depth > target + 0.25F ? CueKit.Danger : depth >= target - 0.12F ? CueKit.Good : CueKit.Guide;
            txt = $"{depth:0.00} m deep\ntarget {target:0.00}";
            Vector3 t = new Vector3(tip.x, refY - target, tip.z) + off;
            if (UndergroundSideView.Active) txt = $"{depth:0.00} m deep (target {target:0.00})";
            Vector3 across = Vector3.Cross(Vector3.up, toCam.sqrMagnitude > 0.01F ? toCam.normalized : Vector3.forward);
            targetTick.loop = false;
            targetTick.positionCount = 2;
            targetTick.SetPosition(0, t - across * 0.35F);
            targetTick.SetPosition(1, t + across * 0.35F);
            CueKit.Paint(targetTick, CueKit.Bright);
            if (UndergroundSideView.Active) CueKit.Hide(targetTick);   // the cut-away already shows the target depth line
        }

        // Double-headed vertical arrow.
        Vector3 side = Vector3.Cross(Vector3.up, off.sqrMagnitude > 0F ? off.normalized : Vector3.forward) * 0.12F;
        heightArrow.loop = false;
        heightArrow.positionCount = 8;
        heightArrow.SetPosition(0, top - Vector3.up * 0.18F + side);
        heightArrow.SetPosition(1, top);
        heightArrow.SetPosition(2, top - Vector3.up * 0.18F - side);
        heightArrow.SetPosition(3, top);
        heightArrow.SetPosition(4, bottom);
        heightArrow.SetPosition(5, bottom + Vector3.up * 0.18F + side);
        heightArrow.SetPosition(6, bottom);
        heightArrow.SetPosition(7, bottom + Vector3.up * 0.18F - side);
        CueKit.Paint(heightArrow, c);

        // In the cut-away the label always goes to the LEFT of the arrow (the pipe label is placed below the pipe).
        PlaceText(heightText, cam, (top + bottom) / 2F, txt, c, UndergroundSideView.Active ? -1 : 0);
        if (root.Profile.heightArrowInDriverView) PlaceText(heightTextDriver, m.OperatorCamera, (top + bottom) / 2F, txt.Replace("\n", "  "), c);
        else HideText(heightTextDriver);
    }

    void HideHeightArrow()
    {
        CueKit.Hide(heightArrow);
        CueKit.Hide(targetTick);
        HideText(heightText);
        HideText(heightTextDriver);
    }

    // ---------- small world-space text (drawn on top, facing the given camera) ----------

    // Readable labels: a coloured tag with dark text (screen-space UI), placed next to a world point as seen
    // by the given camera, inside that camera's window. Hidden when the point is outside the window.
    static void PlaceText(ScreenBox box, Camera cam, Vector3 world, string text, Color c, int forceSide = 0)
    {
        if (cam == null) { box.Hide(); return; }
        Vector3 vp = cam.WorldToViewportPoint(world);
        if (vp.z <= 0F || vp.x < 0F || vp.x > 1F || vp.y < 0F || vp.y > 1F) { box.Hide(); return; }
        Rect r = cam.rect;
        Vector2 p = new Vector2(r.xMin + vp.x * r.width, r.yMin + vp.y * r.height);
        bool rightHalf = forceSide != 0 ? forceSide < 0 : vp.x > 0.6F;   // keep the tag inside the window (or forced side)
        p.x += (rightHalf ? -1F : 1F) * 0.006F;
        c.a = 1F;
        box.ShowTag(p, new Vector2(rightHalf ? 1F : 0F, 0.5F), c, text);
    }

    static void HideText(ScreenBox box) { if (box != null) box.Hide(); }

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

        // Loaded bucket near the dump area (truck bed / spoil square): dump marker instead of the dig marker.
        // Empty bucket away from the dig area (or loaded and away from the dump area): no marker.
        bool loaded = load != null && load.IsLoaded;
        if (loaded && root.Scenario != null && root.Scenario.DumpArea(out Vector3 dc, out Vector3 dfw, out float dl, out float dw)
            && DumpMarker(m, tip, dc, dfw, dl, dw)) return;

        // Dig marker (teeth projection on the ground): only when the bucket is open more than the threshold.
        CueKit.Hide(ring); CueKit.Hide(ring2); CueKit.Hide(cross);   // (used by the dump marker only)
        if (m.bucketAngle <= markerMinBucketAngle)
        {
            CueKit.Hide(drop); HideTeeth();
            if (tipState != "hidden") { tipState = "hidden"; StudyLogger.Log("cue_teeth_marker", $"hidden;bucket_angle={m.bucketAngle:F0}"); }
            return;
        }
        if (loaded || !d.Contains(tip, 1.5F))
        {
            CueKit.Hide(drop); HideTeeth();
            if (tipState != "off") { tipState = "off"; StudyLogger.Log("cue_teeth_marker", "off"); }
            return;
        }

        float groundY = TerrainDigger.CurrentGroundHeight(tip);
        Vector3 g = new Vector3(tip.x, Mathf.Min(groundY, tip.y) + 0.04F, tip.z);
        float depthHere = refY - Mathf.Min(tip.y, groundY);   // how deep the ground (or the teeth) is at this spot
        float target = TargetDepth;
        bool inside = d.Contains(tip, 0.1F);
        bool nearCable = Near(HazardKind.BuriedCable) || Near(HazardKind.BuriedPipe);

        string state; Color col; bool atGrade = false;
        if (nearCable) { state = "near_cable"; col = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.5F)); }
        else if (!inside) { state = "outside"; col = CueKit.Warn; }
        else if (depthHere > target + 0.25F) { state = "over_dig"; col = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(8F, 0.5F)); }
        else if (depthHere >= target - 0.12F) { state = "at_grade"; col = CueKit.Bright; atGrade = true; }
        else { state = "dig_here"; col = CueKit.Good; }
        if (state != tipState) { tipState = state; StudyLogger.Log("cue_teeth_marker", $"{state};depth={depthHere:F2}"); }

        // Teeth line: a bold line as wide as the cutting edge (no drop line).
        // Bucket side view: the same line seen from the side = a short horizontal line along the arm at the teeth.
        Vector3 f = CueKit.Flat(tip - m.SwingCenter).normalized, s = Vector3.Cross(Vector3.up, f);
        Vector3 ec = EdgeCentre(m, s, out float hw);
        float wk = atGrade ? 1.4F : 1F;
        // Driver view: the line lies ON THE GROUND straight below the teeth (in the trench: on the trench floor,
        // i.e. at the teeth when they are cutting), like the earlier ring + cross target symbol.
        Vector3 gl = new Vector3(ec.x, Mathf.Min(TerrainDigger.CurrentGroundHeight(ec), ec.y) + 0.04F, ec.z);
        SetSeg(teethBack, gl - s * (hw + 0.06F), gl + s * (hw + 0.06F), new Color(0F, 0F, 0F, 0.85F), 0.26F * wk);
        SetSeg(teethLine, gl - s * hw, gl + s * hw, col, 0.16F * wk);
        SetSeg(sideBack, ec - f * 0.65F, ec + f * 0.65F, new Color(0F, 0F, 0F, 0.85F), 0.16F * wk);
        SetSeg(teethGround, ec - f * 0.6F, ec + f * 0.6F, col, 0.1F * wk);
        CueKit.Hide(drop);
    }

    static void SetSeg(LineRenderer lr, Vector3 a, Vector3 b, Color c, float width)
    {
        lr.loop = false;
        lr.widthMultiplier = width;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        c.a = Mathf.Max(c.a, 0.85F);   // always solid
        CueKit.Paint(lr, c);
    }

    void HideTeeth() { CueKit.Hide(teethLine); CueKit.Hide(teethBack); CueKit.Hide(teethGround); CueKit.Hide(sideBack); }

    // Centre and half width of the bucket's cutting edge along the lateral direction s (from the edge mesh).
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

    // Dump marker: ring + cross where the soil would land (on the truck bed floor, found by a ray straight
    // down from the teeth), coloured by how far inside the dump area it is:
    //   green = well inside, yellow -> orange = close to the edge / wall, red (blinking) = outside (spill).
    // Shown when the loaded bucket is within 3 m of the dump area. Returns false when not shown.
    bool DumpMarker(Excavator m, Vector3 tip, Vector3 c, Vector3 fw, float len, float wid)
    {
        fw = CueKit.Flat(fw).normalized;
        Vector3 sd = Vector3.Cross(Vector3.up, fw), rel = CueKit.Flat(tip - c);
        float du = Mathf.Abs(Vector3.Dot(rel, fw)) / (len / 2F), dv = Mathf.Abs(Vector3.Dot(rel, sd)) / (wid / 2F);
        float u = Mathf.Max(du, dv);                                   // 0 = centre, 1 = edge
        float outside = Mathf.Max(0F, Mathf.Max(Mathf.Abs(Vector3.Dot(rel, fw)) - len / 2F, Mathf.Abs(Vector3.Dot(rel, sd)) - wid / 2F));
        if (outside > 3F) return false;

        Color col; string state;
        if (u <= 0.5F) { col = CueKit.Good; state = "dump_centre"; }
        else if (u <= 0.85F) { col = Color.Lerp(CueKit.Good, CueKit.Look, (u - 0.5F) / 0.35F); state = "dump_inside"; }
        else if (u <= 1F) { col = Color.Lerp(CueKit.Look, CueKit.Warn, (u - 0.85F) / 0.15F); state = "dump_edge"; }
        else { col = CueKit.Alpha(CueKit.Danger, CueKit.Pulse(8F, 0.5F)); state = "dump_outside"; }
        if (state != tipState) { tipState = state; StudyLogger.Log("cue_teeth_marker", $"{state};u={u:F2}"); }
        HideTeeth();

        // Where the soil would land: first thing below the teeth that is not the machine.
        Vector3 g = new Vector3(tip.x, TerrainDigger.CurrentGroundHeight(tip), tip.z);
        var hits = Physics.RaycastAll(tip + Vector3.up * 0.05F, Vector3.down, 30F, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
            if (!h.collider.transform.IsChildOf(m.transform) && h.distance < best) { best = h.distance; g = h.point; }
        g += Vector3.up * 0.05F;

        CueKit.FlatCircle(ring, g, 0.45F);
        CueKit.Paint(ring, col);
        CueKit.FlatCircle(ring2, g, 0.25F);
        CueKit.Paint(ring2, CueKit.Alpha(col, 0.7F));
        Vector3 a = fw * 0.3F, b = sd * 0.3F;
        cross.loop = false;
        cross.positionCount = 5;
        cross.SetPosition(0, g - a); cross.SetPosition(1, g + a); cross.SetPosition(2, g);
        cross.SetPosition(3, g - b); cross.SetPosition(4, g + b);
        CueKit.Paint(cross, col);
        if (tip.y - g.y > 0.15F)
        {
            drop.positionCount = 2;
            drop.SetPosition(0, tip);
            drop.SetPosition(1, g);
            CueKit.Paint(drop, CueKit.Alpha(col, 0.6F));
        }
        else CueKit.Hide(drop);
        return true;
    }

    static bool Near(HazardKind kind)
    {
        foreach (var h in Hazard.All) if (h.kind == kind && h.active && h.CurrentLevel != Hazard.Level.Clear) return true;
        return false;
    }

    static Hazard Find(HazardKind kind)
    {
        foreach (var h in Hazard.All) if (h.kind == kind && h.active) return h;
        return null;
    }

    void HideArea()
    {
        CueKit.Hide(boundary); CueKit.Hide(ring); CueKit.Hide(ring2); CueKit.Hide(cross); CueKit.Hide(drop);
        HideTeeth();
        tipState = "";
    }

    // ---------- cue 5: bucket trajectory ----------

    // Lowest safe height for the teeth at p: the target depth, lifted where a buried service is close.
    float SafeBottom(Vector3 p)
    {
        float y = TerrainDigger.ReferenceGroundHeight(p) - TargetDepth;
        foreach (var h in Hazard.All)
        {
            if (!(h is SegmentHazard seg) || !h.active) continue;
            if (h.kind != HazardKind.BuriedCable && h.kind != HazardKind.BuriedPipe) continue;
            float R = h.warnDistance + serviceMargin;
            // closest point of the service line, horizontally
            Vector3 a = seg.a, b = seg.b, ab = b - a; ab.y = 0F;
            Vector3 ap = p - a; ap.y = 0F;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-5F, ab.sqrMagnitude));
            Vector3 q = Vector3.Lerp(seg.a, seg.b, t);
            float dh = new Vector2(p.x - q.x, p.z - q.z).magnitude;
            if (dh < R) y = Mathf.Max(y, q.y + Mathf.Sqrt(R * R - dh * dh));
        }
        return y;
    }

    float Remaining(Vector3 p) => Mathf.Max(0F, TerrainDigger.CurrentGroundHeight(p) - SafeBottom(p));

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
        outDir = r;
        curlKeys.Clear();
        Vector3 e = P(tFar);
        float cut0 = Mathf.Min(cutDepthPerPass, Remaining(e));

        cutPts.Clear();
        entry = new Vector3(e.x, TerrainDigger.CurrentGroundHeight(e), e.z);
        aboveEntry = entry + Vector3.up * 0.9F;
        cutPts.Add(aboveEntry);
        cutPts.Add(entry);
        curlKeys.Add((1, 20F));                                       // entry: bucket upright, teeth down
        int dragStart = -1;
        for (float t = tFar - 0.2F; t >= tNear - 1e-3F; t -= 0.2F)
        {
            Vector3 p = P(t);
            float surf = TerrainDigger.CurrentGroundHeight(p);
            float ramp = Mathf.Clamp01((tFar - t) / 0.4F);          // penetrate over the first 0.4 m
            float y = Mathf.Max(surf - cut0 * ramp, SafeBottom(p));  // never below target, never into a service
            cutPts.Add(new Vector3(p.x, Mathf.Min(y, surf), p.z));
            if (dragStart < 0 && ramp >= 1F) dragStart = cutPts.Count - 1;
        }
        int dragEnd = cutPts.Count - 1;
        if (dragStart < 0) dragStart = Mathf.Min(2, dragEnd);
        int dragMid = (dragStart + dragEnd) / 2;
        curlKeys.Add((dragStart, 35F));                               // full depth: start curling
        if (dragMid > dragStart && dragMid < dragEnd) curlKeys.Add((dragMid, 60F));
        curlKeys.Add((dragEnd, 85F));                                 // end of drag: mostly curled
        Vector3 exitP = P(tNear - 0.35F);
        cutPts.Add(new Vector3(exitP.x, TerrainDigger.CurrentGroundHeight(exitP) + 0.15F, exitP.z));   // curl out
        curlKeys.Add((cutPts.Count - 1, 105F));                       // exit: fully curled, soil held
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

        DrawAngleLabel(m, tip);

    }

    // Current curl of the real bucket, in the same measure.
    float CurrentPhi(Excavator m)
    {
        Vector3 d = m.BucketPivotPosition - m.BucketTipPosition;
        float along = Vector3.Dot(d, outDir), up = d.y;
        return Mathf.Atan2(along, up) * Mathf.Rad2Deg;
    }

    // Ideal curl at the path point nearest to the teeth (interpolated between the keyframes), or
    // NaN if the teeth are not close to the path.
    float IdealPhiNear(Vector3 tip)
    {
        if (curlKeys.Count == 0) return float.NaN;
        int nearest = -1; float best = 0.8F;
        for (int i = 1; i < cutPts.Count; i++)
        {
            float d = (cutPts[i] - tip).magnitude;
            if (d < best) { best = d; nearest = i; }
        }
        if (nearest < 0) return float.NaN;
        if (nearest <= curlKeys[0].idx) return curlKeys[0].phi;
        for (int k = 1; k < curlKeys.Count; k++)
            if (nearest <= curlKeys[k].idx)
                return Mathf.Lerp(curlKeys[k - 1].phi, curlKeys[k].phi,
                                  Mathf.InverseLerp(curlKeys[k - 1].idx, curlKeys[k].idx, nearest));
        return curlKeys[curlKeys.Count - 1].phi;
    }

    // Current bucket angle next to the bucket (bucket side view only).
    void DrawAngleLabel(Excavator m, Vector3 tip)
    {
        var cam = ContextViews.BucketSideCamera;
        if (!root.Profile.bucketSideAngleLabel || cam == null) { HideText(angleText); return; }
        float ideal = IdealPhiNear(tip);
        string txt = $"BUCKET {m.bucketAngle:0}°";
        Color c = CueKit.Bright;
        if (!float.IsNaN(ideal))
        {
            float diff = Mathf.DeltaAngle(ideal, CurrentPhi(m));   // > 0: more curled than ideal
            if (Mathf.Abs(diff) <= curlMatchDegrees) { c = CueKit.Good; txt += "  OK"; }
            else { c = CueKit.Warn; txt += diff < 0F ? "  CURL MORE" : "  OPEN MORE"; }
        }
        Vector3 pos = m.BucketPivotPosition + Vector3.up * 0.6F;
        PlaceText(angleText, cam, pos, txt, c);
    }

    void HideTrajectory()
    {
        CueKit.Hide(cut); CueKit.Hide(approach); CueKit.Hide(entryRing);
        foreach (var a in arrows) CueKit.Hide(a);
        HideText(angleText);
        if (load != null && load.IsLoaded) hasPlan = false;   // plan a fresh scoop after dumping
    }
}
