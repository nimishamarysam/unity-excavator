// ---------------------------------------------------------------------------
// Shared definitions for the user study: the 3 UI conditions, the 3 scenarios
// and the Latin-square order that decides which condition comes first.
// ---------------------------------------------------------------------------

public enum StudyCondition
{
    C1_Baseline = 0,         // measurement bar only
    C2_ContextAware = 1,     // + context-aware camera views
    C3_AttentionGuided = 2   // + context-aware views + attention-guiding cues
}

public enum StudyScenario
{
    Practice = -1,           // free practice, no hazards
    Easy_Driving = 0,
    Moderate_Digging = 1,
    Hard_DigAndDump = 2
}

public struct TrialInfo
{
    public int index;              // 0 = practice (if used), then 1..9
    public StudyCondition condition;
    public StudyScenario scenario;
    public int seed;               // random seed for hazard positions/timings (logged)

    public override string ToString() => $"Trial {index}: {condition} / {scenario}";
}

public static class StudyDesign
{
    // 3x3 Latin square: each condition appears once in each position.
    static readonly StudyCondition[][] latinSquare =
    {
        new[] { StudyCondition.C1_Baseline, StudyCondition.C2_ContextAware, StudyCondition.C3_AttentionGuided },
        new[] { StudyCondition.C2_ContextAware, StudyCondition.C3_AttentionGuided, StudyCondition.C1_Baseline },
        new[] { StudyCondition.C3_AttentionGuided, StudyCondition.C1_Baseline, StudyCondition.C2_ContextAware },
    };

    // The three scenarios. Their order is shuffled per condition block (see BuildTrials).
    static readonly StudyScenario[] scenarioOrder =
    {
        StudyScenario.Easy_Driving, StudyScenario.Moderate_Digging, StudyScenario.Hard_DigAndDump
        //StudyScenario.Moderate_Digging
    };

    // Participant 1 gets row 0, participant 2 row 1, participant 3 row 2, participant 4 row 0 ...
    public static StudyCondition[] ConditionOrder(int participantId)
    {
        int row = ((participantId - 1) % 3 + 3) % 3;
        return latinSquare[row];
    }

    public static System.Collections.Generic.List<TrialInfo> BuildTrials(int participantId, bool includePractice)
    {
        var trials = new System.Collections.Generic.List<TrialInfo>();
        var rng = new System.Random(participantId * 1000);
        int index = 0;

        if (includePractice)
            trials.Add(new TrialInfo { index = index++, condition = StudyCondition.C1_Baseline, scenario = StudyScenario.Practice, seed = rng.Next() });

        // Scenario order is randomised inside each condition block. A separate, participant-based
        // random generator makes the order reproducible: the same participant number always gets the same order.
        var orderRng = new System.Random(participantId * 7919 + 17);
        foreach (var c in ConditionOrder(participantId))
        {
            var order = (StudyScenario[])scenarioOrder.Clone();
            for (int i = order.Length - 1; i > 0; i--)          // Fisher-Yates shuffle
            {
                int j = orderRng.Next(i + 1);
                var tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }
            foreach (var s in order)
                trials.Add(new TrialInfo { index = index++, condition = c, scenario = s, seed = rng.Next() });
        }

        return trials;
    }

    public static string ConditionLabel(StudyCondition c)
    {
        switch (c)
        {
            case StudyCondition.C1_Baseline: return "C1 Baseline";
            case StudyCondition.C2_ContextAware: return "C2 Context-aware views";
            default: return "C3 Attention-guided";
        }
    }

    // Short text shown to the participant before each block.
    public static string ConditionBriefing(StudyCondition c)
    {
        switch (c)
        {
            case StudyCondition.C1_Baseline:
                return "You will see the measurement bar at the bottom of the screen. No other help is shown.";
            case StudyCondition.C2_ContextAware:
                return "You will see the measurement bar and an extra camera view that changes to match what you are doing.";
            default:
                return "You will see the measurement bar, the extra camera views, and guidance overlays: boxes around obstacles " +
                       "and people, the path to drive, the dig area with the bucket position marked on the ground, " +
                       "and a suggested bucket path for each scoop.";
        }
    }

    public static string ScenarioLabel(StudyScenario s)
    {
        switch (s)
        {
            case StudyScenario.Practice: return "Practice";
            case StudyScenario.Easy_Driving: return "Easy - Driving";
            case StudyScenario.Moderate_Digging: return "Moderate - Digging";
            default: return "Hard - Dig and dump";
        }
    }

    public static string ScenarioBriefing(StudyScenario s)
    {
        switch (s)
        {
            case StudyScenario.Practice:
                return "Nothing is measured. " + (InstructionDisplay.Active ? "The instruction screen" : "The panel on the right") + " guides you through three phases:\n\n" +
                       "Phase 1 - Basic controls: one control at a time - switching between DRIVE and DIG mode, driving and turning, " +
                       "and the boom, arm, bucket and swing. Hold each control until its bar is full. Then a short explanation of the screen.\n\n" +
                       "Phase 2 - Digging and dumping: dig soil from the BROWN square and dump it on the WHITE square, step by step.\n\n" +
                       $"Phase 3 - Free practice: practise on your own. {StickControls.HoldConfirm} when you are ready to start the tasks " +
                       "(the practice ends after 3 minutes at the latest).";
            case StudyScenario.Easy_Driving:
                return "Goal: drive through the 4 gates in order, then reverse to the yellow stop line.\n\n" +
                       $"1. Drive forward: {StickControls.DriveForward} and pass through gates 1, 2, 3 and 4 in order.\n" +
                       $"2. Reverse: after gate 4, {StickControls.DriveBack}. The machine moves backwards while the cab " +
                       "keeps facing forward - do NOT swing the cab around.\n" +
                       "3. Stop when the middle of the machine is on the yellow stop line.\n\n" +
                       "Do not touch the gates, obstacles or people.";
            case StudyScenario.Moderate_Digging:
                return "Drive into the yellow box, then dig the marked trench to the target depth. Dump soil only on the small white square. " +
                       "Avoid the buried cable, the crossing pipe below the trench, and the worker.";
            default:
                return "Goal: load the truck 4 times within 6 minutes. The truck is out of reach from the pit, so you drive between them.\n\n" +
                       "For each load:\n" +
                       "1. Dig: from the DIG box, fill the bucket from the brown pit in front of you.\n" +
                       $"2. Reverse to the DUMP box: {StickControls.DriveBack}. The machine moves backwards while the cab keeps " +
                       "facing the pit - do NOT swing the cab around. Follow the lane: it bends right, then left, around the barriers.\n" +
                       "3. Dump: in the DUMP box, swing the cab LEFT to the truck and empty the bucket into the truck bed.\n" +
                       $"4. Drive forward: {StickControls.DriveForward} to return along the lane to the DIG box.\n\n" +
                       "Do not hit the barriers, the truck or the workers. A water pipe crosses under the pit between the yellow posts - do not dig into it. " +
                       "A LOW power line runs along the far side of the truck - do not lift the bucket too high or reach too far over the truck. " +
                       "While working, a small map in the task panel shows where you are and where to go next.";
        }
    }
}
