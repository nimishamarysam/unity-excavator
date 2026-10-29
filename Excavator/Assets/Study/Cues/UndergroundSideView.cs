using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// UNDERGROUND BUCKET SIDE VIEW  (C2 and C3, profile option bucketSideUnderground - Dig and dump)
//
// C2 gets the plain cut-away (soil section, trench, original ground line, buried services as grey circles).
// C3 adds the guidance: target-depth line and warning colours on the services (the distance is shown by the
// proximity widget of BucketSideServices).
//
// When the bucket goes into the pit, the BUCKET SIDE VIEW turns into a cut-away (section) view:
//   - the terrain is hidden for that camera only, and the camera drops to the bucket's level;
//   - behind the bucket a vertical soil section is drawn in the arm's plane: brown soil below the
//     current ground, the dug-out part of the trench as a dark gap, the ORIGINAL ground line (white)
//     and the target / maximum depth (green);
//   - buried services (pipe, cable) that cross this plane are drawn as a circle with a label
//     "PIPE 0.42 m from teeth" (yellow, orange at warning distance, red at contact).
// The height/depth arrow of DiggingCues stays on top of this, so depth is read against the section.
// Everything is on BucketSideOnlyLayer, so the driver view and other windows are not affected.
// It switches on when the teeth are inside the dig area and below the original ground (+0.3 m),
// and off again when they are 0.8 m above the original ground or leave the area.
// ---------------------------------------------------------------------------

public class UndergroundSideView : MonoBehaviour
{
    public static bool Active { get; private set; }

    public float halfSpan = 5F;          // section length on each side of the bucket (m)
    public float sectionDepth = 3.5F;    // how far below the original ground the section goes (m)
    public float behindBucket = 0.9F;    // section plane this far behind the bucket (seen from the camera)
    public float overDigShow = 0.15F;    // C3: dug more than this below the target -> red in the section (= Scenario.OverDigTolerance)
    public float labelDistance = 0.6F;   // C3: "PIPE x m" tag only when the teeth are closer than this (or at warning)
    const int N = 60;

    AttentionCues root;
    GameObject sectionGo;
    Mesh mesh;
    LineRenderer refLine, curLine, targetLine;
    readonly List<LineRenderer> serviceRings = new List<LineRenderer>();
    readonly List<ScreenBox> serviceLabels = new List<ScreenBox>();
    readonly List<Terrain> hidden = new List<Terrain>();
    Transform own;   // always-active parent (the cue roots are only active in C3)

    static bool InContextCondition
    {
        get
        {
            var sm = StudyManager.Instance;
            return sm != null && sm.ShowContextViews &&
                   (sm.CurrentState == StudyManager.State.Running || sm.CurrentState == StudyManager.State.Paused);
        }
    }
    bool Guidance => root.Visible;   // C3

    public void Init(AttentionCues r)
    {
        root = r;
        if (r.Scenario != null) overDigShow = r.Scenario.OverDigTolerance;
        int layer = CueKit.BucketSideOnlyLayer;
        own = new GameObject("UndergroundSideView").transform;
        own.SetParent(transform, false);
        sectionGo = new GameObject("CueUndergroundSection") { layer = layer };
        sectionGo.transform.SetParent(own, false);
        mesh = new Mesh { name = "UndergroundSection" };
        mesh.MarkDynamic();
        sectionGo.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = sectionGo.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3050 };
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        sectionGo.SetActive(false);

