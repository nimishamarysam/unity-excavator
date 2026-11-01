using UnityEngine;

// ---------------------------------------------------------------------------
// TERRAIN DIGGER
// Makes digging visible: when the bucket teeth go below the ground INSIDE the
// marked dig zone, the terrain there is lowered to the teeth height, and the
// dug ground is painted soil-brown so the hole is easy to see on the snow.
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

    // When true, any pass deeper than 0.3 m inside the zone counts as cutting, even if the
    // teeth do not touch the dug surface. Currently not used by any scenario (realistic
    // digging: the teeth must touch the ground in the hole). Kept as an option for tuning.
    public bool EndlessSoil;

    public Vector3 ZoneCenter => center;
    public Vector3 ZoneAxisU => axisU;
    public float ZoneHalfLength => halfU;
    public float ZoneHalfWidth => halfV;
    public float ZoneMaxDepth => maxDepth;
    public bool HasZone => hasZone;

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
        AddSoilLayer();
    }

    // ---- Soil paint: an extra terrain layer, painted where the ground has been dug ----
    int soilLayer = -1;

    // ---- Ground look: replaces the original snow paint with a summer construction site ----
    // Layers: 0 grass, 1 dirt (also used for dug ground), 2 gravel, 3 rock. Textures are generated
    // from noise, so no extra asset files are needed. Painted once per trial on the runtime copy only.
    // Max 4 layers: Unity's built-in terrain shader draws 4 layers in one pass; a 5th layer needs an
    // extra pass that renders white here (dug ground and distant terrain turned white).
    const int Grass = 0, Dirt = 1, Gravel = 2, Rock = 3, DugSoil = Dirt;
    const int LayerCount = 4;

    // Work area (world x/z) that is painted as packed dirt and gravel.
    public static Rect WorkArea = new Rect(-42F, -19F, 82F, 34F);
    public float riverBankHeight = 99.7F;   // world height below which the ground is muddy bank

    void AddSoilLayer()
    {
        var layers = new TerrainLayer[LayerCount];
        layers[Grass]   = MakeLayer(NoiseTexture(new Color(0.33F, 0.42F, 0.18F), new Color(0.22F, 0.32F, 0.12F), 0.08F, 0.0F, 11), 4F);
        layers[Dirt]    = MakeLayer(NoiseTexture(new Color(0.40F, 0.28F, 0.17F), new Color(0.28F, 0.19F, 0.11F), 0.12F, 0.1F, 12), 2F);
        layers[Gravel]  = MakeLayer(NoiseTexture(new Color(0.55F, 0.53F, 0.49F), new Color(0.40F, 0.39F, 0.36F), 0.35F, 0.35F, 13), 1.5F);
        layers[Rock]    = MakeLayer(NoiseTexture(new Color(0.50F, 0.48F, 0.45F), new Color(0.32F, 0.31F, 0.30F), 0.05F, 0.1F, 14), 8F);
        try
        {
            working.terrainLayers = layers;
            soilLayer = DugSoil;
            PaintGround();
            working.SetBaseMapDirty();   // the far-away terrain picture must be rebuilt too

            // Fresh terrain material: the scene's old one may ignore the new layers.
            string oldShader = terrain.materialTemplate != null && terrain.materialTemplate.shader != null ? terrain.materialTemplate.shader.name : "none";
            Shader sh = Shader.Find("Nature/Terrain/Standard");
            if (sh == null || !sh.isSupported) sh = Shader.Find("Nature/Terrain/Diffuse");
            if (sh != null) terrain.materialTemplate = new Material(sh);

            // The scene has an old baked lightmap; a washed-out lightmap makes the ground look white.
            int oldLightmap = terrain.lightmapIndex;
            terrain.lightmapIndex = -1;   // use only the real-time sun light

            terrain.basemapDistance = 5000F;   // always draw the full-detail paint, also far away (mirror view)
            terrain.Flush();
            Debug.Log($"[Study] Terrain material: {oldShader} -> {(sh != null ? sh.name : "NOT FOUND")}, lightmap {oldLightmap} -> -1");
            Debug.Log($"[Study] Terrain repainted: {working.alphamapLayers} layers, alphamap {working.alphamapResolution}px");
        }
        catch (System.Exception e)
        {
            soilLayer = -1;
            Debug.LogError("[Study] Terrain repaint failed: " + e);
        }
    }

    static TerrainLayer MakeLayer(Texture2D tex, float tile)
    {
        return new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tile, tile) };
    }

    // A tileable texture that blends two colours with layered noise. 'grain' adds fine speckles
    // (gravel), 'scale' sets how big the blotches are.
    static Texture2D NoiseTexture(Color a, Color b, float scale, float grain, int seed)
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        var rnd = new System.Random(seed);
        float ox = seed * 13.1F, oy = seed * 7.7F;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // Sample noise on a torus so the texture repeats without seams.
                float u = x / (float)N * Mathf.PI * 2F, v = y / (float)N * Mathf.PI * 2F;
                float n = 0F, amp = 0.5F, freq = 1F + scale * 20F;
                for (int o = 0; o < 4; o++)
                {
                    n += amp * Mathf.PerlinNoise(ox + Mathf.Cos(u) * freq + Mathf.Sin(v) * freq * 0.5F,
                                                 oy + Mathf.Sin(u) * freq + Mathf.Cos(v) * freq * 0.5F);
                    amp *= 0.5F; freq *= 2F;
                }
                float t = Mathf.Clamp01(n * 1.1F);
                if (grain > 0F) t = Mathf.Clamp01(t + ((float)rnd.NextDouble() - 0.5F) * grain);
                px[y * N + x] = Color.Lerp(a, b, t);
            }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    void PaintGround()
    {
        int res = working.alphamapResolution;
        Vector3 tp = terrain.transform.position, size = working.size;
        var maps = new float[res, res, LayerCount];
        for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                float u = (ix + 0.5F) / res, v = (iz + 0.5F) / res;
                float wx = tp.x + u * size.x, wz = tp.z + v * size.z;
                float hy = working.GetInterpolatedHeight(u, v) + tp.y;
                float slope = working.GetSteepness(u, v);
                float patch = Mathf.PerlinNoise(wx * 0.06F + 31F, wz * 0.06F + 17F);   // large-scale variation

                float grass = 1F, dirt = 0F, gravel = 0F, rock = 0F;

                // Bare dirt patches in the grass.
                dirt = Mathf.Clamp01((patch - 0.62F) * 4F);

                // Work area: packed dirt with gravel, soft noisy edge.
                float edge = Mathf.PerlinNoise(wx * 0.15F, wz * 0.15F) * 6F;
                float dx = Mathf.Max(WorkArea.xMin - wx, 0F, wx - WorkArea.xMax);
                float dz = Mathf.Max(WorkArea.yMin - wz, 0F, wz - WorkArea.yMax);
                float site = 1F - Mathf.Clamp01((Mathf.Sqrt(dx * dx + dz * dz) - edge) / 6F);
                if (site > 0F)
                {
                    // Mostly gravel, with only light dirt traces, so freshly dug (dark) ground stands out.
                    float d = Mathf.Clamp01(Mathf.PerlinNoise(wx * 0.2F + 5F, wz * 0.2F + 9F) * 1.6F - 0.9F) * 0.5F;
                    gravel = Mathf.Max(gravel, site * (1F - d));
                    dirt = Mathf.Max(dirt * (1F - site), site * d);
                    grass *= 1F - site;
                }

                // Muddy river banks.
                float bank = Mathf.Clamp01((riverBankHeight - hy) / 0.5F);
                dirt = Mathf.Max(dirt, bank);
                grass *= 1F - bank;

                // Steep slopes are rock.
                rock = Mathf.Clamp01((slope - 22F) / 10F);
                grass *= 1F - rock; dirt *= 1F - rock; gravel *= 1F - rock;
                grass *= 1F - Mathf.Clamp01(dirt + gravel);

                float sum = grass + dirt + gravel + rock;
                if (sum <= 0.0001F) { grass = 1F; sum = 1F; }
                maps[iz, ix, Grass] = grass / sum;
                maps[iz, ix, Dirt] = dirt / sum;
                maps[iz, ix, Gravel] = gravel / sum;
                maps[iz, ix, Rock] = rock / sum;
            }
        working.SetAlphamaps(0, 0, maps);
    }

    void PaintSoil(Vector3 tip, float radius)
    {
        if (soilLayer < 0) return;
        Vector3 tp = terrain.transform.position, size = working.size;
        int aw = working.alphamapWidth, ah = working.alphamapHeight, n = working.alphamapLayers;
        if (soilLayer >= n) return;
        float cx = (tip.x - tp.x) / size.x * aw, cz = (tip.z - tp.z) / size.z * ah;
        float rx = radius / size.x * aw, rz = radius / size.z * ah;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(cx - rx), 0, aw - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(cx + rx), 0, aw - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(cz - rz), 0, ah - 1), z1 = Mathf.Clamp(Mathf.CeilToInt(cz + rz), 0, ah - 1);
        int w = x1 - x0 + 1, h = z1 - z0 + 1;
        float[,,] a = working.GetAlphamaps(x0, z0, w, h);
        for (int iz = 0; iz < h; iz++)
            for (int ix = 0; ix < w; ix++)
            {
                Vector3 wp = new Vector3(tp.x + (x0 + ix + 0.5F) / aw * size.x, 0, tp.z + (z0 + iz + 0.5F) / ah * size.z);
                if (!Contains(wp, 0.2F) || (new Vector2(wp.x - tip.x, wp.z - tip.z)).magnitude > radius) continue;
                for (int l = 0; l < n; l++) a[iz, ix, l] = l == soilLayer ? 1F : 0F;
            }
        working.SetAlphamaps(x0, z0, a);
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

    // Width of the cut: soil is removed in a circle of this radius around the teeth (about the bucket width,
    // 1.2 m; BucketBlocker.bucketHalfWidth = 0.45 m). Was 0.9 m (1.8 m wide), which cut far more than a bucket.
    public const float CutRadius = 0.6F;

    // Lower the ground around the bucket teeth, removing at most maxVolume (m3). Returns the volume removed (m3).
    // If the full cut would be more than maxVolume, every point is lowered only part of the way, so the bucket
    // never takes more soil than it has room for.
    public float Dig(Vector3 tip, float radius = CutRadius, float maxVolume = float.MaxValue)
    {
        if (terrain == null || maxVolume <= 0F || !Contains(tip, 0.3F)) return 0F;

        Vector3 tp = terrain.transform.position, size = working.size;
        int res = working.heightmapResolution;
        float cell = size.x / (res - 1), cellArea = cell * (size.z / (res - 1));
        int cx = Mathf.RoundToInt((tip.x - tp.x) / cell), cz = Mathf.RoundToInt((tip.z - tp.z) / cell);
        int r = Mathf.CeilToInt(radius / cell);
        int x0 = Mathf.Clamp(cx - r, 0, res - 1), z0 = Mathf.Clamp(cz - r, 0, res - 1);
        int x1 = Mathf.Clamp(cx + r, 0, res - 1), z1 = Mathf.Clamp(cz + r, 0, res - 1);
        int w = x1 - x0 + 1, h = z1 - z0 + 1;

        float[,] heights = working.GetHeights(x0, z0, w, h);   // [z, x], values 0..1
        float[,] target = new float[h, w];
        float volume = 0F;
        for (int iz = 0; iz < h; iz++)
            for (int ix = 0; ix < w; ix++)
            {
                target[iz, ix] = heights[iz, ix];
                Vector3 wp = new Vector3(tp.x + (x0 + ix) * cell, 0, tp.z + (z0 + iz) * cell);
                if (!Contains(wp) || (new Vector2(wp.x - tip.x, wp.z - tip.z)).magnitude > radius) continue;
                float refY = ReferenceGroundHeight(wp);
                float targetY = Mathf.Max(tip.y, refY - maxDepth);
                float targetN = (targetY - tp.y) / size.y;
                if (targetN < heights[iz, ix])
                {
                    target[iz, ix] = targetN;
                    volume += (heights[iz, ix] - targetN) * size.y * cellArea;
                }
            }
        if (volume <= 0F) return 0F;
        float share = volume > maxVolume ? maxVolume / volume : 1F;   // bucket nearly full: cut only part of the way
        for (int iz = 0; iz < h; iz++)
            for (int ix = 0; ix < w; ix++)
                heights[iz, ix] -= (heights[iz, ix] - target[iz, ix]) * share;
        working.SetHeights(x0, z0, heights);
        PaintSoil(tip, radius);   // touched ground turns brown, even at the bottom of the hole
        return volume * share;
    }

    // Smooth progress (0..1): the average over a 0.5 m grid of how far each cell is dug towards the
    // depth, each cell capped at 1 (full credit at depth - tolerance). Rises with every scoop instead of
    // only when a whole cell reaches depth, so the "Trench done" number follows what the operator sees.
    // inset = metres kept free of sample points along the zone edges. Dig() only lowers terrain vertices INSIDE
    // the zone, so the ground right at the edge is a sloped wall (interpolated with the undug ground outside)
    // and can never reach depth - without an inset those edge cells cap the progress at ~75-80 %.
    public float ProgressTo(float depth, float tolerance = 0.2F, float inset = 0F)
    {
        if (!hasZone) return 0F;
        float full = Mathf.Max(0.05F, depth - tolerance), sum = 0F;
        int total = 0;
        foreach (var p in Grid(inset))
        {
            total++;
            sum += Mathf.Clamp01((ReferenceGroundHeight(p) - CurrentGroundHeight(p)) / full);
        }
        return total == 0 ? 0F : sum / total;
    }

    // Sample points: a 0.5 m grid centred in the zone, 'inset' metres away from every edge (at least one row / column).
    System.Collections.Generic.IEnumerable<Vector3> Grid(float inset)
    {
        int nu = Mathf.Max(1, Mathf.FloorToInt(2F * (halfU - inset) / 0.5F + 0.001F));
        int nv = Mathf.Max(1, Mathf.FloorToInt(2F * (halfV - inset) / 0.5F + 0.001F));
        for (int iu = 0; iu < nu; iu++)
            for (int iv = 0; iv < nv; iv++)
                yield return center + axisU * ((iu - (nu - 1) * 0.5F) * 0.5F) + axisV * ((iv - (nv - 1) * 0.5F) * 0.5F);
    }

    // DIAGNOSTIC: dug depth per 0.5 m row ACROSS the zone, from the end nearest 'from' to the far end.
    // Each entry "dist:mean/min" = distance of the row from 'from' (m) : mean / smallest dug depth in that row (m).
    // Shows which part of the zone could not be dug to depth (e.g. out of reach at the far end, too close at the near end).
    public string DepthProfile(Vector3 from)
    {
        if (!hasZone) return "";
        var sb = new System.Text.StringBuilder();
        float sign = Vector3.Dot(new Vector3(from.x - center.x, 0F, from.z - center.z), axisU) > 0F ? -1F : 1F;   // near -> far
        for (float s = -halfU + 0.25F; s <= halfU - 0.25F; s += 0.5F)
        {
            float u = sign * s, sum = 0F, min = float.MaxValue; int n = 0;
            for (float v = -halfV + 0.25F; v <= halfV - 0.25F; v += 0.5F)
            {
                Vector3 p = center + axisU * u + axisV * v;
                float d = ReferenceGroundHeight(p) - CurrentGroundHeight(p);
                sum += d; min = Mathf.Min(min, d); n++;
            }
            Vector3 rowC = center + axisU * u;
            float dist = new Vector2(rowC.x - from.x, rowC.z - from.z).magnitude;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append($"{dist:F1}:{sum / Mathf.Max(1, n):F2}/{min:F2}");
        }
        return sb.ToString();
    }

    // ---- Continuous measures for the end-of-trial summary ----

    // Trench floor (same sample grid as ProgressTo, 'inset' from the edges) compared with the target depth:
    // mean depth, RMS error to the target, share within the target band (target - below .. target + above),
    // share shallower than the band (under-dug) and share deeper than the band (over-dug).
    // Optional: only points between minR and maxR (horizontal distance) from 'from' (e.g. the reachable band).
    // n = number of sample points used.
    public void FloorStats(float target, float below, float above, float inset,
                           out float mean, out float rmse, out float inBand, out float under, out float over)
        => FloorStats(target, below, above, inset, Vector3.zero, 0F, float.MaxValue, out mean, out rmse, out inBand, out under, out over, out _);

    public void FloorStats(float target, float below, float above, float inset, Vector3 from, float minR, float maxR,
                           out float mean, out float rmse, out float inBand, out float under, out float over, out int n)
    {
        mean = rmse = inBand = under = over = 0F;
        n = 0;
        if (!hasZone) return;
        foreach (var p in Grid(inset))
        {
            float rr = new Vector2(p.x - from.x, p.z - from.z).magnitude;
            if (maxR < float.MaxValue && (rr < minR || rr > maxR)) continue;
            float d = ReferenceGroundHeight(p) - CurrentGroundHeight(p);
            n++; mean += d; rmse += (d - target) * (d - target);
            if (d < target - below) under++; else if (d > target + above) over++; else inBand++;
        }
        if (n == 0) return;
        mean /= n; rmse = Mathf.Sqrt(rmse / n); inBand /= n; under /= n; over /= n;
    }

    // Soil volume (m3) removed inside the zone, and the part of it deeper than depthLimit below the original ground.
    public void DugVolumes(float depthLimit, out float total, out float beyond)
    {
        total = beyond = 0F;
        if (!hasZone || terrain == null) return;
        Vector3 tp = terrain.transform.position, size = working.size;
        int res = working.heightmapResolution;
        float cx = size.x / (res - 1), cz = size.z / (res - 1);
        Vector3 eu = axisU * halfU, ev = axisV * halfV;
        float minX = center.x - Mathf.Abs(eu.x) - Mathf.Abs(ev.x), maxX = center.x + Mathf.Abs(eu.x) + Mathf.Abs(ev.x);
        float minZ = center.z - Mathf.Abs(eu.z) - Mathf.Abs(ev.z), maxZ = center.z + Mathf.Abs(eu.z) + Mathf.Abs(ev.z);
        int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - tp.x) / cx), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - tp.x) / cx), 0, res - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - tp.z) / cz), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - tp.z) / cz), 0, res - 1);
        float[,] h = working.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        for (int iz = 0; iz <= z1 - z0; iz++)
            for (int ix = 0; ix <= x1 - x0; ix++)
            {
                Vector3 wp = new Vector3(tp.x + (x0 + ix) * cx, 0F, tp.z + (z0 + iz) * cz);
                if (!Contains(wp)) continue;
                float d = ReferenceGroundHeight(wp) - (h[iz, ix] * size.y + tp.y);
                if (d > 0F) total += d * cx * cz;
                if (d > depthLimit) beyond += (d - depthLimit) * cx * cz;
            }
    }

    // Share of the zone (0..1) that has been dug at least to the given depth (same sample grid as ProgressTo).
    public float FractionDugTo(float depth, float tolerance = 0.15F, float inset = 0F)
    {
        if (!hasZone) return 0F;
        int total = 0, ok = 0;
        foreach (var p in Grid(inset))
        {
            total++;
            if (ReferenceGroundHeight(p) - CurrentGroundHeight(p) >= depth - tolerance) ok++;
        }
        return total == 0 ? 0F : (float)ok / total;
    }
}
