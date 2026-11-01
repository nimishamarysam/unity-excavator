using UnityEngine;

// ---------------------------------------------------------------------------
// THE FOUR SCENARIOS
// All positions are on the flat plain between the river (north) and the hills
// (south). The machine always faces east (+x) at the start.
// Random jitter comes from the trial's seed, so a trial can be reproduced.
// ---------------------------------------------------------------------------

// PRACTICE (run by PracticeGuide, instructions in the right panel), three phases:
//   1 basic controls   2 digging and dumping (guided)   3 free practice (hold A / joystick confirm button when ready, or time limit).
// The bucket is solid during phase 1 (cutting edges off: it stops on the ground). When phase 2 starts,
// BuildDigSite() switches digging on in a large square around the machine: NO marked dig or dump area
// (so the practice does not look like the digging scenario) - dig anywhere, dump anywhere. No hazards.
public class PracticeScenario : Scenario
{
    PracticeGuide guide;
    bool siteBuilt;

    public BucketLoad Load { get; private set; }
    public int Dumps { get; private set; }

    public override string TaskText
    {
        get
        {
            int phase = guide != null ? guide.Phase : 3;
            return $"Practice - phase {phase} of 3: {PracticeGuide.PhaseName(phase)}";
        }
    }

    public override string ProgressText
    {
        get
        {
            string follow = InstructionDisplay.Active ? "Follow the instructions on the instruction screen." : "Follow the instructions on the right.";
            // Free-practice instruction page: show the full time, not running yet.
            if (guide != null && guide.OnFreePracticeIntro && SM != null)
            {
                float full = SM.practiceDurationSeconds;
                return follow + $"\nTime: {Mathf.FloorToInt(full / 60F)}:{Mathf.FloorToInt(full % 60F):00} (starts when you press {StickControls.ConfirmLabel})";
            }
            if (guide != null && (!guide.Done || guide.MenuOpen)) return follow;
            string text = $"Practise on your own. {StickControls.HoldConfirm} when you are ready to start the tasks.";
            if (SM != null)
            {
                float start = guide != null ? guide.DoneAt : 0F;
                float left = Mathf.Max(0F, start + SM.practiceDurationSeconds - SM.TrialTime);
                text += $"\nTime left: {Mathf.FloorToInt(left / 60F)}:{Mathf.FloorToInt(left % 60F):00}";
            }
            return text;
        }
    }

    protected override void Build()
    {
        PlaceMachine(-16.5F, -7.5F, Vector3.right);
        M.EnableCuttingEdges(false);   // phase 1: solid bucket, it stops on the ground instead of going into it

        if (PracticeGuide.Enabled)
        {
            guide = gameObject.AddComponent<PracticeGuide>();
            guide.Init(Ctl, this);
        }
        else BuildDigSite();
    }

    // Phase 2: digging possible all around the machine (where it stands NOW), nothing marked on the ground.
    // Dumping works anywhere (BucketLoad only needs the bucket lifted >= 0.5 m and opened fully).
    // Safe to call more than once.
    public void BuildDigSite()
    {
        if (siteBuilt) return;
        siteBuilt = true;
        float size = 2F * (M.MaxReach + 3F);   // covers everything the bucket can reach, also after a little driving
        M.EnableCuttingEdges(true);            // lets the bucket go into the ground
        TerrainDigger.Instance.SetZone(WorldBuilder.OnGround(M.SwingCenter), M.ForwardFlat, size, size, 1.5F);

        Load = gameObject.AddComponent<BucketLoad>();
        Load.Init(M);
        Load.Dumped += p => { Dumps++; StudyLogger.Log("dump", $"anywhere;x={p.x:F1};z={p.z:F1};amount={Load.LastDumpAmount:F2}"); };
        StudyLogger.Log("practice_dig_site", $"center={M.SwingCenter.x:F1},{M.SwingCenter.z:F1};size={size:F1}");
    }
}

// EASY - DRIVING: drive through 4 gates, then reverse to a stop line.
// Layout (tuned to be easy to drive): gates 10 m wide (machine ~3.2 m), 15-16 m apart, and only a
// gentle zig-zag (max. ~16 degree heading change between gates), so there is room to line up for each gate.
// Hazards: barrels near the route, a worker crossing the route, a worker walking behind while reversing.
public class DrivingScenario : Scenario
{
    const float GateWidth = 10F;      // distance between the two posts (was 7 m)
    const float GatePassBefore = 1.5F; // gate counts once the machine centre is this close to the gate line (or past it)
    readonly Vector3[] gates = { new Vector3(-16, 0, -2.5F), new Vector3(0, 0, 1.5F), new Vector3(16, 0, -1.5F), new Vector3(31, 0, 0) };
    static readonly Vector3 start = new Vector3(-32, 0, -2);
    Vector3 stopLine = new Vector3(22, 0, 0);
    int nextGate;
    bool reversing;
    float stillTime;
    WorkerWalker crosser, behind;
    float crosserAt = -1F, behindAt = -1F;
    int contacts;

