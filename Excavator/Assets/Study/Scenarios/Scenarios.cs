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
    public override bool DumpArea(out Vector3 c, out Vector3 fw, out float len, out float wid) { c = spoil; fw = f; len = wid = 3.5F; return true; }

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
// Layout (tuned to be easy to drive): gates 10 m wide (machine ~3.2 m), 15-16 m apart, and only a
// gentle zig-zag (max. ~16 degree heading change between gates), so there is room to line up for each gate.
// Hazards: barrels near the route, a worker crossing the route, a worker walking behind while reversing.
public class DrivingScenario : Scenario
{
    const float GateWidth = 10F;      // distance between the two posts (was 7 m)
    readonly Vector3[] gates = { new Vector3(-16, 0, -2.5F), new Vector3(0, 0, 1.5F), new Vector3(16, 0, -1.5F), new Vector3(31, 0, 0) };
    static readonly Vector3 start = new Vector3(-32, 0, -2);
    Vector3 stopLine = new Vector3(22, 0, 0);
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
        PlaceMachine(start.x, start.z, Vector3.right);

        for (int i = 0; i < gates.Length; i++)
        {
            Vector3 from = i == 0 ? start : gates[i - 1];
            WorldBuilder.Gate(Root, "Gate" + (i + 1), WorldBuilder.OnGround(gates[i]), gates[i] - from, GateWidth);
        }
        WorldBuilder.GroundMark(Root, "StopLine", WorldBuilder.OnGround(stopLine), 0.5F, 6F, 90F, WorldBuilder.Yellow);

        // Barrels beside the ideal line, about 2.7-3 m from it (small random shift per trial):
        // the machine passes them with ~1 m to spare if it stays on the line.
        WorldBuilder.Barrel(Root, "Barrel1", WorldBuilder.OnGround(new Vector3(-8F + Rand(-1, 1), 0, 2.2F + Rand(-0.3F, 0.3F))));
        WorldBuilder.Barrel(Root, "Barrel2", WorldBuilder.OnGround(new Vector3(6F + Rand(-1, 1), 0, 2.8F + Rand(-0.3F, 0.3F))));
        WorldBuilder.Barrel(Root, "Barrel3", WorldBuilder.OnGround(new Vector3(25.5F + Rand(-1, 1), 0, -3.6F + Rand(-0.3F, 0.3F))));

        // Workers are created now but stay hidden until their moment.
        crosser = WorldBuilder.Worker(Root, "WorkerCrossing", new Vector3(10.5F, 0, -13));
        crosser.Hide();
        behind = WorldBuilder.Worker(Root, "WorkerBehind", new Vector3(23.5F, 0, 9));
        behind.Hide();
    }

    protected override void Tick()
    {
        Vector3 c = M.SwingCenter;
        float t = SM.TrialTime;

        if (!reversing)
        {
            // A gate counts as passed when the machine centre crosses the line between the posts
            // (anywhere between them), not only near the middle - the gate is wider now.
            Vector3 g = gates[nextGate];
            Vector3 from = nextGate == 0 ? start : gates[nextGate - 1];
            Vector3 dir = g - from; dir.y = 0F; dir.Normalize();
            Vector3 rel = c - g; rel.y = 0F;
            float along = Vector3.Dot(rel, dir), across = Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir));
            if (along >= 0F && along < 4F && Mathf.Abs(across) < GateWidth / 2F)
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
            float x = 10.5F + Rand(-1.5F, 1.5F);   // between barrel 2 and gate 3
            crosser.Appear(new Vector3(x, 0, fromNorth ? 13 : -13));
            crosser.WalkTo(new Vector3(x, 0, 0), 3F);
            crosser.WalkTo(new Vector3(x, 0, fromNorth ? -13 : 13));
            StudyLogger.Log("event_start", "worker_crossing");
        }

        // Worker 2 walks behind the machine while it reverses, and stops in the path for 4 s.
        if (behindAt > 0 && t >= behindAt)
        {
            behindAt = -1F;
            float x = 23.5F + Rand(-1F, 1F);   // just in front of the stop line
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

    // Route for the C3 driving cue: for every gate still to pass, a point 4 m before it, the gate
    // itself and a point 2 m after it (so the path goes straight through the gate). When reversing: the stop line.
    public override System.Collections.Generic.List<Vector3> RouteAhead(out bool reverse)
    {
        reverse = reversing;
        var list = new System.Collections.Generic.List<Vector3>();
        if (reversing) { list.Add(WorldBuilder.OnGround(stopLine)); return list; }
        for (int i = nextGate; i < gates.Length; i++)
        {
            Vector3 from = i == 0 ? start : gates[i - 1];
            Vector3 d = gates[i] - from; d.y = 0F; d.Normalize();
            list.Add(WorldBuilder.OnGround(gates[i] - d * 4F));
            list.Add(WorldBuilder.OnGround(gates[i]));
            list.Add(WorldBuilder.OnGround(gates[i] + d * 2F));
        }
        return list;
    }
}

