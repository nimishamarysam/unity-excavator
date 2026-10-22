using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// BURIED SERVICES IN THE BUCKET SIDE VIEW  (C2 + C3, profile option bucketSideUnderground - Digging, Dig & dump)
//
// Seen from the side, the surface markers of the buried services (orange cable stakes + ground line,
// yellow pipe stakes) looked like a fence crossing the view and did not say where the services are
// underground. In the BUCKET SIDE VIEW only:
//   - the surface markers are NOT drawn (they stay in the driver view and the other windows);
//   - each service is drawn as a DASHED line (dashed = buried) at its REAL position under the ground,
//     fixed in the world (it does not move with the arm or the bucket), in the markers' colours
//     (cable orange, pipe yellow), seen through the ground;
//   - a compact PROXIMITY WIDGET (parking-sensor bar, see ProximityWidget) per service, "CABLE [#][#][ ][ ][ ] 1.5 m",
//     ONLY while the bucket is within barRange of it (not from the start), DOCKED at a FIXED place in the
//     window (bottom-left corner, stacked) so it never moves with the arm or the bucket;
//   - a TETHER line from the bucket teeth to the nearest point of the service, when closer than barRange.
//   - C2 shows only the service name (no bar, no number, no tether): proximity guidance is C3 only.
//   - C3 only: the colour turns red (pulsing) at warning / contact distance. C2: the same without warnings.
//   - Each service is drawn once, even if it is registered twice.
// In the underground cut-away (UndergroundSideView.Active) the PIPE is drawn by that view instead.
// ---------------------------------------------------------------------------

public class BucketSideServices : MonoBehaviour
{
    public float dash = 0.3F, gap = 0.2F;
    public float cableWidth = 0.07F, pipeWidth = 0.12F;
    public float barRange = 2F;          // the proximity bar starts to fill (and the tether appears) inside this distance (m)

    static readonly string[] MarkerNames = { "CableMarker", "CableLine", "PipeMarker", "PipeLine" };

    AttentionCues root;
    Transform own;
    Canvas canvas;
    readonly List<LineRenderer> dashes = new List<LineRenderer>();
    readonly List<ProximityWidget> labels = new List<ProximityWidget>();
    readonly List<LineRenderer> tethers = new List<LineRenderer>();
    readonly List<Renderer> markers = new List<Renderer>();
    readonly List<Renderer> hiddenNow = new List<Renderer>();
    readonly HashSet<string> seenServices = new HashSet<string>();
    bool active, markersFound;

    struct Req { public ProximityWidget box; public float x, y; public Color c; public string text; public float dist; public bool contact; }
    readonly List<Req> reqs = new List<Req>();

    static bool InContextCondition
    {
        get
        {
            var sm = StudyManager.Instance;
            return sm != null && sm.ShowContextViews &&
                   (sm.CurrentState == StudyManager.State.Running || sm.CurrentState == StudyManager.State.Paused);
        }
    }

    public void Init(AttentionCues r)
    {
        root = r;
        own = new GameObject("BucketSideServices").transform;   // always active (also in C2)
        own.SetParent(transform, false);
        canvas = CueKit.MakeCanvas(own, "BucketSideServicesCanvas", 43);
        Camera.onPreCull += PreCull;
        Camera.onPostRender += PostRender;
    }

    void OnDestroy()
    {
        Camera.onPreCull -= PreCull;
        Camera.onPostRender -= PostRender;
        RestoreMarkers();
    }

    void OnDisable() { RestoreMarkers(); Hide(); active = false; }

    // ---- surface markers: not drawn by the bucket side camera ----
    void PreCull(Camera cam)
    {
        if (!active || cam == null || cam != ContextViews.BucketSideCamera) return;
        foreach (var m in markers)
            if (m != null && m.enabled) { m.enabled = false; hiddenNow.Add(m); }
    }

    void PostRender(Camera cam) { if (cam == ContextViews.BucketSideCamera) RestoreMarkers(); }

    void RestoreMarkers()
    {
        foreach (var m in hiddenNow) if (m != null) m.enabled = true;
        hiddenNow.Clear();
    }