    public override string TaskText => reversing ? "Reverse to the yellow stop line" : "Drive forward through the gates in order";
    public override string ResultText => $"Gates passed: {nextGate} of {gates.Length}" + (reversing ? "  -  reversing to the stop line" : "");
    public override string ProgressText => reversing
        ? $"{StickControls.Cap(StickControls.DriveBack)} to reverse. The cab keeps facing forward - do not swing it around.\nStop when the middle of the machine is on the yellow line."
        : $"Next: gate {nextGate + 1} of {gates.Length}. {StickControls.Cap(StickControls.DriveForward)}; steer around obstacles and people.";

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
            // GatePassBefore: the centre may still be up to 1.5 m before the gate line - then the front half of the
            // machine is already through. Without it, stopping IN the last gate and reversing (a natural move) left
            // gate 4 uncounted, the scenario never switched to "reverse", and the stop line never ended the trial.
            if (along >= -GatePassBefore && along < 4F && Mathf.Abs(across) < GateWidth / 2F)
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

// MODERATE - DIGGING: drive to the work position, dig a trench to 1.6 m (TargetDepth), dump spoil on the white square.
// Task demands:
//   - Some driving: the machine starts 9 m behind the work position (yellow box) and must drive there first.
//   - Strict spoil placement: spoil square (3.0 m, was 4.5 m) about 120 degrees swing from the trench, on the
//     side away from the cable, ~7.3 m from the swing centre (SpoilDistOfReach; was the nearest spot >= 5 m from
//     the trench, ~6.3 m) and at least 5 m from the trench edge - smaller and farther, so dumping accuracy matters. Dumps outside it are spills; dumps within 5 m
//     of the trench edge are logged as "too_close_to_trench" spills.
//   - Buried cable parallel to the trench (0.5-0.8 m outside the edge, 0.9 m deep), marked with orange posts.
//   - Crossing service: a pipe crossing the trench 0.25 m BELOW the target depth (yellow posts on both sides).
//     Safe when digging to the target, struck when digging too deep at that spot.
//   - One worker event: at a random time a worker walks to the trench edge on the spoil side (inside the swing
//     path), stands there for a few seconds and walks away. Logged: bucket_over_worker, swing_toward_worker.
// Hazards also logged by the HazardTracker: over-digging, cable, pipe, trench edge (tracks), worker.
public class DiggingScenario : Scenario
{
    public const float TargetDepth = 1.6F;               // was 1.2 m (deeper trench); pipe stays PipeBelowTarget below it
    const float TrenchWidth = 3F;             // was 2 m (bigger dig area)
    // Trench position and length as shares of the max reach, measured from the WORK position. Fitted to the machine's
    // DIG ENVELOPE at the target depth (pilot 2026-10-22, reach 10.1 m): the teeth reach 2.5+ m deep from 3 to 6 m
    // from the swing centre, 2.0 m at 6.5 m, but only ~1 m at 7 m and beyond. The trench therefore runs from ~3.5 m
    // to ~6.6 m from the swing centre (a margin for stopping up to 1.2 m off the work position). Was centre 0.635 /
    // length 0.5 (to ~8.5 m): the far end could not be dug to depth and progress stopped around 79 %.
    const float TrenchCentreOfReach = 0.54F;
    const float TrenchLengthOfReach = 0.31F;
    const float SpoilSize = 3.0F;             // 2026-11-01: was 4.5 m, then 3.5 m (2.5 m before that) - smaller so dumping accuracy matters
    const float SpoilDistOfReach = 0.72F;     // 2026-11-01: centre of the spoil square at least this share of the reach from the
                                              // swing centre (~7.3 m; was the nearest spot >= SpoilMinGap from the trench, ~6.3 m)
    const float SpoilMaxOfReach = 0.85F;
    const float SpoilMinGap = 5F;             // minimum distance between spoil square and trench edge
    const float SpoilSwingDeg = 120F;         // swing angle from the trench direction to the spoil square
    const float PipeBelowTarget = 0.2F;       // crossing pipe depth below the target depth (was 0.25: digging just past the
                                              // target now comes close to it, so depth control matters)
    // Buried cable: this far outside the trench edge (m). Was 0.5-0.8 m (almost never reached); now digging right at the
    // trench wall (or leaning the bucket past it) comes within its warning distance.
    const float CableGapMin = 0.35F, CableGapMax = 0.5F;
    // Surface marks of the buried services in ALL conditions: false = only the marker posts (as on a real site);
    // true = also the painted lines on the ground (the old layout, which showed the C3 dashed line's information to everybody).
    public static bool ServiceLinesOnGround = false;
    // Worker events: the worker walks to the trench edge when the trench is about this far done (+-0.05, per trial),
    // or at the latest at the given trial time - so every participant meets the same number of events at a similar stage.
    static readonly float[] WorkerAtProgress = { 0.25F, 0.6F };
    static readonly float[] WorkerLatest = { 100F, 180F };
    const float WorkerStandSeconds = 6F;
    // Trench done %: smooth progress (TerrainDigger.ProgressTo) - each 0.5 m cell gives partial credit up to
    // full at TargetDepth - ProgressTolerance (1.0 m). The trial is completed at CompleteAt of the SAME number
    // the participant sees. (The old whole-cell share is still logged as dug= in the summary.)
    const float ProgressTolerance = 0.2F;
    // Edge strip (m) of the trench NOT counted in "Trench done": the walls slope (see TerrainDigger.ProgressTo) and the
    // row nearest the cab is barely reachable at depth, so with 0 the number stalled at ~76 % when the trench already
    // looked finished (pilot 2026-10-31). 0.5 m = the trench floor the operator can actually dig.
    const float ProgressWallMargin = 0.5F;
    const float CompleteAt = 0.85F;
    // Logged only (floor_at_depth): share of the trench floor dug to TargetDepth - AtDepthTolerance. Not a completion
    // condition any more (removed 2026-10-31).
    const float AtDepthTolerance = 0.1F;
    float FloorAtDepth => TerrainDigger.Instance.FractionDugTo(TargetDepth, AtDepthTolerance, ProgressWallMargin);
    // FIXED LAYOUT: true = trench, cable side, pipe position and spoil square are the SAME in every trial
    // (C1, C2, C3 and every participant), drawn from LayoutSeed; only the worker's timing still comes from the
    // trial seed. Keeps the three conditions comparable. false = everything from the trial seed (layout varies).
    public static bool FixedLayout = true;
    public const int LayoutSeed = 20261018;     // -> cable on the right, spoil square on the left
    const float OverWorkerRadius = 1.0F;      // bucket within this horizontal distance of the worker's centre = over the worker

    static readonly Vector3 workPos = new Vector3(-16.5F, 0, -7.5F);
    const float StartBehind = 9F;             // metres behind the work position at the start
    // Machine counts as "at the work position" when its swing centre is this close to workPos (was 1.2 m: the trench then
    // started anywhere from 3.1 to 4.0 m from the machine, which changed how deep each part of it could be dug).
    const float WorkPosTolerance = 0.7F;
    // Depth-accuracy measures (floor_*) use only the part of the trench floor the teeth can dig to depth from the work
    // position: this distance band from the swing centre (pilot tip_max: >= 1.6 m from about 4.5 to 6.0 m).
    const float ReachBandMin = 4.5F, ReachBandMax = 6.0F;

    BucketLoad load;
    Vector3 trench, spoil, f, r;
    float trenchLen, maxDepth, side;
    int spoilDumps, spills, tooClose, overDigs, bucketOverWorker, swingTowardWorker, pipeStrikes, cableStrikes;
    bool overDigArmed = true, arrived;
    SegmentHazard cable, pipe;
    WorkerWalker worker;
    float workerUntil = -1F;
    float[] workerAtProgress;
    int workerEvents;
    bool overLogged, swingLogged;
    float prevAngleToWorker = -1F;
    Hazard.Level pipeLevel, cableLevel;

    public override string ResultText =>
        $"Trench done: {(TerrainDigger.Instance != null ? TerrainDigger.Instance.ProgressTo(TargetDepth, ProgressTolerance, ProgressWallMargin) * 100F : 0F):0}%  (deepest {maxDepth:0.00} m, target {TargetDepth:0.0} m)\n" +
        $"Dumps on the white square: {spoilDumps} of {dumpCount}";
    public override string TaskText => arrived ? $"Dig the trench between the stakes to {TargetDepth:0.0} m deep"
                                               : $"Drive into the yellow box ({StickControls.ModeSwitch} for DRIVE mode)";
    public override Vector3? DumpTarget => spoil;
    public override float? DigTargetDepth => TargetDepth;
    public override bool DumpArea(out Vector3 c, out Vector3 fw, out float len, out float wid) { c = spoil; fw = f; len = wid = SpoilSize; return true; }
    public override string ProgressText => arrived
        ? $"Trench done: {TerrainDigger.Instance.ProgressTo(TargetDepth, ProgressTolerance, ProgressWallMargin) * 100F:0}%   Deepest: {maxDepth:0.00} m   " +
          "Dump soil ONLY on the small white square. Avoid the orange cable and the yellow pipe markers."
        : $"Then {StickControls.ModeSwitch} again for digging mode.";

