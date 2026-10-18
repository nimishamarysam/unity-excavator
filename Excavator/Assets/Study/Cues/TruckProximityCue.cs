using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// TRUCK WALL PROXIMITY  (C3, profile option truckWallProximity)
// When the bucket or arm comes close to a wall of the truck bed, two things appear at the part of the wall
// nearest to the bucket (visible in the driver view and all context views), sliding along with the bucket:
//   - a short bar on the wall's TOP EDGE (rim);
//   - a PROXIMITY FIELD: a translucent curved sheet that bulges out of the wall face towards the bucket,
//     coloured at the wall and fading to transparent white at its outer edge. It bulges further and gets
//     more opaque as the gap shrinks.
// Both get warmer as the gap shrinks:
//   < 1.2 m yellow,  < 0.6 m orange,  < 0.25 m red (blinking) - at 0 the BucketBlocker stops the bucket.
// The gap is measured from the same bucket/arm probes the BucketBlocker uses. Logged as cue_on/cue_off
// "truck_wall;<wall>;gap=..".
// ---------------------------------------------------------------------------

public class TruckProximityCue : MonoBehaviour
{
    public float showGap = 1.2F, warnGap = 0.6F, dangerGap = 0.25F;
    public float rimLength = 1.6F;   // length of the highlighted piece of the rim (m)

    AttentionCues root;
    readonly List<(Collider col, LineRenderer lr)> walls = new List<(Collider, LineRenderer)>();
    readonly Dictionary<Collider, bool> shown = new Dictionary<Collider, bool>();
    readonly Dictionary<Collider, (GameObject go, Mesh mesh)> fields = new Dictionary<Collider, (GameObject, Mesh)>();
    Material fieldMat;
    public float fieldWidth = 1.8F, fieldHalfHeight = 0.9F, fieldMinBulge = 0.15F, fieldMaxBulge = 0.8F;
    const int GU = 18, GV = 12;

    public void Init(AttentionCues r)
    {
        root = r;
        foreach (var c in BucketBlocker.Solids)
            if (c != null && c.name.Contains("_wall"))
                walls.Add((c, CueKit.Line(r.WorldRoot, "CueTruckWall_" + c.name, 0.06F, true, false)));
    }

    void LateUpdate()
    {
        var bb = BucketBlocker.Instance;
        bool on = root != null && root.Visible && root.Profile.truckWallProximity && bb != null && !root.Ctl.IsTravelMode;
        foreach (var (col, lr) in walls)
        {
            float gap = on && col != null ? bb.Gap(col) : float.MaxValue;
            bool want = gap < showGap;
            shown.TryGetValue(col, out bool was);
            if (want != was) { shown[col] = want; AttentionCues.LogCue(want, "truck_wall", $"{col.name};gap={gap:F2}"); }
            if (!want) { CueKit.Hide(lr); HideField(col); continue; }

            RimSegment(lr, col.transform);
            Color c = gap < dangerGap ? CueKit.Alpha(CueKit.Danger, CueKit.Pulse(10F, 0.5F))
                    : gap < warnGap ? CueKit.Warn
                    : CueKit.Alpha(CueKit.Look, Mathf.Lerp(1F, 0.5F, (gap - warnGap) / (showGap - warnGap)));
            CueKit.Paint(lr, c);
            lr.widthMultiplier = gap < warnGap ? 0.14F : 0.09F;
            DrawField(col, gap, c);
        }
    }

    void HideField(Collider col)
    {
        if (col != null && fields.TryGetValue(col, out var f) && f.go.activeSelf) f.go.SetActive(false);
    }

