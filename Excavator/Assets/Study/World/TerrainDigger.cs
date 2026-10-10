using UnityEngine;

// ---------------------------------------------------------------------------
// TERRAIN DIGGER
// Makes digging visible: when the bucket teeth go below the ground INSIDE the
// marked dig zone, the terrain there is lowered to the teeth height.
//
// Safety: the terrain is copied at trial start and only the copy is changed,
// so the terrain file in your project is never modified. Reloading the scene
// (which happens at every trial start) brings back the untouched terrain.
// ---------------------------------------------------------------------------

public class TerrainDigger : MonoBehaviour
{
    public static TerrainDigger Instance { get; private set; }

    Terrain terrain;
    TerrainData original;   // untouched copy, used as the "original ground" reference
    TerrainData working;    // the copy that gets dug

    // Dig zone: a rectangle on the ground. u = along the long side, v = across.
    Vector3 center, axisU, axisV;
    float halfU, halfV, maxDepth;
    bool hasZone;

    // When true, the zone works like a soil supply: any pass deeper than 0.3 m inside the
    // zone counts as cutting, even where the pit is already dug out. Used for practice and
    // dig-and-dump. The trench scenario keeps it false, because there the depth is the task.
    public bool EndlessSoil;

    public Vector3 ZoneCenter => center;
    public Vector3 ZoneAxisU => axisU;
    public float ZoneHalfLength => halfU;
    public float ZoneHalfWidth => halfV;

    // Height of the ORIGINAL ground at a point (before any digging). Works even without a digger.
    public static float ReferenceGroundHeight(Vector3 p)
    {
        Terrain t = Terrain.activeTerrain;
        if (t == null) return 0F;
        if (Instance != null && Instance.original != null)
        {
            Vector3 tp = t.transform.position, size = Instance.original.size;
            return Instance.original.GetInterpolatedHeight((p.x - tp.x) / size.x, (p.z - tp.z) / size.z) + tp.y;
        }
        return t.SampleHeight(p) + t.transform.position.y;
    }

    // Current (possibly dug) ground height at a point.
    public static float CurrentGroundHeight(Vector3 p)
    {
        Terrain t = Terrain.activeTerrain;
        return t == null ? 0F : t.SampleHeight(p) + t.transform.position.y;
    }

    void Awake()
    {
        Instance = this;
        terrain = Terrain.activeTerrain;
        if (terrain == null) return;
        original = terrain.terrainData;
        working = Instantiate(original);
        terrain.terrainData = working;
        var col = terrain.GetComponent<TerrainCollider>();
        if (col != null) col.terrainData = working;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // center = middle of the zone on the ground, forward = direction of the long side.
    public void SetZone(Vector3 zoneCenter, Vector3 forward, float length, float width, float maxDigDepth)
    {
        center = zoneCenter;
        axisU = new Vector3(forward.x, 0, forward.z).normalized;
        axisV = Vector3.Cross(Vector3.up, axisU);
        halfU = length / 2F; halfV = width / 2F; maxDepth = maxDigDepth;
        hasZone = true;
    }

    public bool Contains(Vector3 p, float margin = 0F)
    {
        if (!hasZone) return false;
        Vector3 d = p - center;
        return Mathf.Abs(Vector3.Dot(d, axisU)) <= halfU + margin && Mathf.Abs(Vector3.Dot(d, axisV)) <= halfV + margin;
    }

    // Lower the ground around the bucket teeth. Returns true if any ground was removed.
    public bool Dig(Vector3 tip, float radius = 0.9F)
    {
        if (terrain == null || !Contains(tip, 0.3F)) return false;

        Vector3 tp = terrain.transform.position, size = working.size;
        int res = working.heightmapResolution;
        float cell = size.x / (res - 1);
        int cx = Mathf.RoundToInt((tip.x - tp.x) / cell), cz = Mathf.RoundToInt((tip.z - tp.z) / cell);
        int r = Mathf.CeilToInt(radius / cell);
        int x0 = Mathf.Clamp(cx - r, 0, res - 1), z0 = Mathf.Clamp(cz - r, 0, res - 1);
        int x1 = Mathf.Clamp(cx + r, 0, res - 1), z1 = Mathf.Clamp(cz + r, 0, res - 1);
        int w = x1 - x0 + 1, h = z1 - z0 + 1;

        float[,] heights = working.GetHeights(x0, z0, w, h);   // [z, x], values 0..1
        bool changed = false;
        for (int iz = 0; iz < h; iz++)
            for (int ix = 0; ix < w; ix++)
            {
                Vector3 wp = new Vector3(tp.x + (x0 + ix) * cell, 0, tp.z + (z0 + iz) * cell);
                if (!Contains(wp) || (new Vector2(wp.x - tip.x, wp.z - tip.z)).magnitude > radius) continue;
                float refY = ReferenceGroundHeight(wp);
                float targetY = Mathf.Max(tip.y, refY - maxDepth);
                float targetN = (targetY - tp.y) / size.y;
                if (targetN < heights[iz, ix]) { heights[iz, ix] = targetN; changed = true; }
            }
        if (changed) working.SetHeights(x0, z0, heights);
        return changed;
    }

    // Share of the zone (0..1) that has been dug at least to the given depth.
    public float FractionDugTo(float depth, float tolerance = 0.15F)
    {
        if (!hasZone) return 0F;
        int total = 0, ok = 0;
        for (float u = -halfU + 0.25F; u <= halfU - 0.25F; u += 0.5F)
            for (float v = -halfV + 0.25F; v <= halfV - 0.25F; v += 0.5F)
            {
                Vector3 p = center + axisU * u + axisV * v;
                total++;
                if (ReferenceGroundHeight(p) - CurrentGroundHeight(p) >= depth - tolerance) ok++;
            }
        return total == 0 ? 0F : (float)ok / total;
    }
}