    public override System.Collections.Generic.List<Vector3> RouteAhead(out bool reverse)
    {
        reverse = false;
        if (arrived) return null;
        return new System.Collections.Generic.List<Vector3> { WorldBuilder.OnGround(workPos) };
    }

    protected override void Build()
    {
        var trialRng = Rng;                                   // kept for the worker timing below
        if (FixedLayout) Rng = new System.Random(LayoutSeed);
        f = Vector3.right;
        r = Vector3.Cross(Vector3.up, f);

        // Lay out everything relative to the WORK position, then move the machine back to the start.
        PlaceMachine(workPos.x, workPos.z, f);
        M.EnableCuttingEdges(true);
        float reach = M.MaxReach;

        trenchLen = TrenchLengthOfReach * reach;
        trench = Rel(TrenchCentreOfReach * reach, 0);
        float hw = TrenchWidth / 2F;
        TerrainDigger.Instance.SetZone(trench, f, trenchLen, TrenchWidth, TargetDepth + 0.6F);
        Outline("trench", trench, f, trenchLen, TrenchWidth, BucketLoad.SoilMaterial().color);

        // Work position box (where the machine should stand while digging).
        Outline("work", WorldBuilder.OnGround(workPos), f, 5.0F, 4.0F, WorldBuilder.Yellow);   // was 5.5 x 4.5 m

        // Buried cable: parallel to the trench, CableGapMin-Max outside its edge, 0.9 m deep. Side is random.
        side = Rng.NextDouble() < 0.5 ? 1F : -1F;
        float offset = side * (hw + Rand(CableGapMin, CableGapMax));
        Vector3 a = trench - f * (trenchLen / 2F + 2F) + r * offset, b = trench + f * (trenchLen / 2F + 2F) + r * offset;
        cable = new GameObject("BuriedCable").AddComponent<SegmentHazard>();
        cable.transform.SetParent(Root);
        cable.a = WorldBuilder.OnGround(a) + Vector3.down * 0.9F;
        cable.b = WorldBuilder.OnGround(b) + Vector3.down * 0.9F;
        cable.hazardId = "buried_cable"; cable.kind = HazardKind.BuriedCable;
        cable.measure = HazardMeasure.BucketTeethOnly; cable.warnDistance = 0.7F; cable.contactDistance = 0.3F;
        for (float s = 0; s <= 1.001F; s += 0.25F)
            WorldBuilder.Shape(PrimitiveType.Cylinder, Root, "CableMarker", Vector3.Lerp(a, b, s), new Vector3(0.1F, 0.5F, 0.1F), WorldBuilder.Orange, 0, false);
        if (ServiceLinesOnGround) WorldBuilder.GroundMark(Root, "CableLine", WorldBuilder.OnGround((a + b) / 2F), (b - a).magnitude, 0.1F, 90F, WorldBuilder.Orange);

        // Crossing service: a pipe across the trench, 0.25 m below the target depth, in the middle part of the trench.
        Vector3 cross = trench + f * Rand(-0.2F, 0.2F) * trenchLen;
        Vector3 pa = cross - r * (hw + 1.5F), pb = cross + r * (hw + 1.5F);
        pipe = new GameObject("CrossingPipe").AddComponent<SegmentHazard>();
        pipe.transform.SetParent(Root);
        float pipeDepth = TargetDepth + PipeBelowTarget;
        pipe.a = new Vector3(pa.x, TerrainDigger.ReferenceGroundHeight(pa) - pipeDepth, pa.z);
        pipe.b = new Vector3(pb.x, TerrainDigger.ReferenceGroundHeight(pb) - pipeDepth, pb.z);
        pipe.hazardId = "crossing_pipe"; pipe.kind = HazardKind.BuriedPipe;
        pipe.measure = HazardMeasure.Bucket; pipe.warnDistance = 0.15F; pipe.contactDistance = 0.05F;   // whole bucket (2026-11-01)   // pipe 1.8 m deep: warning from 1.65 m, strike at 1.75 m (= the over-dig limit)
        // Markers only OUTSIDE the trench (a mark across the hole would float in the air once dug).
        foreach (float sgn in new[] { -1F, 1F })
        {
            Vector3 post = cross + r * sgn * (hw + 0.9F);
            WorldBuilder.Shape(PrimitiveType.Cylinder, Root, "PipeMarker", post, new Vector3(0.12F, 0.55F, 0.12F), WorldBuilder.Yellow, 0, false);
            if (ServiceLinesOnGround) WorldBuilder.GroundMark(Root, "PipeLine", WorldBuilder.OnGround(cross + r * sgn * (hw + 0.75F)), 1.5F, 0.12F, 0F, WorldBuilder.Yellow);
        }

        // Trench edge: warns when the TRACKS get close to the trench.
        var edge = new GameObject("TrenchEdge").AddComponent<RectEdgeHazard>();
        edge.transform.SetParent(Root);
        edge.center = trench; edge.axisU = f; edge.halfU = trenchLen / 2F; edge.halfV = hw;
        edge.hazardId = "trench_edge"; edge.kind = HazardKind.TrenchEdge; edge.measure = HazardMeasure.TracksOnly;
        edge.warnDistance = 1.5F; edge.contactDistance = 0.3F;

        // Spoil square: ~120 degree swing towards the side away from the cable, at the shortest distance
        // that keeps it at least 5 m from the trench edge.
        Vector3 c0 = WorldBuilder.OnGround(workPos);
        spoil = WorldBuilder.OnGround(SpoilCentre(c0, f, r, side, reach, trench, trenchLen, out bool ok));
        LastReach = reach;
        if (!ok) Debug.LogWarning($"[Study] Spoil square only {GapToTrench(spoil, SpoilSize / 2F):F1} m from the trench (reach {reach:F1} m)");
        Outline("spoil", spoil, f, SpoilSize, SpoilSize, WorldBuilder.White);
        StudyLogger.Log("layout", $"fixed={(FixedLayout ? 1 : 0)};reach={reach:F2};spoil_gap={GapToTrench(spoil, SpoilSize / 2F):F2};spoil_dist={(spoil - c0).magnitude:F2};pipe_depth={pipeDepth:F2};cable_side={side}");

        // Worker: hidden until the event.
        Vector3 stand = trench + r * (-side) * (hw + 1F) + f * Rand(-0.25F, 0.25F) * trenchLen;
        worker = WorldBuilder.Worker(Root, "WorkerAtTrench", stand + r * (-side) * 12F + f * 4F);
        worker.Hide();
        Rng = trialRng;                                       // event timing varies per trial
        workerAtProgress = new float[WorkerAtProgress.Length];
        for (int i = 0; i < workerAtProgress.Length; i++) workerAtProgress[i] = WorkerAtProgress[i] + Rand(-0.05F, 0.05F);
        workerStand = stand;

        load = gameObject.AddComponent<BucketLoad>();
        load.Init(M);
        load.Dumped += p =>
        {
            // Continuous: where the soil was released relative to the spoil square (0 = centre, 1 = edge, >1 = outside).
            Vector3 dd = p - spoil;
            float off = new Vector2(dd.x, dd.z).magnitude;
            float u = Mathf.Max(Mathf.Abs(Vector3.Dot(dd, f)), Mathf.Abs(Vector3.Dot(dd, r))) / (SpoilSize / 2F);
            float vol = load.LastDumpAmount * load.bucketCapacity;
            dumpCount++; dumpOffsetSum += off; dumpOffsetMax = Mathf.Max(dumpOffsetMax, off); dumpEdgeSum += u; dumpedM3 += vol;
            bool inside = InRect(p, spoil, f, SpoilSize, SpoilSize, 0.3F);
            if (!inside) spilledM3 += vol;
            StudyLogger.Log("dump_position", $"offset={off:F2};edge={u:F2};m3={vol:F2}");
            if (inside) { spoilDumps++; StudyLogger.Log("dump", $"spoil_area;amount={load.LastDumpAmount:F2}"); }
            else if (GapToTrench(p, 0F) < SpoilMinGap) { spills++; tooClose++; StudyLogger.Log("spill", $"too_close_to_trench;gap={GapToTrench(p, 0F):F2};amount={load.LastDumpAmount:F2}"); }
            else { spills++; StudyLogger.Log("spill", $"outside_spoil_area;amount={load.LastDumpAmount:F2}"); }
        };

        // Start position: 9 m behind the work position, same heading.
        Vector3 start = workPos - f * StartBehind;
        PlaceMachine(start.x, start.z, f);
    }

