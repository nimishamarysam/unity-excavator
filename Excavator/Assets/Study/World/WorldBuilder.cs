using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// WORLD BUILDER
// Small helpers that create the scenario objects from simple shapes
// (cubes, cylinders, capsules), placed on the ground. Everything is made a
// child of one "Scenario" object, so a scene reload removes it all.
// ---------------------------------------------------------------------------

public static class WorldBuilder
{
    static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();

    public static readonly Color Yellow = new Color(1F, 0.82F, 0.1F);
    public static readonly Color Orange = new Color(1F, 0.45F, 0.05F);
    public static readonly Color HiVis = new Color(0.85F, 1F, 0.1F);
    public static readonly Color White = new Color(0.95F, 0.95F, 0.95F);
    public static readonly Color Grey = new Color(0.45F, 0.45F, 0.47F);
    public static readonly Color Blue = new Color(0.15F, 0.35F, 0.75F);
    public static readonly Color Red = new Color(0.8F, 0.12F, 0.1F);

    public static Material MakeMaterial(Color c)
    {
        if (cache.TryGetValue(c, out var m) && m != null) return m;
        Shader s = Shader.Find("Standard");
        if (s == null) s = Shader.Find("Unlit/Color");
        m = new Material(s) { color = c };
        cache[c] = m;
        return m;
    }

    public static float Ground(float x, float z) => TerrainDigger.ReferenceGroundHeight(new Vector3(x, 0, z));
    public static Vector3 OnGround(Vector3 p, float lift = 0F) { p.y = Ground(p.x, p.z) + lift; return p; }

