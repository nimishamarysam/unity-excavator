using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// DIG VOLUME MODEL  (C3 only, digging tasks, 2026-11-02)
// A SEPARATE small 3D window ("DIG VOLUME") - not an overlay on the scene. It shows a miniature model of the volume
// to be dug (dig area x target depth), seen at an angle from behind the cab, on a plain dark background:
//   thin light wireframe box            = the whole volume to dig (with a faint grid on its floor)
//   solid orange blocks (0.5 m columns)  = soil STILL TO DIG there (from the target depth up to the current ground)
//   green tile on the floor              = dug down to the target (done)
//   red tile                             = dug too deep
//   small yellow block                   = where the bucket teeth are
// Drawn by its own camera into a small inset in the lower-right corner of the DRIVER VIEW (InDriverView; false = the
// upper-right corner of the BIRD'S-EYE window, InsetWidth/Height of that window). The model's meshes are rendered ONLY by that camera (switched on just for it), so no other view
// shows them. Rebuilt RefreshHz times per second. Logged as cue_on / cue_off "dig_volume_model".
// ---------------------------------------------------------------------------

public class DigVolumeGrid : MonoBehaviour
{
    public static bool Enabled = true;
    public float CellSize = 0.5F;
    public float AtGradeBand = 0.1F;
    public float RefreshHz = 5F;
    public float InsetWidth = 0.5F, InsetHeight = 0.6F;   // share of the bird's-eye window (InDriverView = false)
    // 2026-11-02: the model is drawn in the DRIVER VIEW with NO background (straight over the scene), placed right
    // NEXT TO the pit as the driver sees it (left of it, vertically centred on it), never over it, kept below the
    // mirror / task panel (MaxTop) and above the status bar. false = back in the bird's-eye window (dark panel).
    public static bool InDriverView = true;
    public float DriverInsetWidth = 0.2F, DriverInsetHeight = 0.26F;   // share of the driver view
    // 2026-11-02 (final): FIXED place in the upper-left of the front window (sky area, left of the boom), as a
    // share of the DRIVER VIEW (x, y from its lower-left, width, height); the depth gauge sits right next to it.
    // Shown only while the bucket teeth are over the dig area (OverOn / OverOff margins, hysteresis).
    public static Rect FixedInDriverView = new Rect(0.15F, 0.63F, 0.166F, 0.21F);
    public static bool FollowPit = false;
    // Horizontal position: the panel's RIGHT edge just LEFT of the machine ARM as the driver sees it (the arm's
    // centre line, arm pivot -> bucket pin, at the panel's mid height), ArmGap away; vertical as FixedInDriverView.
    public static bool BesideArm = true;
    public float ArmGap = 0.035F;          // share of the driver view width (the arm is ~0.03 wide on screen)  // true = the earlier "beside the pit" placement (BesidePit)
    public float OverOn = 0.3F, OverOff = 0.8F;
    bool over;
    public static Rect FixedRect()
    {
        Rect w = StudyLayout.DriverView, f = FixedInDriverView;
        return new Rect(w.xMin + f.x * w.width, w.yMin + f.y * w.height, f.width * w.width, f.height * w.height);
    }
    public float PitGap = 0.004F;          // (was 0.015; closer to the pit 2026-11-02)          // gap between the pit (on screen) and the model, share of the display width
    public float MaxTop = 1F;              // the model's top edge stays below this (display height). Was 0.5: pushed the
                                           // model far below the pit when the pit is high in the view (2026-11-02)
    // Rear-view mirror on screen (share of the display, measured on the lab screenshot): the model never covers it.
    public static Rect MirrorRect = new Rect(0.04F, 0.70F, 0.12F, 0.23F);
    public float FollowSpeed = 6F;         // smoothing of the position when the cab swings
    Vector2 placedCentre; bool placed;
    public const int ModelLayer = 28;                     // unused layer: only the model camera draws it

