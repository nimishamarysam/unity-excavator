using System.Collections;
using UnityEngine;

// ---------------------------------------------------------------------------
// SCENARIO BASE + RUNNER
// A Scenario builds its objects at trial start, checks progress while the
// trial runs, and tells the StudyManager when the task is done.
// The ScenarioRunner (created automatically) picks the right scenario for
// each trial and shows the short task panel at the top-left of the screen.
// ---------------------------------------------------------------------------

public abstract class Scenario : MonoBehaviour
{
    protected ExcavatorController Ctl;
    protected Excavator M;
    protected TrialInfo Trial;
    protected System.Random Rng;
    protected Transform Root;            // parent of all scenario objects
    protected StudyManager SM => StudyManager.Instance;

    // SA PROBE VARIANTS (2026-11-06, for the post-task SAGAT-style questions).
    // Every scenario is played twice per participant (C1 and C3) with the same fixed layout, so questions about
    // fixed things would be answered from memory. Each run therefore gets variant 0 or 1, which changes a few
    // details that the questions ask about but that do not change the task (see each scenario's "sa_truth" log line):
    //   Driving:      side the crossing worker comes from; number and side of bystanders (standing workers, far from the route)
    //   Digging:      direction the worker walks in from (front / behind); number and position of bystanders
    //   Dig and dump: side of the pit worker (front-left / front-right); number and position of bystanders
    // The participant's two runs of a scenario always get DIFFERENT variants. Which condition gets which variant is
    // counterbalanced in groups of 4 participants (P1, P2: C1 = 0, C3 = 1; P3, P4: C1 = 1, C3 = 0; ...), so
    // variant is not confounded with condition or with order. false = previous behaviour (random / fixed details, no bystanders).
    public static bool SaVariants = true;
    protected int Variant { get; private set; }
    public static int VariantFor(int participantId, StudyCondition c)
    {
        int pair = (Mathf.Max(0, participantId - 1) / 2) % 2;
        return (pair + (c == StudyCondition.C3_AttentionGuided ? 1 : 0)) % 2;
    }

    // A worker who just stands still (faces 'lookAt'), part of the SA questions ("how many workers ..."). Placed
    // far from the work area, so no warnings in normal work. Hazard id starts with "bystander" (separate in the analysis).
    protected WorkerWalker Bystander(string name, Vector3 pos, Vector3 lookAt)
    {
        var w = WorldBuilder.Worker(Root, name, WorldBuilder.KeepDry(pos, lookAt));   // never in the river
        Vector3 d = lookAt - w.transform.position; d.y = 0F;
        if (d.sqrMagnitude > 0.01F) w.transform.rotation = Quaternion.LookRotation(d);
        return w;
    }

    public abstract string TaskText { get; }
    public virtual string ProgressText => "";
    // Result shown on the "Task complete" screen (e.g. "Truck loads: 3 of 4"); "" = none.
    public virtual string ResultText => "";

    // Where loaded soil should go (truck bed or spoil square), or null if the scenario has no dumping.
    public virtual Vector3? DumpTarget => null;

    // The area soil should be dumped into (truck bed or spoil square), for the C3 dump marker.
    // Returns false if the scenario has none.
    public virtual bool DumpArea(out Vector3 center, out Vector3 forward, out float length, out float width)
    { center = forward = Vector3.zero; length = width = 0F; return false; }

    // For the C3 driving cue: the rest of the route as ground points in driving order
    // (null = no route). 'reverse' = the machine should drive backwards to these points.
    public virtual System.Collections.Generic.List<Vector3> RouteAhead(out bool reverse) { reverse = false; return null; }

    // Name of the current route destination for the C3 banner (e.g. "DUMP BOX").
    public virtual string RouteTargetName => "STOP LINE";

    // For the C3 digging cues: target depth of the dig zone below the original ground in metres
    // (null = use the zone's maximum depth).
    public virtual float? DigTargetDepth => null;

