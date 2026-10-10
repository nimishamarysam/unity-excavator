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

    public ExcavatorController Ctl { get; private set; }
    public Excavator M => Ctl != null ? Ctl.Machine : null;
    public Scenario Scenario { get; private set; }
    public Transform WorldRoot { get; private set; }    // parent of all 3D cue objects
    public Transform CanvasRoot { get; private set; }   // parent of all driver-view cue objects
    public Font Font { get; private set; }
    public ObstacleCues Obstacles { get; private set; }

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

        var w = new GameObject("CueWorld");
        w.transform.SetParent(transform, false);
        WorldRoot = w.transform;
        CanvasRoot = CueKit.MakeCanvas(transform, "CueCanvas", 42).transform;   // above the camera frames (40), below pop-up frames (45)

        // The cab camera (and the machine's mirror/rear cameras) must not draw the context-only highlights.
        foreach (var cam in controller.Machine.transform.GetComponentsInChildren<Camera>(true))
            cam.cullingMask &= ~(1 << CueKit.ContextOnlyLayer);

        Obstacles = gameObject.AddComponent<ObstacleCues>();
        Obstacles.Init(this);
        gameObject.AddComponent<DrivingCues>().Init(this);
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

    // Logs a cue appearing or disappearing, e.g. cue_on static_obstacle;Barrel1;dist=4.20
    public static void LogCue(bool on, string type, string detail)
    {
        StudyLogger.Log(on ? "cue_on" : "cue_off", type + ";" + detail);
    }
}
