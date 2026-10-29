using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// ---------------------------------------------------------------------------
// STUDY MANAGER
// Runs one study session from start to finish:
//   Setup screen  -> enter participant ID (experimenter)
//   Briefing      -> participant reads the condition + task, presses A to start
//   Running       -> the trial; the scene is reloaded so the machine starts fresh
//   Questionnaire -> participant fills in SIM-TLX + SART, experimenter presses Enter
//   ...repeat for all trials...
//   Finished
//
// It is created automatically when you press Play (see Bootstrap at the bottom),
// so you do not need to add anything to the scene by hand.
//
// Experimenter keys:  Enter = continue   P or gamepad Start = pause/resume
//                     End = end the current trial early (F10 is taken by Unity's Recorder)
// Gamepad only (drive lab, no keyboard):
//   A  = start / continue: start the session (setup), start the task (briefing), next task (questionnaire)
//   X  = end the current trial early (also while paused)
//   setup screen: LB / RB = participant number -1 / +1, Y = practice trial on/off
//   Start = pause / resume
// Joystick connected (StickControls.Active): the screens name only the joystick, and the same functions come
//   from it: confirm button (default 2) = A, button 5 = Start (pause), button 6 = X (end trial),
//   hat left/right = participant number, hat up = practice on/off. See StickControls.cs.
//   The gamepad + keyboard still work for testing unless 'Block Gamepad Keyboard' is ticked.
// ---------------------------------------------------------------------------

public class StudyManager : MonoBehaviour
{
    public enum State { Setup, Briefing, Running, Paused, Questionnaire, Finished }

    public static StudyManager Instance { get; private set; }

    // Fired when a trial starts or ends. Scenario, camera-view and cue scripts listen to these.
    public static event Action<TrialInfo> TrialStarted;
    public static event Action<TrialInfo, string> TrialEnded;

    [Header("Timing")]
    public float trialDurationSeconds = 240F;     // max length of a measured trial
    public float practiceDurationSeconds = 180F;  // length of the FREE practice; with the guided walkthrough
                                                  // (PracticeGuide) it starts when the walkthrough is finished

    public State CurrentState { get; private set; } = State.Setup;
    public int ParticipantId { get; private set; } = 1;
    public TrialInfo CurrentTrial { get; private set; }
    public float TrialTime { get; private set; }
    public bool IsTrialRunning => CurrentState == State.Running;
    // Time limit of the current trial: the scenario's own limit if it has one, else trialDurationSeconds.
    public float TrialLimit
    {
        get
        {
            var sc = ScenarioRunner.Current;
            return sc != null && sc.TimeLimitSeconds.HasValue ? sc.TimeLimitSeconds.Value : trialDurationSeconds;
        }
    }
    public ExcavatorController Controller { get; private set; }

    // Practice: no time limit during the guided walkthrough, then practiceDurationSeconds of free practice.
    float PracticeLimit
    {
        get
        {
            var g = PracticeGuide.Current;
            if (g == null) return practiceDurationSeconds;          // walkthrough switched off (or not built yet)
            return g.Done ? g.DoneAt + practiceDurationSeconds : float.MaxValue;
        }
    }

    // What the other scripts should show in the current condition.
    public bool ShowContextViews => IsMeasured && CurrentTrial.condition >= StudyCondition.C2_ContextAware;
    public bool ShowAttentionCues => IsMeasured && CurrentTrial.condition == StudyCondition.C3_AttentionGuided;
    bool IsMeasured => CurrentState != State.Setup && CurrentState != State.Finished;

    List<TrialInfo> trials = new List<TrialInfo>();
    int trialPos = -1;
    string participantText = "1";
    bool includePractice = true;
    bool startTrialAfterLoad;

    GUIStyle big, normal, boxStyle;

    void Awake()
    {
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        FindController();
        Freeze(true);
    }

    void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    // ---------- Flow ----------

    void BeginSession()
    {
        if (!int.TryParse(participantText, out int id) || id < 1) return;
        ParticipantId = id;
        trials = StudyDesign.BuildTrials(id, includePractice);
        trialPos = -1;
        StudyLogger.Instance.OpenSession(id);
        StudyLogger.Log("session_start",
            "order=" + string.Join(" ", Array.ConvertAll(StudyDesign.ConditionOrder(id), c => c.ToString())));
        StudyLogger.Log("trial_order", string.Join(" ", trials.ConvertAll(t => t.condition + "/" + t.scenario)));
        NextTrial();
    }

    void NextTrial()
    {
        trialPos++;
        if (trialPos >= trials.Count)
        {
            StudyLogger.Log("session_end");
            StudyLogger.Instance.CloseSession();
            CurrentState = State.Finished;
            Freeze(true);
            return;
        }
        CurrentTrial = trials[trialPos];
        CurrentState = State.Briefing;
        Freeze(true);
    }