    // Deeper than the target by more than this = over-dig. ONE value for the scored incident AND the cues
    // (red depth arrow / teeth line / depth gauge / red cut-away shading), so what turns red is what counts.
    public virtual float OverDigTolerance => 0.15F;

    // Time limit of this scenario in seconds (null = StudyManager.trialDurationSeconds).
    public virtual float? TimeLimitSeconds => null;

    // Optional site sketch for the task panel (null = none). Filled by the scenario in Build.
    public MiniMap Map { get; protected set; }

    public void Setup(ExcavatorController ctl, TrialInfo trial)
    {
        Ctl = ctl; M = ctl.Machine; Trial = trial;
        Rng = new System.Random(trial.seed);
        Root = transform;
        Variant = SaVariants && SM != null ? VariantFor(SM.ParticipantId, trial.condition) : 0;
        BucketBlocker.Solids.Clear();
        if (StartRetracted) Retract();
        if (SaVariants && trial.scenario != StudyScenario.Practice) StudyLogger.Log("sa_variant", Variant.ToString());
        Build();
    }

    // START POSTURE: true = every trial starts with the boom fully raised and the arm hanging vertically
    // (fully retracted, compact). The bucket keeps its angle. false = posture from the scene.
    public static bool StartRetracted = true;

    // Finds the boom angle that lifts the boom-arm joint highest, then the arm angle that makes the
    // arm point straight down. Searched numerically so it does not depend on the model's angle conventions.
    void Retract()
    {
        float bestBoom = M.boomAngle, bestY = float.MinValue;
        for (float a = 0F; a <= 56F; a += 1F)
        {
            M.boomAngle = a;
            float y = M.BoomArmJointPosition.y;
            if (y > bestY) { bestY = y; bestBoom = a; }
        }
        M.boomAngle = bestBoom;

        float bestArm = M.armAngle, bestDev = float.MaxValue;
        for (float a = 0F; a <= 120F; a += 1F)
        {
            M.armAngle = a;
            float dev = Vector3.Angle(M.ArmBucketJointPosition - M.BoomArmJointPosition, Vector3.down);
            if (dev < bestDev) { bestDev = dev; bestArm = a; }
        }
        M.armAngle = bestArm;
        Physics.SyncTransforms();
        StudyLogger.Log("start_posture", $"boom={bestBoom:F0};arm={bestArm:F0};arm_from_vertical={bestDev:F1};bucket={M.bucketAngle:F0}");
    }

    protected abstract void Build();
    protected virtual void Tick() { }
    public virtual string Summary() => "";

    void FixedUpdate()
    {
        if (SM != null && SM.IsTrialRunning && SM.CurrentTrial.index == Trial.index) Tick();
    }

    protected void Complete(string reason) => SM.EndTrial(reason);

