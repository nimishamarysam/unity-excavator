using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// OBSTACLE CUES  (C3, part of AttentionCues)
// Cue 1 - static obstacle (barrel, gate post, truck, power line, buried cable)
// Cue 2 - dynamic obstacle (worker)
//
// Driver view:   a rectangle around the object with a label (what it is, distance, which side
//                to pass it, which way a worker moves). If the object is outside the driver view
//                or behind the cab, a label at the matching screen edge points to it instead.
// Context views: a 3D wire box and a ring on the ground around the object (context-only layer).
//                Workers also get an arrow on the ground showing where they walk in the next 2 s.
// Because everything is recalculated every frame, the overlays move with a walking worker.
//
// A cue appears when the object is closer than its cue distance (see Range) and disappears again
// 0.5 m further away. Colour: yellow = in cue range, orange (blinking) = warning distance, red = contact.
//
// BURIED services (cable, pipe) use a DIFFERENT cue from visible obstacles, so they can't be mistaken for
// an object standing on the ground. Visible obstacles have a rectangle (driver view) and a 3D box (context views).
// Buried services have NO box: like a utility locate marking, a DASHED line is painted on the ground
// directly above the service (on the dug trench floor too), in the service's marker colour (cable orange,
// pipe yellow), with small arrows pointing DOWN into the ground. No written tag: a PROXIMITY WIDGET (parking
// sensor bar, see ProximityWidget) "PIPE [#][#][#][ ][ ] 0.3 m" and a TETHER line from the bucket teeth to the
// nearest point of the service show how close the bucket is. At warning / contact the marking turns orange / red.
// Drawn in the driver view and the bird's-eye / bucket front views (not in the bucket side view, which shows
// the services in its own section, see BucketSideServices).
// ---------------------------------------------------------------------------

public class ObstacleCues : MonoBehaviour
{
    public float workerRange = 12F;        // metres from the machine
    public float obstacleExtra = 3F;       // cue distance = warning distance + this
    public float powerLineExtra = 2F;
    public float cableExtra = 0.8F;        // buried cable is measured from the bucket teeth
    public float hysteresis = 0.5F;
    public float motionPreview = 2F;       // seconds of walking shown by the worker's ground arrow

    AttentionCues root;
    readonly Dictionary<Hazard, Track> tracks = new Dictionary<Hazard, Track>();
    readonly HashSet<Hazard> seen = new HashSet<Hazard>();

    class Track
    {
        public Hazard h;
        public Renderer[] renderers;
        public ScreenBox box;
        public LineRenderer wire, ring, motion, segment;
        public readonly List<LineRenderer> marks = new List<LineRenderer>();   // buried: dashes + down arrows
        public ProximityWidget widget;                                         // buried: parking-sensor bar
        public LineRenderer tetherD, tetherC;                                  // buried: teeth -> service (driver / context)
        public bool shown;
        public Vector3 lastPos, vel;
        public bool hasLast;
    }

    public void Init(AttentionCues r) { root = r; }

    // Smoothed walking velocity of a hazard (zero for things that do not move).
    public Vector3 VelocityOf(Hazard h) => h != null && tracks.TryGetValue(h, out var t) ? t.vel : Vector3.zero;

    public static bool IsDynamic(Hazard h) => h.kind == HazardKind.Worker;

