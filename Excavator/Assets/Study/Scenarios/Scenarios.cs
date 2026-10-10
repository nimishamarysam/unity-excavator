using UnityEngine;

// ---------------------------------------------------------------------------
// THE FOUR SCENARIOS
// All positions are on the flat plain between the river (north) and the hills
// (south). The machine always faces east (+x) at the start.
// Random jitter comes from the trial's seed, so a trial can be reproduced.
// ---------------------------------------------------------------------------

// PRACTICE: free driving + a dig square and a dump square to learn the controls. No hazards.
public class PracticeScenario : Scenario
{
    BucketLoad load;
    Vector3 spoil, f;
    int dumps;

    public override string TaskText => "Practice: try driving, digging and dumping";
    public override Vector3? DumpTarget => spoil;
    public override string ProgressText => $"Dig in the brown square, dump on the white square.  Dumps: {dumps}";

    protected override void Build()
    {
        f = Vector3.right;
        PlaceMachine(-16.5F, -7.5F, f);
        float reach = M.MaxReach;
        M.EnableCuttingEdges(true);   // lets the bucket go into the ground

        Vector3 pit = Rel(0.6F * reach, 0);
        TerrainDigger.Instance.SetZone(pit, f, 3F, 3F, 1.5F);
        Outline("dig", pit, f, 3F, 3F, BucketLoad.SoilMaterial().color);

        spoil = Rel(0, 0.6F * reach);
        Outline("spoil", spoil, f, 3.5F, 3.5F, WorldBuilder.White);

        load = gameObject.AddComponent<BucketLoad>();
        load.Init(M);
        load.Dumped += p => { dumps++; StudyLogger.Log("dump", (InRect(p, spoil, f, 3.5F, 3.5F, 0.8F) ? "spoil_area" : "outside") + $";amount={load.LastDumpAmount:F2}"); };
    }
}

// EASY - DRIVING: drive through 4 gates, then reverse to a stop line.
// Hazards: barrels near the route, a worker crossing the route, a worker walking behind while reversing.
public class DrivingScenario : Scenario
{
    readonly Vector3[] gates = { new Vector3(-14, 0, -4), new Vector3(0, 0, 3), new Vector3(14, 0, -3), new Vector3(30, 0, 0) };
    Vector3 stopLine = new Vector3(23, 0, 0);
    int nextGate;
    bool reversing;
    float stillTime;
    WorkerWalker crosser, behind;
    float crosserAt = -1F, behindAt = -1F;
    int contacts;

    public override string TaskText => reversing ? "Reverse (drive backwards) to the yellow stop line" : "Drive through the gates in order";
    public override string ProgressText => reversing ? "Stop with the machine centre on the line." : $"Next gate: {nextGate + 1} of {gates.Length}";

    protected override void Build()
    {
        PlaceMachine(-32F, -2F, Vector3.right);

        for (int i = 0; i < gates.Length; i++)
        {
            Vector3 from = i == 0 ? new Vector3(-32, 0, -2) : gates[i - 1];
            WorldBuilder.Gate(Root, "Gate" + (i + 1), WorldBuilder.OnGround(gates[i]), gates[i] - from, 7F);
        }
        WorldBuilder.GroundMark(Root, "StopLine", WorldBuilder.OnGround(stopLine), 0.5F, 6F, 90F, WorldBuilder.Yellow);

        // Barrels close to the ideal line (small random shift per trial).
        WorldBuilder.Barrel(Root, "Barrel1", WorldBuilder.OnGround(new Vector3(-6.5F + Rand(-1, 1), 0, 2.0F + Rand(-0.3F, 0.3F))));
        WorldBuilder.Barrel(Root, "Barrel2", WorldBuilder.OnGround(new Vector3(8F + Rand(-1, 1), 0, 2.4F + Rand(-0.3F, 0.3F))));
        WorldBuilder.Barrel(Root, "Barrel3", WorldBuilder.OnGround(new Vector3(21F + Rand(-1, 1), 0, -4.0F + Rand(-0.3F, 0.3F))));

        // Workers are created now but stay hidden until their moment.
        crosser = WorldBuilder.Worker(Root, "WorkerCrossing", new Vector3(7, 0, -13));
        crosser.Hide();
        behind = WorldBuilder.Worker(Root, "WorkerBehind", new Vector3(24.5F, 0, 9));
        behind.Hide();
    }