        refLine    = CueKit.Line(own, "CueSectionOriginalGround", 0.04F, true, false, layer);
        curLine    = CueKit.Line(own, "CueSectionGround", 0.05F, true, false, layer);
        targetLine = CueKit.Line(own, "CueSectionTarget", 0.04F, true, false, layer);
        Camera.onPreCull += PreCull;
        Camera.onPostRender += PostRender;
    }

    void OnDestroy()
    {
        Camera.onPreCull -= PreCull;
        Camera.onPostRender -= PostRender;
        RestoreTerrain();
        Active = false;
    }

    void OnDisable() { RestoreTerrain(); Active = false; }

    // ---- hide the terrain only while the bucket side camera renders ----
    void PreCull(Camera cam)
    {
        if (!Active || cam == null || cam != ContextViews.BucketSideCamera) return;
        foreach (var t in Terrain.activeTerrains)
            if (t != null && t.drawHeightmap) { t.drawHeightmap = false; t.drawTreesAndFoliage = false; hidden.Add(t); }
    }

    void PostRender(Camera cam) { if (cam == ContextViews.BucketSideCamera) RestoreTerrain(); }

    void RestoreTerrain()
    {
        foreach (var t in hidden) if (t != null) { t.drawHeightmap = true; t.drawTreesAndFoliage = true; }
        hidden.Clear();
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
        var m = root.M;
        Vector3 tip = m.BucketTipPosition;
        bool can = InContextCondition && root.Profile.bucketSideUnderground && !root.Ctl.IsTravelMode
                   && Digger != null && Digger.HasZone && ContextViews.BucketSideCamera != null;
        float refY = TerrainDigger.ReferenceGroundHeight(tip);
        bool want = can && (Active ? Digger.Contains(tip, 1F) && tip.y < refY + 0.8F
                                   : Digger.Contains(tip, 0.3F) && tip.y < refY + 0.3F);
        if (want != Active)
        {
            Active = want;
            AttentionCues.LogCue(want, "underground_side_view", "");
            if (!want) Hide();
        }
        if (Active) Draw();
    }

    void Hide()
    {
        sectionGo.SetActive(false);
        CueKit.Hide(refLine); CueKit.Hide(curLine); CueKit.Hide(targetLine);
        foreach (var r in serviceRings) CueKit.Hide(r);
        foreach (var l in serviceLabels) l.Hide();
    }

    void Draw()
    {
        var m = root.M;
        Vector3 tip = m.BucketTipPosition, bmid = (m.BucketPivotPosition + tip) / 2F;
        Vector3 reachDir = CueKit.Flat(tip - m.SwingCenter);
        reachDir = reachDir.sqrMagnitude > 0.01F ? reachDir.normalized : m.ForwardFlat;
        Vector3 side = Vector3.Cross(Vector3.up, reachDir);            // the camera sits on the -side
        Vector3 o = CueKit.Flat(bmid) + side * behindBucket;           // a point of the section plane (y = 0)

        // --- soil section mesh ---
        var verts = new List<Vector3>(N * 4 + 8);
        var cols = new List<Color>(N * 4 + 8);
        var tris = new List<int>(N * 12);
        Color soilTop = new Color(0.55F, 0.4F, 0.26F, 0.95F), soilDeep = new Color(0.3F, 0.21F, 0.13F, 0.95F);
        Color gap = new Color(0.08F, 0.07F, 0.06F, 0.55F);
        var refPts = new Vector3[N + 1];
        var curPts = new Vector3[N + 1];
        float baseY = float.MaxValue;
        for (int i = 0; i <= N; i++)
        {
            Vector3 p = o + reachDir * Mathf.Lerp(-halfSpan, halfSpan, i / (float)N);
            // The ground profile is taken in the ARM'S plane (straight under the bucket, where it digs) and drawn
            // on the section plane behind the bucket. Sampling the section plane itself (0.9 m to the side) missed
            // the dug strip, so the trench did not appear in the cut-away although it was dug.
            Vector3 ps = p - side * behindBucket;
            float ry = TerrainDigger.ReferenceGroundHeight(ps), cy = Mathf.Min(TerrainDigger.CurrentGroundHeight(ps), ry);
            refPts[i] = new Vector3(p.x, ry, p.z);
            curPts[i] = new Vector3(p.x, cy, p.z);
            baseY = Mathf.Min(baseY, ry - sectionDepth);
        }
        for (int i = 0; i < N; i++)
        {
            // soil: from the current ground down to the section base (two-colour gradient)
            int v = verts.Count;
            verts.Add(curPts[i]); verts.Add(curPts[i + 1]);
            verts.Add(new Vector3(curPts[i].x, baseY, curPts[i].z)); verts.Add(new Vector3(curPts[i + 1].x, baseY, curPts[i + 1].z));
            cols.Add(soilTop); cols.Add(soilTop); cols.Add(soilDeep); cols.Add(soilDeep);
            tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            // dug-out part of the trench: dark gap between the current and the original ground
            if (refPts[i].y - curPts[i].y > 0.02F || refPts[i + 1].y - curPts[i + 1].y > 0.02F)
            {
                v = verts.Count;
                verts.Add(refPts[i]); verts.Add(refPts[i + 1]); verts.Add(curPts[i]); verts.Add(curPts[i + 1]);
                cols.Add(gap); cols.Add(gap); cols.Add(gap); cols.Add(gap);
                tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            }
        }
        // C3: SOIL STILL TO DIG (like the "cut" of a grade-control system). Inside the dig area, the band between
        // the current ground and the target depth is filled cyan (more opaque = deeper still to go), and soil
        // removed below the target (over-dig) is filled red. Drawn last, so it lies over the soil and the gap.
        if (Guidance)
        {
            float tgtDepth = TargetDepth;
            Color toDigTop = CueKit.Alpha(CueKit.Guide, 0.35F), toDigBottom = CueKit.Alpha(CueKit.Guide, 0.7F);
            Color overDig = CueKit.Alpha(CueKit.Danger, 0.6F);
            Vector3 front = -side * 0.01F;   // a hair towards the camera, so it is not hidden in the soil quad
            for (int i = 0; i < N; i++)
            {
                if (!Digger.Contains(refPts[i] - side * behindBucket, 0F) || !Digger.Contains(refPts[i + 1] - side * behindBucket, 0F)) continue;
                float t0 = refPts[i].y - tgtDepth, t1 = refPts[i + 1].y - tgtDepth;
                float c0 = curPts[i].y, c1 = curPts[i + 1].y;
                if (c0 > t0 + 0.01F || c1 > t1 + 0.01F)   // soil left above the target
                {
                    int v = verts.Count;
                    verts.Add(new Vector3(curPts[i].x, Mathf.Max(c0, t0), curPts[i].z) + front);
                    verts.Add(new Vector3(curPts[i + 1].x, Mathf.Max(c1, t1), curPts[i + 1].z) + front);
                    verts.Add(new Vector3(curPts[i].x, t0, curPts[i].z) + front);
                    verts.Add(new Vector3(curPts[i + 1].x, t1, curPts[i + 1].z) + front);
                    cols.Add(toDigTop); cols.Add(toDigTop); cols.Add(toDigBottom); cols.Add(toDigBottom);
                    tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                }
                if (c0 < t0 - overDigShow || c1 < t1 - overDigShow)   // dug below the target
                {
                    int v = verts.Count;
                    verts.Add(new Vector3(curPts[i].x, t0, curPts[i].z) + front);
                    verts.Add(new Vector3(curPts[i + 1].x, t1, curPts[i + 1].z) + front);
                    verts.Add(new Vector3(curPts[i].x, Mathf.Min(c0, t0), curPts[i].z) + front);
                    verts.Add(new Vector3(curPts[i + 1].x, Mathf.Min(c1, t1), curPts[i + 1].z) + front);
                    cols.Add(overDig); cols.Add(overDig); cols.Add(overDig); cols.Add(overDig);
                    tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                }
            }
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);   // both windings below: Sprites/Default does not cull
        mesh.RecalculateBounds();
        sectionGo.SetActive(true);

        // --- lines: original ground (white), current ground (light brown), target depth inside the dig area (green) ---
        SetLine(refLine, refPts, new Color(1F, 1F, 1F, 0.75F));
        SetLine(curLine, curPts, new Color(0.95F, 0.75F, 0.5F));
        var tgt = new List<Vector3>();
        float target = TargetDepth;
        foreach (var p in refPts)
            if (Digger.Contains(p - side * behindBucket, 0F)) tgt.Add(new Vector3(p.x, p.y - target, p.z));
        if (tgt.Count >= 2 && Guidance) SetLine(targetLine, tgt.ToArray(), CueKit.Alpha(CueKit.Good, 0.9F)); else CueKit.Hide(targetLine);

        // --- buried services crossing the section plane ---
        int k = 0;
        foreach (var h in Hazard.All)
        {
            if (!(h is SegmentHazard seg) || !h.active) continue;
            if (h.kind != HazardKind.BuriedPipe && h.kind != HazardKind.BuriedCable) continue;
            float da = Vector3.Dot(CueKit.Flat(seg.a) - o, side), db = Vector3.Dot(CueKit.Flat(seg.b) - o, side);
            if (Mathf.Abs(da - db) < 1e-4F || Mathf.Sign(da) == Mathf.Sign(db)) continue;   // does not cross the plane
            Vector3 q = Vector3.Lerp(seg.a, seg.b, da / (da - db));
            if (Mathf.Abs(Vector3.Dot(CueKit.Flat(q) - o, reachDir)) > halfSpan) continue;

            while (serviceRings.Count <= k)
            {
                serviceRings.Add(CueKit.Line(own, "CueSectionService", 0.07F, true, false, CueKit.BucketSideOnlyLayer));
                serviceLabels.Add(new ScreenBox(root.ContextCanvasRoot, root.Font, "CueSectionServiceLabel", 16));   // bucket side view (Display 5)
            }
            Color c = !Guidance ? new Color(0.75F, 0.75F, 0.78F)          // C2: just the object, no warning colours
                    : h.CurrentLevel == Hazard.Level.Contact ? CueKit.Danger
                    : h.CurrentLevel == Hazard.Level.Warning ? CueKit.Warn : CueKit.Look;
            var ring = serviceRings[k];
            float rad = h.kind == HazardKind.BuriedPipe ? 0.18F : 0.08F;
            ring.loop = true;
            ring.positionCount = 20;
            for (int j = 0; j < 20; j++)
            {
                float a = j / 20F * Mathf.PI * 2F;
                ring.SetPosition(j, q - side * 0.05F + reachDir * Mathf.Cos(a) * rad + Vector3.up * Mathf.Sin(a) * rad);
            }
            CueKit.Paint(ring, c);
            float dist = Mathf.Max(0F, Vector3.Distance(m.BucketTipPosition, q) - (h.kind == HazardKind.BuriedPipe ? 0.15F : 0.03F));   // to the pipe surface
            string name = h.kind == HazardKind.BuriedPipe ? "PIPE" : "CABLE";
            if (!Guidance) { serviceLabels[k].Hide(); k++; continue; }
            // No text tag here any more: the proximity widget of BucketSideServices shows the distance
            // (one cue per service in every view). The coloured ring stays.
            serviceLabels[k].Hide();
            k++;
        }
        for (int j = k; j < serviceRings.Count; j++) { CueKit.Hide(serviceRings[j]); serviceLabels[j].Hide(); }
    }

    static void SetLine(LineRenderer lr, Vector3[] pts, Color c)
    {
        lr.loop = false;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
        CueKit.Paint(lr, c);
    }

    static void PlaceLabel(ScreenBox box, Camera cam, Vector3 world, string text, Color c)
    {
        if (cam == null) { box.Hide(); return; }
        Vector3 vp = cam.WorldToViewportPoint(world);
        if (vp.z <= 0F || vp.x < 0F || vp.x > 1F || vp.y < 0F || vp.y > 1F) { box.Hide(); return; }
        Rect r = cam.rect;
        // Beside the pipe, on the side with more room, and kept inside the window (no tag cut off at the edge).
        bool left = vp.x > 0.65F;
        float x = r.xMin + vp.x * r.width + (left ? -0.004F : 0.004F);
        float y = Mathf.Clamp(r.yMin + vp.y * r.height, r.yMin + 0.03F, r.yMax - 0.03F);
        c.a = 1F;
        box.ShowTag(new Vector2(x, y), new Vector2(left ? 1F : 0F, 0.5F), c, text);
    }
}