    AttentionCues root;
    Camera cam;
    Mesh lines, blocks;
    MeshRenderer lineMr, blockMr;
    Transform marker;
    ScreenBox label;
    bool active;
    float nextBuild;

    // Light lines on the faint dark background.
    static readonly Color Edge = new Color(0.9F, 0.92F, 0.95F, 0.85F);
    static readonly Color FloorGrid = new Color(0.9F, 0.92F, 0.95F, 0.25F);
    // Faint background in the driver view: the status bar colour (0.08, 0.09, 0.10), more see-through.
    public static Color DriverBackground = new Color(0.08F, 0.09F, 0.10F, 0.55F);
    public float CameraDistance = 0.9F;    // camera distance factor in the driver view (1 = old framing; smaller = model fills the panel)
    Transform backQuad;

    public void Init(AttentionCues r)
    {
        root = r;
        var lg = new GameObject("DigVolumeModelLines") { layer = ModelLayer };
        lg.transform.SetParent(transform, false);
        lines = new Mesh { name = "DigVolumeLines" }; lines.MarkDynamic();
        lg.AddComponent<MeshFilter>().sharedMesh = lines;
        lineMr = lg.AddComponent<MeshRenderer>();
        lineMr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        lineMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var bg = new GameObject("DigVolumeModelBlocks") { layer = ModelLayer };
        bg.transform.SetParent(transform, false);
        blocks = new Mesh { name = "DigVolumeBlocks" }; blocks.MarkDynamic();
        bg.AddComponent<MeshFilter>().sharedMesh = blocks;
        blockMr = bg.AddComponent<MeshRenderer>();
        // Unlit, opaque, vertex colours shaded by hand (no dependence on scene lighting; was Standard -> black faces).
        var sh = Shader.Find("Study/UnlitVertexColor");
        blockMr.sharedMaterial = new Material(sh != null ? sh : Shader.Find("Sprites/Default"));
        blockMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        blockMr.receiveShadows = false;

        var mk = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(mk.GetComponent<Collider>());
        mk.name = "DigVolumeBucketMarker";
        mk.layer = ModelLayer;
        mk.transform.SetParent(transform, false);
        mk.transform.localScale = new Vector3(0.45F, 0.2F, 0.45F);
        // 2026-11-04: was Shader.Find("Unlit/Color"). That shader is NOT in a player build (no material uses it and it is
        // not in Always Included Shaders), so Find returned null, new Material(null) threw, Init stopped before the model
        // camera was made and the DIG VOLUME window never appeared in the built app (it worked in the Editor).
        // Now uses shaders that are in the build (the Resources one first).
        Shader mkSh = Shader.Find("Study/UnlitVertexColor");
        if (mkSh == null) mkSh = Shader.Find("Sprites/Default");
        var mkMesh = mk.GetComponent<MeshFilter>().mesh;   // the vertex-colour shader takes its colour from the mesh
        var mkCols = new Color[mkMesh.vertexCount];
        for (int i = 0; i < mkCols.Length; i++) mkCols[i] = new Color(1F, 0.85F, 0.1F);
        mkMesh.colors = mkCols;
        mk.GetComponent<Renderer>().sharedMaterial = new Material(mkSh) { color = new Color(1F, 0.85F, 0.1F) };
        marker = mk.transform;

        // Faint background: a see-through quad behind the model, filling this camera's view (driver view only).
        var bq = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(bq.GetComponent<Collider>());
        bq.name = "DigVolumeBackground";
        bq.layer = ModelLayer;
        bq.transform.SetParent(transform, false);
        bq.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = DriverBackground };
        bq.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backQuad = bq.transform;

        var cg = new GameObject("DigVolumeCamera");
        cg.transform.SetParent(transform, false);
        cam = cg.AddComponent<Camera>();
        cam.clearFlags = InDriverView ? CameraClearFlags.Depth : CameraClearFlags.SolidColor;   // no background in the driver view
        cam.backgroundColor = new Color(0.1F, 0.11F, 0.13F);
        cam.fieldOfView = 40F;
        cam.cullingMask = 1 << ModelLayer;    // ONLY the model - not the terrain, machine or other cues
        cam.depth = 60;                       // above the context windows (50-54)
        if (InDriverView) cam.targetDisplay = StudyLayout.DriverDisplayIndex; else StudyLayout.ToContextDisplay(cam);
        cam.enabled = false;

