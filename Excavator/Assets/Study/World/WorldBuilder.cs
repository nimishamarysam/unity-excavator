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

    // A route gate: two posts with a bar on top. The bar is 6 m up so it stays
    // above the driver's line of sight from the cab when driving through.
    public static void Gate(Transform parent, string name, Vector3 center, Vector3 direction, float width)
    {
        Vector3 side = Vector3.Cross(Vector3.up, direction.normalized) * (width / 2F);
        float yaw = Quaternion.LookRotation(direction).eulerAngles.y;
        var l = Shape(PrimitiveType.Cylinder, parent, name + "_L", center - side, new Vector3(0.25F, 3.1F, 0.25F), Orange, 0, true);
        var r = Shape(PrimitiveType.Cylinder, parent, name + "_R", center + side, new Vector3(0.25F, 3.1F, 0.25F), Orange, 0, true);
        AddHazard<ColliderHazard>(l, name + "_post_L", HazardKind.Obstacle, 1.5F, 0.15F);
        AddHazard<ColliderHazard>(r, name + "_post_R", HazardKind.Obstacle, 1.5F, 0.15F);
        Shape(PrimitiveType.Cube, parent, name + "_bar", center, new Vector3(width, 0.3F, 0.3F), White, yaw, false, 6F);
    }

    // A barrel obstacle.
    public static GameObject Barrel(Transform parent, string name, Vector3 pos)
    {
        var b = Shape(PrimitiveType.Cylinder, parent, name, pos, new Vector3(0.7F, 0.5F, 0.7F), Blue, 0, true);
        AddHazard<ColliderHazard>(b, name, HazardKind.Obstacle, 1.5F, 0.15F);
        return b;
    }

    // A site worker in high-visibility clothing. Returns the walker so the scenario can send it somewhere.
    // A site worker built from simple shapes: boots, trousers, hi-vis vest with a reflective stripe,
    // arms, hands, head and a hard hat. ~1.8 m tall. Legs and arms swing while walking (WorkerWalker).
    // One capsule trigger collider (does not push the machine) carries the Worker hazard.
    public static readonly Color Skin = new Color(0.86F, 0.67F, 0.52F);
    public static readonly Color Trousers = new Color(0.16F, 0.18F, 0.24F);
    public static readonly Color Boot = new Color(0.12F, 0.10F, 0.08F);

    public static WorkerWalker Worker(Transform parent, string name, Vector3 pos)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, true);
        root.transform.position = OnGround(pos);
        var col = root.AddComponent<CapsuleCollider>();
        col.isTrigger = true;                       // does not physically push the machine
        col.center = new Vector3(0, 0.9F, 0); col.height = 1.8F; col.radius = 0.3F;

        Transform t = root.transform;
        // Legs (pivot at the hip) with boots.
        Transform legL = Pivot(t, "LegL", new Vector3(-0.11F, 0.92F, 0)), legR = Pivot(t, "LegR", new Vector3(0.11F, 0.92F, 0));
        foreach (var leg in new[] { legL, legR })
        {
            Part(leg, PrimitiveType.Capsule, new Vector3(0, -0.44F, 0), new Vector3(0.17F, 0.45F, 0.17F), Trousers);
            Part(leg, PrimitiveType.Cube, new Vector3(0, -0.87F, 0.05F), new Vector3(0.15F, 0.11F, 0.28F), Boot);
        }
        Part(t, PrimitiveType.Cube, new Vector3(0, 0.95F, 0), new Vector3(0.38F, 0.16F, 0.22F), Trousers);          // hips
        Part(t, PrimitiveType.Cube, new Vector3(0, 1.26F, 0), new Vector3(0.44F, 0.56F, 0.25F), HiVis);             // vest
        Part(t, PrimitiveType.Cube, new Vector3(0, 1.16F, 0), new Vector3(0.45F, 0.05F, 0.26F), new Color(0.85F, 0.85F, 0.85F)); // stripe
        Part(t, PrimitiveType.Cube, new Vector3(0, 1.33F, 0), new Vector3(0.45F, 0.05F, 0.26F), new Color(0.85F, 0.85F, 0.85F));
        // Arms (pivot at the shoulder) with hands.
        Transform armL = Pivot(t, "ArmL", new Vector3(-0.28F, 1.5F, 0)), armR = Pivot(t, "ArmR", new Vector3(0.28F, 1.5F, 0));
        foreach (var arm in new[] { armL, armR })
        {
            Part(arm, PrimitiveType.Capsule, new Vector3(0, -0.3F, 0), new Vector3(0.13F, 0.31F, 0.13F), HiVis);
            Part(arm, PrimitiveType.Sphere, new Vector3(0, -0.62F, 0), new Vector3(0.1F, 0.11F, 0.1F), Skin);
        }
        Part(t, PrimitiveType.Cylinder, new Vector3(0, 1.58F, 0), new Vector3(0.1F, 0.05F, 0.1F), Skin);             // neck
        Part(t, PrimitiveType.Sphere, new Vector3(0, 1.71F, 0), new Vector3(0.21F, 0.25F, 0.22F), Skin);            // head
        Part(t, PrimitiveType.Sphere, new Vector3(0, 1.8F, -0.01F), new Vector3(0.27F, 0.17F, 0.29F), White);       // hard hat
        Part(t, PrimitiveType.Cylinder, new Vector3(0, 1.77F, 0.02F), new Vector3(0.32F, 0.008F, 0.34F), White);    // brim

        AddHazard<ColliderHazard>(root, name, HazardKind.Worker, 5F, 0.4F);
        var w = root.AddComponent<WorkerWalker>();
        w.SetLimbs(legL, legR, armL, armR);
        return w;
    }

    static Transform Pivot(Transform parent, string name, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        return go.transform;
    }

    static GameObject Part(Transform parent, PrimitiveType type, Vector3 local, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color);
        return go;
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
    // Truck bed inside size (m). Was 5.0 x 2.5.
    // 2026-11-01: 4.5 x 2.6 m (was 6.0 x 3.2 m) - a smaller bed so the dump has to be aimed (Dig and dump).
    public const float TruckBedLength = 4.5F, TruckBedWidth = 2.6F;

    public static Vector3 Truck(Transform parent, string name, Vector3 center, Vector3 forward, out Bounds bedArea)
    {
        forward = new Vector3(forward.x, 0, forward.z).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        float yaw = Quaternion.LookRotation(forward).eulerAngles.y;
        float bedLen = TruckBedLength, bedW = TruckBedWidth, floor = 1.3F, wall = 1.1F;
        float bodyW = bedW - 0.1F;   // chassis and cab as wide as the bed

        var chassis = Shape(PrimitiveType.Cube, parent, name + "_chassis", center, new Vector3(bodyW, 0.9F, 8.5F), Grey, yaw, true, 0.75F);
        AddHazard<ColliderHazard>(chassis, name, HazardKind.Truck, 0.8F, 0.1F);   // small warning distance: dumping over the bed is normal
        var cab = Shape(PrimitiveType.Cube, parent, name + "_cab", center + forward * 3F, new Vector3(bodyW, 2.2F, 2F), Yellow, yaw, true, 2.3F);
        Vector3 bedC = center - forward * 1.5F;   // bed between the cab (front) and the rear end
        var bedFloor = Shape(PrimitiveType.Cube, parent, name + "_bedFloor", bedC, new Vector3(bedW, 0.15F, bedLen), Yellow, yaw, true, floor);
        var wallL = Shape(PrimitiveType.Cube, parent, name + "_wallL", bedC - side * (bedW / 2F), new Vector3(0.12F, wall, bedLen), Yellow, yaw, true, floor + wall / 2F);
        var wallR = Shape(PrimitiveType.Cube, parent, name + "_wallR", bedC + side * (bedW / 2F), new Vector3(0.12F, wall, bedLen), Yellow, yaw, true, floor + wall / 2F);
        var wallF = Shape(PrimitiveType.Cube, parent, name + "_wallF", bedC + forward * (bedLen / 2F), new Vector3(bedW, wall, 0.12F), Yellow, yaw, true, floor + wall / 2F);
        var wallB = Shape(PrimitiveType.Cube, parent, name + "_wallB", bedC - forward * (bedLen / 2F), new Vector3(bedW, wall, 0.12F), Yellow, yaw, true, floor + wall / 2F);
        // The bucket and arm may not pass through any part of the truck (see BucketBlocker).
        foreach (var part in new[] { chassis, cab, bedFloor, wallL, wallR, wallF, wallB })
            BucketBlocker.Solids.Add(part.GetComponent<Collider>());

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
    public float stride = 28F;          // leg swing in degrees while walking
    // 2026-11-01: a worker never walks INTO the machine. Closer than KeepAway (m, measured like the hazard: to the
    // nearest part of the machine) he walks AROUND it - the part of his step towards the machine is removed, and he
    // is pushed back out if the machine itself comes closer. Before, his route went straight through the excavator
    // (and that counted as the participant hitting him).
    public static float KeepAway = 0.8F;
    readonly Queue<(Vector3 pos, float wait)> route = new Queue<(Vector3, float)>();
    float waitUntil, phase, swing;
    Hazard hazard;
    Transform legL, legR, armL, armR;

    void Awake() { hazard = GetComponent<Hazard>(); }

    public void SetLimbs(Transform ll, Transform lr, Transform al, Transform ar) { legL = ll; legR = lr; armL = al; armR = ar; }

    public void Hide() { gameObject.SetActive(false); }

    public void WalkTo(Vector3 p, float waitThere = 0F) { route.Enqueue((p, waitThere)); }

    public void Appear(Vector3 at)
    {
        transform.position = WorldBuilder.OnGround(at);
        gameObject.SetActive(true);
        if (hazard != null) hazard.active = true;
    }

    void Update()
    {
        bool walking = false;
        if (route.Count > 0 && Time.time >= waitUntil)
        {
            var (target, wait) = route.Peek();
            Vector3 pos = transform.position;
            Vector3 flat = new Vector3(target.x - pos.x, 0, target.z - pos.z);
            float step = speed * Time.deltaTime;
            if (hazard != null && hazard.active && hazard.CurrentDistance < KeepAway && flat.magnitude > step)
            {
                // Next to the machine: slide along it instead of walking through it.
                Vector3 n = pos - hazard.MachinePoint; n.y = 0F;
                n = n.sqrMagnitude > 1e-4F ? n.normalized : -transform.forward;
                Vector3 dir = flat.normalized;
                float into = Vector3.Dot(dir, n);
                if (into < 0F) dir -= n * into;                                          // drop the part towards the machine
                if (hazard.CurrentDistance < KeepAway * 0.6F) dir += n * 0.8F;         // too close: step back out
                if (dir.sqrMagnitude < 1e-4F) dir = Vector3.Cross(Vector3.up, n);       // straight at it: go sideways
                flat = dir.normalized * Mathf.Max(flat.magnitude, step * 2F);
            }
            if (flat.magnitude <= step)
            {
                pos = new Vector3(target.x, pos.y, target.z);
                route.Dequeue();
                waitUntil = Time.time + wait;
            }
            else
            {
                pos += flat.normalized * step;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 360F * Time.deltaTime);
                walking = step > 0F;
            }
            transform.position = WorldBuilder.OnGround(pos);
        }
        Animate(walking);
    }

    // Legs and arms swing in opposite directions while walking, and settle when standing.
    void Animate(bool walking)
    {
        if (legL == null) return;
        swing = Mathf.MoveTowards(swing, walking ? 1F : 0F, Time.deltaTime * 4F);
        if (walking) phase += Time.deltaTime * speed * 5.5F;
        float a = Mathf.Sin(phase) * stride * swing;
        legL.localRotation = Quaternion.Euler(a, 0, 0);
        legR.localRotation = Quaternion.Euler(-a, 0, 0);
        armL.localRotation = Quaternion.Euler(-a * 0.8F, 0, 4F);
        armR.localRotation = Quaternion.Euler(a * 0.8F, 0, -4F);
    }
}