// MODERATE - DIGGING: drive to the work position, dig a trench to 1.2 m, dump spoil on the white square.
// Task demands:
//   - Some driving: the machine starts 9 m behind the work position (yellow box) and must drive there first.
//   - Strict spoil placement: small spoil square (2.5 m) at least 5 m from the trench edge, about 120 degrees
//     swing from the trench, on the side away from the cable. Dumps outside it are spills; dumps within 5 m
//     of the trench edge are logged as "too_close_to_trench" spills.
//   - Buried cable parallel to the trench (0.5-0.8 m outside the edge, 0.9 m deep), marked with orange posts.
//   - Crossing service: a pipe crossing the trench 0.25 m BELOW the target depth (yellow posts on both sides).
//     Safe when digging to the target, struck when digging too deep at that spot.
//   - One worker event: at a random time a worker walks to the trench edge on the spoil side (inside the swing
//     path), stands there for a few seconds and walks away. Logged: bucket_over_worker, swing_toward_worker.
// Hazards also logged by the HazardTracker: over-digging, cable, pipe, trench edge (tracks), worker.
public class DiggingScenario : Scenario
{
    const float TargetDepth = 1.2F;
    const float SpoilSize = 2.5F;             // was 3.5 m
    const float SpoilMinGap = 5F;             // minimum distance between spoil square and trench edge
    const float SpoilSwingDeg = 120F;         // swing angle from the trench direction to the spoil square
    const float PipeBelowTarget = 0.25F;      // crossing pipe depth below the target depth
    const float WorkerStandSeconds = 6F;

    static readonly Vector3 workPos = new Vector3(-16.5F, 0, -7.5F);
    const float StartBehind = 9F;             // metres behind the work position at the start

    BucketLoad load;
    Vector3 trench, spoil, f, r;
    float trenchLen, maxDepth, side;
    int spoilDumps, spills, tooClose, overDigs, bucketOverWorker, swingTowardWorker, pipeStrikes, cableStrikes;
    bool overDigArmed = true, arrived;
    SegmentHazard cable, pipe;
    WorkerWalker worker;
    float workerAt, workerUntil = -1F;
    bool overLogged, swingLogged;
    float prevAngleToWorker = -1F;
    Hazard.Level pipeLevel, cableLevel;

    public override string TaskText => arrived ? $"Dig the trench between the stakes to {TargetDepth:0.0} m deep"
                                               : "Drive into the yellow box (press B for travel mode)";
    public override Vector3? DumpTarget => spoil;
    public override float? DigTargetDepth => TargetDepth;
    public override bool DumpArea(out Vector3 c, out Vector3 fw, out float len, out float wid) { c = spoil; fw = f; len = wid = SpoilSize; return true; }
    public override string ProgressText => arrived
        ? $"Trench done: {TerrainDigger.Instance.FractionDugTo(TargetDepth) * 100F:0}%   Deepest: {maxDepth:0.00} m   " +
          "Dump soil ONLY on the small white square. Avoid the orange cable and the yellow pipe markers."
        : "Then press B again for digging mode.";

