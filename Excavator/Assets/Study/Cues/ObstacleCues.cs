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
        seen.Clear();

        foreach (var h in Hazard.All)
        {
            if (h == null || h is RectEdgeHazard) continue;   // trench edge: shown by the dig-area outline
            var t = Get(h);
            seen.Add(h);
            UpdateVelocity(t);

            bool typeOn = IsDynamic(h) ? AttentionCues.DynamicObstacleCues : AttentionCues.StaticObstacleCues;
            bool want = visible && typeOn && h.active && h.gameObject.activeInHierarchy && InRange(h, t.shown);
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

    // ---------- when is a cue shown ----------

    float Range(Hazard h)
    {
        switch (h.kind)
        {
            case HazardKind.Worker: return workerRange;
            case HazardKind.Obstacle: return h.warnDistance + obstacleExtra;
            case HazardKind.PowerLine: return h.warnDistance + powerLineExtra;
            case HazardKind.BuriedCable: return h.warnDistance + cableExtra;
            default: return -1F;   // truck: only at warning level (dumping right over it is normal)
        }
    }

    bool InRange(Hazard h, bool shownNow)
    {
        float range = Range(h);
        if (range <= 0F) return h.CurrentLevel != Hazard.Level.Clear;
        return h.CurrentDistance <= (shownNow ? range + hysteresis : range);
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
        t.wire = CueKit.Line(root.WorldRoot, "CueWire_" + id, 0.07F, true, false, L);
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

    static string Name(Hazard h)
    {
        switch (h.kind)
        {
            case HazardKind.Worker: return "WORKER";
            case HazardKind.Truck: return "TRUCK";
            case HazardKind.PowerLine: return "POWER LINE";
            case HazardKind.BuriedCable: return "BURIED CABLE";
            default: return h.hazardId.Contains("post") ? "GATE POST" : "OBSTACLE";
        }
    }

    Bounds WorldBounds(Track t)
    {
        var h = t.h;
        if (h is SegmentHazard)
        {
            if (h.kind == HazardKind.BuriedCable)
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
        Bounds b = WorldBounds(t);
        Color col = Severity(h);

        // --- driver view: rectangle or edge marker ---
        string label = $"{Name(h)}  {h.CurrentDistance:0.0} m";
        if (root.PassAdvice.TryGetValue(h, out var advice)) label += "\n" + advice;
        if (IsDynamic(h)) label += "\n" + MotionText(t, cam);

        Vector3 centerVp = Vector3.zero;
        if (cam != null && CueKit.ProjectBox(cam, b, out Rect r, out centerVp))
            t.box.ShowBox(r, col, label);
        else if (cam != null && (IsDynamic(h) || h.CurrentLevel != Hazard.Level.Clear))
            EdgeMarker(t, cam, centerVp, col, $"{Name(h)}  {h.CurrentDistance:0.0} m", edgeCount);
        else
            t.box.Hide();

        // --- context views: 3D highlight ---
        if (h is SegmentHazard seg)
        {
            Vector3 a = seg.a, bb = seg.b;
            if (h.kind == HazardKind.BuriedCable) { a = CueKit.OnSurface(a, 0.08F); bb = CueKit.OnSurface(bb, 0.08F); }
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
            CueKit.Circle(t.ring, b.center, Mathf.Max(b.extents.x, b.extents.z) + 0.7F);
            CueKit.Paint(t.ring, CueKit.Alpha(col, 0.8F));
            CueKit.Hide(t.segment);
        }

        // --- worker: where will they be in the next seconds ---
        if (IsDynamic(h) && t.vel.magnitude > 0.25F)
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
    }
}