    void LateUpdate()
    {
        if (root == null || root.M == null) return;
        bool visible = root.Visible;
        Camera cam = root.M.OperatorCamera;
        int[] edgeCount = new int[5];
        buriedSlot = 0;
        seen.Clear();

        // 1. Which hazards want a cue.
        wanted.Clear();
        foreach (var h in Hazard.All)
        {
            if (h == null || h is RectEdgeHazard) continue;   // trench edge: shown by the dig-area outline
            var t = Get(h);
            seen.Add(h);
            UpdateVelocity(t);
            bool typeOn = IsDynamic(h) ? root.DynamicOn : root.StaticOn;
            if (visible && typeOn && h.active && h.gameObject.activeInHierarchy && InRange(h, t.shown) && Relevant(h)) wanted.Add(h);
        }
        // 2. Profile limit: keep only the most urgent ones (contact > warning > in range; workers first; closest first).
        int max = root.Profile.maxObstacleCues;
        if (max > 0 && wanted.Count > max)
        {
            wanted.Sort((a, b) => Urgency(a).CompareTo(Urgency(b)));
            wanted.RemoveRange(max, wanted.Count - max);
        }

        // 3. Show / hide.
        foreach (var h in Hazard.All)
        {
            if (h == null || h is RectEdgeHazard) continue;
            var t = tracks[h];
            bool want = wanted.Contains(h);
            if (want != t.shown)
            {
                t.shown = want;
                AttentionCues.LogCue(want, IsDynamic(h) ? "dynamic_obstacle" : "static_obstacle", $"{h.hazardId};dist={h.CurrentDistance:F2}");
            }
            if (!want) { HideTrack(t); continue; }
            Draw(t, cam, edgeCount);
        }

        // Hazards that disappeared (e.g. a worker hidden again).
        foreach (var kv in tracks)
        {
            if (seen.Contains(kv.Key) || !kv.Value.shown) continue;
            kv.Value.shown = false;
            if (kv.Key != null) AttentionCues.LogCue(false, IsDynamic(kv.Key) ? "dynamic_obstacle" : "static_obstacle", kv.Key.hazardId + ";gone");
            HideTrack(kv.Value);
        }
    }

    readonly List<Hazard> wanted = new List<Hazard>();
    int buriedSlot;   // buried-service widgets docked at a fixed place: stacking index this frame

    static float Urgency(Hazard h)
    {
        float level = h.CurrentLevel == Hazard.Level.Contact ? 0F : h.CurrentLevel == Hazard.Level.Warning ? 100F : 200F;
        return level + (IsDynamic(h) ? -50F : 0F) + Mathf.Min(h.CurrentDistance, 49F);
    }

    // ---------- when is a cue shown ----------

    float Range(Hazard h)
    {
        switch (h.kind)
        {
            case HazardKind.Worker: return workerRange;
            case HazardKind.Obstacle: return h.warnDistance + obstacleExtra;
            case HazardKind.PowerLine: return h.warnDistance + powerLineExtra;
            case HazardKind.BuriedCable:
            case HazardKind.BuriedPipe: return h.warnDistance + cableExtra;
            default: return -1F;   // truck: only at warning level (dumping right over it is normal)
        }
    }

    bool InRange(Hazard h, bool shownNow)
    {
        float range = Range(h);
        if (range <= 0F) return h.CurrentLevel != Hazard.Level.Clear;
        return h.CurrentDistance <= (shownNow ? range + hysteresis : range);
    }

    // Profile filters (e.g. driving: only obstacles near the planned path, gate posts only when close).
    bool Relevant(Hazard h)
    {
        var p = root.Profile;
        if (p.truckOnlyAtContact && h.kind == HazardKind.Truck) return h.CurrentLevel == Hazard.Level.Contact;
        if (p.obstaclesHiddenInDigMode && !root.Ctl.IsTravelMode && h.kind == HazardKind.Obstacle)
            return h.CurrentLevel == Hazard.Level.Contact;
        if (IsDynamic(h) || h.CurrentLevel != Hazard.Level.Clear) return true;
        if (p.staticOnlyAtWarning) return false;
        if (p.gatePostsOnlyAtWarning && h.hazardId.Contains("post")) return false;
        if (p.obstaclesOnlyNearPath && h.kind == HazardKind.Obstacle)
        {
            var d = root.Driving;
            return d == null || !d.HasPath || d.NearPath.Contains(h);
        }
        return true;
    }

    // ---------- drawing ----------

