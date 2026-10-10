using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// ATTENTION-GUIDING CUES  (condition C3 only)
// Created for every trial by the ScenarioRunner; shows nothing in C1 and C2.
//
//   1. Static obstacle   (ObstacleCues)  rectangle around the object in the driver view,
//                                         3D highlight (box + ground ring) in the context-aware views
//   2. Dynamic obstacle  (ObstacleCues)  same, but the overlay follows the worker; the label says
//                                         which way the worker is moving
//   3. Digging area      (DiggingCues)   outline over the pit boundary + marker on the ground
//                                         straight below the bucket teeth
//   4. Driving path      (DrivingCues)   path on the ground to the next gate / stop line that bends
//                                         around obstacles, moving arrows, and a short instruction
//                                         banner (steer left/right, pass right of it, stop for worker)
//   5. Bucket trajectory (DiggingCues)   suggested path of the teeth for the next scoop
//
// The 3D highlights of (1)/(2) are on CueKit.ContextOnlyLayer, so only the context cameras draw them;
// the driver view gets the flat 2D rectangle instead. Every cue logs cue_on / cue_off so the
// timing of a cue can be compared with hazard_warning / hazard_response in the analysis.
// ---------------------------------------------------------------------------

public class AttentionCues : MonoBehaviour
{
    // EXPERIMENT SWITCHES. All true = full C3. Switch single cue types off to test them separately.
    public static bool StaticObstacleCues = true;
    public static bool DynamicObstacleCues = true;
    public static bool DigAreaCues = true;
    public static bool DrivingPathCues = true;
    public static bool BucketTrajectoryCues = true;

    public static AttentionCues Instance { get; private set; }

    // Which cues (and how much detail) the current scenario shows. Chosen in Init from the scenario.
    public CueProfile Profile { get; private set; } = new CueProfile();

    public ExcavatorController Ctl { get; private set; }
    public Excavator M => Ctl != null ? Ctl.Machine : null;
    public Scenario Scenario { get; private set; }
    public Transform WorldRoot { get; private set; }    // parent of all 3D cue objects
    public Transform CanvasRoot { get; private set; }   // parent of all driver-view cue objects
    public Font Font { get; private set; }
    public ObstacleCues Obstacles { get; private set; }
    public DrivingCues Driving { get; private set; }

    // Written by DrivingCues: which side to pass an obstacle on. Shown in that obstacle's label.
    public readonly Dictionary<Hazard, string> PassAdvice = new Dictionary<Hazard, string>();

    bool wasVisible;

    public bool Visible
    {
        get
        {
            var sm = StudyManager.Instance;
            return sm != null && sm.ShowAttentionCues && M != null &&
                   (sm.CurrentState == StudyManager.State.Running || sm.CurrentState == StudyManager.State.Paused);
        }
    }

    public void Init(ExcavatorController controller, Scenario scenario)
    {
        Instance = this;
        Ctl = controller;
        Scenario = scenario;
        Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Profile = CueProfile.For(StudyManager.Instance != null ? StudyManager.Instance.CurrentTrial.scenario : StudyScenario.Practice);
        StudyLogger.Log("cue_profile", Profile.name);

        var w = new GameObject("CueWorld");
        w.transform.SetParent(transform, false);
        WorldRoot = w.transform;
        CanvasRoot = CueKit.MakeCanvas(transform, "CueCanvas", 42).transform;   // above the camera frames (40), below pop-up frames (45)

        // The cab camera (and the machine's mirror/rear cameras) must not draw the context-only highlights.
        foreach (var cam in controller.Machine.transform.GetComponentsInChildren<Camera>(true))
            cam.cullingMask &= ~(1 << CueKit.ContextOnlyLayer);

        Obstacles = gameObject.AddComponent<ObstacleCues>();
        Obstacles.Init(this);
        Driving = gameObject.AddComponent<DrivingCues>();
        Driving.Init(this);
        gameObject.AddComponent<DiggingCues>().Init(this);

        WorldRoot.gameObject.SetActive(false);
        CanvasRoot.gameObject.SetActive(false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void LateUpdate()
    {
        bool v = Visible;
        if (v == wasVisible || WorldRoot == null) return;
        wasVisible = v;
        WorldRoot.gameObject.SetActive(v);
        CanvasRoot.gameObject.SetActive(v);
        StudyLogger.Log(v ? "cues_enabled" : "cues_disabled");
    }

    // Combined check: experiment switch AND scenario profile.
    public bool StaticOn => StaticObstacleCues && Profile.staticObstacles;
    public bool DynamicOn => DynamicObstacleCues && Profile.dynamicObstacles;
    public bool DigAreaOn => DigAreaCues && Profile.digArea;
    public bool TrajectoryOn => BucketTrajectoryCues && Profile.trajectory;
    public bool DrivingPathOn => DrivingPathCues && Profile.drivingPath;

    // Logs a cue appearing or disappearing, e.g. cue_on static_obstacle;Barrel1;dist=4.20
    public static void LogCue(bool on, string type, string detail)
    {
        StudyLogger.Log(on ? "cue_on" : "cue_off", type + ";" + detail);
    }
}

// ---------------------------------------------------------------------------
// CUE PROFILE: what each scenario shows in C3. The defaults are the full set; a scenario
// profile switches off what is not needed for that task, to keep the screen readable.
// ---------------------------------------------------------------------------
public class CueProfile
{
    public string name = "full";

    // Which cue types
    public bool staticObstacles = true, dynamicObstacles = true, drivingPath = true, digArea = true, trajectory = true;

    // Obstacle cue detail
    public bool obstaclesOnlyNearPath = false;   // static obstacles: only those close to the planned path (or at warning level)
    public bool gatePostsOnlyAtWarning = false;  // gate posts: only at warning distance
    public bool workerMotionText = true;         // "WALKING LEFT" etc. in the worker label
    public bool workerMotionArrow = true;        // ground arrow of the worker's next 2 s (context views)
    public bool groundRing = true;               // ring on the ground around obstacles (context views)
    public bool edgeMarkersForAll = true;        // false = edge markers only for workers at warning level

    // Driving cue detail
    public bool steeringBanner = true;           // false = banner only for STOP / SLOW (workers)
    public bool pathEndRing = true;

    public static CueProfile For(StudyScenario s)
    {
        switch (s)
        {
            case StudyScenario.Easy_Driving:
                // Driving: (1) static obstacle box + highlight, (2) worker box that follows the worker,
                // with walking direction and ground arrow, (3) path with directions and the steering /
                // reverse banner. Edge markers for objects out of view. No digging cues, no ground
                // rings, no end-of-path ring; static obstacles only near the path, gate posts only when close.
                return new CueProfile
                {
                    name = "driving",
                    digArea = false, trajectory = false,
                    obstaclesOnlyNearPath = true, gatePostsOnlyAtWarning = true,
                    groundRing = false, pathEndRing = false,
                };
            default:
                return new CueProfile();
        }
    }
}