    void FindMarkers()
    {
        markersFound = true;
        markers.Clear();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var n in MarkerNames)
                if (r.name.StartsWith(n)) { markers.Add(r); break; }
    }

    void LateUpdate()
    {
        if (root == null || root.M == null) return;
        bool want = InContextCondition && root.Profile.bucketSideUnderground && !root.Ctl.IsTravelMode
                    && ContextViews.BucketSideCamera != null && HasServices();
        if (want && !markersFound) FindMarkers();
        if (want != active)
        {
            active = want;
            AttentionCues.LogCue(want, "bucket_side_services", "");
            if (!want) { Hide(); markersFound = false; }   // found again next time (new trial = new objects)
        }
        if (active) Draw();
    }

    static bool HasServices()
    {
        foreach (var h in Hazard.All)
            if (h is SegmentHazard && h.active && (h.kind == HazardKind.BuriedCable || h.kind == HazardKind.BuriedPipe)) return true;
        return false;
    }

    void Hide()
    {
        foreach (var d in dashes) CueKit.Hide(d);
        foreach (var l in labels) l.Hide();
        foreach (var t in tethers) CueKit.Hide(t);
    }

    void Draw()
    {
        Camera cam = ContextViews.BucketSideCamera;
        bool guidance = root.Visible;   // C3

        int nd = 0, nl = 0, nt = 0;
        reqs.Clear();
        seenServices.Clear();
        foreach (var h in Hazard.All)
        {
            if (!(h is SegmentHazard seg) || !h.active) continue;
            bool isCable = h.kind == HazardKind.BuriedCable, isPipe = h.kind == HazardKind.BuriedPipe;
            if (!isCable && !isPipe) continue;
            bool linesHere = !(isPipe && UndergroundSideView.Active);   // in the cut-away the pipe ring is drawn by that view
            string key = $"{h.kind}:{seg.a.x:F1},{seg.a.z:F1},{seg.b.x:F1},{seg.b.z:F1}";
            if (!seenServices.Add(key)) continue;                  // registered twice: draw once

            Color baseCol = isCable ? WorldBuilder.Orange : WorldBuilder.Yellow;
            bool warn = guidance && h.CurrentLevel != Hazard.Level.Clear;
            Color col = warn ? CueKit.Alpha(CueKit.Danger, CueKit.Pulse(h.CurrentLevel == Hazard.Level.Contact ? 10F : 6F, 0.5F))
                             : CueKit.Alpha(baseCol, 0.95F);

            // Dashed line at the service's real position (fixed in the world).
            Vector3 a = seg.a, b = seg.b;
            float len = (b - a).magnitude;
            if (len < 1e-3F) continue;
            Vector3 u = (b - a) / len;
            for (float t = 0F; linesHere && t < len; t += dash + gap)
            {
                var d = Pool(nd++, isPipe ? pipeWidth : cableWidth);
                d.widthMultiplier = isPipe ? pipeWidth : cableWidth;
                d.loop = false;
                d.positionCount = 2;
                d.SetPosition(0, a + u * t);
                d.SetPosition(1, a + u * Mathf.Min(len, t + dash));
                CueKit.Paint(d, col);
            }

            float dist = Mathf.Max(0F, h.CurrentDistance);
            // Tether: teeth -> nearest point of the service, when close.
            if (guidance && dist < barRange)   // C3 only
            {
                var tl = Tether(nt++);
                tl.loop = false; tl.positionCount = 2;
                tl.SetPosition(0, h.MachinePoint); tl.SetPosition(1, h.HazardPoint);
                CueKit.Paint(tl, col);
            }
            // Proximity widget only when close; docked at a fixed place (LayoutLabels).
            if (dist < barRange)
                reqs.Add(new Req { box = Label(nl++), y = dist, c = baseCol, text = isCable ? "CABLE" : "PIPE", dist = dist,
                                   contact = guidance && h.CurrentLevel == Hazard.Level.Contact });
        }
        LayoutLabels(cam);
        for (int i = nd; i < dashes.Count; i++) CueKit.Hide(dashes[i]);
        for (int i = nl; i < labels.Count; i++) labels[i].Hide();
        for (int i = nt; i < tethers.Count; i++) CueKit.Hide(tethers[i]);
    }

    LineRenderer Pool(int i, float width)
    {
        while (dashes.Count <= i) dashes.Add(CueKit.Line(own, "CueSideService", width, true, false, CueKit.BucketSideOnlyLayer));
        return dashes[i];
    }

    ProximityWidget Label(int i)
    {
        while (labels.Count <= i) labels.Add(new ProximityWidget(canvas.transform, root.Font, "CueSideServiceProximity"));
        return labels[i];
    }

    LineRenderer Tether(int i)
    {
        while (tethers.Count <= i) tethers.Add(CueKit.Line(own, "CueSideServiceTether", 0.04F, true, false, CueKit.BucketSideOnlyLayer));
        return tethers[i];
    }

    // Widgets docked in the bottom-left corner of the window, stacked upwards, closest service at the bottom.
    void LayoutLabels(Camera cam)
    {
        if (cam == null || reqs.Count == 0) return;
        Rect r = cam.rect;
        float lineH = (ProximityWidget.Height + 4F) / Mathf.Max(1F, Screen.height);   // screen units
        reqs.Sort((ra, rb) => ra.dist.CompareTo(rb.dist));
        for (int i = 0; i < reqs.Count; i++)
        {
            var q = reqs[i];
            q.box.Show(new Vector2(r.xMin + 0.01F * r.width, r.yMin + 0.02F * r.height + i * lineH), new Vector2(0F, 0F),
                       q.text, q.c, q.dist, barRange, q.contact, root.Visible);   // bar: C3 only
        }
    }
}