    protected override void Tick()
    {
        Vector3 c = M.SwingCenter;
        float t = SM.TrialTime;

        if (!reversing)
        {
            Vector3 g = gates[nextGate];
            if (new Vector2(c.x - g.x, c.z - g.z).magnitude < 3.5F)
            {
                StudyLogger.Log("gate_passed", (nextGate + 1).ToString());
                if (nextGate == 0) crosserAt = t + Rand(0.5F, 3.5F);
                nextGate++;
                if (nextGate >= gates.Length)
                {
                    reversing = true;
                    behindAt = t + Rand(1.5F, 3.5F);
                    StudyLogger.Log("stage", "reverse");
                }
            }
        }

        // Worker 1 walks across the route between gate 2 and gate 3.
        if (crosserAt > 0 && t >= crosserAt)
        {
            crosserAt = -1F;
            bool fromNorth = Rng.NextDouble() < 0.5;
            float x = 7F + Rand(-1.5F, 1.5F);
            crosser.Appear(new Vector3(x, 0, fromNorth ? 13 : -13));
            crosser.WalkTo(new Vector3(x, 0, 0), 3F);
            crosser.WalkTo(new Vector3(x, 0, fromNorth ? -13 : 13));
            StudyLogger.Log("event_start", "worker_crossing");
        }

        // Worker 2 walks behind the machine while it reverses, and stops in the path for 4 s.
        if (behindAt > 0 && t >= behindAt)
        {
            behindAt = -1F;
            float x = 24.5F + Rand(-1F, 1F);
            behind.Appear(new Vector3(x, 0, 10));
            behind.WalkTo(new Vector3(x, 0, 0), 4F);
            behind.WalkTo(new Vector3(x, 0, -10));
            StudyLogger.Log("event_start", "worker_behind");
        }

        if (reversing)
        {
            bool onLine = Mathf.Abs(c.x - stopLine.x) < 1.5F && Mathf.Abs(c.z - stopLine.z) < 3F;
            stillTime = onLine && M.SpeedKmh < 1F ? stillTime + Time.fixedDeltaTime : 0F;
            if (stillTime > 1F) Complete("completed");
        }
    }

    public override string Summary() => $"gates={nextGate};reached_stop={(stillTime > 1F ? 1 : 0)}";
}

// MODERATE - DIGGING: dig a trench to 1.2 m, dump spoil on the white square.
// Hazards: over-digging, a buried cable next to the trench, driving too close to the trench edge.
public class DiggingScenario : Scenario
{
    const float TargetDepth = 1.2F;
    BucketLoad load;
    Vector3 trench, spoil, f;
    float trenchLen, maxDepth;
    int spoilDumps, spills, overDigs;
    bool overDigArmed = true;

    public override string TaskText => $"Dig the trench between the stakes to {TargetDepth:0.0} m deep";
    public override Vector3? DumpTarget => spoil;
    public override string ProgressText =>
        $"Trench done: {TerrainDigger.Instance.FractionDugTo(TargetDepth) * 100F:0}%   Deepest: {maxDepth:0.00} m   " +
        "Dump soil on the white square. Do not dig near the orange cable markers.";

    protected override void Build()
    {
        f = Vector3.right;
        PlaceMachine(-16.5F, -7.5F, f);
        M.EnableCuttingEdges(true);
        float reach = M.MaxReach;

        trenchLen = 0.43F * reach;
        trench = Rel(0.635F * reach, 0);
        TerrainDigger.Instance.SetZone(trench, f, trenchLen, 2F, TargetDepth + 0.6F);
        Outline("trench", trench, f, trenchLen, 2F, BucketLoad.SoilMaterial().color);

        // Buried cable: parallel to the trench, 0.5-0.8 m outside its edge, 0.9 m deep. Side is random.
        float side = Rng.NextDouble() < 0.5 ? 1F : -1F;
        float offset = side * (1F + Rand(0.5F, 0.8F));
        Vector3 r = Vector3.Cross(Vector3.up, f);
        Vector3 a = trench - f * (trenchLen / 2F + 2F) + r * offset, b = trench + f * (trenchLen / 2F + 2F) + r * offset;
        var cable = new GameObject("BuriedCable").AddComponent<SegmentHazard>();
        cable.transform.SetParent(Root);
        cable.a = WorldBuilder.OnGround(a) + Vector3.down * 0.9F;
        cable.b = WorldBuilder.OnGround(b) + Vector3.down * 0.9F;
        cable.hazardId = "buried_cable"; cable.kind = HazardKind.BuriedCable;
        cable.measure = HazardMeasure.BucketTeethOnly; cable.warnDistance = 0.7F; cable.contactDistance = 0.3F;
        for (float s = 0; s <= 1.001F; s += 0.25F)
            WorldBuilder.Shape(PrimitiveType.Cylinder, Root, "CableMarker", Vector3.Lerp(a, b, s), new Vector3(0.1F, 0.5F, 0.1F), WorldBuilder.Orange, 0, false);
        WorldBuilder.GroundMark(Root, "CableLine", WorldBuilder.OnGround((a + b) / 2F), (b - a).magnitude, 0.1F, 90F, WorldBuilder.Orange);

        // Trench edge: warns when the TRACKS get close to the trench.
        var edge = new GameObject("TrenchEdge").AddComponent<RectEdgeHazard>();
        edge.transform.SetParent(Root);
        edge.center = trench; edge.axisU = f; edge.halfU = trenchLen / 2F; edge.halfV = 1F;
        edge.hazardId = "trench_edge"; edge.kind = HazardKind.TrenchEdge; edge.measure = HazardMeasure.TracksOnly;
        edge.warnDistance = 1.5F; edge.contactDistance = 0.3F;

        // Spoil square on the side away from the cable.
        spoil = Rel(0.1F * reach, -side * 0.6F * reach);
        Outline("spoil", spoil, f, 3.5F, 3.5F, WorldBuilder.White);

        load = gameObject.AddComponent<BucketLoad>();
        load.Init(M);
        load.Dumped += p =>
        {
            if (InRect(p, spoil, f, 3.5F, 3.5F, 0.8F)) { spoilDumps++; StudyLogger.Log("dump", $"spoil_area;amount={load.LastDumpAmount:F2}"); }
            else { spills++; StudyLogger.Log("spill", $"outside_spoil_area;amount={load.LastDumpAmount:F2}"); }
        };
    }