    public override System.Collections.Generic.List<Vector3> RouteAhead(out bool reverse)
    {
        reverse = false;
        if (arrived) return null;
        return new System.Collections.Generic.List<Vector3> { WorldBuilder.OnGround(workPos) };
    }

    protected override void Build()
    {
        f = Vector3.right;
        r = Vector3.Cross(Vector3.up, f);

        // Lay out everything relative to the WORK position, then move the machine back to the start.
        PlaceMachine(workPos.x, workPos.z, f);
        M.EnableCuttingEdges(true);
        float reach = M.MaxReach;

        trenchLen = 0.43F * reach;
        trench = Rel(0.635F * reach, 0);
        TerrainDigger.Instance.SetZone(trench, f, trenchLen, 2F, TargetDepth + 0.6F);
        Outline("trench", trench, f, trenchLen, 2F, BucketLoad.SoilMaterial().color);

        // Work position box (where the machine should stand while digging).
        Outline("work", WorldBuilder.OnGround(workPos), f, 5.5F, 4.5F, WorldBuilder.Yellow);

        // Buried cable: parallel to the trench, 0.5-0.8 m outside its edge, 0.9 m deep. Side is random.
        side = Rng.NextDouble() < 0.5 ? 1F : -1F;
        float offset = side * (1F + Rand(0.5F, 0.8F));
        Vector3 a = trench - f * (trenchLen / 2F + 2F) + r * offset, b = trench + f * (trenchLen / 2F + 2F) + r * offset;
        cable = new GameObject("BuriedCable").AddComponent<SegmentHazard>();
        cable.transform.SetParent(Root);
        cable.a = WorldBuilder.OnGround(a) + Vector3.down * 0.9F;
        cable.b = WorldBuilder.OnGround(b) + Vector3.down * 0.9F;
        cable.hazardId = "buried_cable"; cable.kind = HazardKind.BuriedCable;
        cable.measure = HazardMeasure.BucketTeethOnly; cable.warnDistance = 0.7F; cable.contactDistance = 0.3F;
        for (float s = 0; s <= 1.001F; s += 0.25F)
            WorldBuilder.Shape(PrimitiveType.Cylinder, Root, "CableMarker", Vector3.Lerp(a, b, s), new Vector3(0.1F, 0.5F, 0.1F), WorldBuilder.Orange, 0, false);
        WorldBuilder.GroundMark(Root, "CableLine", WorldBuilder.OnGround((a + b) / 2F), (b - a).magnitude, 0.1F, 90F, WorldBuilder.Orange);

        // Crossing service: a pipe across the trench, 0.25 m below the target depth, in the middle part of the trench.
        Vector3 cross = trench + f * Rand(-0.2F, 0.2F) * trenchLen;
        Vector3 pa = cross - r * 2.5F, pb = cross + r * 2.5F;
        pipe = new GameObject("CrossingPipe").AddComponent<SegmentHazard>();
        pipe.transform.SetParent(Root);
        float pipeDepth = TargetDepth + PipeBelowTarget;
        pipe.a = new Vector3(pa.x, TerrainDigger.ReferenceGroundHeight(pa) - pipeDepth, pa.z);
        pipe.b = new Vector3(pb.x, TerrainDigger.ReferenceGroundHeight(pb) - pipeDepth, pb.z);
        pipe.hazardId = "crossing_pipe"; pipe.kind = HazardKind.BuriedPipe;
        pipe.measure = HazardMeasure.BucketTeethOnly; pipe.warnDistance = 0.2F; pipe.contactDistance = 0.08F;
        // Markers only OUTSIDE the trench (a mark across the hole would float in the air once dug).
        foreach (float sgn in new[] { -1F, 1F })
        {
            Vector3 post = cross + r * sgn * 1.9F;
            WorldBuilder.Shape(PrimitiveType.Cylinder, Root, "PipeMarker", post, new Vector3(0.12F, 0.55F, 0.12F), WorldBuilder.Yellow, 0, false);
            WorldBuilder.GroundMark(Root, "PipeLine", WorldBuilder.OnGround(cross + r * sgn * 1.75F), 1.5F, 0.12F, 0F, WorldBuilder.Yellow);
        }

        // Trench edge: warns when the TRACKS get close to the trench.
        var edge = new GameObject("TrenchEdge").AddComponent<RectEdgeHazard>();
        edge.transform.SetParent(Root);
        edge.center = trench; edge.axisU = f; edge.halfU = trenchLen / 2F; edge.halfV = 1F;
        edge.hazardId = "trench_edge"; edge.kind = HazardKind.TrenchEdge; edge.measure = HazardMeasure.TracksOnly;
        edge.warnDistance = 1.5F; edge.contactDistance = 0.3F;

        // Spoil square: ~120 degree swing towards the side away from the cable, at the shortest distance
        // that keeps it at least 5 m from the trench edge.
        float ang = SpoilSwingDeg * Mathf.Deg2Rad;
        Vector3 dir = f * Mathf.Cos(ang) + r * (-side) * Mathf.Sin(ang);
        Vector3 c0 = WorldBuilder.OnGround(workPos);
        spoil = c0 + dir * (0.85F * reach);
        bool ok = false;
        for (float d = 0.5F * reach; d <= 0.85F * reach + 1e-3F; d += 0.1F)
        {
            Vector3 p = WorldBuilder.OnGround(c0 + dir * d);
            if (GapToTrench(p, SpoilSize / 2F) >= SpoilMinGap) { spoil = p; ok = true; break; }
        }
        if (!ok) Debug.LogWarning($"[Study] Spoil square only {GapToTrench(spoil, SpoilSize / 2F):F1} m from the trench (reach {reach:F1} m)");
        Outline("spoil", spoil, f, SpoilSize, SpoilSize, WorldBuilder.White);
        StudyLogger.Log("layout", $"reach={reach:F2};spoil_gap={GapToTrench(spoil, SpoilSize / 2F):F2};spoil_dist={(spoil - c0).magnitude:F2};pipe_depth={pipeDepth:F2};cable_side={side}");

        // Worker: hidden until the event.
        Vector3 stand = trench + r * (-side) * 2F + f * Rand(-0.25F, 0.25F) * trenchLen;
        worker = WorldBuilder.Worker(Root, "WorkerAtTrench", stand + r * (-side) * 12F + f * 4F);
        worker.Hide();
        workerAt = Rand(45F, 110F);
        workerStand = stand;

        load = gameObject.AddComponent<BucketLoad>();
        load.Init(M);
        load.Dumped += p =>
        {
            if (InRect(p, spoil, f, SpoilSize, SpoilSize, 0.3F)) { spoilDumps++; StudyLogger.Log("dump", $"spoil_area;amount={load.LastDumpAmount:F2}"); }
            else if (GapToTrench(p, 0F) < SpoilMinGap) { spills++; tooClose++; StudyLogger.Log("spill", $"too_close_to_trench;gap={GapToTrench(p, 0F):F2};amount={load.LastDumpAmount:F2}"); }
            else { spills++; StudyLogger.Log("spill", $"outside_spoil_area;amount={load.LastDumpAmount:F2}"); }
        };

        // Start position: 9 m behind the work position, same heading.
        Vector3 start = workPos - f * StartBehind;
        PlaceMachine(start.x, start.z, f);
    }

