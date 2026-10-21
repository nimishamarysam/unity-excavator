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
    public Transform PanelCanvasRoot { get; private set; }   // labels inside the camera windows when those are on display 2

    // The canvas on the same monitor as 'cam', so a label placed in that camera's window shows up there.
    public Transform CanvasFor(Camera cam) =>
        cam != null && cam.targetDisplay != 0 && PanelCanvasRoot != null ? PanelCanvasRoot : CanvasRoot;
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
        if (StudyLayout.PanelDisplay != 0)
        {
            var pc = CueKit.MakeCanvas(transform, "CueCanvas_Display2", 42);
            pc.targetDisplay = StudyLayout.PanelDisplay;
            PanelCanvasRoot = pc.transform;
        }

        // The cab camera (and the machine's mirror/rear cameras) must not draw the context-only highlights.
        foreach (var cam in controller.Machine.transform.GetComponentsInChildren<Camera>(true))
            cam.cullingMask &= ~((1 << CueKit.ContextOnlyLayer) | (1 << CueKit.BucketSideOnlyLayer));

        Obstacles = gameObject.AddComponent<ObstacleCues>();
        Obstacles.Init(this);
        Driving = gameObject.AddComponent<DrivingCues>();
        Driving.Init(this);
        gameObject.AddComponent<DiggingCues>().Init(this);
        gameObject.AddComponent<UndergroundSideView>().Init(this);
        gameObject.AddComponent<BuriedServicesXray>().Init(this);
        gameObject.AddComponent<TruckProximityCue>().Init(this);

        WorldRoot.gameObject.SetActive(false);
        CanvasRoot.gameObject.SetActive(false);
        if (PanelCanvasRoot != null) PanelCanvasRoot.gameObject.SetActive(false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void LateUpdate()
    {
        bool v = Visible;
        if (v == wasVisible || WorldRoot == null) return;
        wasVisible = v;
        WorldRoot.gameObject.SetActive(v);
        CanvasRoot.gameObject.SetActive(v);
        if (PanelCanvasRoot != null) PanelCanvasRoot.gameObject.SetActive(v);
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
    public bool edgeMarkersWorkersOnly = false;  // true = edge markers only for workers (overrides the above)
    public bool staticOnlyAtWarning = false;     // static obstacles only once at warning distance
    public bool truckOnlyAtContact = false;      // the truck is the dump target: cue only at contact
    public bool obstaclesHiddenInDigMode = false;// in digging mode: no boxes for lane obstacles (except contact)
    public bool workerMotionOnlyApproaching = false; // motion text only when the worker comes towards the machine
    public int maxObstacleCues = 0;              // > 0: show only this many obstacle cues, most urgent first
    public bool thinHighlight = false;           // thinner 3D boxes in the context views
    public bool truckWallProximity = false;      // truck bed walls light up when the bucket/arm gets close

    // Driving cue detail
    public bool steeringBanner = true;           // false = banner only for STOP / SLOW (workers)
    public bool pathEndRing = true;
    public bool bannerWithTarget = false;        // "REVERSE 7.0 m TO THE DUMP BOX" (target name from the scenario)
    public float steerThreshold = 15F;           // degrees off the path before "STEER LEFT/RIGHT" is shown
    public bool passAdviceInBanner = true;       // "PASS THE OBSTACLE ON ITS ..." added to the banner

    // Digging cue detail
    public bool trajectoryInBucketSideViewOnly = false;   // true = bucket path drawn only in the BUCKET SIDE VIEW
    public bool bucketSideHeightArrow = false;            // vertical arrow + height/depth in the BUCKET SIDE VIEW
    public bool bucketSideAngleLabel = false;             // current bucket angle next to the bucket in that view
    public bool heightArrowInDriverView = false;          // the height arrow (and its label) also in the driver view
    public bool intoBucketXray = false;                   // buried pipe/cable drawn through the ground in the VIEW INTO BUCKET (C2 + C3)
    public bool bucketSideUnderground = false;            // bucket side view becomes an underground cut-away in the pit (C2 + C3)

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
            case StudyScenario.Moderate_Digging:
                // Digging: dig area outline + teeth marker in all views; the bucket path, height arrow and
                // bucket angle only in the BUCKET SIDE VIEW. No driving-path end ring, no ground rings.
                return new CueProfile
                {
                    name = "digging",
                    trajectoryInBucketSideViewOnly = true,
                    trajectory = false,             // suggested bucket path off: the cut-away shows the soil to dig instead
                    bucketSideHeightArrow = true,
                    bucketSideAngleLabel = true, heightArrowInDriverView = true,
                    bucketSideUnderground = true,   // cut-away bucket side view in the trench (C2 + C3)
                    intoBucketXray = true,          // pipe + cable through the ground in the view into the bucket (C2 + C3)
                    groundRing = false, pathEndRing = false,
                };
            case StudyScenario.Hard_DigAndDump:
                // Dig and dump: only what needs action now. Static obstacles only at warning distance, at most
                // two obstacle cues, truck only at contact, edge markers only for workers, short labels, thin
                // highlights, no ground rings. Banner names the DIG / DUMP box; steering hint only when > 25 deg off.
                // Digging mode: no lane-obstacle boxes; digging overlays as in the Digging scenario.
                return new CueProfile
                {
                    name = "digdump",
                    staticOnlyAtWarning = true, truckOnlyAtContact = true, obstaclesHiddenInDigMode = true,
                    maxObstacleCues = 2, edgeMarkersWorkersOnly = true, workerMotionOnlyApproaching = true,
                    workerMotionArrow = false, groundRing = false, thinHighlight = true,
                    bannerWithTarget = true, steerThreshold = 25F, passAdviceInBanner = false, pathEndRing = false,
                    trajectory = false,   // no suggested bucket path in Dig and dump (too much in the bucket side view)
                    bucketSideHeightArrow = true,
                    truckWallProximity = true, heightArrowInDriverView = true, bucketSideUnderground = true,
                };
            default:
                return new CueProfile();
        }
    }
}