    void StartTrial()
    {
        // Reload the scene so every trial starts from the same machine position.
        startTrialAfterLoad = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FindController();
        if (!startTrialAfterLoad) return;
        startTrialAfterLoad = false;

        UnityEngine.Random.InitState(CurrentTrial.seed);
        TrialTime = 0F;
        CurrentState = State.Running;
        Freeze(false);
        StudyLogger.Log("trial_start", "seed=" + CurrentTrial.seed);
        TrialStarted?.Invoke(CurrentTrial);
    }

    // Scenario scripts call this when the task is completed or failed,
    // e.g. StudyManager.Instance.EndTrial("completed");
    public void EndTrial(string reason)
    {
        if (CurrentState != State.Running && CurrentState != State.Paused) return;
        StudyLogger.Log("trial_end", reason);
        StudyLogger.Instance.Flush();
        TrialEnded?.Invoke(CurrentTrial, reason);
        CurrentState = State.Questionnaire;
        Freeze(true);
    }

    void Update()
    {
        // Joystick or gamepad; the gamepad is null only when blocked (StickControls.cs).
        var kb = StickControls.ExperimenterKeyboard;
        var pad = StickControls.Pad;
        bool stick = StickControls.Active;
        bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        bool padA = StickControls.ConfirmPressed();                     // gamepad A or the joystick confirm button
        bool pause = (kb != null && kb.pKey.wasPressedThisFrame) || StickControls.PausePressed();
        bool abort = kb != null && kb.endKey.wasPressedThisFrame;

        bool padX = StickControls.EndTrialPressed();                    // end trial (gamepad X / joystick button)
        if (padX && (CurrentState == State.Running || CurrentState == State.Paused))
            StudyLogger.Log(stick ? "experimenter_joystick" : "experimenter_gamepad", "X_end_trial");

        switch (CurrentState)
        {
            case State.Setup:
                if (pad != null)
                {
                    int.TryParse(participantText, out int pid);
                    if (pad.rightShoulder.wasPressedThisFrame) participantText = Mathf.Max(1, pid + 1).ToString();
                    if (pad.leftShoulder.wasPressedThisFrame) participantText = Mathf.Max(1, pid - 1).ToString();
                    if (pad.buttonNorth.wasPressedThisFrame) includePractice = !includePractice;
                }
                if (stick)
                {
                    int.TryParse(participantText, out int pid);
                    if (StickControls.HatRight()) participantText = Mathf.Max(1, pid + 1).ToString();
                    if (StickControls.HatLeft()) participantText = Mathf.Max(1, pid - 1).ToString();
                    if (StickControls.HatUp()) includePractice = !includePractice;
                }
                if (enter || padA) BeginSession();
                break;

            case State.Briefing:
                if (enter || padA) StartTrial();
                break;

            case State.Running:
                TrialTime += Time.deltaTime;
                float limit = CurrentTrial.scenario == StudyScenario.Practice ? PracticeLimit : TrialLimit;
                if (TrialTime >= limit) EndTrial("time_limit");
                else if (abort || padX) EndTrial("ended_by_experimenter");
                else if (pause) { CurrentState = State.Paused; Freeze(true); StudyLogger.Log("pause"); }
                break;

            case State.Paused:
                if (pause) { CurrentState = State.Running; Freeze(false); StudyLogger.Log("resume"); }
                else if (abort || padX) EndTrial("ended_by_experimenter");
                break;

            case State.Questionnaire:
                if (enter || padA) NextTrial();
                break;
        }
    }

    // Stops (or restarts) physics and the excavator. Time.timeScale = 0 pauses everything that uses time.
    void Freeze(bool frozen)
    {
        Time.timeScale = frozen ? 0F : 1F;
        ExcavatorController.AutonomousDemoEnabled = false;
    }

    void FindController()
    {
        Controller = FindFirstObjectByType<ExcavatorController>();
    }

    // ---------- Screens (experimenter/participant panels, drawn with Unity's simple IMGUI) ----------

    MiniMap briefingMap;

    void OnGUI()
    {
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
            normal = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
            boxStyle = new GUIStyle(GUI.skin.box);
        }

        if (CurrentState == State.Running) return;