    Vector3 workerStand;

    // Distance from a square (centre p, half size h, aligned with the trench) to the trench rectangle.
    float GapToTrench(Vector3 p, float h)
    {
        Vector3 d = p - trench;
        float du = Mathf.Abs(Vector3.Dot(d, f)) - (trenchLen / 2F + h);
        float dv = Mathf.Abs(Vector3.Dot(d, r)) - (1F + h);
        return new Vector2(Mathf.Max(0F, du), Mathf.Max(0F, dv)).magnitude;
    }

    protected override void Tick()
    {
        float t = SM.TrialTime;

        if (!arrived)
        {
            Vector3 c = M.SwingCenter;
            if (new Vector2(c.x - workPos.x, c.z - workPos.z).magnitude < 1.2F)
            {
                arrived = true;
                StudyLogger.Log("stage", "at_work_position");
            }
        }

        Vector3 tip = M.BucketTipPosition;
        float depth = TerrainDigger.ReferenceGroundHeight(tip) - tip.y;
        if (TerrainDigger.Instance.Contains(tip))
        {
            maxDepth = Mathf.Max(maxDepth, depth);
            if (overDigArmed && depth > TargetDepth + 0.35F) { overDigs++; overDigArmed = false; StudyLogger.Log("over_dig", $"depth={depth:F2}"); }
        }
        if (depth < 0F) overDigArmed = true;

        // Strikes (contact level reached) of the buried services.
        if (pipe.CurrentLevel == Hazard.Level.Contact && pipeLevel != Hazard.Level.Contact) pipeStrikes++;
        if (cable.CurrentLevel == Hazard.Level.Contact && cableLevel != Hazard.Level.Contact) cableStrikes++;
        pipeLevel = pipe.CurrentLevel; cableLevel = cable.CurrentLevel;

        WorkerEvent(t, tip);

        if (TerrainDigger.Instance.FractionDugTo(TargetDepth) >= 0.75F) Complete("completed");
    }