    protected float Rand(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

    // Place the machine so its swing centre is at (x, z), tracks facing 'direction'.
    protected void PlaceMachine(float x, float z, Vector3 direction)
    {
        Transform rootT = M.transform;
        float groundOffset = rootT.position.y - WorldBuilder.Ground(rootT.position.x, rootT.position.z);

        float delta = Vector3.SignedAngle(M.ForwardFlat, new Vector3(direction.x, 0, direction.z), Vector3.up);
        rootT.Rotate(0F, delta, 0F, Space.World);

        Vector3 off = M.SwingCenter - rootT.position; off.y = 0F;
        Vector3 p = new Vector3(x, 0, z) - off;
        p.y = WorldBuilder.Ground(p.x, p.z) + groundOffset + 0.05F;
        rootT.position = p;
        M.Body.position = p;
        M.Body.rotation = rootT.rotation;
        M.Body.linearVelocity = Vector3.zero;
        M.Body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    // A point relative to the machine's starting position: 'ahead' metres forward, 'right' metres to the right.
    protected Vector3 Rel(float ahead, float right)
    {
        Vector3 f = M.ForwardFlat, r = Vector3.Cross(Vector3.up, f);
        return WorldBuilder.OnGround(M.SwingCenter + f * ahead + r * right);
    }

    // Four thin tapes around a rectangle on the ground, plus corner stakes.
    protected void Outline(string name, Vector3 center, Vector3 forward, float length, float width, Color color)
    {
        Vector3 f = new Vector3(forward.x, 0, forward.z).normalized, r = Vector3.Cross(Vector3.up, f);
        float yaw = Quaternion.LookRotation(f).eulerAngles.y;
        WorldBuilder.GroundMark(Root, name + "_tapeL", center - r * (width / 2F), length, 0.15F, yaw, color);
        WorldBuilder.GroundMark(Root, name + "_tapeR", center + r * (width / 2F), length, 0.15F, yaw, color);
        WorldBuilder.GroundMark(Root, name + "_tapeF", center + f * (length / 2F), 0.15F, width, yaw, color);
        WorldBuilder.GroundMark(Root, name + "_tapeB", center - f * (length / 2F), 0.15F, width, yaw, color);
        foreach (var c in new[] { f * length / 2F + r * width / 2F, f * length / 2F - r * width / 2F, -f * length / 2F + r * width / 2F, -f * length / 2F - r * width / 2F })
            WorldBuilder.Shape(PrimitiveType.Cylinder, Root, name + "_stake", center + c, new Vector3(0.08F, 0.4F, 0.08F), color, 0, false);
    }

    // DUMPED SOIL PARTLY OUTSIDE THE DUMP AREA (2026-11-02): the soil lands as a round pile of radius SpillRadius(amount)
    // around the release point; a dump is a SPILL (incident) when more than SpillOutsideShare of that pile lies outside
    // the dump area (spoil square / truck bed). Before, only the release point was checked (with 0.3 m extra margin).
    public const float SpillOutsideShare = 0.25F;
    public static float SpillRadius(float amount) => 0.35F + 0.35F * Mathf.Clamp01(amount);   // full bucket: 0.7 m

    // Share (0..1) of a disc (centre p, radius rad) that lies outside the rectangle.
    protected static float OutsideShare(Vector3 p, float rad, Vector3 center, Vector3 forward, float length, float width)
    {
        const int n = 12;
        int total = 0, outside = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float u = ((i + 0.5F) / n * 2F - 1F) * rad, v = ((j + 0.5F) / n * 2F - 1F) * rad;
                if (u * u + v * v > rad * rad) continue;
                total++;
                if (!InRect(p + new Vector3(u, 0F, v), center, forward, length, width)) outside++;
            }
        return total == 0 ? 0F : outside / (float)total;
    }

    // WORKING POSITION (2026-11-02): each scoop (bucket filled) and each dump is checked against the marked yellow
    // box the machine should stand in. Always logged as "position_check"; outside the box (swing centre outside the
    // rectangle) also as "not_in_box", which is an incident ("Dug / dumped from outside the marked box").
    protected void CheckInBox(string what, string boxName, Vector3 boxCenter, Vector3 forward, float length, float width)
    {
        Vector3 c = M.SwingCenter, fw = new Vector3(forward.x, 0F, forward.z).normalized, rt = Vector3.Cross(Vector3.up, fw);
        Vector3 d = c - boxCenter;
        bool inBox = InRect(c, boxCenter, fw, length, width);
        string pos = $"along={Vector3.Dot(d, fw):F2};across={Vector3.Dot(d, rt):F2}";
        StudyLogger.Log("position_check", $"{what};box={boxName};in_box={(inBox ? 1 : 0)};{pos}");
        if (!inBox) StudyLogger.Log("not_in_box", $"{what};box={boxName};{pos}");
    }

    protected static bool InRect(Vector3 p, Vector3 center, Vector3 forward, float length, float width, float margin = 0F)
    {
        Vector3 f = new Vector3(forward.x, 0, forward.z).normalized, r = Vector3.Cross(Vector3.up, f), d = p - center;
        return Mathf.Abs(Vector3.Dot(d, f)) <= length / 2F + margin && Mathf.Abs(Vector3.Dot(d, r)) <= width / 2F + margin;
    }
}

public class ScenarioRunner : MonoBehaviour
{
    public static Scenario Current { get; private set; }
    Scenario current;
    GUIStyle title, body, incidentStyle;

