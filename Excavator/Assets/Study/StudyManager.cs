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
//   Questionnaire -> participant fills in the questionnaires outside the simulator (SIM-TLX and SART are
//                    collected separately); the experimenter presses Enter / A to continue
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
            // With the walkthrough the guide ends the practice itself: when the free-practice time is up it
            // shows the "Practice complete" menu (start the tasks / repeat a phase) instead of ending the trial.
            return float.MaxValue;
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
        endReason = reason;
        endTime = TrialTime;
        endIncidents = IncidentFeedback.Count;
        endResult = ScenarioRunner.Current != null ? ScenarioRunner.Current.ResultText : "";
        StudyLogger.Log("trial_end", reason);
        StudyLogger.Instance?.Flush();   // null after a script reload during Play mode: do not get stuck here every frame
        TrialEnded?.Invoke(CurrentTrial, reason);
        CurrentState = State.Questionnaire;
        Freeze(true);
        // SIM-TLX on the instruction screen after every measured task (not after the practice).
        tlxDone = !SimTlxForm.Enabled || CurrentTrial.scenario == StudyScenario.Practice;
        if (!tlxDone) tlx.Begin();
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
                if (!tlxDone)
                {
                    // Participant fills in the SIM-TLX; Enter (experimenter keyboard) skips it.
                    if (enter) { tlxDone = true; StudyLogger.Log("sim_tlx_skipped", "by_experimenter"); }
                    else if (tlx.Tick())
                    {
                        tlxDone = true;
                        StudyLogger.Log("sim_tlx", tlx.Detail());
                        StudyLogger.Instance.WriteQuestionnaire("SIM-TLX", SimTlxForm.ItemKeys, tlx.Values);
                    }
                    break;   // the confirm press that submitted the form must not also start the next task
                }
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

    MiniMap briefingMap, briefingMapDigging;

    // How the last trial ended, captured in EndTrial for the "Task complete" screen.
    string endReason = "", endResult = "";
    int endIncidents;
    float endTime;
    readonly SimTlxForm tlx = new SimTlxForm();
    bool tlxDone = true;

    void OnGUI()
    {
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
            normal = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
            boxStyle = new GUIStyle(GUI.skin.box);
        }

        if (CurrentState == State.Running) return;

        // Participant screens (task instructions, practice/task complete, pause) go to the instruction display
        // (Display 2 by default, see StudyLayout.InstructionDisplayIndex). Setup and Finished stay on Display 1.
        bool participantScreen = CurrentState == State.Briefing || CurrentState == State.Paused || CurrentState == State.Questionnaire;
        if (participantScreen && InstructionDisplay.Begin())
        {
            try { DrawScreen(InstructionDisplay.Width, InstructionDisplay.Height, InstructionDisplay.UiScale); }
            finally { InstructionDisplay.End(); }
            DrawDriverNote();
        }
        else DrawScreen(Screen.width, Screen.height, 1F);
    }

    // Small note on Display 1 while the instructions are shown on the instruction display.
    void DrawDriverNote()
    {
        SetFonts(1F);
        string text = CurrentState == State.Paused ? "Paused" : "Please read the instruction screen";
        var r = new Rect((Screen.width - 620F) / 2F, (Screen.height - 90F) / 2F, 620F, 90F);
        GUI.color = new Color(0F, 0F, 0F, 0.6F);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var style = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(r, text, style);
    }

    // One screen in a sw x sh pixel area (Display 1 or the instruction display). k = size factor (1 = 1280 x 720 design).
    void DrawScreen(float sw, float sh, float k)
    {
        // Dig and dump: the task instruction screen also shows a detailed site map on the right.
        MiniMap siteMap = null;
        if (CurrentState == State.Briefing && CurrentTrial.scenario == StudyScenario.Hard_DigAndDump)
            siteMap = briefingMap ??= DigDumpScenario.SiteMap();
        if (CurrentState == State.Briefing && CurrentTrial.scenario == StudyScenario.Moderate_Digging)
            siteMap = briefingMapDigging ??= DiggingScenario.SiteMap();
        float m = 20F * k;
        float w = Mathf.Min((siteMap != null ? 1400F : 1000F) * k, sw - 2F * m), h = sh - 2F * m;
        float mapW = siteMap != null ? Mathf.Min(480F * k, w * 0.4F) : 0F;
        var r = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
        GUI.color = new Color(1, 1, 1, 0.97F);
        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.color = Color.white;
        var inner = new Rect(r.x + 30F * k, r.y + 25F * k, r.width - 60F * k - (siteMap != null ? mapW + 30F * k : 0F), r.height - 50F * k);

        if (CurrentState == State.Setup) DrawSetup(inner);
        else if (CurrentState == State.Questionnaire && !tlxDone) tlx.Draw(inner, k);
        else DrawItems(inner, ScreenItems(), k);

        if (siteMap != null)
        {
            SetFonts(k);
            var mr = new Rect(r.xMax - 30F * k - mapW, r.y + 55F * k, mapW, r.height - 80F * k);
            GUI.Label(new Rect(mr.x, r.y + 22F * k, mapW, 30F * k), "Site map (seen from above)", normal);
            // Legend under the map: what each colour / symbol means.
            int lf = Mathf.Max(11, Mathf.RoundToInt(14F * k));
            float lh = Mathf.Min(siteMap.LegendHeight(mapW, lf), mr.height * 0.55F);
            if (lh > 0F)
            {
                siteMap.DrawLegend(new Rect(mr.x, mr.yMax - lh, mapW, lh), lf);
                mr.height -= lh + 8F * k;
            }
            siteMap.Draw(mr);
        }
    }

    // Experimenter setup screen (Display 1, keyboard / mouse / gamepad).
    void DrawSetup(Rect area)
    {
        SetFonts(1F);
        GUILayout.BeginArea(area);
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
        GUILayout.EndArea();
    }

    // One line / paragraph of a participant screen. bottom = anchored to the bottom of the box.
    struct ScreenItem
    {
        public string text; public bool big; public float space; public bool bottom;
        public ScreenItem(string text, bool big = false, float space = 0F, bool bottom = false)
        { this.text = text; this.big = big; this.space = space; this.bottom = bottom; }
    }

    List<ScreenItem> ScreenItems()
    {
        var list = new List<ScreenItem>();
        switch (CurrentState)
        {
            case State.Briefing:
                var t = CurrentTrial;
                int measured = t.scenario == StudyScenario.Practice ? 0 : CountMeasuredUpTo(trialPos);
                list.Add(new ScreenItem(t.scenario == StudyScenario.Practice ? "Practice" : $"Task {measured} of 9", true));
                if (t.scenario != StudyScenario.Practice)
                {
                    list.Add(new ScreenItem("Display: " + StudyDesign.ConditionLabel(t.condition), space: 10));
                    list.Add(new ScreenItem(StudyDesign.ConditionBriefing(t.condition)));
                }
                list.Add(new ScreenItem("Task: " + StudyDesign.ScenarioLabel(t.scenario), space: 10));
                list.Add(new ScreenItem(StudyDesign.ScenarioBriefing(t.scenario)));
                list.Add(new ScreenItem(StickControls.PressConfirm + " when you are ready to start.", true, 16, true));
                break;

            case State.Paused:
                list.Add(new ScreenItem("Paused", true));
                list.Add(new ScreenItem($"Press {StickControls.PauseLabel} or P to continue."));
                break;

            case State.Questionnaire:
                if (CurrentTrial.scenario == StudyScenario.Practice)
                {
                    // Practice: no questionnaires, just an encouraging message before the measured tasks.
                    list.Add(new ScreenItem("Practice complete - well done!", true));
                    list.Add(new ScreenItem("Great job! You have learned how to drive the excavator and use the boom, arm, bucket and swing controls.", space: 10));
                    list.Add(new ScreenItem("Next, you will start the actual tasks. Before each task you will see a short description of what to do. " +
                                            "Take your time to read it, and work carefully. ", space: 10));
                    list.Add(new ScreenItem(StickControls.PressConfirm + " when you are ready to continue.", space: 16, bottom: true));
                }
                else
                {
                    list.Add(new ScreenItem("Task complete", true));
                    // How the task ended + the result.
                    string how = endReason == "completed" ? "You completed the task."
                               : endReason == "time_limit" ? "The time ran out before the task was finished."
                               : endReason == "ended_by_experimenter" ? "The task was stopped by the experimenter."
                               : "The task ended (" + endReason + ").";
                    list.Add(new ScreenItem(how, space: 10));
                    string res = $"Time: {Mathf.FloorToInt(endTime / 60F)}:{Mathf.FloorToInt(endTime % 60F):00}      Incidents: {endIncidents}";
                    if (!string.IsNullOrEmpty(endResult)) res = endResult + "\n" + res;
                    list.Add(new ScreenItem(res, space: 6));
                    list.Add(new ScreenItem(SimTlxForm.Enabled ? "Thank you for your ratings. Please now fill in the SART questionnaire for this task."
                                                               : "Please fill in the questionnaires for this task.", space: 10));
                    list.Add(new ScreenItem("Experimenter: " + StickControls.PressConfirm + " (or Enter) to continue.", space: 16, bottom: true));
                }
                break;

            case State.Finished:
                list.Add(new ScreenItem("Session complete - thank you!", true));
                list.Add(new ScreenItem("Data saved in: " + StudyLogger.Instance.FolderPath, space: 10));
                break;
        }
        return list;
    }

    // Draws the items in 'area' with the largest text size (up to k) at which everything fits - never overflows.
    void DrawItems(Rect area, List<ScreenItem> items, float k)
    {
        float f = k;
        for (int i = 0; i < 20 && ItemsHeight(items, area.width, f) > area.height; i++) f *= 0.93F;
        SetFonts(f);

        float y = area.y, bottomH = 0F;
        foreach (var it in items)
            if (it.bottom) bottomH += it.space * f + Style(it).CalcHeight(new GUIContent(it.text), area.width);
        float yb = Mathf.Max(area.yMax - bottomH, y);
        foreach (var it in items)
        {
            var st = Style(it);
            float hh = st.CalcHeight(new GUIContent(it.text), area.width);
            if (it.bottom) { yb += it.space * f; GUI.Label(new Rect(area.x, yb, area.width, hh), it.text, st); yb += hh; }
            else { y += it.space * f; GUI.Label(new Rect(area.x, y, area.width, hh), it.text, st); y += hh + 4F * f; }
        }
    }

    float ItemsHeight(List<ScreenItem> items, float width, float f)
    {
        SetFonts(f);
        float total = 0F;
        foreach (var it in items) total += it.space * f + Style(it).CalcHeight(new GUIContent(it.text), width) + 4F * f;
        return total;
    }

    GUIStyle Style(ScreenItem it) => it.big ? big : normal;

    void SetFonts(float f)
    {
        big.fontSize = Mathf.Max(8, Mathf.RoundToInt(30F * f));
        normal.fontSize = Mathf.Max(8, Mathf.RoundToInt(20F * f));
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
