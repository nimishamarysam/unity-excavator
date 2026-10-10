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
                return "You will see the measurement bar, the extra camera view, and warning cues that point you to hazards.";
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
                return "Try out the controls. Nothing is measured. Press B to switch between driving and digging mode.";
            case StudyScenario.Easy_Driving:
                return "Drive along the marked route to the work site. Avoid obstacles and people.";
            case StudyScenario.Moderate_Digging:
                return "Dig the marked trench to the target depth. Avoid the buried cable and the trench edge.";
            default:
                return "Load the truck as many times as you can before time runs out. Watch for workers and the power line.";
        }
    }
}
