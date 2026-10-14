using System;
using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// BUCKET LOAD
// Fakes soil handling, because the original simulation has no soil physics.
// Bucket joint angle (as shown in the measurement bar): 0 = fully curled in, 160 = fully open.
//
//   1. Fill:  while the teeth are in the soil inside the dig zone, soil enters the bucket
//             in proportion to how the bucket moves through it, like a real bucket:
//               - dragging the teeth horizontally (1.5 m fills the bucket), and/or
//               - curling the bucket in (70 degrees fills the bucket).
//             The soil lump appears once there is a little soil and grows with the amount.
//   2. Scoop: when the bucket leaves the soil at least 25% full, it is loaded
//             ("bucket_filled", with the fill amount). Less than that falls back.
//   3. Carry: soil stays in the bucket at any angle below 140 degrees.
//   4. Dump:  bucket opened to >= 140 degrees while lifted >= 0.5 m above the ORIGINAL
//             ground -> the soil falls and lands as a heap on whatever is below
//             (ground or truck bed). Dumping on the same spot makes the heap grow.
// Digging outside the zone removes nothing and is logged once per pass as cut_outside_zone.
// ---------------------------------------------------------------------------

public class BucketLoad : MonoBehaviour
{
    public float dragMetersToFill = 1.5F;  // horizontal travel through soil that fills the bucket
    public float curlDegreesToFill = 70F;  // curling through soil that fills the bucket
    public float minScoop = 0.25F;         // smallest load that counts as a scoop (0..1)
    public float dumpAngle = 140F;         // opened to this angle or more = soil falls out
    public float dumpMinHeight = 0.5F;     // metres above the ground needed to dump
    public float cutTolerance = 0.15F;     // teeth within this distance above the dug surface still count as in the soil
    public float zoneMargin = 0.5F;        // metres of tolerance around the marked dig zone

    // Called with the world position where the soil was released. LastDumpAmount says how much (0..1).
    public event Action<Vector3> Dumped;
    public float LastDumpAmount { get; private set; }

    public bool IsLoaded { get; private set; }
    public float Fill { get; private set; }   // 0 = empty, 1 = full bucket
    public int Scoops { get; private set; }

    Excavator machine;
    GameObject lump;
    bool wasInSoil, outsideLogged;
    Vector3 prevTip;
    float prevAngle;
    static Material soilMat;

    public void Init(Excavator m)
    {
        machine = m;
        // Soil in the bucket: a rough heap (same mound shape as the heaps on the ground) that stays upright.
        lump = new GameObject("SoilInBucket");
        lump.AddComponent<MeshFilter>().sharedMesh = SoilHeap.MoundMesh(7);
        lump.AddComponent<MeshRenderer>().sharedMaterial = SoilMaterial();
        lump.SetActive(false);
        AttachLumpToBucket(m);
        BuildClods(m);
        prevTip = m.BucketTipPosition;
        prevAngle = m.bucketAngle;
    }

    public static Material SoilMaterial()
    {
        if (soilMat == null) soilMat = WorldBuilder.MakeMaterial(new Color(0.36F, 0.25F, 0.15F));
        return soilMat;
    }