    // The proximity field: a grid on the wall face (nearest to the bucket), pushed out along the face normal
    // by a dome-shaped bulge. Vertex colour: wall colour near the wall -> transparent white at the far edge.
    void DrawField(Collider col, float gap, Color baseCol)
    {
        if (!fields.TryGetValue(col, out var f))
        {
            if (fieldMat == null) fieldMat = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3090 };
            var go = new GameObject("CueTruckWallField_" + col.name);
            go.transform.SetParent(root.WorldRoot, false);
            var mesh = new Mesh { name = "WallField" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = fieldMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            f = (go, mesh);
            fields[col] = f;
        }
        f.go.SetActive(true);

        Transform t = col.transform;
        Vector3 sc = t.lossyScale;
        bool alongZ = sc.z >= sc.x;
        Vector3 along = (alongZ ? t.forward : t.right).normalized;
        Vector3 n = (alongZ ? t.right : t.forward).normalized;
        float len = alongZ ? sc.z : sc.x, thick = alongZ ? sc.x : sc.z, h = sc.y;
        Vector3 c = t.position;
        var m = root.M;
        Vector3 q = (m.BucketPivotPosition + m.BucketTipPosition) / 2F;
        if (Vector3.Dot(q - c, n) < 0F) n = -n;                     // the face on the bucket's side
        float bottom = c.y - h / 2F, top = c.y + h / 2F;

        float w = Mathf.Min(fieldWidth, len);
        float u0 = Mathf.Clamp(Vector3.Dot(q - c, along), -len / 2F + w / 2F, len / 2F - w / 2F);
        float vc = Mathf.Clamp(q.y, bottom, top + 0.6F);
        float y0 = Mathf.Max(bottom, vc - fieldHalfHeight), y1 = Mathf.Min(top + 0.6F, vc + fieldHalfHeight);
        float closeness = Mathf.Clamp01(1F - gap / showGap);
        float bulge = Mathf.Lerp(fieldMinBulge, fieldMaxBulge, closeness);
        float a0 = Mathf.Lerp(0.35F, 0.8F, closeness);
        Vector3 face = c + n * (thick / 2F + 0.01F);

        var verts = new Vector3[(GU + 1) * (GV + 1)];
        var cols = new Color[verts.Length];
        for (int j = 0; j <= GV; j++)
            for (int i = 0; i <= GU; i++)
            {
                float uu = (i / (float)GU) * 2F - 1F, vv = (j / (float)GV) * 2F - 1F;       // -1..1
                float y = Mathf.Lerp(y0, y1, j / (float)GV);
                float yy = (y - vc) / fieldHalfHeight;
                float dome = Mathf.Clamp01(1F - uu * uu) * Mathf.Clamp01(1F - yy * yy);
                float off = bulge * dome;
                Vector3 p = face + along * (u0 + uu * w / 2F) + n * off;
                p.y = y;
                verts[j * (GU + 1) + i] = p;
                float k = Mathf.Pow(dome, 0.8F);                                            // 0 at the wall, 1 at the tip
                Color col2 = Color.Lerp(new Color(baseCol.r, baseCol.g, baseCol.b, a0), new Color(1F, 1F, 1F, 0.05F), k);
                cols[j * (GU + 1) + i] = col2;
            }
        var tris = new int[GU * GV * 6];
        int ti = 0;
        for (int j = 0; j < GV; j++)
            for (int i = 0; i < GU; i++)
            {
                int v = j * (GU + 1) + i;
                tris[ti++] = v; tris[ti++] = v + GU + 1; tris[ti++] = v + 1;
                tris[ti++] = v + 1; tris[ti++] = v + GU + 1; tris[ti++] = v + GU + 2;
            }
        var mesh2 = f.mesh;
        mesh2.Clear();
        mesh2.vertices = verts;
        mesh2.colors = cols;
        mesh2.triangles = tris;
        mesh2.RecalculateBounds();
    }

    // The piece of the wall's top edge nearest to the bucket.
    void RimSegment(LineRenderer lr, Transform t)
    {
        // The wall is a scaled unit cube: its rim runs along the longer horizontal local axis, on top.
        Vector3 s = t.lossyScale;
        bool alongZ = s.z >= s.x;
        Vector3 a = t.TransformPoint(alongZ ? new Vector3(0, 0.5F, -0.5F) : new Vector3(-0.5F, 0.5F, 0));
        Vector3 b = t.TransformPoint(alongZ ? new Vector3(0, 0.5F, 0.5F) : new Vector3(0.5F, 0.5F, 0));
        Vector3 ab = b - a;
        float len = ab.magnitude;
        var m = root.M;
        Vector3 q = (m.BucketPivotPosition + m.BucketTipPosition) / 2F;
        float u = Mathf.Clamp(Vector3.Dot(q - a, ab) / Mathf.Max(1e-4F, len * len), 0F, 1F) * len;   // along the rim
        float half = Mathf.Min(rimLength, len) / 2F;
        u = Mathf.Clamp(u, half, len - half);
        Vector3 dir = ab / Mathf.Max(1e-4F, len), lift = Vector3.up * 0.04F;
        lr.loop = false;
        lr.positionCount = 2;
        lr.SetPosition(0, a + dir * (u - half) + lift);
        lr.SetPosition(1, a + dir * (u + half) + lift);
    }
}