    protected override void Tick()
    {
        Vector3 tip = M.BucketTipPosition;
        float depth = TerrainDigger.ReferenceGroundHeight(tip) - tip.y;
        if (TerrainDigger.Instance.Contains(tip))
        {
            maxDepth = Mathf.Max(maxDepth, depth);
            if (overDigArmed && depth > TargetDepth + 0.35F) { overDigs++; overDigArmed = false; StudyLogger.Log("over_dig", $"depth={depth:F2}"); }
        }
        if (depth < 0F) overDigArmed = true;

        if (TerrainDigger.Instance.FractionDugTo(TargetDepth) >= 0.75F) Complete("completed");
    }

    public override string Summary() =>
        $"dug={TerrainDigger.Instance.FractionDugTo(TargetDepth):F2};scoops={load.Scoops};spoil_dumps={spoilDumps};spills={spills};over_digs={overDigs};max_depth={maxDepth:F2}";
}

// HARD - DIG AND DUMP: load the truck 6 times before time runs out.
// Hazards: a worker walking into the swing area (twice), an overhead power line above the truck, spills.
public class DigDumpScenario : Scenario
{
    const int Goal = 6;
    BucketLoad load;
    Vector3 f, left, bedCenter;
    int loads, spills;
    WorkerWalker worker;
    float[] workerTimes;
    int workerEpisode;
    float reach;

    public override string TaskText => $"Load the truck {Goal} times";
    public override Vector3? DumpTarget => bedCenter;
    public override string ProgressText
    {
        get
        {
            float remaining = Mathf.Max(0F, SM.trialDurationSeconds - SM.TrialTime);
            return $"Loads: {loads} / {Goal}    Time left: {Mathf.FloorToInt(remaining / 60)}:{Mathf.FloorToInt(remaining % 60):00}    Watch for workers and the power line.";
        }
    }

    protected override void Build()
    {
        f = Vector3.right;
        left = -Vector3.Cross(Vector3.up, f);
        PlaceMachine(-16.5F, -7.5F, f);
        M.EnableCuttingEdges(true);
        reach = M.MaxReach;

        Vector3 pit = Rel(0.6F * reach, 0);
        TerrainDigger.Instance.SetZone(pit, f, 3F, 3F, 2F);
        Outline("pit", pit, f, 3F, 3F, BucketLoad.SoilMaterial().color);

        // Truck to the left (90 degree swing), bed level with the swing centre.
        Vector3 truckCenter = Rel(1F, -0.62F * reach);
        bedCenter = WorldBuilder.Truck(Root, "Truck", truckCenter, f, out _);

        // Power line just beyond the truck, 7.5 m high.
        Vector3 lineMid = Rel(0F, -(0.62F * reach + 2.5F));
        WorldBuilder.PowerLine(Root, "PowerLine", lineMid - f * 14F, lineMid + f * 14F, 7.5F);

        worker = WorldBuilder.Worker(Root, "Worker", Rel(reach + 4F, -(reach + 4F)));
        worker.Hide();
        workerTimes = new[] { Rand(35F, 70F), Rand(110F, 160F) };

        load = gameObject.AddComponent<BucketLoad>();
        load.Init(M);
        load.Dumped += p =>
        {
            if (InRect(p, bedCenter, f, 5F, 2.5F, 0.3F)) { loads++; StudyLogger.Log("truck_load", $"{loads};amount={load.LastDumpAmount:F2}"); if (loads >= Goal) Complete("completed"); }
            else { spills++; StudyLogger.Log("spill", $"missed_truck;amount={load.LastDumpAmount:F2}"); }
        };
    }

    protected override void Tick()
    {
        if (workerEpisode < workerTimes.Length && SM.TrialTime >= workerTimes[workerEpisode])
        {
            // Worker walks into the swing path between the pit and the truck, waits, and walks out again.
            Vector3 diag = (f + left).normalized;
            Vector3 start = workerEpisode == 0 ? WorldBuilder.OnGround(M.SwingCenter + diag * (reach + 4F)) : Rel(9F, -0.62F * reach);
            Vector3 inPath = WorldBuilder.OnGround(M.SwingCenter + diag * (0.55F * reach));
            worker.Appear(start);
            worker.WalkTo(inPath, 5F);
            worker.WalkTo(start);
            StudyLogger.Log("event_start", "worker_in_swing_area_" + (workerEpisode + 1));
            workerEpisode++;
        }
    }

    public override string Summary() => $"loads={loads};spills={spills};scoops={load.Scoops}";
}
