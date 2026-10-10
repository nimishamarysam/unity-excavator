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

    public abstract string TaskText { get; }
    public virtual string ProgressText => "";

    // Where loaded soil should go (truck bed or spoil square), or null if the scenario has no dumping.
    public virtual Vector3? DumpTarget => null;

    public void Setup(ExcavatorController ctl, TrialInfo trial)
    {
        Ctl = ctl; M = ctl.Machine; Trial = trial;
        Rng = new System.Random(trial.seed);
        Root = transform;
        Build();
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

    protected static bool InRect(Vector3 p, Vector3 center, Vector3 forward, float length, float width, float margin = 0F)
    {
        Vector3 f = new Vector3(forward.x, 0, forward.z).normalized, r = Vector3.Cross(Vector3.up, f), d = p - center;
        return Mathf.Abs(Vector3.Dot(d, f)) <= length / 2F + margin && Mathf.Abs(Vector3.Dot(d, r)) <= width / 2F + margin;
    }
}

public class ScenarioRunner : MonoBehaviour
{
    Scenario current;
    GUIStyle title, body;

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

    void OnTrialStarted(TrialInfo trial) => StartCoroutine(Build(trial));

    IEnumerator Build(TrialInfo trial)
    {
        var sm = StudyManager.Instance;
        // Wait until the excavator has finished its own start-up after the scene reload.
        while (sm.Controller == null || sm.Controller.Machine == null) yield return null;
        yield return new WaitForFixedUpdate();

        var go = new GameObject("Scenario_" + trial.scenario);
        go.AddComponent<TerrainDigger>();
        switch (trial.scenario)
        {
            case StudyScenario.Practice: current = go.AddComponent<PracticeScenario>(); break;
            case StudyScenario.Easy_Driving: current = go.AddComponent<DrivingScenario>(); break;
            case StudyScenario.Moderate_Digging: current = go.AddComponent<DiggingScenario>(); break;
            default: current = go.AddComponent<DigDumpScenario>(); break;
        }
        current.Setup(sm.Controller, trial);
        go.AddComponent<HazardTracker>().Init(sm.Controller);   // after Setup, so it sees the final collider set
        go.AddComponent<ContextMonitor>().Init(sm.Controller, current);
        go.AddComponent<ContextViews>().Init(sm.Controller);    // only visible in C2 and C3
        go.AddComponent<PopupViews>().Init(sm.Controller);      // C2/C3, travel mode, when ContextViews.PopupStyleForDriving
        StudyLogger.Log("scenario_ready", trial.scenario.ToString());
    }

    void OnTrialEnded(TrialInfo trial, string reason)
    {
        if (current != null) StudyLogger.Log("scenario_summary", current.Summary());
    }

    // Task panel: same in every condition (it is the task, not an assistance cue).
    void OnGUI()
    {
        var sm = StudyManager.Instance;
        if (current == null || sm == null || sm.CurrentState != StudyManager.State.Running) return;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true };
            body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
        }
        var r = new Rect(16, 16, 520, 120);
        GUI.color = new Color(0, 0, 0, 0.55F);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16));
        GUILayout.Label(current.TaskText, title);
        GUILayout.Label(current.ProgressText, body);
        GUILayout.EndArea();
    }
}