    void FixedUpdate()
    {
        if (machine == null) return;
        var sm = StudyManager.Instance;
        if (sm == null || !sm.IsTrialRunning) return;

        Vector3 tip = machine.BucketTipPosition, pivot = machine.BucketPivotPosition;
        float angle = machine.bucketAngle;
        float heightAboveSurface = tip.y - TerrainDigger.CurrentGroundHeight(tip);
        float depthBelowOriginal = TerrainDigger.ReferenceGroundHeight(tip) - tip.y;
        var digger = TerrainDigger.Instance;
        bool inZone = digger != null && digger.Contains(tip, zoneMargin);
        bool inGround = heightAboveSurface < cutTolerance || (digger != null && digger.EndlessSoil && depthBelowOriginal > 0.3F);
        bool inSoil = inZone && inGround;

        if (inSoil)
        {
            digger.Dig(tip);
            if (angle >= dumpAngle) Fill = 0F;   // bucket wide open: nothing stays in it
            else
            {
                float drag = new Vector2(tip.x - prevTip.x, tip.z - prevTip.z).magnitude;
                float curl = Mathf.Max(0F, prevAngle - angle);
                Fill = Mathf.Clamp01(Fill + drag / dragMetersToFill + curl / curlDegreesToFill);
            }
        }
        else if (wasInSoil && !IsLoaded)
        {
            // Bucket just left the soil: enough soil = a scoop, otherwise it falls back.
            if (Fill >= minScoop)
            {
                IsLoaded = true;
                Scoops++;
                StudyLogger.Log("bucket_filled", $"scoop={Scoops};fill={Fill:F2}");
            }
            else Fill = 0F;
        }
        wasInSoil = inSoil;

        // Digging outside the zone: log once per pass (a pass ends when the bucket is out of the ground).
        if (depthBelowOriginal < 0F) outsideLogged = false;
        if (!inZone && !outsideLogged && depthBelowOriginal > 0.3F)
        {
            outsideLogged = true;
            StudyLogger.Log("cut_outside_zone", $"x={tip.x:F1};z={tip.z:F1}");
        }

        // Show the soil in the bucket, sized by how full it is.
        bool show = Fill > 0.1F && angle < dumpAngle;
        if (lump.activeSelf != show) lump.SetActive(show);
        if (show)
        {
            // The heap is a child of the bucket: base in the bucket's hollow, heaped out of the opening.
            // It is only the low base layer; the visible load is the pile of clods on top of it.
            float r = Mathf.Lerp(0.2F, 0.42F, Fill);
            lump.transform.localScale = new Vector3(r, Mathf.Lerp(0.03F, 0.14F, Fill), r) * lumpScale;
        }
        ShowClods(show ? Fill : 0F);

        // Dump: bucket opened wide while lifted out of the hole.
        float heightAboveOriginal = -depthBelowOriginal;
        if (IsLoaded && angle >= dumpAngle && heightAboveSurface > dumpMinHeight && heightAboveOriginal > dumpMinHeight) Dump(tip);

        prevTip = tip;
        prevAngle = angle;
    }

    // Puts the soil heap inside the bucket and keeps it there (child of the bucket transform).
    // The bucket's opening side is found once from its shape in the bucket's OWN coordinates (so it does not
    // depend on how the bucket is rotated): the bucket shell lies on one side of the hinge->teeth line, the
    // hollow and the opening on the other.
    float lumpScale = 1F;
    void AttachLumpToBucket(Excavator m)
    {
        Transform b = m.BucketTransform;
        Vector3 tipL = b.InverseTransformPoint(m.BucketTipPosition);
        Vector3 midL = tipL * 0.5F;                          // hinge is the bucket transform's origin
        Vector3 axisL = tipL.sqrMagnitude > 1e-6F ? tipL.normalized : Vector3.forward;
        Vector3 shell = Vector3.zero; int n = 0;
        foreach (var rd in b.GetComponentsInChildren<Renderer>())
        {
            if (rd == null) continue;
            shell += b.InverseTransformPoint(rd.transform.TransformPoint(rd.localBounds.center)); n++;
        }
        Vector3 s = n > 0 ? shell / n - midL : Vector3.zero;
        s -= Vector3.Dot(s, axisL) * axisL;                  // across the hinge->teeth line, towards the shell
        Vector3 openL = s.sqrMagnitude > 1e-6F ? -s.normalized : Vector3.Cross(axisL, Vector3.right).normalized;
        float len = tipL.magnitude;
        // Base of the heap a little into the hollow (towards the shell), heap pointing out of the opening.
        Vector3 baseL = midL - openL * (0.25F * len);
        lump.transform.SetParent(b, false);
        lump.transform.localPosition = baseL;
        lump.transform.localRotation = Quaternion.LookRotation(axisL, openL);   // mound "up" = out of the opening
        // Compensate for a scaled bucket transform so the heap keeps its size in metres.
        Vector3 ls = b.lossyScale;
        lumpScale = 1F / Mathf.Max(0.01F, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3F);
    }

    // ---- Soil clods: a pile of irregular lumps of earth in the bucket (like real excavated soil) ----
    // Built once per trial: ClodCount lumps at fixed spots in a dome over the bucket's hollow (lowest first),
    // each a randomly dented sphere in one of three soil shades. As the bucket fills, more lumps appear,
    // so the pile grows from the bottom up. They are children of the bucket, so they move with it.
    const int ClodCount = 30;
    readonly System.Collections.Generic.List<GameObject> clods = new System.Collections.Generic.List<GameObject>();
    int shownClods = -1;