    void OnEnable()
    {
        StudyManager.TrialStarted += OnTrialStarted;
        StudyManager.TrialEnded += OnTrialEnded;
    }

    void OnDisable()
    {
        StudyManager.TrialStarted -= OnTrialStarted;
        StudyManager.TrialEnded -= OnTrialEnded;
    }

    // A trial can end before its scenario was built (e.g. skipped right after the start). The waiting build must
    // NOT continue into the next trial's scene: that left two or three scenarios alive at once (each with its own
    // bucket load, digging and cues: doubled log events, stacked cues, bucket load 0 % while soil was dug).
    void OnTrialStarted(TrialInfo trial)
    {
        StopAllCoroutines();   // cancel a build that is still waiting for an earlier trial
        RemoveScenarios();
        StartCoroutine(Build(trial));
    }

    // Removes every scenario object that is still alive (normally none: the scene reload destroys them).
    void RemoveScenarios()
    {
        foreach (var sc in FindObjectsByType<Scenario>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            StudyLogger.Log("scenario_removed", sc.gameObject.name);
            Destroy(sc.gameObject);
        }
        current = null;
        Current = null;
    }

    IEnumerator Build(TrialInfo trial)
    {
        var sm = StudyManager.Instance;
        // Wait until the excavator has finished its own start-up after the scene reload.
        while (sm.Controller == null || sm.Controller.Machine == null) yield return null;
        yield return new WaitForFixedUpdate();
        // The trial may have ended (or the next one started) while waiting: then build nothing.
        if (!sm.IsTrialRunning || sm.CurrentTrial.index != trial.index) yield break;

        var go = new GameObject("Scenario_" + trial.scenario);
        go.AddComponent<TerrainDigger>();
        switch (trial.scenario)
        {
            case StudyScenario.Practice: current = go.AddComponent<PracticeScenario>(); break;
            case StudyScenario.Easy_Driving: current = go.AddComponent<DrivingScenario>(); break;
            case StudyScenario.Moderate_Digging: current = go.AddComponent<DiggingScenario>(); break;
            default: current = go.AddComponent<DigDumpScenario>(); break;
        }
        Current = current;
        current.Setup(sm.Controller, trial);
        go.AddComponent<HazardTracker>().Init(sm.Controller);   // after Setup, so it sees the final collider set
        go.AddComponent<ContextMonitor>().Init(sm.Controller, current);
        go.AddComponent<ContextViews>().Init(sm.Controller);    // only visible in C2 and C3
        go.AddComponent<PopupViews>().Init(sm.Controller);      // C2/C3, travel mode, when ContextViews.PopupStyleForDriving
        go.AddComponent<AttentionCues>().Init(sm.Controller, current);
        go.AddComponent<SoilSideBlocker>().Init(sm.Controller); // no swinging sideways while the bucket is buried in soil
        go.AddComponent<BucketBlocker>().Init(sm.Controller);   // bucket/arm cannot pass through the truck   // only visible in C3
        StudyLogger.Log("scenario_ready", trial.scenario.ToString());
    }

    void OnTrialEnded(TrialInfo trial, string reason)
    {
        StopAllCoroutines();   // a build still waiting for this trial must not run later
        if (current != null) StudyLogger.Log("scenario_summary", current.Summary());
    }

    // Task panel: same in every condition (it is the task, not an assistance cue).
    void OnGUI()
    {
        var sm = StudyManager.Instance;
        if (current == null || sm == null || sm.CurrentState != StudyManager.State.Running) return;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
            body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        }
        if (incidentStyle == null) incidentStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };

