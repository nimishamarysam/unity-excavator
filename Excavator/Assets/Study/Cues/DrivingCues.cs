using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// DRIVING CUES  (C3, travel mode, part of AttentionCues)
// Cue 4 - driving path and directions.
//
//   - A see-through band on the ground, as wide as the machine, from the machine to the next
//     waypoints of the route (gate approach -> gate -> gate exit, or the stop line when reversing).
//     Arrows move along the band in the direction of travel.
//   - The band bends around static obstacles (barrels, gate posts) that would touch the machine.
//     The obstacle's driver-view label then says which side to pass it on ("PASS RIGHT >>").
//   - Workers are not driven around: if a worker stands on the band, the band turns red from the
//     worker onwards and the banner says STOP; if a worker is about to walk onto it, SLOW DOWN.
//   - A banner at the top of the driver view gives one short instruction at a time.
//
// The route comes from Scenario.RouteAhead(); scenarios without a route show no driving cue.
// The path is planned 10 times per second.
// ---------------------------------------------------------------------------

public class DrivingCues : MonoBehaviour
{
    public float planRateHz = 10F;
    public float maxPathLength = 28F;     // metres of path shown ahead
    public float obstacleMargin = 0.6F;   // extra space kept between machine side and obstacle
    public float workerMargin = 1.2F;
    public float chevronSpacing = 2F;
    public float chevronSpeed = 1.5F;     // metres per second the arrows move along the path
    public float lift = 0.07F;

    AttentionCues root;
    float halfWidth = 1.6F;
    float nextPlan;
    bool shown;

    // Planned path (ground points, from the machine to the end) and its cumulative lengths.
    readonly List<Vector3> path = new List<Vector3>();
    readonly List<float> cum = new List<float>();
    float stopAt = -1F;        // path distance where a worker blocks the path (-1 = free)
    string banner = "", bannerCode = "";
    Color bannerColor = CueKit.Guide;
    bool reverse, slowForWorker;

    // Static obstacles within this distance of the path count as "near the path" (for the obstacle cue filter).
    public float nearPathDistance = 2.5F;
    public readonly HashSet<Hazard> NearPath = new HashSet<Hazard>();
    public bool HasPath => shown && path.Count > 1;

    LineRenderer band, endRing;
    readonly List<LineRenderer> chevrons = new List<LineRenderer>();
    ScreenBox bannerBox;

    public void Init(AttentionCues r)
    {
        root = r;
        band = CueKit.Line(r.WorldRoot, "CuePathBand", 1F, false, true);
        endRing = CueKit.Line(r.WorldRoot, "CuePathEnd", 0.18F, false, true);
        for (int i = 0; i < 16; i++) chevrons.Add(CueKit.Line(r.WorldRoot, "CuePathArrow", 0.2F, false, true));
        bannerBox = new ScreenBox(r.CanvasRoot, r.Font, "CueBanner", 22);

        // Machine width from the two tracks (the machine faces +x at the start of every scenario).
        var m = r.M;
        Bounds tb = default; bool any = false;
        foreach (var c in m.TrackColliders)
        {
            if (c == null) continue;
            if (!any) { tb = c.bounds; any = true; } else tb.Encapsulate(c.bounds);
        }
        if (any)
        {
            Vector3 right = Vector3.Cross(Vector3.up, m.ForwardFlat);
            float w = Mathf.Abs(right.x) * tb.size.x + Mathf.Abs(right.z) * tb.size.z;
            halfWidth = Mathf.Clamp(w / 2F, 1F, 2.2F);
        }
    }

    void LateUpdate()
    {
        if (root == null || root.M == null) return;
        List<Vector3> route = null;
        bool rev = false;
        bool want = root.Visible && root.DrivingPathOn && root.Ctl.IsTravelMode && root.Scenario != null &&
                    (route = root.Scenario.RouteAhead(out rev)) != null && route.Count > 0;
        if (want != shown)
        {
            shown = want;
            AttentionCues.LogCue(want, "driving_path", want ? (rev ? "reverse" : "forward") : "");
            if (!want) HideAll();
        }
        if (!want) return;

        if (Time.unscaledTime >= nextPlan)
        {
            nextPlan = Time.unscaledTime + 1F / planRateHz;
            reverse = rev;
            Plan(route);
            MakeBanner();
        }
        Render();
    }

    // ---------- planning ----------