    void BuildClods(Excavator m)
    {
        var holder = new GameObject("SoilClods").transform;
        holder.SetParent(lump.transform.parent, false);
        holder.localPosition = lump.transform.localPosition;
        holder.localRotation = lump.transform.localRotation;
        holder.localScale = Vector3.one * lumpScale;

        var rnd = new System.Random(4242);
        float R() => (float)rnd.NextDouble();
        Mesh[] shapes = { ClodMesh(1), ClodMesh(2), ClodMesh(3), ClodMesh(4) };
        Material[] mats = { ClodMaterial(0.40F, 0.30F, 0.20F), ClodMaterial(0.33F, 0.25F, 0.17F), ClodMaterial(0.46F, 0.37F, 0.27F) };

        // Spots in a dome (local y = out of the bucket opening), sorted bottom-up.
        var spots = new System.Collections.Generic.List<(Vector3 p, float size)>();
        for (int i = 0; i < ClodCount; i++)
        {
            float a = R() * Mathf.PI * 2F, rad = Mathf.Sqrt(R()) * 0.36F;
            float dome = 0.24F * (1F - (rad / 0.42F) * (rad / 0.42F));
            Vector3 p = new Vector3(Mathf.Cos(a) * rad, dome * Mathf.Lerp(0.35F, 1F, R()), Mathf.Sin(a) * rad * 0.85F);
            spots.Add((p, Mathf.Lerp(0.09F, 0.17F, R())));
        }
        spots.Sort((x, y) => x.p.y.CompareTo(y.p.y));

        foreach (var (p, size) in spots)
        {
            var go = new GameObject("Clod");
            go.transform.SetParent(holder, false);
            go.transform.localPosition = p;
            go.transform.localRotation = Quaternion.Euler(R() * 360F, R() * 360F, R() * 360F);
            go.transform.localScale = new Vector3(size, size * Mathf.Lerp(0.75F, 1F, R()), size * Mathf.Lerp(0.85F, 1.1F, R()));
            go.AddComponent<MeshFilter>().sharedMesh = shapes[rnd.Next(shapes.Length)];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mats[rnd.Next(mats.Length)];
            go.SetActive(false);
            clods.Add(go);
        }
    }

    void ShowClods(float fill)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(fill * ClodCount), 0, ClodCount);
        if (n == shownClods) return;
        shownClods = n;
        for (int i = 0; i < clods.Count; i++) if (clods[i] != null && clods[i].activeSelf != (i < n)) clods[i].SetActive(i < n);
    }

    static readonly System.Collections.Generic.Dictionary<int, Mesh> clodMeshes = new System.Collections.Generic.Dictionary<int, Mesh>();

    // A lumpy rock-like ball: Unity's sphere with every vertex pushed in or out by smooth noise (based on the
    // vertex direction, so the seams of the sphere stay closed).
    static Mesh ClodMesh(int seed)
    {
        if (clodMeshes.TryGetValue(seed, out var cached) && cached != null) return cached;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh src = tmp.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.Destroy(tmp);
        var v = src.vertices;
        float ox = seed * 3.7F, oy = seed * 5.3F;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 d = v[i].normalized;
            float n = Mathf.PerlinNoise(d.x * 1.6F + ox, d.y * 1.6F + d.z * 0.9F + oy) - 0.5F;
            float n2 = Mathf.PerlinNoise(d.z * 4F + oy, d.x * 4F + d.y * 2F + ox) - 0.5F;
            v[i] = d * 0.5F * (1F + n * 0.55F + n2 * 0.18F);
        }
        var mesh = new Mesh { name = "Clod" + seed };
        mesh.vertices = v;
        mesh.triangles = src.triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        clodMeshes[seed] = mesh;
        return mesh;
    }

    static readonly System.Collections.Generic.Dictionary<Color, Material> clodMats = new System.Collections.Generic.Dictionary<Color, Material>();
    static Material ClodMaterial(float r, float g, float b)
    {
        var c = new Color(r, g, b);
        if (clodMats.TryGetValue(c, out var m) && m != null) return m;
        m = WorldBuilder.MakeMaterial(c);
        clodMats[c] = m;
        return m;
    }

    void Dump(Vector3 at)
    {
        LastDumpAmount = Fill;
        IsLoaded = false;
        Fill = 0F;
        lump.SetActive(false);
        ShowClods(0F);

        // Find what is below the bucket (truck bed or ground), ignoring the excavator itself.
        Vector3 landing = WorldBuilder.OnGround(at);
        foreach (var hit in SortedHits(at))
        {
            if (hit.collider.transform.IsChildOf(machine.transform)) continue;
            landing = hit.point;
            break;
        }

        var fall = new GameObject("FallingSoil").AddComponent<SoilFall>();
        fall.transform.SetParent(transform, true);
        fall.Begin(at, landing, LastDumpAmount, transform);

        Dumped?.Invoke(at);
    }

    static RaycastHit[] SortedHits(Vector3 from)
    {
        var hits = Physics.RaycastAll(from, Vector3.down, 30F);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    void OnDestroy()
    {
        if (lump != null) Destroy(lump);
    }
}