    public static GameObject Shape(PrimitiveType type, Transform parent, string name, Vector3 groundPos, Vector3 size,
                                   Color color, float yaw = 0F, bool solid = true, float lift = -1F)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.localScale = size;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        // Cylinders/capsules are 2 units tall at scale 1, cubes 1 unit: put the bottom on the ground.
        float halfHeight = (type == PrimitiveType.Cylinder || type == PrimitiveType.Capsule) ? size.y : size.y / 2F;
        go.transform.position = OnGround(groundPos, lift >= 0F ? lift : halfHeight);
        go.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color);
        var col = go.GetComponent<Collider>();
        if (!solid) Object.Destroy(col);
        return go;
    }

    // A flat painted marking on the ground (no collider).
    public static GameObject GroundMark(Transform parent, string name, Vector3 center, float length, float width, float yaw, Color color)
    {
        return Shape(PrimitiveType.Cube, parent, name, center, new Vector3(width, 0.04F, length), color, yaw, false, 0.03F);
    }

    // A route gate: two striped posts with a bar on top.
    public static void Gate(Transform parent, string name, Vector3 center, Vector3 direction, float width)
    {
        Vector3 side = Vector3.Cross(Vector3.up, direction.normalized) * (width / 2F);
        float yaw = Quaternion.LookRotation(direction).eulerAngles.y;
        var l = Shape(PrimitiveType.Cylinder, parent, name + "_L", center - side, new Vector3(0.25F, 1.2F, 0.25F), Orange, 0, true);
        var r = Shape(PrimitiveType.Cylinder, parent, name + "_R", center + side, new Vector3(0.25F, 1.2F, 0.25F), Orange, 0, true);
        AddHazard<ColliderHazard>(l, name + "_post_L", HazardKind.Obstacle, 1.5F, 0.15F);
        AddHazard<ColliderHazard>(r, name + "_post_R", HazardKind.Obstacle, 1.5F, 0.15F);
        Shape(PrimitiveType.Cube, parent, name + "_bar", center, new Vector3(width, 0.25F, 0.25F), White, yaw, false, 2.5F);
    }

    // A barrel obstacle.
    public static GameObject Barrel(Transform parent, string name, Vector3 pos)
    {
        var b = Shape(PrimitiveType.Cylinder, parent, name, pos, new Vector3(0.7F, 0.5F, 0.7F), Blue, 0, true);
        AddHazard<ColliderHazard>(b, name, HazardKind.Obstacle, 1.5F, 0.15F);
        return b;
    }

    // A site worker in high-visibility clothing. Returns the walker so the scenario can send it somewhere.
    public static WorkerWalker Worker(Transform parent, string name, Vector3 pos)
    {
        var body = Shape(PrimitiveType.Capsule, parent, name, pos, new Vector3(0.6F, 0.9F, 0.6F), HiVis, 0, true);
        body.GetComponent<Collider>().isTrigger = true;   // does not physically push the machine
        var helmet = Shape(PrimitiveType.Sphere, body.transform, name + "_helmet", pos, new Vector3(0.35F, 0.25F, 0.35F), White, 0, false, 1.85F);
        helmet.transform.localScale = new Vector3(0.55F, 0.3F, 0.55F);
        AddHazard<ColliderHazard>(body, name, HazardKind.Worker, 5F, 0.4F);
        return body.AddComponent<WorkerWalker>();
    }

    // Overhead power line between two poles.
    public static SegmentHazard PowerLine(Transform parent, string name, Vector3 a, Vector3 b, float height)
    {
        Shape(PrimitiveType.Cylinder, parent, name + "_poleA", a, new Vector3(0.35F, height / 2F, 0.35F), Grey, 0, true);
        Shape(PrimitiveType.Cylinder, parent, name + "_poleB", b, new Vector3(0.35F, height / 2F, 0.35F), Grey, 0, true);
        Vector3 pa = OnGround(a, height - 0.3F), pb = OnGround(b, height - 0.3F);
        var wire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wire.name = name + "_wire";
        wire.transform.SetParent(parent, true);
        Object.Destroy(wire.GetComponent<Collider>());
        wire.transform.position = (pa + pb) / 2F;
        wire.transform.up = (pb - pa).normalized;
        wire.transform.localScale = new Vector3(0.06F, (pb - pa).magnitude / 2F, 0.06F);
        wire.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.1F, 0.1F, 0.1F));
        var h = wire.AddComponent<SegmentHazard>();
        h.a = pa; h.b = pb; h.hazardId = name; h.kind = HazardKind.PowerLine;
        h.warnDistance = 3F; h.contactDistance = 1F;
        return h;
    }

    // A dump truck seen from the side: cab, chassis and an open box bed. Returns the bed's inside centre (at floor level).
    public static Vector3 Truck(Transform parent, string name, Vector3 center, Vector3 forward, out Bounds bedArea)
    {
        forward = new Vector3(forward.x, 0, forward.z).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        float yaw = Quaternion.LookRotation(forward).eulerAngles.y;
        float bedLen = 5F, bedW = 2.5F, floor = 1.3F, wall = 1.1F;

        var chassis = Shape(PrimitiveType.Cube, parent, name + "_chassis", center, new Vector3(2.4F, 0.9F, 8F), Grey, yaw, true, 0.75F);
        AddHazard<ColliderHazard>(chassis, name, HazardKind.Truck, 0.8F, 0.1F);   // small warning distance: dumping over the bed is normal
        Shape(PrimitiveType.Cube, parent, name + "_cab", center + forward * 3F, new Vector3(2.4F, 2.2F, 2F), Yellow, yaw, true, 2.3F);
        Vector3 bedC = center - forward * 1F;
        Shape(PrimitiveType.Cube, parent, name + "_bedFloor", bedC, new Vector3(bedW, 0.15F, bedLen), Yellow, yaw, true, floor);
        Shape(PrimitiveType.Cube, parent, name + "_wallL", bedC - side * (bedW / 2F), new Vector3(0.12F, wall, bedLen), Yellow, yaw, true, floor + wall / 2F);
        Shape(PrimitiveType.Cube, parent, name + "_wallR", bedC + side * (bedW / 2F), new Vector3(0.12F, wall, bedLen), Yellow, yaw, true, floor + wall / 2F);
        Shape(PrimitiveType.Cube, parent, name + "_wallF", bedC + forward * (bedLen / 2F), new Vector3(bedW, wall, 0.12F), Yellow, yaw, true, floor + wall / 2F);
        Shape(PrimitiveType.Cube, parent, name + "_wallB", bedC - forward * (bedLen / 2F), new Vector3(bedW, wall, 0.12F), Yellow, yaw, true, floor + wall / 2F);

        Vector3 inside = OnGround(bedC, floor);
        bedArea = new Bounds(inside + Vector3.up * 2F, new Vector3(Mathf.Abs(side.x) * bedW + Mathf.Abs(forward.x) * bedLen,
                                                                   6F, Mathf.Abs(side.z) * bedW + Mathf.Abs(forward.z) * bedLen));
        return inside;
    }

    public static T AddHazard<T>(GameObject go, string id, HazardKind kind, float warn, float contact) where T : Hazard
    {
        var h = go.AddComponent<T>();
        h.hazardId = id; h.kind = kind; h.warnDistance = warn; h.contactDistance = contact;
        return h;
    }
}

// Moves a worker along waypoints at walking speed, waiting at each point if asked.
public class WorkerWalker : MonoBehaviour
{
    public float speed = 1.3F;
    readonly Queue<(Vector3 pos, float wait)> route = new Queue<(Vector3, float)>();
    float waitUntil;
    Hazard hazard;

    void Awake() { hazard = GetComponent<Hazard>(); }

    public void Hide() { gameObject.SetActive(false); }

    public void WalkTo(Vector3 p, float waitThere = 0F) { route.Enqueue((p, waitThere)); }

    public void Appear(Vector3 at)
    {
        transform.position = WorldBuilder.OnGround(at, transform.localScale.y);
        gameObject.SetActive(true);
        if (hazard != null) hazard.active = true;
    }

    void Update()
    {
        if (route.Count == 0 || Time.time < waitUntil) return;
        var (target, wait) = route.Peek();
        Vector3 pos = transform.position;
        Vector3 flat = new Vector3(target.x - pos.x, 0, target.z - pos.z);
        float step = speed * Time.deltaTime;
        if (flat.magnitude <= step)
        {
            pos = new Vector3(target.x, pos.y, target.z);
            route.Dequeue();
            waitUntil = Time.time + wait;
        }
        else
        {
            pos += flat.normalized * step;
            transform.rotation = Quaternion.LookRotation(flat);
        }
        transform.position = WorldBuilder.OnGround(pos, transform.localScale.y);
    }
}