    // One worker walks to the trench edge (inside the swing path to the spoil square), waits, and walks away.
    void WorkerEvent(float t, Vector3 tip)
    {
        if (workerAt > 0F && t >= workerAt)
        {
            workerAt = -1F;
            Vector3 from = workerStand + r * (-side) * 12F + f * 4F;
            worker.Appear(from);
            worker.WalkTo(workerStand, WorkerStandSeconds);
            worker.WalkTo(from);
            workerUntil = t + 30F;
            StudyLogger.Log("event_start", "worker_at_trench");
        }
        if (workerUntil < 0F || t > workerUntil || !worker.gameObject.activeInHierarchy) return;

        Vector3 wp = worker.transform.position;
        Vector3 c = M.SwingCenter;

        // Bucket over the worker: teeth within 1.5 m horizontally and above them.
        bool over = new Vector2(tip.x - wp.x, tip.z - wp.z).magnitude < 1.5F && tip.y > wp.y - 0.5F;
        if (over && !overLogged) { bucketOverWorker++; overLogged = true; StudyLogger.Log("bucket_over_worker", $"dist_h={new Vector2(tip.x - wp.x, tip.z - wp.z).magnitude:F2}"); }
        if (!over && new Vector2(tip.x - wp.x, tip.z - wp.z).magnitude > 3F) overLogged = false;

        // Swinging towards the worker: swing input active, the boom turning towards them, worker within reach.
        Vector3 toW = wp - c; toW.y = 0F;
        Vector3 toTip = tip - c; toTip.y = 0F;
        float angle = Vector3.Angle(toTip, toW);
        bool inReach = toW.magnitude < M.MaxReach + 1F;
        if (prevAngleToWorker >= 0F && inReach && Ctl.LastInput.swing != 0F && angle < 60F && angle < prevAngleToWorker - 0.2F)
        {
            if (!swingLogged) { swingTowardWorker++; swingLogged = true; StudyLogger.Log("swing_toward_worker", $"angle={angle:F0};dist={toW.magnitude:F1}"); }
        }
        if (angle > 75F || Ctl.LastInput.swing == 0F) swingLogged = false;
        prevAngleToWorker = angle;
    }

    public override string Summary() =>
        $"dug={TerrainDigger.Instance.FractionDugTo(TargetDepth):F2};scoops={load.Scoops};spoil_dumps={spoilDumps};spills={spills};" +
        $"too_close={tooClose};over_digs={overDigs};max_depth={maxDepth:F2};pipe_strikes={pipeStrikes};cable_strikes={cableStrikes};" +
        $"bucket_over_worker={bucketOverWorker};swing_toward_worker={swingTowardWorker};arrived={(arrived ? 1 : 0)}";
}

