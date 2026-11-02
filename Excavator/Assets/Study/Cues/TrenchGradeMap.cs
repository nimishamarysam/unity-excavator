using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// TRENCH GRADE MAP  (C3 only, digging tasks, 2026-11-02)
// Like the cut/fill colours of a machine-control (grade) system: the floor of the dig area is covered with a grid
// of coloured squares (CellSize), each showing how much soil is LEFT above the target depth at that spot:
//     dark orange  = a lot of soil left (>= 2/3 of the target depth)
//     orange       = some left
//     light orange = almost there
//     green        = at the target (within AtGradeBand above it, or down to OverDigTolerance below)
//     red          = dug too deep (more than OverDigTolerance below the target)
// The squares lie on the current ground (they move down as the soil is dug) and are drawn ONLY in the
// DIG PIT (TOP VIEW) context window (ContextViews.PitTopCamera) - not in the driver view or other windows.
// Rebuilt RefreshHz times per second. Logged as cue_on / cue_off "trench_grade_map".
// ---------------------------------------------------------------------------

public class TrenchGradeMap : MonoBehaviour
{
    public static bool Enabled = false;   // 2026-11-02: OFF after the pilot - too distracting (kept for reference)
    public float CellSize = 0.5F;
    public float Lift = 0.05F;             // above the ground surface
    public float AtGradeBand = 0.1F;       // m above the target that still counts as "at target" (green)
    public float RefreshHz = 5F;

    AttentionCues root;
    GameObject go;
    Mesh mesh;
    MeshRenderer mr;
    bool active;
    float nextBuild;

    public void Init(AttentionCues r)
    {
        root = r;
        go = new GameObject("CueTrenchGradeMap") { layer = CueKit.ContextOnlyLayer };
        go.transform.SetParent(transform, false);
        mesh = new Mesh { name = "TrenchGradeMap" };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3020 };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.enabled = false;
        Camera.onPreCull += PreCull;
        Camera.onPostRender += PostRender;
    }

    void OnDestroy()
    {
        Camera.onPreCull -= PreCull;
        Camera.onPostRender -= PostRender;
    }

    // Visible only while the DIG PIT (TOP VIEW) camera renders.
    void PreCull(Camera cam) { if (mr != null) mr.enabled = active && cam != null && cam == ContextViews.PitTopCamera; }
    void PostRender(Camera cam) { if (mr != null) mr.enabled = false; }

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
        bool want = Enabled && root.Visible && Digger != null && Digger.HasZone && ContextViews.PitTopCamera != null;
        if (want != active)
        {
            active = want;
            AttentionCues.LogCue(want, "trench_grade_map", "");
        }
        if (active && Time.unscaledTime >= nextBuild)
        {
            nextBuild = Time.unscaledTime + 1F / RefreshHz;
            Build();
        }
    }

    static readonly Color DarkOrange = new Color(0.76F, 0.25F, 0.05F, 0.9F);
    static readonly Color LightOrange = new Color(0.97F, 0.72F, 0.45F, 0.9F);
    static readonly Color Green = new Color(0.26F, 0.63F, 0.28F, 0.9F);
    static readonly Color Red = new Color(0.83F, 0.18F, 0.18F, 0.9F);

    void Build()
    {
        var d = Digger;
        Vector3 c = d.ZoneCenter, u = d.ZoneAxisU, v = Vector3.Cross(Vector3.up, u);
        float hu = d.ZoneHalfLength, hv = d.ZoneHalfWidth, target = TargetDepth;
        float overDig = root.Scenario != null ? root.Scenario.OverDigTolerance : 0.15F;
        int nu = Mathf.Max(1, Mathf.CeilToInt(2F * hu / CellSize)), nv = Mathf.Max(1, Mathf.CeilToInt(2F * hv / CellSize));
        float su = 2F * hu / nu, sv = 2F * hv / nv, gap = 0.03F;

        var verts = new List<Vector3>(nu * nv * 4);
        var cols = new List<Color>(nu * nv * 4);
        var tris = new List<int>(nu * nv * 6);
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                float cu = -hu + (i + 0.5F) * su, cv = -hv + (j + 0.5F) * sv;
                Vector3 p = c + u * cu + v * cv;
                float refY = TerrainDigger.ReferenceGroundHeight(p);
                float curY = Mathf.Min(TerrainDigger.CurrentGroundHeight(p), refY);
                float left = curY - (refY - target);   // soil still above the target (m); negative = below it
                Color col = left > AtGradeBand ? Color.Lerp(LightOrange, DarkOrange, Mathf.Clamp01(left / (target * 0.67F)))
                          : left >= -overDig ? Green : Red;
                float y = curY + Lift;
                Vector3 du = u * (su / 2F - gap), dv = v * (sv / 2F - gap);
                int k = verts.Count;
                Vector3 pc = new Vector3(p.x, y, p.z);
                verts.Add(pc - du - dv); verts.Add(pc + du - dv); verts.Add(pc + du + dv); verts.Add(pc - du + dv);
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3, k, k + 2, k + 1, k, k + 3, k + 2 });   // both sides
            }
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }
}
