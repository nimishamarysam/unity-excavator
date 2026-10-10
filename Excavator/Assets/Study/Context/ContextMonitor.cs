using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// CONTEXT MONITOR
// Works out what the operator is doing right now (the "task phase") and which
// hazards are currently close. Runs in ALL conditions, so the phase is logged
// for every participant; only C2/C3 use it to change what is shown.
//
// Phases:
//   Driving    travel mode, moving forward
//   Reversing  travel mode, moving backwards
//   Digging    bucket in or near the dig zone, not yet loaded
//   Carrying   bucket loaded, on its way to the dump place
//   Dumping    bucket loaded and near the truck / spoil square
//   Idle       nothing of the above
// A new phase must last 0.5 s before it is accepted, so it does not flicker.
// ---------------------------------------------------------------------------

public enum TaskPhase { Idle, Driving, Reversing, Digging, Carrying, Dumping }

public class ContextMonitor : MonoBehaviour
{
    public static ContextMonitor Instance { get; private set; }

    public float rateHz = 10F;
    public float holdSeconds = 0.5F;

    public TaskPhase Phase { get; private set; } = TaskPhase.Idle;
    public float PhaseSince { get; private set; }

    // Hazards that are currently at warning or contact level, closest first.
    public readonly List<Hazard> ActiveHazards = new List<Hazard>();

    ExcavatorController ctl;
    Scenario scenario;
    BucketLoad load;
    TaskPhase candidate;
    float candidateSince, next;

    public void Init(ExcavatorController controller, Scenario sc)
    {
        Instance = this;
        ctl = controller;
        scenario = sc;
        load = sc.GetComponent<BucketLoad>();
        StudyLogger.Log("context_phase", Phase.ToString());
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void FixedUpdate()
    {
        var sm = StudyManager.Instance;
        if (ctl == null || ctl.Machine == null || sm == null || !sm.IsTrialRunning) return;
        if (Time.time < next) return;
        next = Time.time + 1F / rateHz;

        // Hazards close right now.
        ActiveHazards.Clear();
        foreach (var h in Hazard.All)
            if (h.active && h.CurrentLevel != Hazard.Level.Clear) ActiveHazards.Add(h);
        ActiveHazards.Sort((a, b) => a.CurrentDistance.CompareTo(b.CurrentDistance));

        // Phase, with a short hold time before switching.
        TaskPhase now = Detect();
        if (now != candidate) { candidate = now; candidateSince = Time.time; }
        if (candidate != Phase && Time.time - candidateSince >= holdSeconds)
        {
            Phase = candidate;
            PhaseSince = Time.time;
            StudyLogger.Log("context_phase", Phase.ToString());
        }
    }

    TaskPhase Detect()
    {
        var m = ctl.Machine;
        var input = ctl.LastInput;

        if (ctl.IsTravelMode)
        {
            float v = Vector3.Dot(m.Body.linearVelocity, m.ForwardFlat);
            bool stickBack = input.trackLeft < 0 || input.trackRight < 0;
            bool stickFwd = input.trackLeft > 0 || input.trackRight > 0;
            if (v < -0.15F || (stickBack && !stickFwd)) return TaskPhase.Reversing;
            if (v > 0.15F || stickFwd) return TaskPhase.Driving;
            // Standing still in travel mode: keep the last travel phase.
            return Phase == TaskPhase.Reversing || Phase == TaskPhase.Driving ? Phase : TaskPhase.Idle;
        }

        if (load == null) return TaskPhase.Idle;
        Vector3 tip = m.BucketTipPosition;

        if (load.IsLoaded)
        {
            Vector3? target = scenario.DumpTarget;
            if (target.HasValue)
            {
                Vector3 d = target.Value - tip; d.y = 0F;
                if (d.magnitude < 4F) return TaskPhase.Dumping;
            }
            return TaskPhase.Carrying;
        }

        var digger = TerrainDigger.Instance;
        float heightAboveOriginal = tip.y - TerrainDigger.ReferenceGroundHeight(tip);
        if (load.Fill > 0F || (digger != null && digger.Contains(tip, 2.5F) && heightAboveOriginal < 1.5F))
            return TaskPhase.Digging;

        return TaskPhase.Idle;
    }
}