// HARD - DIG AND DUMP: load the truck 4 times before time runs out (6 minutes).
// The truck is OUT OF REACH of the pit, so every load is a cycle:
//   1. dig at the pit, from the yellow DIG box
//   2. drive (reversing) along a dog-leg lane to the yellow DUMP box beside the truck, ~14 m back and 6 m to
//      the right. The lane has TWO TURNS, both forced by barriers:
//        - straight back 4 m, then a row of concrete blocks behind the machine forces a turn to the RIGHT,
//        - a diagonal of ~8 m, then a fence on the outside forces a turn back to the LEFT,
//        - straight back 5 m into the DUMP box. The truck itself blocks the shortcut on the inside of the 2nd turn.
//      Barrels mark the inside of the 1st turn and the outside of the diagonal.
//   3. swing about 90 degrees left and dump into the truck
//   4. drive back along the lane to the DIG box
// Hazards: a worker walking into the swing area at the pit, a worker walking into the swing path
// between machine and truck at the DUMP box, an overhead power line beyond the truck, spills.
public class DigDumpScenario : Scenario
{
    const int Goal = 4;
    const float Limit = 360F;          // seconds (other scenarios: StudyManager.trialDurationSeconds)
    // Lane waypoints in "lane coordinates": (metres back from the DIG box, metres to the right).
    static readonly Vector2 W1 = new Vector2(4F, 0F);      // end of the first straight -> turn right
    static readonly Vector2 W2 = new Vector2(9F, 6F);      // end of the diagonal       -> turn left
    static readonly Vector2 WP = new Vector2(14F, 6F);     // DUMP box
    // Truck centre distance from the DUMP box centre, as a share of the machine's max reach
    // (was 0.62: dumping now needs the arm further out and the bucket higher over the truck wall).
    const float TruckOffset = 0.75F;

    static readonly Vector3 digPos = new Vector3(-16.5F, 0, -7.5F);
    Vector3 dumpPos;

    BucketLoad load;
    Vector3 f, left, bedCenter, pit;
    int loads, spills, trips;
    bool atDump;
    WorkerWalker pitWorker, truckWorker;
    float pitWorkerAt, truckWorkerAt;
    float reach;

    public override string TaskText => $"Load the truck {Goal} times";
    public override Vector3? DumpTarget => bedCenter;
    public override bool DumpArea(out Vector3 c, out Vector3 fw, out float len, out float wid) { c = bedCenter; fw = f; len = WorldBuilder.TruckBedLength; wid = WorldBuilder.TruckBedWidth; return true; }
    public override float? TimeLimitSeconds => Limit;
    public override string ProgressText
    {
        get
        {
            float remaining = Mathf.Max(0F, SM.TrialLimit - SM.TrialTime);
            string next = load != null && load.IsLoaded ? "Reverse to the yellow box at the truck and dump."
                                                        : "Drive to the yellow box at the pit and dig.";
            return $"Loads: {loads} / {Goal}    Time left: {Mathf.FloorToInt(remaining / 60)}:{Mathf.FloorToInt(remaining % 60):00}    {next}";
        }
    }

    // A point given in lane coordinates (back from the DIG box, to the right), on the ground.
    Vector3 L(float back, float rightOff) => WorldBuilder.OnGround(digPos - f * back - left * rightOff);
    Vector3 L(Vector2 v) => L(v.x, v.y);

    // Where the C3 driving path leads. Normally: loaded -> DUMP box, empty -> DIG box. When the machine is
    // already in that box (e.g. at the start, empty at the DIG box) the path switches to the OTHER box, so
    // there is always a path while driving, whether or not there is soil in the bucket.
    bool routeToDump, wasLoaded, inRouteTarget;