// A lump of soil falling from the bucket. When it lands it becomes (or adds to) a heap.
public class SoilFall : MonoBehaviour
{
    Vector3 target;
    float amount, speed;
    Transform heapParent;
    GameObject chunk;

    public void Begin(Vector3 from, Vector3 to, float amt, Transform parent)
    {
        target = to; amount = amt; heapParent = parent;
        transform.position = from;
        chunk = new GameObject("Chunk");
        chunk.AddComponent<MeshFilter>().sharedMesh = SoilHeap.MoundMesh(11);
        chunk.AddComponent<MeshRenderer>().sharedMaterial = BucketLoad.SoilMaterial();
        chunk.transform.SetParent(transform, false);
        float s = Mathf.Lerp(0.25F, 0.5F, amt);
        chunk.transform.localScale = new Vector3(s, s * 0.8F, s);
    }

    void Update()
    {
        speed += 9.81F * Time.deltaTime;
        Vector3 p = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
        transform.position = p;
        if ((p - target).sqrMagnitude < 0.0004F)
        {
            SoilHeap.AddAt(target, amount, heapParent);
            Destroy(gameObject);
        }
    }
}

// A soil heap: a rough mound mesh that grows as more soil is dumped on the same spot.
public class SoilHeap : MonoBehaviour
{
    static readonly List<SoilHeap> heaps = new List<SoilHeap>();
    public float Volume { get; private set; }   // in bucket-loads

    public static void AddAt(Vector3 point, float amount, Transform parent)
    {
        heaps.RemoveAll(h => h == null);
        foreach (var h in heaps)
        {
            Vector3 d = h.transform.position - point; d.y *= 0.5F;
            if (d.magnitude < 0.6F + h.Radius * 0.6F) { h.Grow(amount); return; }
        }
        var go = new GameObject("SoilHeap");
        go.transform.SetParent(parent, true);
        go.transform.position = point;
        go.transform.rotation = Quaternion.Euler(0F, UnityEngine.Random.Range(0F, 360F), 0F);
        var heap = go.AddComponent<SoilHeap>();
        heap.Build();
        heap.Grow(amount);
        heaps.Add(heap);
    }

    float Radius => 0.7F * Mathf.Pow(Mathf.Max(Volume, 0.1F), 1F / 3F) * 1.2F;

    void Grow(float amount)
    {
        Volume += Mathf.Max(amount, 0.1F);
        // Width grows with the cube root of the volume, so a heap gets wider and taller together.
        float r = Radius;
        transform.localScale = new Vector3(r, r * 0.55F, r);
    }

    void Build()
    {
        gameObject.AddComponent<MeshFilter>().sharedMesh = MoundMesh(UnityEngine.Random.Range(0, int.MaxValue));   // a slightly different shape for each heap
        gameObject.AddComponent<MeshRenderer>().sharedMaterial = BucketLoad.SoilMaterial();
    }

    // A unit mound (radius 1, height 1) with a slightly uneven surface. Same seed = same shape.
    static readonly Dictionary<int, Mesh> mounds = new Dictionary<int, Mesh>();
    public static Mesh MoundMesh(int seed)
    {
        if (mounds.TryGetValue(seed, out var cached) && cached != null) return cached;
        const int rings = 6, segs = 18;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var rnd = new System.Random(seed);
        verts.Add(new Vector3(0, 1F, 0));                       // top
        for (int r = 1; r <= rings; r++)
        {
            float t = (float)r / rings;                          // 0 = top, 1 = outer edge
            for (int s = 0; s < segs; s++)
            {
                float a = s * Mathf.PI * 2F / segs;
                float bump = 1F + ((float)rnd.NextDouble() - 0.5F) * 0.18F;
                float rad = t * bump;
                float y = Mathf.Max(0F, Mathf.Cos(t * Mathf.PI * 0.5F)) * (1F + ((float)rnd.NextDouble() - 0.5F) * 0.1F);
                if (r == rings) y = 0F;
                verts.Add(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad));
            }
        }
        for (int s = 0; s < segs; s++) { tris.Add(0); tris.Add(1 + (s + 1) % segs); tris.Add(1 + s); }
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < segs; s++)
            {
                int a = 1 + r * segs + s, b = 1 + r * segs + (s + 1) % segs;
                int c = a + segs, d = b + segs;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (seed < 1000) mounds[seed] = mesh;   // keep the fixed shapes (bucket, falling chunk); heaps get their own
        return mesh;
    }

    void OnDestroy() { heaps.Remove(this); }
}