    void Plan(List<Vector3> route)
    {
        var m = root.M;
        Vector3 c = CueKit.Flat(m.SwingCenter);
        var wps = new List<Vector3>();
        foreach (var w in route) wps.Add(CueKit.Flat(w));

        // Drop waypoints the machine has already passed.
        while (wps.Count > 1)
        {
            Vector3 toW = wps[0] - c, seg = wps[1] - wps[0];
            if (toW.magnitude < 2F || Vector3.Dot(toW, seg) < 0F) wps.RemoveAt(0);
            else break;
        }

        var pts = new List<Vector3> { c };
        float len = 0F;
        foreach (var w in wps)
        {
            len += (w - pts[pts.Count - 1]).magnitude;
            pts.Add(w);
            if (len > maxPathLength) break;
        }

        // Bend around static obstacles.
        root.PassAdvice.Clear();
        var obstacles = new List<Hazard>();
        foreach (var h in Hazard.All)
            if (h.active && h.gameObject.activeInHierarchy && h.kind == HazardKind.Obstacle && h is ColliderHazard) obstacles.Add(h);

        Vector3 machineRight = Vector3.Cross(Vector3.up, m.ForwardFlat);
        for (int iter = 0; iter < 10; iter++)
        {
            bool changed = false;
            for (int i = 0; i < pts.Count - 1 && !changed; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                foreach (var o in obstacles)
                {
                    Bounds ob = o.GetComponent<Collider>().bounds;
                    Vector3 p = CueKit.Flat(ob.center);
                    float R = halfWidth + Mathf.Max(ob.extents.x, ob.extents.z) + obstacleMargin;
                    float t = SegT(a, b, p);
                    if (t <= 0.02F || t >= 0.98F) continue;
                    Vector3 q = Vector3.Lerp(a, b, t);
                    if ((q - p).magnitude >= R - 0.05F) continue;

                    Vector3 off = q - p;
                    if (off.sqrMagnitude < 1e-4F) off = Vector3.Cross(Vector3.up, (b - a).normalized);   // dead centre: go right
                    Vector3 d = p + off.normalized * (R + 0.3F);
                    pts.Insert(i + 1, d);
                    // Machine passes on the right of the obstacle if the detour is to the operator's right.
                    root.PassAdvice[o] = Vector3.Dot(off, machineRight) > 0F ? "PASS RIGHT OF IT >>" : "<< PASS LEFT OF IT";
                    changed = true;
                    break;
                }
            }
            if (!changed) break;
        }

        // Smooth the corners and sample every 0.5 m on the ground.
        pts = Chaikin(pts, 2);
        path.Clear(); cum.Clear();
        float s = 0F;
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                float segLen = (b - a).magnitude;
                int n = Mathf.Max(1, Mathf.CeilToInt(segLen / 0.5F));
                for (int k = 1; k <= n; k++)
                {
                    s += segLen / n;
                    path.Add(CueKit.OnSurface(Vector3.Lerp(a, b, k / (float)n), lift));
                    cum.Add(s);
                }
            }
            else { path.Add(CueKit.OnSurface(pts[0], lift)); cum.Add(0F); }
        }

        // Static obstacles close to the path (used by the obstacle cue filter).
        NearPath.Clear();
        foreach (var o in obstacles)
        {
            Vector3 p = CueKit.Flat(o.GetComponent<Collider>().bounds.center);
            float lim = halfWidth + nearPathDistance;
            for (int i = 0; i < path.Count; i++)
                if ((CueKit.Flat(path[i]) - p).magnitude < lim) { NearPath.Add(o); break; }
        }

        // Workers on or about to walk onto the path.
        stopAt = -1F;
        bool slow = false;
        foreach (var h in Hazard.All)
        {
            if (!h.active || !h.gameObject.activeInHierarchy || h.kind != HazardKind.Worker) continue;
            Vector3 wp = CueKit.Flat(h.transform.position);
            Vector3 v = root.Obstacles.VelocityOf(h);
            for (int i = 0; i < path.Count; i++)
            {
                if (cum[i] > 15F) break;
                Vector3 pp = CueKit.Flat(path[i]);
                if ((pp - wp).magnitude < halfWidth + workerMargin)
                {
                    float at = Mathf.Max(0F, cum[i] - 1.5F);
                    stopAt = stopAt < 0F ? at : Mathf.Min(stopAt, at);
                    break;
                }
                if (cum[i] < 10F && v.magnitude > 0.25F)
                    for (float tt = 0.5F; tt <= 3F; tt += 0.5F)
                        if ((pp - (wp + v * tt)).magnitude < halfWidth + workerMargin * 0.8F) { slow = true; break; }
            }
        }
        slowForWorker = slow && stopAt < 0F;
    }

    static float SegT(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        return Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-5F, ab.sqrMagnitude));
    }

    static List<Vector3> Chaikin(List<Vector3> p, int iterations)
    {
        for (int k = 0; k < iterations && p.Count > 2; k++)
        {
            var q = new List<Vector3> { p[0] };
            for (int i = 0; i < p.Count - 1; i++)
            {
                if (i > 0) q.Add(Vector3.Lerp(p[i], p[i + 1], 0.25F));
                if (i < p.Count - 2) q.Add(Vector3.Lerp(p[i], p[i + 1], 0.75F));
            }
            q.Add(p[p.Count - 1]);
            p = q;
        }
        return p;
    }

    float Length => cum.Count > 0 ? cum[cum.Count - 1] : 0F;

    Vector3 PointAt(float s, out Vector3 tangent)
    {
        tangent = Vector3.forward;
        if (path.Count < 2) return path.Count == 1 ? path[0] : Vector3.zero;
        s = Mathf.Clamp(s, 0F, Length);
        int i = 1;
        while (i < cum.Count - 1 && cum[i] < s) i++;
        float t = Mathf.InverseLerp(cum[i - 1], cum[i], s);
        tangent = CueKit.Flat(path[i] - path[i - 1]).normalized;
        return Vector3.Lerp(path[i - 1], path[i], t);
    }

    // ---------- banner ----------

    void MakeBanner()
    {
        var m = root.M;
        Vector3 c = CueKit.Flat(m.SwingCenter);
        if (stopAt >= 0F)
        {
            Set("STOP - WORKER ON YOUR PATH", "STOP_WORKER", CueKit.Danger);
        }
        else if (slowForWorker)
        {
            Set("SLOW DOWN - WORKER ABOUT TO CROSS", "SLOW_WORKER", CueKit.Warn);
        }
        else if (root.Profile.bannerWithTarget)
        {
            // Name of the destination from the scenario, remaining path length, and a steering hint only
            // when the path ahead is clearly off to one side.
            string name = root.Scenario != null ? root.Scenario.RouteTargetName : "TARGET";
            float thr = root.Profile.steerThreshold;
            Vector3 fwd = m.ForwardFlat, opRight = Vector3.Cross(Vector3.up, fwd);
            Vector3 look = CueKit.Flat(PointAt(Mathf.Min(4F, Length), out _)) - c;
            Vector3 travelDir = reverse ? -fwd : fwd;
            float off = look.sqrMagnitude > 0.01F ? Vector3.Angle(travelDir, look) : 0F;
            bool toRight = Vector3.Dot(look, opRight) > 0F;
            string verb = reverse ? "REVERSE" : "DRIVE";
            string text = $"{verb} {Length:0.0} m TO THE {name}";
            string code = verb + "_" + name.Replace(' ', '_');
            if (off > thr)
            {
                if (reverse) { text += toRight ? "  -  PATH BENDS TO YOUR RIGHT" : "  -  PATH BENDS TO YOUR LEFT"; code += toRight ? "+BEND_RIGHT" : "+BEND_LEFT"; }
                else { text = (toRight ? "STEER RIGHT >>  " : "<< STEER LEFT  ") + text; code += toRight ? "+STEER_RIGHT" : "+STEER_LEFT"; }
            }
            Set(text, code, CueKit.Guide);
        }
        else if (reverse)
        {
            Vector3 end = CueKit.Flat(path[path.Count - 1]);
            Vector3 d = end - c;
            float back = -Vector3.Dot(d, m.ForwardFlat);
            float side = Vector3.Dot(d, Vector3.Cross(Vector3.up, m.ForwardFlat));
            string lat = Mathf.Abs(side) < 0.8F ? "" : side < 0 ? $"  -  line is {-side:0.0} m to your LEFT" : $"  -  line is {side:0.0} m to your RIGHT";
            if (back < 0.8F && Mathf.Abs(side) < 1.5F) Set("STOP - YOU ARE ON THE LINE", "AT_TARGET", CueKit.Good);
            else Set($"REVERSE {Mathf.Max(0F, back):0.0} m TO THE STOP LINE{lat}", "REVERSE" + (lat == "" ? "" : side < 0 ? "_LEFT" : "_RIGHT"), CueKit.Guide);
        }
        else
        {
            // Steering: direction to a point 5 m ahead on the path, relative to where the tracks point.
            Vector3 look = CueKit.Flat(PointAt(Mathf.Min(5F, Length), out _)) - c;
            float angle = look.sqrMagnitude > 0.01F ? Vector3.SignedAngle(m.ForwardFlat, look, Vector3.up) : 0F;
            float thr = root.Profile.steerThreshold;
            string steer = angle > thr ? "STEER RIGHT >>" : angle < -thr ? "<< STEER LEFT" : "^ STRAIGHT AHEAD";
            string code = angle > thr ? "STEER_RIGHT" : angle < -thr ? "STEER_LEFT" : "STRAIGHT";

            // Nearest obstacle the path bends around.
            Hazard nearest = null;
            foreach (var kv in root.PassAdvice)
                if (kv.Key != null && (nearest == null || kv.Key.CurrentDistance < nearest.CurrentDistance)) nearest = kv.Key;
            if (root.Profile.passAdviceInBanner && nearest != null && nearest.CurrentDistance < 12F)
            {
                bool right = root.PassAdvice[nearest].Contains("RIGHT");
                steer += right ? "   |   PASS THE OBSTACLE ON ITS RIGHT" : "   |   PASS THE OBSTACLE ON ITS LEFT";
                code += right ? "+PASS_RIGHT" : "+PASS_LEFT";
            }
            Set(steer, code, CueKit.Guide);
        }
    }

    void Set(string text, string code, Color c)
    {
        // Profiles without the steering banner only show (and log) the worker warnings.
        if (!root.Profile.steeringBanner && code != "STOP_WORKER" && code != "SLOW_WORKER") code = "NONE";
        banner = text;
        bannerColor = c;
        if (code != bannerCode)
        {
            bannerCode = code;
            StudyLogger.Log("cue_instruction", code);
        }
    }

    // ---------- drawing ----------

    void Render()
    {
        if (path.Count < 2) { HideAll(); return; }
        float total = Length;
        float stopFrac = stopAt >= 0F ? Mathf.Clamp01(stopAt / Mathf.Max(0.01F, total)) : 1F;

        band.widthMultiplier = halfWidth * 2F;
        band.loop = false;
        band.positionCount = path.Count;
        band.SetPositions(path.ToArray());
        band.colorGradient = Split(CueKit.Guide, CueKit.Danger, stopFrac, 0.16F, 0.3F);

        // Moving arrows (start 1.5 m ahead so they are not hidden under the machine).
        float phase = Mathf.Repeat(Time.unscaledTime * chevronSpeed, chevronSpacing);
        int used = 0;
        for (float s = 1.5F + phase; s < total - 0.5F && used < chevrons.Count; s += chevronSpacing)
        {
            Vector3 p = PointAt(s, out Vector3 tan);
            Vector3 side = Vector3.Cross(Vector3.up, tan);
            var lr = chevrons[used++];
            CueKit.Chevron(lr, p + Vector3.up * 0.01F, tan, side, 0.6F, halfWidth * 0.45F);
            bool red = stopAt >= 0F && s >= stopAt;
            float fade = Mathf.Clamp01((s - 1.5F) / 1.5F) * Mathf.Clamp01((total - s) / 2F);
            CueKit.Paint(lr, CueKit.Alpha(red ? CueKit.Danger : CueKit.Guide, 0.9F * fade));
        }
        for (int i = used; i < chevrons.Count; i++) CueKit.Hide(chevrons[i]);

        // Pulsing ring at the end of the path (next gate / stop line).
        if (root.Profile.pathEndRing)
        {
            CueKit.Circle(endRing, path[path.Count - 1], reverse ? 1.5F : 1.0F, lift);
            CueKit.Paint(endRing, CueKit.Alpha(reverse ? CueKit.Look : CueKit.Guide, CueKit.Pulse(4F, 0.4F)));
        }
        else CueKit.Hide(endRing);

        // Banner at the top of the driver view, below the task panel.
        Rect view = StudyLayout.DriverView;
        bool urgent = bannerCode.StartsWith("STOP_WORKER") || bannerCode.StartsWith("SLOW_WORKER");
        if (!root.Profile.steeringBanner && !urgent) { bannerBox.Hide(); return; }
        Color bc = bannerColor;
        if (bannerCode.StartsWith("STOP")) bc.a = CueKit.Pulse(8F, 0.55F);
        bannerBox.ShowTag(new Vector2(view.center.x, view.yMax - 0.15F), new Vector2(0.5F, 1F), bc, banner);
    }

    static Gradient Split(Color a, Color b, float split, float alphaA, float alphaB)
    {
        var g = new Gradient();
        if (split >= 0.999F)
        {
            g.SetKeys(new[] { new GradientColorKey(a, 0F), new GradientColorKey(a, 1F) },
                      new[] { new GradientAlphaKey(alphaA, 0F), new GradientAlphaKey(alphaA, 1F) });
            return g;
        }
        float s0 = Mathf.Max(0F, split - 0.01F);
        g.SetKeys(new[] { new GradientColorKey(a, 0F), new GradientColorKey(a, s0), new GradientColorKey(b, split), new GradientColorKey(b, 1F) },
                  new[] { new GradientAlphaKey(alphaA, 0F), new GradientAlphaKey(alphaA, s0), new GradientAlphaKey(alphaB, split), new GradientAlphaKey(alphaB, 1F) });
        return g;
    }

    void HideAll()
    {
        CueKit.Hide(band);
        CueKit.Hide(endRing);
        foreach (var c in chevrons) CueKit.Hide(c);
        bannerBox.Hide();
        root.PassAdvice.Clear();
        bannerCode = "";
    }
}