    void UpdateRouteTarget()
    {
        bool loaded = load != null && load.IsLoaded;
        if (loaded != wasLoaded) { wasLoaded = loaded; routeToDump = loaded; }   // new load -> truck; dumped -> pit
        Vector3 target = routeToDump ? dumpPos : digPos, c = M.SwingCenter;
        bool inside = new Vector2(c.x - target.x, c.z - target.z).magnitude < 1.2F;
        if (inside && !inRouteTarget) routeToDump = !routeToDump;              // arrived: next leg goes the other way
        inRouteTarget = new Vector2(c.x - (routeToDump ? dumpPos : digPos).x, c.z - (routeToDump ? dumpPos : digPos).z).magnitude < 1.2F;
    }

    public override string RouteTargetName => routeToDump ? "DUMP BOX" : "DIG BOX";

    // C3 driving cue: along the lane to the DUMP box (reversing) or back to the DIG box (forward).
    public override System.Collections.Generic.List<Vector3> RouteAhead(out bool reverse)
    {
        UpdateRouteTarget();
        reverse = routeToDump;
        return routeToDump ? new System.Collections.Generic.List<Vector3> { L(W1), L(W2), L(WP) }
                           : new System.Collections.Generic.List<Vector3> { L(W2), L(W1), L(Vector2.zero) };
    }

    protected override void Build()
    {
        f = Vector3.right;
        left = -Vector3.Cross(Vector3.up, f);
        PlaceMachine(digPos.x, digPos.z, f);
        M.EnableCuttingEdges(true);
        reach = M.MaxReach;

        dumpPos = L(WP);
        Outline("digbox", WorldBuilder.OnGround(digPos), f, 5.5F, 4.5F, WorldBuilder.Yellow);
        Outline("dumpbox", WorldBuilder.OnGround(dumpPos), f, 5.5F, 4.5F, WorldBuilder.Yellow);

        pit = Rel(0.6F * reach, 0);
        const float pitSize = 4.5F;   // was 3 m
        TerrainDigger.Instance.SetZone(pit, f, pitSize, pitSize, 2F);
        Outline("pit", pit, f, pitSize, pitSize, BucketLoad.SoilMaterial().color);

        // --- Barriers that force the two turns ---
        float yawLat = Quaternion.LookRotation(left).eulerAngles.y;   // long side across the lane
        float yawLane = Quaternion.LookRotation(f).eulerAngles.y;     // long side along the lane
        // Turn 1 (right): concrete blocks straight behind the end of the first straight.
        for (int i = 0; i < 3; i++)
        {
            var block = WorldBuilder.Shape(PrimitiveType.Cube, Root, $"ConcreteBlock{i + 1}", L(8F, -3.0F + i * 1.05F),
                                           new Vector3(0.6F, 0.8F, 1.0F), new Color(0.7F, 0.7F, 0.68F), yawLat, true);
            WorldBuilder.AddHazard<ColliderHazard>(block, $"concrete_block_{i + 1}", HazardKind.Obstacle, 1.2F, 0.15F);
        }
        // Turn 2 (left): fence on the outside of the second turn, across the continuation of the diagonal.
        {
            Vector3 fa = L(8.5F, 9.6F), fb = L(12.5F, 9.6F);
            var fence = WorldBuilder.Shape(PrimitiveType.Cube, Root, "LaneFence", (fa + fb) / 2F,
                                           new Vector3(0.1F, 1.1F, (fb - fa).magnitude), WorldBuilder.Orange, yawLane, true);
            WorldBuilder.AddHazard<ColliderHazard>(fence, "lane_fence", HazardKind.Obstacle, 1.2F, 0.15F);
            for (float k = 0F; k <= 1.001F; k += 0.5F)
                WorldBuilder.Shape(PrimitiveType.Cylinder, Root, "LaneFencePost", Vector3.Lerp(fa, fb, k), new Vector3(0.12F, 0.65F, 0.12F), WorldBuilder.White, 0, false);
        }
        // Barrels: inside of turn 1 (no early cutting to the right), outside of the diagonal, behind the DUMP box.
        WorldBuilder.Barrel(Root, "LaneBarrel1", L(2.5F + Rand(-0.2F, 0.2F), 3.4F));
        WorldBuilder.Barrel(Root, "LaneBarrel2", L(4.0F + Rand(-0.2F, 0.2F), 5.1F));
        WorldBuilder.Barrel(Root, "LaneBarrel3", L(16.5F, 8.6F + Rand(-0.2F, 0.2F)));

        // Truck to the left of the DUMP box (90 degree swing from there), out of reach from the DIG box.
        Vector3 truckCenter = WorldBuilder.OnGround(dumpPos + left * (TruckOffset * reach) + f * 1F);
        bedCenter = WorldBuilder.Truck(Root, "Truck", truckCenter, f, out _);

        // Power line just beyond the truck, 7.5 m high.
        Vector3 lineMid = dumpPos + left * (TruckOffset * reach + 2.5F);
        WorldBuilder.PowerLine(Root, "PowerLine", lineMid - f * 8F, lineMid + f * 8F, 7.5F);   // short: not over the DIG box

        pitWorker = WorldBuilder.Worker(Root, "WorkerPit", pit + left * 12F);
        pitWorker.Hide();
        truckWorker = WorldBuilder.Worker(Root, "WorkerTruck", dumpPos + f * 7F + left * 10F);
        truckWorker.Hide();
        pitWorkerAt = Rand(35F, 90F);
        truckWorkerAt = Rand(150F, 230F);

        StudyLogger.Log("layout", $"reach={reach:F2};truck_from_pit={CueKit.Flat(bedCenter - pit).magnitude:F1}");

        load = gameObject.AddComponent<BucketLoad>();
        load.Init(M);
        load.Dumped += p =>
        {
            if (InRect(p, bedCenter, f, WorldBuilder.TruckBedLength, WorldBuilder.TruckBedWidth, 0.3F)) { loads++; StudyLogger.Log("truck_load", $"{loads};amount={load.LastDumpAmount:F2}"); if (loads >= Goal) Complete("completed"); }
            else { spills++; StudyLogger.Log("spill", $"missed_truck;amount={load.LastDumpAmount:F2}"); }
        };
    }