        // Dig and dump: the task instruction screen also shows a detailed site map on the right.
        MiniMap siteMap = null;
        if (CurrentState == State.Briefing && CurrentTrial.scenario == StudyScenario.Hard_DigAndDump)
            siteMap = briefingMap ??= DigDumpScenario.SiteMap();
        float w = Mathf.Min(siteMap != null ? 1400 : 900, Screen.width - 80), h = Mathf.Min(siteMap != null ? 780 : 680, Screen.height - 80);
        float mapW = siteMap != null ? Mathf.Min(480F, w * 0.4F) : 0F;
        var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        GUI.color = new Color(1, 1, 1, 0.97F);
        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(r.x + 30, r.y + 25, r.width - 60 - (siteMap != null ? mapW + 30F : 0F), r.height - 50));

        switch (CurrentState)
        {
            case State.Setup:
                GUILayout.Label("Excavator study - setup", big);
                GUILayout.Space(10);
                GUILayout.Label("Participant number:", normal);
                participantText = GUILayout.TextField(participantText, 4, GUILayout.Width(120), GUILayout.Height(34));
                includePractice = GUILayout.Toggle(includePractice, "  Start with a practice trial", GUILayout.Height(30));
                if (int.TryParse(participantText, out int id) && id >= 1)
                {
                    var order = StudyDesign.ConditionOrder(id);
                    GUILayout.Label("Condition order: " + string.Join("  ->  ", Array.ConvertAll(order, StudyDesign.ConditionLabel)), normal);
                }
                else GUILayout.Label("Enter a number of 1 or more.", normal);
                GUILayout.Space(10);
                GUILayout.Label("Data folder: " + StudyLogger.Instance.FolderPath, normal);
                GUILayout.Space(20);
                GUILayout.Label(StickControls.Active
                    ? $"Joystick: hat left / right = participant number,  hat up = practice on/off,  {StickControls.ConfirmName} = start"
                    : "Gamepad: LB / RB = participant number,  Y = practice on/off,  A = start", normal);
                if (GUILayout.Button("Start session  (Enter)", GUILayout.Height(44))) BeginSession();
                break;

            case State.Briefing:
                var t = CurrentTrial;
                int measured = t.scenario == StudyScenario.Practice ? 0 : CountMeasuredUpTo(trialPos);
                GUILayout.Label(t.scenario == StudyScenario.Practice ? "Practice" : $"Task {measured} of 9", big);
                GUILayout.Space(10);
                if (t.scenario != StudyScenario.Practice)
                {
                    GUILayout.Label("Display: " + StudyDesign.ConditionLabel(t.condition), normal);
                    GUILayout.Label(StudyDesign.ConditionBriefing(t.condition), normal);
                    GUILayout.Space(10);
                }
                GUILayout.Label("Task: " + StudyDesign.ScenarioLabel(t.scenario), normal);
                GUILayout.Label(StudyDesign.ScenarioBriefing(t.scenario), normal);
                GUILayout.FlexibleSpace();
                GUILayout.Label(StickControls.PressConfirm + " when you are ready to start.", big);
                break;

            case State.Paused:
                GUILayout.Label("Paused", big);
                GUILayout.Label($"Press {StickControls.PauseLabel} or P to continue.", normal);
                break;

            case State.Questionnaire:
                if (CurrentTrial.scenario == StudyScenario.Practice)
                {
                    // Practice: no questionnaires, just an encouraging message before the measured tasks.
                    GUILayout.Label("Practice complete - well done!", big);
                    GUILayout.Space(10);
                    GUILayout.Label("Great job! You have learned how to drive the excavator and use the boom, arm, bucket and swing controls.", normal);
                    GUILayout.Space(10);
                    GUILayout.Label("Next, you will start the actual tasks. Before each task you will see a short description of what to do. " +
                                    "Take your time to read it, and work carefully - try to avoid incidents.", normal);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(StickControls.PressConfirm + " when you are ready to continue.", normal);
                }
                else
                {
                    GUILayout.Label("Task complete", big);
                    GUILayout.Space(10);
                    GUILayout.Label("Please fill in the SIM-TLX and SART questionnaires for this task.", normal);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Experimenter: " + StickControls.PressConfirm.ToLower() + " (or Enter) to continue.", normal);
                }
                break;

            case State.Finished:
                GUILayout.Label("Session complete - thank you!", big);
                GUILayout.Space(10);
                GUILayout.Label("Data saved in: " + StudyLogger.Instance.FolderPath, normal);
                break;
        }
        GUILayout.EndArea();
        if (siteMap != null)
        {
            var mr = new Rect(r.xMax - 30F - mapW, r.y + 55F, mapW, r.height - 80F);
            GUI.Label(new Rect(mr.x, r.y + 22F, mapW, 30F), "Site map (seen from above)", normal);
            siteMap.Draw(mr);
        }
    }

    int CountMeasuredUpTo(int pos)
    {
        int n = 0;
        for (int i = 0; i <= pos && i < trials.Count; i++)
            if (trials[i].scenario != StudyScenario.Practice) n++;
        return n;
    }
}

// ---------------------------------------------------------------------------
// BOOTSTRAP
// Runs automatically once when you press Play. It creates one hidden-from-reload
// object "StudySystems" holding the manager, logger and measurement bar.
// To run the simulator WITHOUT the study (free driving), set Enabled = false.
// ---------------------------------------------------------------------------
public static class StudyBootstrap
{
    public static bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Enabled || StudyManager.Instance != null) return;

        // Always write numbers with a dot (1.25), whatever the computer's language setting is.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        if (UnityEngine.Object.FindFirstObjectByType<ExcavatorController>() == null) return;

        var go = new GameObject("StudySystems");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<StudyLogger>();
        go.AddComponent<StudyManager>();
        go.AddComponent<StudyLayout>();
        go.AddComponent<MeasurementBar>();
        go.AddComponent<ScenarioRunner>();
        go.AddComponent<IncidentFeedback>();
        go.AddComponent<AudioListenerKeeper>();   // one audio listener (avoids Unity's "no audio listeners" warning)
    }
}