    Track Get(Hazard h)
    {
        if (tracks.TryGetValue(h, out var t)) return t;
        t = new Track { h = h };
        if (h is ColliderHazard) t.renderers = h.GetComponentsInChildren<Renderer>(true);
        string id = h.hazardId;
        t.box = new ScreenBox(root.CanvasRoot, root.Font, "CueBox_" + id);
        int L = CueKit.ContextOnlyLayer;
        t.wire = CueKit.Line(root.WorldRoot, "CueWire_" + id, root.Profile.thinHighlight ? 0.035F : 0.07F, true, false, L);
        t.ring = CueKit.Line(root.WorldRoot, "CueRing_" + id, 0.12F, true, true, L);
        t.motion = CueKit.Line(root.WorldRoot, "CueMotion_" + id, 0.15F, true, true, L);
        t.segment = CueKit.Line(root.WorldRoot, "CueSegment_" + id, 0.2F, true, false, L);
        tracks[h] = t;
        return t;
    }

    void UpdateVelocity(Track t)
    {
        float dt = Time.deltaTime;
        if (dt <= 0F || !t.h.gameObject.activeInHierarchy) { t.hasLast = false; return; }
        Vector3 p = t.h.transform.position;
        if (t.hasLast)
        {
            Vector3 v = CueKit.Flat(p - t.lastPos) / dt;
            t.vel = Vector3.Lerp(t.vel, v, 1F - Mathf.Exp(-8F * dt));
        }
        t.lastPos = p;
        t.hasLast = true;
    }