    Vector3 workerStand;
    // Continuous measures (end-of-trial summary)
    int dumpCount;
    float dumpOffsetSum, dumpOffsetMax, dumpEdgeSum, dumpedM3, spilledM3;
    float overDigSeconds, outsideSeconds, outsideMax, overWorkerSeconds;
    float pipeMin = float.MaxValue, cableMin = float.MaxValue, pipeWarnSeconds, cableWarnSeconds;
    readonly System.Collections.Generic.SortedDictionary<int, float> tipMaxByDist = new System.Collections.Generic.SortedDictionary<int, float>();

    string TipEnvelope()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in tipMaxByDist) { if (sb.Length > 0) sb.Append(' '); sb.Append($"{kv.Key * 0.5F:F1}:{kv.Value:F2}"); }
        return sb.ToString();
    }

    // Distance from a square (centre p, half size h, aligned with the trench) to the trench rectangle.
    float GapToTrench(Vector3 p, float h) => GapToRect(p, h, trench, f, r, trenchLen);

    static float GapToRect(Vector3 p, float h, Vector3 trenchC, Vector3 fw, Vector3 rt, float len)
    {
        Vector3 d = p - trenchC;
        float du = Mathf.Abs(Vector3.Dot(d, fw)) - (len / 2F + h);
        float dv = Mathf.Abs(Vector3.Dot(d, rt)) - (TrenchWidth / 2F + h);
        return new Vector2(Mathf.Max(0F, du), Mathf.Max(0F, dv)).magnitude;
    }

    // Spoil square centre: SpoilSwingDeg swing towards the side away from the cable, at SpoilDistOfReach of the reach
    // (or farther, up to SpoilMaxOfReach, until it is SpoilMinGap from the trench). Used by Build and the briefing map.
    static Vector3 SpoilCentre(Vector3 c0, Vector3 fw, Vector3 rt, float side, float reach, Vector3 trenchC, float len, out bool ok)
    {
        float ang = SpoilSwingDeg * Mathf.Deg2Rad;
        Vector3 dir = fw * Mathf.Cos(ang) + rt * (-side) * Mathf.Sin(ang);
        ok = false;
        for (float d = SpoilDistOfReach * reach; d <= SpoilMaxOfReach * reach + 1e-3F; d += 0.1F)
        {
            Vector3 p = c0 + dir * d;
            if (GapToRect(p, SpoilSize / 2F, trenchC, fw, rt, len) >= SpoilMinGap) { ok = true; return p; }
        }
        return c0 + dir * (SpoilMaxOfReach * reach);
    }

    // ---- Briefing (task instruction screen): site map with legend, and which side things are on ----
    public static float LastReach = 10.14F;   // machine reach (updated from the real machine in Build)

    // The fixed layout's random choices, drawn in the SAME order as Build (side, cable gap, pipe position).
    static void FixedLayoutValues(out float side, out float cableGap, out float crossFrac)
    {
        var rng = new System.Random(LayoutSeed);
        side = rng.NextDouble() < 0.5 ? 1F : -1F;
        cableGap = CableGapMin + (float)rng.NextDouble() * (CableGapMax - CableGapMin);
        crossFrac = -0.2F + (float)rng.NextDouble() * 0.4F;
    }

    // "right" / "left" as seen from the cab at the work position (null when the layout varies per trial).
    public static void BriefingSides(out string cableSide, out string dumpSide)
    {
        if (!FixedLayout) { cableSide = dumpSide = null; return; }
        FixedLayoutValues(out float side, out _, out _);
        cableSide = side > 0F ? "right" : "left";
        dumpSide = side > 0F ? "left" : "right";
    }

    public static MiniMap SiteMap()
    {
        float reach = LastReach;
        Vector3 F = Vector3.right, R = Vector3.Cross(Vector3.up, F);
        float side = 1F, gap = CableGapMin, crossFrac = 0F;
        if (FixedLayout) FixedLayoutValues(out side, out gap, out crossFrac);
        float len = TrenchLengthOfReach * reach, hw = TrenchWidth / 2F;
        Vector3 c0 = workPos, trenchC = c0 + F * (TrenchCentreOfReach * reach);
        Vector3 spoilC = SpoilCentre(c0, F, R, side, reach, trenchC, len, out _);
        Vector3 start = workPos - F * StartBehind;
        float off = side * (hw + gap);
        Vector3 ca = trenchC - F * (len / 2F + 2F) + R * off, cb = trenchC + F * (len / 2F + 2F) + R * off;
        Vector3 cross = trenchC + F * crossFrac * len;

        Color brown = new Color(0.55F, 0.38F, 0.22F), yellow = WorldBuilder.Yellow, orange = WorldBuilder.Orange,
              white = Color.white, cyan = new Color(0.1F, 0.85F, 1F), pipeBlue = new Color(0.3F, 0.6F, 1F);
        var m = new MiniMap { Up = F, LineWidth = 4F, FontSize = 15 };
        m.Line(start, c0, cyan);
        m.Dot(start, 0.5F, cyan);
        m.Label(start - F * 1.2F, "START", cyan);
        m.Rect(c0, 5.0F, 4.0F, yellow, false);
        m.Label(c0, "WORK\nBOX", yellow);
        m.Rect(trenchC, len, TrenchWidth, brown);
        m.Label(trenchC, "DIG", white);
        m.Line(ca, cb, orange);
        for (float s = 0; s <= 1.001F; s += 0.25F) m.Dot(Vector3.Lerp(ca, cb, s), 0.35F, orange);
        m.Label(cb + F * 1.4F, "cable", orange);
        m.Line(cross - R * (hw + 1.5F), cross + R * (hw + 1.5F), pipeBlue);
        foreach (float sgn in new[] { -1F, 1F }) m.Dot(cross + R * sgn * (hw + 0.9F), 0.35F, yellow);
        m.Label(cross - R * side * (hw + 2.6F), "pipe", pipeBlue);
        m.Rect(spoilC, SpoilSize, SpoilSize, white, false);
        m.Label(spoilC, "DUMP", white);
        m.Label(trenchC + F * (len / 2F + 3.4F), "\u2191 cab faces this way", white);

        m.AddLegend(MiniMap.Kind.Fill, brown, $"DIG PIT (trench): Dig only inside it, {TargetDepth:0.0} m deep");
        m.AddLegend(MiniMap.Kind.Outline, white, "DUMP PIT: Put ALL the soil here (anywhere else is a spill)");
        m.AddLegend(MiniMap.Kind.Outline, yellow, "WORK BOX: Drive the machine in here and dig from here");
        m.AddLegend(MiniMap.Kind.Dot, orange, "ORANGE posts: A buried electric CABLE runs along the trench between them");
        m.AddLegend(MiniMap.Kind.Dot, yellow, $"YELLOW posts: A water PIPE crosses UNDER the trench between them, just below {TargetDepth:0.0} m");
        m.AddLegend(MiniMap.Kind.Dot, cyan, "START: Initial position in the scenario");
        return m;
    }

    protected override void Tick()
    {
        float t = SM.TrialTime;

        if (!arrived)
        {
            Vector3 c = M.SwingCenter;
            if (new Vector2(c.x - workPos.x, c.z - workPos.z).magnitude < WorkPosTolerance)
            {
                arrived = true;
                StudyLogger.Log("stage", $"at_work_position;offset_along={Vector3.Dot(c - workPos, f):F2};offset_across={Vector3.Dot(c - workPos, r):F2}");
            }
        }

        Vector3 tip = M.BucketTipPosition;
        float depth = TerrainDigger.ReferenceGroundHeight(tip) - tip.y;
        if (TerrainDigger.Instance.Contains(tip))
        {
            maxDepth = Mathf.Max(maxDepth, depth);
            // Diagnostic: deepest the teeth reached per 0.5 m of distance from the swing centre (reach envelope).
            float rad = new Vector2(tip.x - M.SwingCenter.x, tip.z - M.SwingCenter.z).magnitude;
            int bin = Mathf.FloorToInt(rad / 0.5F);
            if (!tipMaxByDist.TryGetValue(bin, out float prev) || depth > prev) tipMaxByDist[bin] = depth;
            if (overDigArmed && depth > TargetDepth + OverDigTolerance) { overDigs++; overDigArmed = false; StudyLogger.Log("over_dig", $"depth={depth:F2}"); }
        }
        if (depth < 0F) overDigArmed = true;

        // Continuous measures (only once at the work position, so driving there does not count).
        if (arrived)
        {
            float dt = Time.fixedDeltaTime;
            if (TerrainDigger.Instance.Contains(tip) && depth > TargetDepth + OverDigTolerance) overDigSeconds += dt;
            if (!TerrainDigger.Instance.Contains(tip) && depth > 0.1F)
            {   // teeth in the ground outside the marked trench
                outsideSeconds += dt;
                outsideMax = Mathf.Max(outsideMax, GapToTrench(tip, 0F));
            }
            float dp = (pipe.ClosestPointTo(tip) - tip).magnitude, dc = (cable.ClosestPointTo(tip) - tip).magnitude;
            pipeMin = Mathf.Min(pipeMin, dp); cableMin = Mathf.Min(cableMin, dc);
            if (dp <= pipe.warnDistance) pipeWarnSeconds += dt;
            if (dc <= cable.warnDistance) cableWarnSeconds += dt;
        }

        // Strikes (contact level reached) of the buried services.
        if (pipe.CurrentLevel == Hazard.Level.Contact && pipeLevel != Hazard.Level.Contact) pipeStrikes++;
        if (cable.CurrentLevel == Hazard.Level.Contact && cableLevel != Hazard.Level.Contact) cableStrikes++;
        pipeLevel = pipe.CurrentLevel; cableLevel = cable.CurrentLevel;

        WorkerEvent(t, tip);

        if (TerrainDigger.Instance.ProgressTo(TargetDepth, ProgressTolerance, ProgressWallMargin) >= CompleteAt) Complete("completed");
    }

    // Horizontal (x/z) distance from p to the segment a-b.
    static float FlatDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), AB = new Vector2(b.x, b.z) - A;
        float t = AB.sqrMagnitude < 1e-6F ? 0F : Mathf.Clamp01(Vector2.Dot(P - A, AB) / AB.sqrMagnitude);
        return (P - (A + AB * t)).magnitude;
    }

    // One worker walks to the trench edge (inside the swing path to the spoil square), waits, and walks away.
    void WorkerEvent(float t, Vector3 tip)
    {
        // Next worker event: when the trench is far enough along (or at the latest time), and not while one is running.
        if (arrived && workerEvents < workerAtProgress.Length && t > workerUntil
            && (TerrainDigger.Instance.ProgressTo(TargetDepth, ProgressTolerance, ProgressWallMargin) >= workerAtProgress[workerEvents]
                || t >= WorkerLatest[workerEvents]))
        {
            workerEvents++;
            Vector3 from = workerStand + r * (-side) * 12F + f * 4F;
            worker.Appear(from);
            worker.WalkTo(workerStand, WorkerStandSeconds);
            worker.WalkTo(from);
            workerUntil = t + 30F;
            StudyLogger.Log("event_start", $"worker_at_trench;n={workerEvents}");
            overLogged = false; swingLogged = false; prevAngleToWorker = -1F;
        }
        if (workerUntil < 0F || t > workerUntil || !worker.gameObject.activeInHierarchy) return;

        Vector3 wp = worker.transform.position;
        Vector3 c = M.SwingCenter;

        // Bucket over the worker: some part of the bucket (hinge -> teeth) is horizontally within OverWorkerRadius
        // of the worker's centre (worker radius 0.3 m + half the bucket width 0.45 m + margin) AND the whole
        // bucket is above the ground the worker stands on. Digging in the trench NEXT TO the worker (bucket below
        // the worker's feet) is not counted - that was the old rule's false alarm (teeth within 1.5 m, down to 0.5 m deep).
        Vector3 piv = M.BucketPivotPosition;
        float hd = FlatDistanceToSegment(wp, piv, tip);
        float low = Mathf.Min(piv.y, tip.y) - wp.y;
        bool over = hd < OverWorkerRadius && low > 0F;
        if (over) overWorkerSeconds += Time.fixedDeltaTime;
        if (over && !overLogged) { bucketOverWorker++; overLogged = true; StudyLogger.Log("bucket_over_worker", $"dist_h={hd:F2};height={low:F2}"); }
        if (!over && hd > OverWorkerRadius + 1.5F) overLogged = false;

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
        $"progress={TerrainDigger.Instance.ProgressTo(TargetDepth, ProgressTolerance, ProgressWallMargin):F2};dug={TerrainDigger.Instance.FractionDugTo(TargetDepth, 0.15F, ProgressWallMargin):F2};floor_at_depth={FloorAtDepth:F2};progress_full_zone={TerrainDigger.Instance.ProgressTo(TargetDepth, ProgressTolerance):F2};scoops={load.Scoops};spoil_dumps={spoilDumps};spills={spills};" +
        $"too_close={tooClose};over_digs={overDigs};max_depth={maxDepth:F2};pipe_strikes={pipeStrikes};cable_strikes={cableStrikes};" +
        $"bucket_over_worker={bucketOverWorker};swing_toward_worker={swingTowardWorker};arrived={(arrived ? 1 : 0)};" +
        // Diagnostic: how deep each row of the trench got, near end -> far end ("distance from swing centre : mean/min depth").
        $"reach={M.MaxReach:F2};rows={TerrainDigger.Instance.DepthProfile(M.SwingCenter)};tip_max={TipEnvelope()};" +
        Metrics();

    // CONTINUOUS MEASURES, one per C3 cue (see STUDY_SETTINGS.md, "Digging measures"):
    //   teeth line on the ground  -> outside_s, outside_max            (digging outside the marked trench)
    //   dashed buried-service line -> pipe_min, pipe_warn_s, cable_min, cable_warn_s
    //   underground depth markers / depth gauge -> floor_mean, floor_rmse, floor_in_band, floor_under, floor_over
    //                                 (reachable band ReachBandMin-Max only; floor_all_* = whole floor),
    //                                 over_dig_m3, over_dig_s
    //   dump target sign          -> dump_offset_mean / max (m from the square's centre), dump_edge_mean (0 centre, 1 edge),
    //                                 spilled_m3 of dumped_m3
    //   worker events             -> worker_events, over_worker_s (latency and min distance: hazard_response / hazard_clear)
    string Metrics()
    {
        var d = TerrainDigger.Instance;
        // Main depth measures: reachable band only (same for everybody, whatever the exact stopping position).
        d.FloorStats(TargetDepth, 0.1F, OverDigTolerance, ProgressWallMargin, M.SwingCenter, ReachBandMin, ReachBandMax,
                     out float mean, out float rmse, out float band, out float under, out float over, out int nPts);
        // Whole floor (for reference).
        d.FloorStats(TargetDepth, 0.1F, OverDigTolerance, ProgressWallMargin, out float meanAll, out float rmseAll, out float bandAll, out _, out _);
        Vector3 wo = M.SwingCenter - workPos;
        d.DugVolumes(TargetDepth + OverDigTolerance, out float dug, out float overM3);
        string M2(float v) => v == float.MaxValue ? "NA" : v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        return $"floor_mean={mean:F2};floor_rmse={rmse:F2};floor_in_band={band:F2};floor_under={under:F2};floor_over={over:F2};floor_points={nPts};" +
               $"floor_all_mean={meanAll:F2};floor_all_rmse={rmseAll:F2};floor_all_in_band={bandAll:F2};" +
               $"work_offset_along={Vector3.Dot(wo, f):F2};work_offset_across={Vector3.Dot(wo, r):F2};" +
               $"dug_m3={dug:F2};over_dig_m3={overM3:F2};over_dig_s={overDigSeconds:F1};outside_s={outsideSeconds:F1};outside_max={outsideMax:F2};" +
               $"pipe_min={M2(pipeMin)};pipe_warn_s={pipeWarnSeconds:F1};cable_min={M2(cableMin)};cable_warn_s={cableWarnSeconds:F1};" +
               $"dumps={dumpCount};dump_offset_mean={(dumpCount > 0 ? dumpOffsetSum / dumpCount : 0F):F2};dump_offset_max={dumpOffsetMax:F2};" +
               $"dump_edge_mean={(dumpCount > 0 ? dumpEdgeSum / dumpCount : 0F):F2};dumped_m3={dumpedM3:F2};spilled_m3={spilledM3:F2};" +
               $"worker_events={workerEvents};over_worker_s={overWorkerSeconds:F1}";
    }
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
    // 2026-11-01: a truck load counts only when the bucket is at least this full when it is emptied into the truck
    // (BUCKET LOAD in the status bar). Less is logged as truck_load_too_small and not counted (not a spill).
    public const float MinLoadFill = 0.8F;
    const float Limit = 360F;          // seconds (other scenarios: StudyManager.trialDurationSeconds)
    // Lane waypoints in "lane coordinates": (metres back from the DIG box, metres to the right).
    static readonly Vector2 W1 = new Vector2(4F, 0F);      // end of the first straight -> turn right
    static readonly Vector2 W2 = new Vector2(9F, 6F);      // end of the diagonal       -> turn left
    static readonly Vector2 WP = new Vector2(14F, 6F);     // DUMP box
    // Truck centre distance from the DUMP box centre, as a share of the machine's max reach
    // (was 0.62: dumping now needs the arm further out and the bucket higher over the truck wall).
    const float TruckOffset = 0.75F;
    const float PowerLineHeight = 5.8F;        // pole height; wire hangs 0.3 m lower (was 7.5 m)
    const float PowerLineBeyondTruck = WorldBuilder.TruckBedWidth / 2F;   // wire above the far bed wall (was 1.6 m for the 3.2 m bed)
    // Soil counts as IN the truck only when released over the bed itself (was 0.3 m extra around the bed).
    const float TruckDumpMargin = 0F;
    public const bool PitPipeEnabled = true;
    const float PipeDepth = 1.2F;              // pipe below the original ground in the pit

    static readonly Vector3 digPos = new Vector3(-16.5F, 0, -7.5F);
    Vector3 dumpPos;

    BucketLoad load;
    Vector3 f, left, bedCenter, pit;
    int loads, spills, trips;
    bool atDump;
    WorkerWalker pitWorker, truckWorker;
    float pitWorkerAt, truckWorkerAt;
    float reach;

    public override string TaskText => $"Load the truck {Goal} times (bucket at least {MinLoadFill * 100F:0}% full)";
    public override string ResultText => $"Truck loads: {loads} of {Goal}";
    public override Vector3? DumpTarget => bedCenter;
    // C3 digging cues (trajectory, depth arrow, section view): keep the scoops shallow, above the pipe.
    public override float? DigTargetDepth => PitPipeEnabled ? PipeDepth - 0.3F : (float?)null;
    public override bool DumpArea(out Vector3 c, out Vector3 fw, out float len, out float wid) { c = bedCenter; fw = f; len = WorldBuilder.TruckBedLength; wid = WorldBuilder.TruckBedWidth; return true; }
    public override float? TimeLimitSeconds => Limit;
    public override string ProgressText
    {
        get
        {
            float remaining = Mathf.Max(0F, SM.TrialLimit - SM.TrialTime);
            bool loaded = load != null && load.IsLoaded;
            Vector3 c = M.SwingCenter;
            bool atDig = new Vector2(c.x - digPos.x, c.z - digPos.z).magnitude < 1.5F;
            string next;
            bool full = load != null && load.Fill >= MinLoadFill;
            if (loaded && !full && atDig) next = $"Bucket only {Mathf.RoundToInt(load.Fill * 100F)}% full - scoop more until BUCKET LOAD is at least {MinLoadFill * 100F:0}%.";
            else if (loaded && atDump)  next = "Swing the cab LEFT to the truck and empty the bucket into the truck bed." +
                                               (full ? "" : $" (Only {Mathf.RoundToInt(load.Fill * 100F)}% full - this load will not count.)");
            else if (loaded && full)    next = $"REVERSE to the DUMP box: {StickControls.DriveBack}. The cab keeps facing the pit - do not swing it. The lane bends right, then left.";
            else if (loaded)            next = $"Bucket only {Mathf.RoundToInt(load.Fill * 100F)}% full - fill it to at least {MinLoadFill * 100F:0}% at the pit before going to the truck.";
            else if (atDig)        next = "Fill the bucket from the brown pit in front of you.";
            else                   next = "Drive FORWARD along the lane back to the DIG box.";
            return $"Loads: {loads} / {Goal}      Time left: {Mathf.FloorToInt(remaining / 60)}:{Mathf.FloorToInt(remaining % 60):00}\nNext: {next}";
        }
    }

    // A point given in lane coordinates (back from the DIG box, to the right), on the ground.
    Vector3 L(float back, float rightOff) => WorldBuilder.OnGround(digPos - f * back - left * rightOff);
    Vector3 L(Vector2 v) => L(v.x, v.y);

    // Where the driving route leads (C3 driving path AND the task panel's line map), 2026-11-01:
    //   loaded bucket -> DUMP box; empty after dumping -> DIG box.
    // The route is shown ONLY when the work at the current box is done AND the machine is in DRIVE mode:
    //   - to the DUMP box: once the bucket is loaded to at least MinLoadFill (80 %) and DRIVE mode is on;
    //   - back to the DIG box: once the soil has been dumped and DRIVE mode is on.
    // No route at the start (empty, at the DIG box), while digging / dumping (DIG mode), or once inside the target box.
    bool routeToDump, hasRoute, dumpedSinceLoad;

    void UpdateRouteTarget()
    {
        bool loaded = load != null && load.IsLoaded;
        if (loaded) dumpedSinceLoad = false;
        bool fullEnough = loaded && load.Fill >= MinLoadFill;   // route to the truck only for a load that will count
        routeToDump = loaded;
        Vector3 target = loaded ? dumpPos : digPos, c = M.SwingCenter;
        bool atTarget = new Vector2(c.x - target.x, c.z - target.z).magnitude < 1.2F;
        hasRoute = Ctl != null && Ctl.IsTravelMode && !atTarget && (fullEnough || (!loaded && dumpedSinceLoad));
    }

    public override string RouteTargetName => routeToDump ? "DUMP BOX" : "DIG BOX";

    // Remaining lane points in driving order, or null when no route is shown (see above).
    System.Collections.Generic.List<Vector3> CurrentRoute()
    {
        UpdateRouteTarget();
        if (!hasRoute) return null;
        return routeToDump ? new System.Collections.Generic.List<Vector3> { L(W1), L(W2), L(WP) }
                           : new System.Collections.Generic.List<Vector3> { L(W2), L(W1), L(Vector2.zero) };
    }

    // C3 driving cue: along the lane to the DUMP box (reversing) or back to the DIG box (forward).
    public override System.Collections.Generic.List<Vector3> RouteAhead(out bool reverse)
    {
        var route = CurrentRoute();
        reverse = routeToDump;
        return route;
    }

    protected override void Build()
    {
        f = Vector3.right;
        left = -Vector3.Cross(Vector3.up, f);
        PlaceMachine(digPos.x, digPos.z, f);
        M.EnableCuttingEdges(true);
        reach = M.MaxReach;
        LastReach = reach;

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

        // Power line LOW (wire at 5.5 m) along the far side of the truck bed: lifting the bucket too high or
        // reaching too far over the truck can touch it. Shorter warning distance (2 m) so a normal dump does not warn.
        Vector3 lineMid = dumpPos + left * (TruckOffset * reach + PowerLineBeyondTruck);
        var powerLine = WorldBuilder.PowerLine(Root, "PowerLine", lineMid - f * 8F, lineMid + f * 8F, PowerLineHeight);   // short: not over the DIG box
        // Measured from the real boom/arm/bucket (not the collider hulls): contact = within 0.3 m of the wire.
        powerLine.measure = HazardMeasure.Attachment;
        powerLine.warnDistance = 1.5F;
        powerLine.contactDistance = 0.3F;

        // Water pipe crossing under the pit (across the arm's direction), marked with yellow posts outside the pit.
        if (PitPipeEnabled)
        {
            Vector3 cross = pit + f * Rand(-0.8F, 0.8F);
            Vector3 pa = cross - left * (pitSize / 2F + 1.5F), pb = cross + left * (pitSize / 2F + 1.5F);
            var pipe = new GameObject("PitPipe").AddComponent<SegmentHazard>();
            pipe.transform.SetParent(Root);
            pipe.a = new Vector3(pa.x, TerrainDigger.ReferenceGroundHeight(pa) - PipeDepth, pa.z);
            pipe.b = new Vector3(pb.x, TerrainDigger.ReferenceGroundHeight(pb) - PipeDepth, pb.z);
            pipe.hazardId = "pit_pipe"; pipe.kind = HazardKind.BuriedPipe;
            // distances to the pipe AXIS: pipe radius 0.15 m, so contact = teeth touch the pipe, warning = 0.25 m from it
            pipe.measure = HazardMeasure.Bucket; pipe.warnDistance = 0.4F; pipe.contactDistance = 0.17F;   // whole bucket (2026-11-01)
            // The pipe itself (no collider): hidden by the ground until it is dug free, visible in the cut-away side view.
            var pipeBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pipeBody.name = "PitPipeBody";
            pipeBody.transform.SetParent(Root, true);
            Object.Destroy(pipeBody.GetComponent<Collider>());
            pipeBody.transform.position = (pipe.a + pipe.b) / 2F;
            pipeBody.transform.up = (pipe.b - pipe.a).normalized;
            pipeBody.transform.localScale = new Vector3(0.3F, (pipe.b - pipe.a).magnitude / 2F, 0.3F);
            pipeBody.GetComponent<Renderer>().sharedMaterial = WorldBuilder.MakeMaterial(new Color(0.2F, 0.42F, 0.75F));
            foreach (float sgn in new[] { -1F, 1F })
            {
                Vector3 post = cross + left * sgn * (pitSize / 2F + 0.6F);
                WorldBuilder.Shape(PrimitiveType.Cylinder, Root, "PipeMarker", post, new Vector3(0.12F, 0.55F, 0.12F), WorldBuilder.Yellow, 0, false);
                WorldBuilder.GroundMark(Root, "PipeLine", WorldBuilder.OnGround(cross + left * sgn * (pitSize / 2F + 0.45F)), 0.9F, 0.12F, 0F, WorldBuilder.Yellow);
            }
            StudyLogger.Log("layout", $"pit_pipe_depth={PipeDepth:F2};offset={Vector3.Dot(cross - pit, f):F2}");
        }
        BuildLineMap();

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
            dumpedSinceLoad = true;   // route back to the DIG box may now be shown (in DRIVE mode)
            bool inTruck = InRect(p, bedCenter, f, WorldBuilder.TruckBedLength, WorldBuilder.TruckBedWidth, TruckDumpMargin);
            if (inTruck && load.LastDumpAmount < MinLoadFill - 1e-3F)
                StudyLogger.Log("truck_load_too_small", $"amount={load.LastDumpAmount:F2};min={MinLoadFill:F2}");   // in the truck, but not counted
            else if (inTruck) { loads++; StudyLogger.Log("truck_load", $"{loads};amount={load.LastDumpAmount:F2}"); if (loads >= Goal) Complete("completed"); }
            else { spills++; StudyLogger.Log("spill", $"missed_truck;amount={load.LastDumpAmount:F2}"); }
        };
    }

    // ---- maps ----
    // Simple line map for the task panel: the lane, a "you are here" dot and the current destination.
    void BuildLineMap()
    {
        Color lane = new Color(0.75F, 0.78F, 0.8F);
        var m = new MiniMap { Up = f, LineWidth = 6F, FontSize = 12, Background = new Color(0.1F, 0.1F, 0.1F, 0.6F) };
        m.Line(L(Vector2.zero), L(W1), lane);
        m.Line(L(W1), L(W2), lane);
        m.Line(L(W2), L(WP), lane);
        m.Label(digPos + f * 2.6F, "DIG", WorldBuilder.Yellow);
        m.Label(dumpPos - f * 2.6F, "DUMP", WorldBuilder.Yellow);
        m.Here = () => M.SwingCenter;
        // Destination ring + direction arrows only when a route is shown (loaded / dumped, and in DRIVE mode).
        m.Target = () => routeToDump ? dumpPos : digPos;
        m.Route = CurrentRoute;
        Map = m;
    }

    // Machine reach used by the briefing map before the scenario is built (updated from the real machine).
    public static float LastReach = 10.14F;
    static Vector3 LS(float back, float rightOff) => digPos - Vector3.right * back - new Vector3(0, 0, 1) * rightOff;   // f = +x, left = +z

    // Detailed site map for the task instruction screen (drawn before the trial, so it uses static geometry).
    public static MiniMap SiteMap()
    {
        Vector3 F = Vector3.right, Lf = new Vector3(0, 0, 1);
        float reach = LastReach;
        Vector3 dig = digPos, dump = LS(WP.x, WP.y), pit = dig + F * (0.6F * reach);
        Vector3 truck = dump + Lf * (TruckOffset * reach) + F * 1F, line = dump + Lf * (TruckOffset * reach + PowerLineBeyondTruck);
        Color yellow = WorldBuilder.Yellow, cyan = new Color(0.1F, 0.85F, 1F), brown = new Color(0.55F, 0.38F, 0.22F), grey = new Color(0.75F, 0.75F, 0.72F);

        var m = new MiniMap { Up = F, LineWidth = 4F, FontSize = 15 };
        m.Rect(pit, 4.5F, 4.5F, brown);
        m.Label(pit + F * 1.4F, "PIT", Color.white);
        m.Rect(dig, 5.5F, 4.5F, yellow, false);
        m.Label(dig, "DIG\n(start)", yellow);
        m.Rect(dump, 5.5F, 4.5F, yellow, false);
        m.Label(dump, "DUMP", yellow);
        m.Rect(truck, 8.5F, WorldBuilder.TruckBedWidth, new Color(0.95F, 0.8F, 0.15F));
        m.Label(truck, "TRUCK", Color.black);
        m.Line(line - F * 8F, line + F * 8F, new Color(0.05F, 0.05F, 0.05F));
        m.Label(line + F * 9.6F, "LOW power line", new Color(0.85F, 0.85F, 0.85F));
        m.Line(pit - Lf * 3.4F, pit + Lf * 3.4F, new Color(0.3F, 0.6F, 1F));
        m.Label(pit + Lf * 4.6F, "pipe", new Color(0.4F, 0.7F, 1F));
        m.Line(LS(0, 0), LS(W1.x, W1.y), cyan);
        m.Line(LS(W1.x, W1.y), LS(W2.x, W2.y), cyan);
        m.Line(LS(W2.x, W2.y), LS(WP.x, WP.y), cyan);
        m.Label((LS(W1.x, W1.y) + LS(W2.x, W2.y)) / 2F + Lf * -2.6F, "lane", cyan);
        for (int i = 0; i < 3; i++) m.Rect(LS(8F, -3.0F + i * 1.05F), 0.6F, 1.0F, grey);
        m.Label(LS(9.6F, -2F), "blocks", grey);
        m.Line(LS(8.5F, 9.6F), LS(12.5F, 9.6F), WorldBuilder.Orange);
        m.Label(LS(10.5F, 11.3F), "fence", WorldBuilder.Orange);
        foreach (var br in new[] { LS(2.5F, 3.4F), LS(4F, 5.1F), LS(16.5F, 8.6F) }) m.Dot(br, 0.4F, new Color(0.3F, 0.55F, 1F));
        m.Label(LS(4F, 6.8F), "barrels", new Color(0.4F, 0.65F, 1F));
        m.Label(dig + F * 7.5F + Lf * 6F, "\u2191 cab faces this way", Color.white);

        m.AddLegend(MiniMap.Kind.Fill, brown, "PIT: dig the soil here (only inside it)");
        m.AddLegend(MiniMap.Kind.Outline, yellow, "DIG box / DUMP box: where the machine stands to dig / to dump");
        m.AddLegend(MiniMap.Kind.Fill, new Color(0.95F, 0.8F, 0.15F), "TRUCK: empty the bucket into its bed");
        m.AddLegend(MiniMap.Kind.Line, cyan, "Lane: drive along it between the two boxes");
        m.AddLegend(MiniMap.Kind.Line, new Color(0.3F, 0.6F, 1F), "Water PIPE under the pit, between the YELLOW posts - do not dig into it");
        m.AddLegend(MiniMap.Kind.Line, new Color(0.05F, 0.05F, 0.05F), "LOW power line beyond the truck - keep the bucket away from it");
        m.AddLegend(MiniMap.Kind.Fill, grey, "Concrete blocks, orange fence, blue barrels: obstacles - do not hit them");
        return m;
    }

    protected override void Tick()
    {
        UpdateRouteTarget();
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