        label = new ScreenBox(InDriverView ? root.CanvasRoot : root.ContextCanvasRoot, root.Font, "DigVolumeLabel", 13);
        SetVisible(false);
        Camera.onPreCull += PreCull;
        Camera.onPostRender += PostRender;
    }

    void OnDestroy() { Camera.onPreCull -= PreCull; Camera.onPostRender -= PostRender; }

    // The model is visible ONLY to its own camera.
    void SetVisible(bool v)
    {
        lineMr.enabled = v; blockMr.enabled = v;
        var mr = marker.GetComponent<Renderer>(); if (mr != null) mr.enabled = v;
        if (backQuad != null) backQuad.GetComponent<Renderer>().enabled = v && InDriverView;
    }
    void PreCull(Camera c) { if (lineMr != null) SetVisible(active && c == cam); }
    void PostRender(Camera c) { if (lineMr != null && c == cam) SetVisible(false); }

    TerrainDigger Digger => TerrainDigger.Instance;
    float TargetDepth
    {
        get
        {
            float? t = root.Scenario != null ? root.Scenario.DigTargetDepth : null;
            return t ?? (Digger != null ? Digger.ZoneMaxDepth : 1F);
        }
    }

    // Dig & dump: not needed while the bucket is away from the pit (swinging to / dumping into the truck).
    // Hidden when the teeth are more than AwayMargin outside the dig area, shown again within AwayMargin - 1 m.
    public float AwayMargin = 2F;
    bool away;
    bool AwayAtTruck()
    {
        if (!(root.Scenario is DigDumpScenario) || Digger == null) return false;
        Vector3 tip = root.M.BucketTipPosition;
        if (away) { if (Digger.Contains(tip, AwayMargin - 1F)) away = false; }
        else if (!Digger.Contains(tip, AwayMargin)) away = true;
        return away;
    }

    // Screen rect next to the pit as the driver sees it: left of the pit's on-screen box (right if no room),
    // vertically centred on it, inside the driver view, below MaxTop, smoothed.
    Rect BesidePit()
    {
        Rect w = StudyLayout.DriverView;
        float iw = w.width * DriverInsetWidth, ih = w.height * DriverInsetHeight, gap = 0.006F;
        Vector2 want = new Vector2(w.xMin + iw / 2F + gap, w.yMin + ih / 2F + gap);   // fallback: lower left
        var oc = root.M.OperatorCamera; var d = Digger;
        if (oc != null)
        {
            Vector3 c = d.ZoneCenter, u = d.ZoneAxisU, v = Vector3.Cross(Vector3.up, u);
            float xMin = 2F, xMax = -1F, yMin = 2F, yMax = -1F; bool any = false;
            foreach (var (a, b) in new[] { (-1F, -1F), (1F, -1F), (1F, 1F), (-1F, 1F) })
            {
                Vector3 p = c + u * (a * d.ZoneHalfLength) + v * (b * d.ZoneHalfWidth);
                p.y = TerrainDigger.ReferenceGroundHeight(p);
                Vector3 vp = oc.WorldToViewportPoint(p);
                if (vp.z <= 0F) continue;
                any = true;
                float sx = w.xMin + vp.x * w.width, sy = w.yMin + vp.y * w.height;
                xMin = Mathf.Min(xMin, sx); xMax = Mathf.Max(xMax, sx); yMin = Mathf.Min(yMin, sy); yMax = Mathf.Max(yMax, sy);
            }
            if (any)
            {
                float cy = (yMin + yMax) / 2F;
                float left = xMin - PitGap - iw / 2F, right = xMax + PitGap + iw / 2F;
                float cx = left - iw / 2F >= w.xMin + gap ? left : right;
                want = new Vector2(cx, cy);
                // Not over the mirror: slide right if it still ends left of the pit, otherwise move below the mirror.
                Rect r0 = new Rect(want.x - iw / 2F, want.y - ih / 2F, iw, ih);
                if (r0.Overlaps(MirrorRect))
                {
                    float shiftedX = MirrorRect.xMax + gap + iw / 2F;
                    if (cx == left && shiftedX + iw / 2F <= xMin + 0.01F) want.x = shiftedX;
                    else want.y = Mathf.Min(want.y, MirrorRect.yMin - gap - ih / 2F);
                }
            }
        }
        want.x = Mathf.Clamp(want.x, w.xMin + iw / 2F + gap, w.xMax - iw / 2F - gap);
        want.y = Mathf.Clamp(want.y, w.yMin + ih / 2F + gap, Mathf.Min(MaxTop, w.yMax) - ih / 2F);
        if (!placed) { placedCentre = want; placed = true; }
        else placedCentre = Vector2.Lerp(placedCentre, want, 1F - Mathf.Exp(-FollowSpeed * Time.unscaledDeltaTime));
        return new Rect(placedCentre.x - iw / 2F, placedCentre.y - ih / 2F, iw, ih);
    }

    Rect BesideArmRect()
    {
        Rect f = FixedRect(), w = StudyLayout.DriverView;
        var oc = root.M.OperatorCamera;
        Transform a = root.M.ArmTransform, b = root.M.BucketTransform;
        float cx = f.center.x;
        if (oc != null && a != null && b != null)
        {
            Vector3 pa = oc.WorldToViewportPoint(a.position), pb = oc.WorldToViewportPoint(b.position);
            if (pa.z > 0F && pb.z > 0F)
            {
                Vector2 sa = new Vector2(w.xMin + pa.x * w.width, w.yMin + pa.y * w.height);
                Vector2 sb = new Vector2(w.xMin + pb.x * w.width, w.yMin + pb.y * w.height);
                float y = f.center.y;
                float t = Mathf.Abs(sb.y - sa.y) > 1e-4F ? (y - sa.y) / (sb.y - sa.y) : 0F;
                float armX = sa.x + (sb.x - sa.x) * t;           // arm centre line at the panel's mid height
                // Room between the panel and the arm for the depth gauge (target value 40 px + gauge 22 px + 10 px).
                float gaugeRoom = 72F / Mathf.Max(1F, Screen.width);
                cx = armX - ArmGap * w.width - gaugeRoom - f.width / 2F;
            }
        }
        cx = Mathf.Clamp(cx, w.xMin + f.width / 2F + 0.005F, w.xMax - f.width / 2F - 0.005F);
        Vector2 want = new Vector2(cx, f.center.y);
        if (!placed) { placedCentre = want; placed = true; }
        else placedCentre = Vector2.Lerp(placedCentre, want, 1F - Mathf.Exp(-FollowSpeed * Time.unscaledDeltaTime));
        return new Rect(placedCentre.x - f.width / 2F, placedCentre.y - f.height / 2F, f.width, f.height);
    }

    // Last panel rect (for the depth gauge next to it).
    public static Rect CurrentRect;

    bool OverArea()
    {
        if (Digger == null || !Digger.HasZone) return false;
        Vector3 tip = root.M.BucketTipPosition;
        over = Digger.Contains(tip, over ? OverOff : OverOn);
        return over;
    }

    void LateUpdate()
    {
        if (root == null || root.M == null) return;
        var sm = StudyManager.Instance;
        bool want = Enabled && root.Visible && sm != null && sm.ShowContextViews && Digger != null && Digger.HasZone
                    && !root.Ctl.IsTravelMode && (InDriverView ? OverArea() : !AwayAtTruck());
        if (want != active)
        {
            active = want;
            cam.enabled = want;
            if (!want) label.Hide();
            AttentionCues.LogCue(want, "dig_volume_model", "");
            if (!BesideArm) placed = false;
        }
        // Keep tracking the arm while the panel is hidden, so the depth gauge next to it follows the arm too.
        if (!active && InDriverView && BesideArm && !FollowPit && root.M.OperatorCamera != null) CurrentRect = BesideArmRect();
        if (!active) return;

        // Inset in the UPPER-right corner of the bird's-eye window (the lower right covered the machine).
        Rect inset;
        if (InDriverView) inset = FollowPit ? BesidePit() : BesideArm ? BesideArmRect() : FixedRect();
        else
        {
            Rect w = StudyLayout.PanelSlot(0);
            inset = new Rect(w.xMax - w.width * InsetWidth, w.yMax - w.height * InsetHeight, w.width * InsetWidth, w.height * InsetHeight);
        }
        cam.rect = inset;
        CurrentRect = inset;
        label.ShowTag(new Vector2(inset.xMin + 0.002F, inset.yMax - 0.002F), new Vector2(0F, 1F), new Color(0.9F, 0.92F, 0.95F, 1F), "DIG VOLUME");

        // Camera: from behind the cab side of the area, above, looking down at it at an angle.
        var d = Digger;
        Vector3 c = d.ZoneCenter, u = d.ZoneAxisU, v = Vector3.Cross(Vector3.up, u);
        float size = Mathf.Max(d.ZoneHalfLength, d.ZoneHalfWidth) * 2F;
        Vector3 look = new Vector3(c.x, TerrainDigger.ReferenceGroundHeight(c) - TargetDepth * 0.5F, c.z);
        float k = InDriverView ? CameraDistance : 1F;
        cam.transform.position = look + (-u * (size * 1.7F) - v * (size * 0.8F) + Vector3.up * (size * 1.5F)) * k;   // far enough to see it all
        cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position, Vector3.up);
        if (InDriverView && backQuad != null)
        {
            // Behind the whole model (twice the camera-to-centre distance), exactly filling the view.
            float dist = Vector3.Distance(cam.transform.position, look) * 2F;
            float h = 2F * dist * Mathf.Tan(cam.fieldOfView * 0.5F * Mathf.Deg2Rad);
            backQuad.position = cam.transform.position + cam.transform.forward * dist;
            backQuad.rotation = cam.transform.rotation;
            backQuad.localScale = new Vector3(h * cam.aspect * 1.02F, h * 1.02F, 1F);
            if (cam.farClipPlane < dist * 1.5F) cam.farClipPlane = dist * 1.5F;
        }

        // Bucket marker at the teeth (only while over the area).
        Vector3 tip = root.M.BucketTipPosition;
        bool over = d.Contains(tip, 0.5F);
        marker.gameObject.SetActive(over);
        if (over) marker.position = tip + Vector3.up * 0.1F;

        if (Time.unscaledTime >= nextBuild)
        {
            nextBuild = Time.unscaledTime + 1F / RefreshHz;
            Build(c, u, v, d.ZoneHalfLength, d.ZoneHalfWidth);
        }
    }

    void Build(Vector3 c, Vector3 u, Vector3 v, float hu, float hv)
    {
        float target = TargetDepth;
        float overDig = root.Scenario != null ? root.Scenario.OverDigTolerance : 0.15F;
        int nu = Mathf.Max(1, Mathf.RoundToInt(2F * hu / CellSize)), nv = Mathf.Max(1, Mathf.RoundToInt(2F * hv / CellSize));
        float su = 2F * hu / nu, sv = 2F * hv / nv;
        float top = TerrainDigger.ReferenceGroundHeight(c), bottom = top - target;
        Vector3 P(float a, float b, float y) { Vector3 p = c + u * a + v * b; return new Vector3(p.x, y, p.z); }

        // --- wireframe box + faint floor grid ---
        var lv = new List<Vector3>(); var lc = new List<Color>();
        void L(Vector3 a, Vector3 b, Color col) { lv.Add(a); lv.Add(b); lc.Add(col); lc.Add(col); }
        foreach (float y in new[] { top, bottom })
        {
            L(P(-hu, -hv, y), P(hu, -hv, y), Edge); L(P(hu, -hv, y), P(hu, hv, y), Edge);
            L(P(hu, hv, y), P(-hu, hv, y), Edge); L(P(-hu, hv, y), P(-hu, -hv, y), Edge);
        }
        foreach (var (a, b) in new[] { (-hu, -hv), (hu, -hv), (hu, hv), (-hu, hv) }) L(P(a, b, top), P(a, b, bottom), Edge);
        for (int i = 1; i < nu; i++) L(P(-hu + i * su, -hv, bottom), P(-hu + i * su, hv, bottom), FloorGrid);
        for (int j = 1; j < nv; j++) L(P(-hu, -hv + j * sv, bottom), P(hu, -hv + j * sv, bottom), FloorGrid);
        var li = new int[lv.Count]; for (int n = 0; n < li.Length; n++) li[n] = n;
        lines.Clear(); lines.SetVertices(lv); lines.SetColors(lc); lines.SetIndices(li, MeshTopology.Lines, 0); lines.RecalculateBounds();

        // --- blocks: soil to dig (sub-mesh 0), done tiles (1), too-deep tiles (2) ---
        var bvs = new List<Vector3>();
        var bcs = new List<Color>();
        var tris = new List<int>();
        Color[] baseCol = { new Color(0.95F, 0.5F, 0.12F), new Color(0.25F, 0.75F, 0.3F), new Color(0.9F, 0.15F, 0.12F) };   // to dig, done, too deep
        void Face(int s, float shade, Vector3 a, Vector3 b, Vector3 e, Vector3 f)
        {
            Color col = baseCol[s] * shade; col.a = 1F;
            int n = bvs.Count; bvs.Add(a); bvs.Add(b); bvs.Add(e); bvs.Add(f);
            bcs.Add(col); bcs.Add(col); bcs.Add(col); bcs.Add(col);
            tris.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }
        void Box(int s, float a0, float a1, float b0, float b1, float y0, float y1)
        {
            Face(s, 1.0F, P(a0, b0, y1), P(a1, b0, y1), P(a1, b1, y1), P(a0, b1, y1));    // top: brightest
            Face(s, 0.62F, P(a0, b0, y0), P(a1, b0, y0), P(a1, b0, y1), P(a0, b0, y1));   // sides: darker
            Face(s, 0.78F, P(a1, b0, y0), P(a1, b1, y0), P(a1, b1, y1), P(a1, b0, y1));
            Face(s, 0.62F, P(a1, b1, y0), P(a0, b1, y0), P(a0, b1, y1), P(a1, b1, y1));
            Face(s, 0.78F, P(a0, b1, y0), P(a0, b0, y0), P(a0, b0, y1), P(a0, b1, y1));
        }
        float g = 0.03F;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                float a0 = -hu + i * su + g, a1 = -hu + (i + 1) * su - g, b0 = -hv + j * sv + g, b1 = -hv + (j + 1) * sv - g;
                Vector3 pc = P((a0 + a1) / 2F, (b0 + b1) / 2F, 0F);
                float r = TerrainDigger.ReferenceGroundHeight(pc);
                float cur = Mathf.Min(TerrainDigger.CurrentGroundHeight(pc), r);
                float left = cur - (r - target);
                if (left > AtGradeBand) Box(0, a0, a1, b0, b1, bottom, bottom + Mathf.Min(left, target));
                else if (left >= -overDig) Box(1, a0, a1, b0, b1, bottom - 0.02F, bottom + 0.03F);
                else Box(2, a0, a1, b0, b1, bottom - 0.02F, bottom + 0.03F);
            }
        blocks.Clear();
        blocks.SetVertices(bvs);
        blocks.SetColors(bcs);
        blocks.SetTriangles(tris, 0);
        blocks.RecalculateBounds();
    }
}