        // Display 1 text size: the whole panel (text, incident line, map) is scaled from the top-left corner.
        var oldMatrix = GUI.matrix;
        float ts = StudyLayout.TextScale * TaskPanelScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(ts, ts, 1F)) * oldMatrix;
        try { DrawTaskPanel(); }
        finally { GUI.matrix = oldMatrix; }
    }

    // COMPACT TASK PANEL (2026-11-07): smaller text, narrower, and only as tall as its text, so it covers less of the
    // driver view and the C3 cues. TaskPanelScale = its size relative to the Display 1 text size (+ / - keys).
    public static float TaskPanelScale = 0.85F;
    const float PanelWidth = 430F, PanelWidthMap = 530F, PanelPad = 10F, IncH = 20F, MapW = 105F, MapMinH = 110F;

    // Where the task panel / its incident line were drawn last (GUI pixels, y down), for the practice-guide
    // outlines and the C3 banner (kept below the panel). Valid while the panel is shown.
    static Rect lastPanel, lastIncidents;
    static int lastPanelFrame = -10;
    public static bool TryGetPanelRect(out Rect panel) { panel = lastPanel; return Time.frameCount - lastPanelFrame <= 2; }
    public static bool TryGetIncidentRect(out Rect inc) { inc = lastIncidents; return Time.frameCount - lastPanelFrame <= 2; }

    void DrawTaskPanel()
    {
        // Task panel: task, progress and incidents on the left; (if the scenario has one) a small line map on the right.
        bool hasMap = current.Map != null;
        float width = hasMap ? PanelWidthMap : PanelWidth;
        float textW = width - 2F * PanelPad - (hasMap ? MapW + 8F : 0F);
        string task = current.TaskText, prog = current.ProgressText;
        float th = title.CalcHeight(new GUIContent(task), textW);
        float bh = string.IsNullOrEmpty(prog) ? 0F : body.CalcHeight(new GUIContent(prog), textW);
        float h = Mathf.Max(th + 2F + bh + 4F + IncH + 2F * PanelPad, hasMap ? MapMinH : 0F);
        var r = new Rect(12, 12, width, h);
        GUI.color = new Color(0, 0, 0, 0.55F);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x + PanelPad, r.y + PanelPad, textW, th), task, title);
        if (bh > 0F) GUI.Label(new Rect(r.x + PanelPad, r.y + PanelPad + th + 2F, textW, bh), prog, body);
        var incRect = new Rect(r.x + PanelPad, r.yMax - PanelPad - IncH, textW, IncH);
        float sc = GUI.matrix.m00;   // panel scale (set in OnGUI)
        lastPanel = new Rect(r.x * sc, r.y * sc, r.width * sc, r.height * sc);
        lastIncidents = new Rect(r.x * sc, (incRect.y - 4F) * sc, r.width * sc, (IncH + 8F) * sc);
        lastPanelFrame = Time.frameCount;

        // Incident line: counter + the last incident; red for a few seconds after a new one.
        float ago = Time.unscaledTime - IncidentFeedback.LastIncidentTime;
        string inc = IncidentFeedback.Count == 0 ? "Incidents: 0"
                   : $"Incidents: {IncidentFeedback.Count}    Last: {IncidentFeedback.LastDescription} ({Mathf.FloorToInt(ago)} s ago)";
        incidentStyle.normal.textColor = IncidentFeedback.Count > 0 && ago < 4F ? new Color(1F, 0.35F, 0.3F) : new Color(0.85F, 0.85F, 0.85F);
        GUI.Label(incRect, inc, incidentStyle);

        if (hasMap) current.Map.Draw(new Rect(r.xMax - PanelPad - MapW, r.y + PanelPad, MapW, r.height - 2F * PanelPad));
    }
}