    protected override void Tick()
    {
        float t = SM.TrialTime;
        Vector3 c = M.SwingCenter;

        // Trips to the truck.
        bool nowAtDump = new Vector2(c.x - dumpPos.x, c.z - dumpPos.z).magnitude < 1.5F;
        if (nowAtDump && !atDump) { trips++; StudyLogger.Log("stage", "at_dump_box_" + trips); }
        atDump = nowAtDump;

        // Worker 1: walks into the swing area at the pit (front-left of the DIG box), waits 5 s, walks out.
        if (pitWorkerAt > 0F && t >= pitWorkerAt)
        {
            pitWorkerAt = -1F;
            Vector3 diag = (f + left).normalized;
            Vector3 start = WorldBuilder.OnGround(digPos + diag * (reach + 4F));
            Vector3 inPath = WorldBuilder.OnGround(digPos + diag * (0.55F * reach));
            pitWorker.Appear(start);
            pitWorker.WalkTo(inPath, 5F);
            pitWorker.WalkTo(start);
            StudyLogger.Log("event_start", "worker_in_swing_area_pit");
        }

        // Worker 2: walks along the truck into the swing path between DUMP box and truck, waits 5 s, walks on.
        if (truckWorkerAt > 0F && t >= truckWorkerAt)
        {
            truckWorkerAt = -1F;
            float off = Mathf.Max(2F, TruckOffset * reach - 2.2F);
            Vector3 a = dumpPos + f * 7F + left * off, mid = dumpPos + left * off, b = dumpPos - f * 7F + left * off;
            truckWorker.Appear(a);
            truckWorker.WalkTo(mid, 5F);
            truckWorker.WalkTo(b);
            StudyLogger.Log("event_start", "worker_in_swing_area_truck");
        }
    }

    public override string Summary() => $"loads={loads};spills={spills};scoops={load.Scoops};trips={trips}";
}