    static Color Severity(Hazard h)
    {
        switch (h.CurrentLevel)
        {
            case Hazard.Level.Contact: return CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.6F));
            case Hazard.Level.Warning: return CueKit.Alpha(CueKit.Warn, CueKit.Pulse(6F, 0.5F));
            default: return CueKit.Alpha(CueKit.Look, 0.95F);
        }
    }

    static bool IsBuried(Hazard h) => h.kind == HazardKind.BuriedCable || h.kind == HazardKind.BuriedPipe;

    public float markHalfLength = 4F;    // buried marking drawn this far each side of the point closest to the teeth (m)
    public float dash = 0.4F, dashGap = 0.25F, arrowEvery = 1.5F;

    LineRenderer Mark(Track t, int i)
    {
        while (t.marks.Count <= i)
        {
            // even = driver view copy, odd = context view copy (bird's-eye, bucket front view)
            int layer = t.marks.Count % 2 == 0 ? CueKit.DriverOnlyLayer : CueKit.ContextOnlyLayer;
            t.marks.Add(CueKit.Line(root.WorldRoot, "CueBuriedMark_" + t.h.hazardId, 0.12F, false, true, layer));
        }
        return t.marks[i];
    }

    // Buried service: dashed line on the ground above it + arrows pointing down + frameless tag.
    void DrawBuried(Track t, Camera cam, int[] edgeCount)
    {
        var h = t.h;
        var seg = (SegmentHazard)h;
        Color baseCol = h.kind == HazardKind.BuriedCable ? WorldBuilder.Orange : WorldBuilder.Yellow;
        Color col = h.CurrentLevel == Hazard.Level.Clear ? CueKit.Alpha(baseCol, 0.95F) : Severity(h);
        CueKit.Hide(t.wire); CueKit.Hide(t.ring); CueKit.Hide(t.motion); CueKit.Hide(t.segment);

        // Part of the service around the point closest to the teeth.
        Vector3 a = seg.a, b = seg.b, ab = b - a;
        float len = ab.magnitude;
        if (len < 1e-3F) { HideTrack(t); return; }
        Vector3 u = ab / len;
        float s0 = Vector3.Dot(h.HazardPoint - a, u);
        float from = Mathf.Max(0F, s0 - markHalfLength), to = Mathf.Min(len, s0 + markHalfLength);

        int k = 0;
        for (float s = from; s < to; s += dash + dashGap)
        {
            Vector3 p = CueKit.OnSurface(a + u * s, 0.06F), q = CueKit.OnSurface(a + u * Mathf.Min(to, s + dash), 0.06F);
            for (int copy = 0; copy < 2; copy++)
            {
                var lr = Mark(t, k++);
                lr.alignment = LineAlignment.TransformZ;
                lr.widthMultiplier = 0.12F;
                lr.loop = false; lr.positionCount = 2;
                lr.SetPosition(0, p); lr.SetPosition(1, q);
                CueKit.Paint(lr, col);
            }
        }
        // Down arrows standing on the line: "it is under here".
        Vector3 side = Vector3.Cross(Vector3.up, u);
        for (float s = from + arrowEvery / 2F; s < to; s += arrowEvery)
        {
            Vector3 g = CueKit.OnSurface(a + u * s, 0.06F);
            for (int copy = 0; copy < 2; copy++)
            {
                var lr = Mark(t, k++);
                lr.alignment = LineAlignment.TransformZ;   // flat in the vertical plane across the service
                lr.widthMultiplier = 0.05F;
                lr.loop = false; lr.positionCount = 5;
                lr.SetPosition(0, g + Vector3.up * 0.7F);
                lr.SetPosition(1, g);
                lr.SetPosition(2, g + Vector3.up * 0.2F + side * 0.12F);
                lr.SetPosition(3, g);
                lr.SetPosition(4, g + Vector3.up * 0.2F - side * 0.12F);
                CueKit.Paint(lr, col);
            }
        }
        for (int i = k; i < t.marks.Count; i++) CueKit.Hide(t.marks[i]);

        // TETHER: a line from the bucket teeth to the nearest point of the service - its length IS the gap.
        if (t.tetherD == null)
        {
            t.tetherD = CueKit.Line(root.WorldRoot, "CueBuriedTether_" + h.hazardId, 0.04F, true, false, CueKit.DriverOnlyLayer);
            t.tetherC = CueKit.Line(root.WorldRoot, "CueBuriedTetherCtx_" + h.hazardId, 0.04F, true, false, CueKit.ContextOnlyLayer);
        }
        foreach (var lr in new[] { t.tetherD, t.tetherC })
        {
            lr.loop = false; lr.positionCount = 2;
            lr.SetPosition(0, h.MachinePoint); lr.SetPosition(1, h.HazardPoint);
            CueKit.Paint(lr, col);
        }

        // PROXIMITY WIDGET (parking sensor) instead of a written tag: "PIPE [#][#][#][ ][ ] 0.3 m".
        if (t.widget == null) t.widget = new ProximityWidget(root.CanvasRoot, root.Font, "CueBuriedProximity_" + h.hazardId);
        t.box.Hide();
        // Docked at a FIXED place (bottom centre of the driver view, stacked), so it does not move with the bucket.
        string what = h.kind == HazardKind.BuriedCable ? "CABLE" : "PIPE";
        if (cam != null)
        {
            Rect r = cam.rect;
            float y = r.yMin + 0.03F + buriedSlot++ * (ProximityWidget.Height + 4F) / Mathf.Max(1F, Screen.height);
            t.widget.Show(new Vector2(r.center.x, y), new Vector2(0.5F, 0F), what, baseCol, h.CurrentDistance, Range(h),
                          h.CurrentLevel == Hazard.Level.Contact);
        }
        else t.widget.Hide();
    }

    static string Name(Hazard h)
    {
        switch (h.kind)
        {
            case HazardKind.Worker: return "WORKER";
            case HazardKind.Truck: return "TRUCK";
            case HazardKind.PowerLine: return "POWER LINE";
            case HazardKind.BuriedCable: return "BURIED CABLE";
            case HazardKind.BuriedPipe: return "CROSSING PIPE";
            default: return h.hazardId.Contains("post") ? "GATE POST" : "OBSTACLE";
        }
    }

    Bounds WorldBounds(Track t)
    {
        var h = t.h;
        if (h is SegmentHazard)
        {
            if (h.kind == HazardKind.BuriedCable || h.kind == HazardKind.BuriedPipe)
                return new Bounds(CueKit.OnSurface(h.HazardPoint, 0.2F), new Vector3(1.6F, 0.4F, 1.6F));
            return new Bounds(h.HazardPoint, new Vector3(1.6F, 0.8F, 1.6F));
        }
        bool any = false;
        Bounds b = new Bounds(h.transform.position, Vector3.one);
        if (t.renderers != null)
            foreach (var r in t.renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        return b;
    }

    string MotionText(Track t, Camera cam)
    {
        float speed = t.vel.magnitude;
        if (speed < 0.25F) return "STANDING";
        Vector3 toMachine = CueKit.Flat(root.M.SwingCenter - t.h.transform.position).normalized;
        float closing = Vector3.Dot(t.vel, toMachine) / speed;
        if (closing > 0.6F) return "COMING TOWARDS YOU";
        if (closing < -0.6F) return "MOVING AWAY";
        Vector3 right = cam != null ? cam.transform.right : Vector3.Cross(Vector3.up, root.M.ForwardFlat);
        return Vector3.Dot(t.vel, right) > 0F ? "WALKING RIGHT >>" : "<< WALKING LEFT";
    }

    void Draw(Track t, Camera cam, int[] edgeCount)
    {
        var h = t.h;
        if (IsBuried(h) && h is SegmentHazard) { DrawBuried(t, cam, edgeCount); return; }
        Bounds b = WorldBounds(t);
        Color col = Severity(h);

        // --- driver view: rectangle or edge marker ---
        string label = $"{Name(h)}  {h.CurrentDistance:0.0} m";
        if (root.PassAdvice.TryGetValue(h, out var advice)) label += "\n" + advice;
        if (IsDynamic(h) && root.Profile.workerMotionText)
        {
            string mt = MotionText(t, cam);
            if (!root.Profile.workerMotionOnlyApproaching || mt == "COMING TOWARDS YOU") label += "\n" + mt;
        }

        Vector3 centerVp = Vector3.zero;
        if (cam != null && CueKit.ProjectBox(cam, b, out Rect r, out centerVp))
        {
            if (root.PassAdvice.ContainsKey(h))
            {   // driving advice ("PASS ON THE LEFT") still needs words
                if (t.widget != null) t.widget.Hide();
                t.box.ShowBox(r, col, label);
            }
            else
            {   // rectangle + proximity widget instead of written text; workers get an arrow for their walking direction
                t.box.ShowFrame(r, col);
                if (t.widget == null) t.widget = new ProximityWidget(root.CanvasRoot, root.Font, "CueProximity_" + h.hazardId);
                float arrow = float.NaN;
                if (IsDynamic(h) && root.Profile.workerMotionText && t.vel.magnitude > 0.25F)
                {
                    Vector3 p0 = cam.WorldToViewportPoint(b.center), p1 = cam.WorldToViewportPoint(b.center + t.vel.normalized);
                    Vector2 dv = new Vector2((p1.x - p0.x) * cam.pixelWidth, (p1.y - p0.y) * cam.pixelHeight);
                    if (dv.sqrMagnitude > 1e-4F) arrow = Mathf.Atan2(dv.y, dv.x) * Mathf.Rad2Deg;
                }
                bool below = r.yMax > 0.93F;
                t.widget.Show(new Vector2(r.xMin, below ? r.yMax - 0.005F : r.yMax + 0.003F), new Vector2(0F, below ? 1F : 0F),
                              Name(h), col, h.CurrentDistance, Range(h), h.CurrentLevel == Hazard.Level.Contact, true, arrow);
            }
        }
        else if (cam != null && (root.Profile.edgeMarkersWorkersOnly ? IsDynamic(h)
                                 : root.Profile.edgeMarkersForAll ? (IsDynamic(h) || h.CurrentLevel != Hazard.Level.Clear)
                                                                  : (IsDynamic(h) && h.CurrentLevel != Hazard.Level.Clear)))
        {
            if (t.widget != null) t.widget.Hide();
            EdgeMarker(t, cam, centerVp, col, $"{Name(h)}  {h.CurrentDistance:0.0} m", edgeCount);
        }
        else
        {
            if (t.widget != null) t.widget.Hide();
            t.box.Hide();
        }

        // --- context views: 3D highlight ---
        if (h is SegmentHazard seg)
        {
            Vector3 a = seg.a, bb = seg.b;
            if (h.kind == HazardKind.BuriedCable || h.kind == HazardKind.BuriedPipe) { a = CueKit.OnSurface(a, 0.08F); bb = CueKit.OnSurface(bb, 0.08F); }
            t.segment.positionCount = 2;
            t.segment.SetPosition(0, a);
            t.segment.SetPosition(1, bb);
            CueKit.Paint(t.segment, CueKit.Alpha(col, 0.85F));
            CueKit.WireBox(t.wire, b);
            CueKit.Paint(t.wire, col);
            CueKit.Hide(t.ring);
        }
        else
        {
            Bounds big = b;
            big.Expand(new Vector3(0.4F, 0.3F, 0.4F));
            CueKit.WireBox(t.wire, big);
            CueKit.Paint(t.wire, col);
            if (root.Profile.groundRing)
            {
                CueKit.Circle(t.ring, b.center, Mathf.Max(b.extents.x, b.extents.z) + 0.7F);
                CueKit.Paint(t.ring, CueKit.Alpha(col, 0.8F));
            }
            else CueKit.Hide(t.ring);
            CueKit.Hide(t.segment);
        }

        // --- worker: where will they be in the next seconds ---
        if (IsDynamic(h) && root.Profile.workerMotionArrow && t.vel.magnitude > 0.25F)
        {
            Vector3 p0 = CueKit.OnSurface(h.transform.position, 0.08F);
            Vector3 d = t.vel.normalized, side = Vector3.Cross(Vector3.up, d);
            Vector3 p1 = CueKit.OnSurface(p0 + t.vel * motionPreview, 0.08F);
            t.motion.loop = false;
            t.motion.positionCount = 5;
            t.motion.SetPosition(0, p0);
            t.motion.SetPosition(1, p1);
            t.motion.SetPosition(2, p1 - d * 0.6F + side * 0.4F);
            t.motion.SetPosition(3, p1);
            t.motion.SetPosition(4, p1 - d * 0.6F - side * 0.4F);
            CueKit.Paint(t.motion, col);
        }
        else CueKit.Hide(t.motion);
    }

    // Label at the edge of the driver view on the side where the object is.
    void EdgeMarker(Track t, Camera cam, Vector3 v, Color col, string text, int[] edgeCount)
    {
        Rect view = cam.rect;
        Vector2 p, pivot;
        int side;
        if (v.z < 0F)
        {   // behind the cab: bottom edge, on the side where the object is
            side = 0;
            p = new Vector2(Mathf.Clamp(view.xMin + (1F - v.x) * view.width, view.xMin + 0.08F, view.xMax - 0.08F), view.yMin + 0.01F);
            pivot = new Vector2(0.5F, 0F);
            text = "v BEHIND: " + text;
        }
        else if (v.x < 0F) { side = 1; p = new Vector2(view.xMin + 0.005F, Mathf.Clamp(view.yMin + v.y * view.height, view.yMin + 0.1F, view.yMax - 0.2F)); pivot = new Vector2(0F, 0.5F); text = "<< " + text; }
        else if (v.x > 1F) { side = 2; p = new Vector2(view.xMax - 0.005F, Mathf.Clamp(view.yMin + v.y * view.height, view.yMin + 0.1F, view.yMax - 0.2F)); pivot = new Vector2(1F, 0.5F); text += " >>"; }
        else if (v.y < 0F) { side = 3; p = new Vector2(view.xMin + v.x * view.width, view.yMin + 0.01F); pivot = new Vector2(0.5F, 0F); text = "v " + text; }
        else { side = 4; p = new Vector2(view.xMin + v.x * view.width, view.yMax - 0.01F); pivot = new Vector2(0.5F, 1F); text = "^ " + text; }

        // Several markers on the same edge: stack them.
        int n = edgeCount[side]++;
        p.y += (side == 4 ? -1F : 1F) * n * 0.04F;
        t.box.ShowTag(p, pivot, col, text);
    }

    void HideTrack(Track t)
    {
        t.box.Hide();
        CueKit.Hide(t.wire);
        CueKit.Hide(t.ring);
        CueKit.Hide(t.motion);
        CueKit.Hide(t.segment);
        foreach (var m in t.marks) CueKit.Hide(m);
        if (t.widget != null) t.widget.Hide();
        if (t.tetherD != null) { CueKit.Hide(t.tetherD); CueKit.Hide(t.tetherC); }
    }
}
