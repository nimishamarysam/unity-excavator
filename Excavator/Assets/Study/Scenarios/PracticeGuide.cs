using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ---------------------------------------------------------------------------
// PRACTICE GUIDE (the whole practice trial, in three phases)
//
// PHASE 1 - BASIC CONTROLS (one control at a time)
//    1  switch to DRIVE mode (B)
//    2  drive forward              3  drive backward
//    4  turn left  (forward)       5  turn right (forward)
//    6  turn right (backward)      7  turn left  (backward)
//    8  switch to DIG mode (B)
//    9  boom down    10 boom up     11 arm out      12 arm in
//   13 bucket open  14 bucket curl  15 swing left   16 swing right
//   17-19 info pages: task panel, status bar, incidents (screen part outlined in yellow, A to continue)
//
// PHASE 2 - DIGGING AND DUMPING (short tasks, checked automatically)
//   No marked areas: when phase 2 starts, digging is switched on all around the machine
//   (PracticeScenario.BuildDigSite), so the participant can dig anywhere and dump anywhere.
//   Steps: teeth into the ground -> scoop -> lift the soil out -> swing anywhere and dump -> one more on your own.
//   If the soil falls out on the way, the guide goes back to the right step and says why.
//
// PHASE 3 - FREE PRACTICE (no guide)
//   Starts with an instruction page (phase3_intro); the free-practice TIMER starts only when the participant
//   has read it and pressed A (not when the last dump of phase 2 happens).
//   The participant practises on their own and HOLDS A for HoldAToContinue seconds when ready
//   (ends the practice: "participant_ready"), or the practice ends after StudyManager.practiceDurationSeconds.
//
// REDO A PHASE - only AFTER phases 1 to 3 have all been completed once (the first run is always 1 -> 2 -> 3).
//   Participant: the "Practice complete" menu below.
//   Experimenter: keyboard 1 / 2 / 3 restarts phase 1 / 2 / 3 from its first step - also only after the first
//     complete run (before that, N still skips a step). Phase 3 restarts at its instruction page; the timer stops.
//   After ALL THREE phases (phase 3 ended by holding A, or its time is up) a "Practice complete" menu
//     appears: Start the tasks / Repeat phase 1 / Repeat phase 2 / Repeat phase 3. Choose with the joystick
//     hat or gamepad D-pad up / down (keyboard arrows), confirm with A (or Enter). Only "Start the tasks"
//     ends the practice trial. Logged: practice_menu (why it opened), practice_ready (tasks started).
//     A phase repeated AFTER the menu was reached once (from the menu, or with the experimenter keys) is played
//     on its own and then goes straight BACK TO THE MENU (phase 1 -> menu, phase 2 -> menu, phase 3 -> its free
//     practice -> menu); the following phases are not replayed.
//   Logged as guide_phase_redo (phase, who, from which step).
//
// DRIVER-VIEW PROGRESS (ShowProgressInDriverView): while the instructions are on the instruction display, the
//   phase-1 drive / dig steps ALSO show the hold (progress) bar at the bottom centre of the driver view on
//   Display 1, just above the status bar, with the green line "Correct - keep holding" while the input is right.
//
// A control step (phase 1) counts as learned when the CORRECT control has been held for HoldFor(step) seconds
// in total - it does not have to be in one go. Wrong-mode or wrong-stick input is not counted.
// The instructions are drawn on the INSTRUCTION DISPLAY (Display 2 by default, StudyLayout.InstructionDisplayIndex,
// see InstructionDisplay.cs) as a centred column. Without that display they are drawn in the right-hand part of
// Display 1 (StudyLayout.GuidePanel), over the driver view. The yellow outlines of the info pages always mark
// the explained part of Display 1. The practice trial runs in C1, so there are no context-aware views on Display 5.
//
// Experimenter: gamepad Select/Back or keyboard N = skip the current step (logged).
//
// JOYSTICK: while a flight stick is connected the texts name only the joystick (the gamepad and keyboard still
//   work for testing unless StickControls.BlockGamepadKeyboard is on; see StickControls.cs).
//   "A" becomes the joystick confirm button, "B" the trigger (single stick: tap; two sticks: either trigger),
//   Select = joystick button 7. With ONE stick the texts and diagrams change to the single-stick mapping
//   (drive = stick forward/back + steer, boom = stick Y, arm = trigger + stick Y, bucket = twist, swing = stick X);
//   with TWO sticks they stay as for the gamepad (same layout). The steps check the machine inputs
//   (tracks, boom, arm, bucket, swing), so the completion rules are the same for every device.
// Logged events: guide_step_start, guide_step_done (took / held / wrong-input seconds), guide_step_skipped,
//                guide_step_back (phase 2: soil lost), guide_phase_redo, guide_complete, practice_ready.
// ---------------------------------------------------------------------------

public class PracticeGuide : MonoBehaviour
{
    public static bool Enabled = true;            // false = old practice (free practice only)
    public static bool ShowProgressInDriverView = true;   // phase 1: hold bar in the driver view too
    public static float HoldSeconds = 2F;         // DEFAULT correct input needed per control step

    // HOLD TIME PER STEP (seconds). Steps listed here use this time instead of HoldSeconds.
    // Change a number to shorten/lengthen one step; remove a line to use the default again.
    // Step names: drive_fwd, drive_back, turn_left_fwd, turn_right_fwd, turn_right_back, turn_left_back,
    //             boom_down, boom_up, arm_out, arm_in, bucket_curl, bucket_open, swing_left, swing_right
    // The digging joints move fast (boom ~18 deg/s, arm ~30 deg/s, bucket ~45 deg/s), so they get shorter times.
    public static readonly Dictionary<string, float> HoldPerStep = new Dictionary<string, float>
    {
        { "boom_down",   1.5F },
        { "boom_up",     2F },
        { "arm_out",     2F },
        { "arm_in",      3F },
        { "bucket_curl", 2F },
        { "bucket_open", 2F },
    };

    public static float HoldFor(string stepId) => HoldPerStep.TryGetValue(stepId, out float t) ? t : HoldSeconds;
    public static float HoldAToContinue = 1.5F;   // phase 3: hold A this long to finish the practice
    const float InfoMinSeconds = 3F;              // info pages: A works only after this many seconds
    const float TaskConfirmSeconds = 0.3F;        // phase 2: condition must be true this long (no flicker)
    const float HintGraceSeconds = 1.5F;          // no "not quite" / wrong-mode hint (and no wrong-input time) in the
                                                  // first seconds of a step: time to release the old control
    const float WrongBeforeHint = 0.5F;           // wrong input must last this long before the hint appears
    const float GuideMaxSeconds = 1200F;          // safety cap: after 20 min the guide ends anyway
    const float StickOn = 0.5F;                   // stick deflection that counts as "pushed"
    const float TrackDiff = 0.35F;                // difference between the two tracks for a turn / allowed for straight

    public static PracticeGuide Current { get; private set; }
    public bool Done { get; private set; }        // phases 1 and 2 finished -> free practice (phase 3)
    public float DoneAt { get; private set; }     // trial time when phase 3 started
    public int Phase => Done ? 3 : steps[index].phase;
    public static string PhaseName(int p) => p == 1 ? "Basic controls" : p == 2 ? "Digging and dumping" : "Free practice";

    enum Kind { ModeDrive, ModeDig, Drive, Dig, Info, Task }
    enum Area { None, TaskPanel, StatusBar, Incidents }   // screen part outlined by an info page

    class Step
    {
        public string id, title, how, keys;
        public string how1;                       // single flight stick: text used instead of 'how' (null = same)
        public Vector2 target1;                   // single flight stick: target stick position for the diagram
        public float twist1;                      // single flight stick: target twist (bucket), 0 = no twist step
        public bool trigger1;                     // single flight stick: trigger must be held (arm)
        public int phase = 1;
        public Kind kind;
        public Area area;
        public Vector2 targetL, targetR;          // target stick positions for the diagrams (-1..1)
        public Func<InputSnapshot, bool> ok;      // control steps: is the current input the correct one?
        public Func<bool> done;                   // task steps: is the task done?
        public Func<string> status;               // task steps: live value line (may be null)
        public Func<string> hint;                 // task steps: contextual hint (may be null / return "")
        public Func<bool> lost;                   // task steps: soil lost -> go back to 'backTo'
        public string backTo, lostText;
        public int redoPhase;                     // phase start page: the phase before it (its "Phase N is complete" lead-in
                                                  // is hidden when a phase is repeated from the menu)
        public Action onStart;
        public float held, wrong, startedAt;
    }

    readonly List<Step> steps = new List<Step>();
    int index;
    ExcavatorController ctl;
    PracticeScenario site;
    StudyManager SM => StudyManager.Instance;
    int trialIndex;
    bool lastOk;
    float wrongStreak;                            // seconds of continuous wrong input (for the hint delay)
    string backMessage = ""; float backMessageUntil = -1F;
    int dumpsAtStepStart;
    int dumpsAtCycleStart;                        // phase 2: dumps when the guided dig cycle (dig_over) started
    float holdA;
    bool waitARelease;                            // phase 3: A from the instruction page must be let go first
    bool menu;                                    // "Practice complete" menu after all three phases
    int menuSel;                                  // 0 = start the tasks, 1..3 = repeat that phase
    float menuOpenedAt;
    string menuReason = "";
    const float MenuMinSeconds = 1F;              // menu: A works only after this long (no accidental choice)
    public bool MenuOpen => Done && menu;
    // On the free-practice instruction page (phase 3, timer not started yet).
    public bool OnFreePracticeIntro => !Done && index >= 0 && index < steps.Count && steps[index].id == "phase3_intro";
    bool allPhasesDone;                           // the menu has been reached at least once
    int repeatOnly;                               // phase being repeated after that (0 = none) -> back to the menu after it
    float guideStartedAt;                         // for the GuideMaxSeconds cap (restarts with each repeat)
    static readonly string[] MenuItems =
        { "Start the tasks", "Repeat phase 1 - basic controls", "Repeat phase 2 - digging and dumping", "Repeat phase 3 - free practice" };

    GUIStyle small, title, body, hint, center, bigCenter;

    public void Init(ExcavatorController controller, PracticeScenario scenario)
    {
        ctl = controller;
        site = scenario;
        trialIndex = SM.CurrentTrial.index;
        Current = this;
        BuildSteps();
        StartStep();
    }

    void OnDestroy() { if (Current == this) Current = null; }

    // ======================= the steps =======================

    void BuildSteps()
    {
        // ---------------- PHASE 1: basic controls ----------------
        steps.Add(new Step { id = "mode_drive", kind = Kind.ModeDrive, title = "Switch to DRIVE mode",
            how = "Press the B button on the gamepad.\nMODE in the status bar (bottom) changes to DRIVE. In DRIVE mode the two sticks move the two tracks.",
            how1 = "Tap the trigger (press it and let go quickly).\nMODE in the status bar (bottom) changes to DRIVE. In DRIVE mode the stick drives the machine: " +
                   "forward / back = drive, left / right = steer.",
            keys = "Gamepad B" });

        AddDrive("drive_fwd", "Drive forward",
            "Push BOTH sticks forward (up) the same amount. Both tracks drive forward.", "Y + U",
            new Vector2(0, 1), new Vector2(0, 1),
            s => s.trackLeft >= StickOn && s.trackRight >= StickOn && Mathf.Abs(s.trackLeft - s.trackRight) <= TrackDiff);
        AddDrive("drive_back", "Drive backward",
            "Pull BOTH sticks back (down) the same amount. Both tracks drive backward.", "R + O",
            new Vector2(0, -1), new Vector2(0, -1),
            s => s.trackLeft <= -StickOn && s.trackRight <= -StickOn && Mathf.Abs(s.trackLeft - s.trackRight) <= TrackDiff);
        AddDrive("turn_left_fwd", "Turn LEFT while driving forward",
            "Push only the RIGHT stick forward, keep the left stick in the middle.\nThe right track drives, so the machine turns to the left.", "U",
            new Vector2(0, 0), new Vector2(0, 1),
            s => s.trackRight >= StickOn && s.trackLeft >= -0.15F && s.trackLeft <= s.trackRight - TrackDiff);
        AddDrive("turn_right_fwd", "Turn RIGHT while driving forward",
            "Push only the LEFT stick forward, keep the right stick in the middle.\nThe left track drives, so the machine turns to the right.", "Y",
            new Vector2(0, 1), new Vector2(0, 0),
            s => s.trackLeft >= StickOn && s.trackRight >= -0.15F && s.trackRight <= s.trackLeft - TrackDiff);
        AddDrive("turn_right_back", "Turn RIGHT while driving backward",
            "Pull only the LEFT stick back, keep the right stick in the middle.\nThe machine reverses and its rear swings to the RIGHT.", "R",
            new Vector2(0, -1), new Vector2(0, 0),
            s => s.trackLeft <= -StickOn && s.trackRight <= 0.15F && s.trackRight >= s.trackLeft + TrackDiff);
        AddDrive("turn_left_back", "Turn LEFT while driving backward",
            "Pull only the RIGHT stick back, keep the left stick in the middle.\nThe machine reverses and its rear swings to the LEFT.", "O",
            new Vector2(0, 0), new Vector2(0, -1),
            s => s.trackRight <= -StickOn && s.trackLeft <= 0.15F && s.trackLeft >= s.trackRight + TrackDiff);

        steps.Add(new Step { id = "mode_dig", kind = Kind.ModeDig, title = "Switch to DIG mode",
            how = "Press the B button again.\nMODE in the status bar (bottom) changes to DIG. In DIG mode the sticks move the boom, arm, bucket and cab.",
            how1 = "Tap the trigger again.\nMODE in the status bar (bottom) changes to DIG. In DIG mode the stick moves the boom (forward / back) " +
                   "and the cab (left / right), trigger + stick moves the arm, and twisting the grip moves the bucket.",
            keys = "Gamepad B" });

        // Operation mode mapping (ISO): left stick X = swing, left Y = arm, right X = bucket, right Y = boom.
        AddDig("boom_down", "Boom DOWN", "Push the RIGHT stick forward (up). The boom lowers.", "I", false, new Vector2(0, 1), s => s.boom, 1);
        AddDig("boom_up", "Boom UP", "Pull the RIGHT stick back (down). The boom rises.", "K", false, new Vector2(0, -1), s => s.boom, -1);
        AddDig("arm_out", "Arm OUT", "Push the LEFT stick forward (up). The arm reaches out, away from the cab.", "T", true, new Vector2(0, 1), s => s.arm, 1);
        AddDig("arm_in", "Arm IN", "Pull the LEFT stick back (down). The arm pulls in, towards the cab.", "G", true, new Vector2(0, -1), s => s.arm, -1);
        AddDig("bucket_open", "Bucket OPEN (dump)", "Push the RIGHT stick to the RIGHT. The bucket opens, as when dumping soil.", "L", false, new Vector2(1, 0), s => s.bucket, 1);
        AddDig("bucket_curl", "Bucket CURL (scoop)", "Push the RIGHT stick to the LEFT. The bucket curls in, as when scooping soil.", "J", false, new Vector2(-1, 0), s => s.bucket, -1);
        AddDig("swing_left", "Swing cab LEFT", "Push the LEFT stick to the LEFT. The cab and arm turn to the left.", "F", true, new Vector2(-1, 0), s => s.swing, -1);
        AddDig("swing_right", "Swing cab RIGHT", "Push the LEFT stick to the RIGHT. The cab and arm turn to the right.", "H", true, new Vector2(1, 0), s => s.swing, 1);

        // Information pages: what is on the screen.
        AddInfo("info_task_panel", "The task panel (top left)", Area.TaskPanel,
            "The panel at the top left (outlined in yellow) describes your current task.\n\n" +
            "- First line: the goal of the task.\n" +
            "- Lines below: what to do next and your progress. \n" +
            "Before each task you also get a short description. During the task, look at this panel whenever you are unsure what to do next.");
        AddInfo("info_status_bar", "The status bar (bottom)", Area.StatusBar,
            "The dark bar along the bottom of the screen (outlined in yellow) shows the state of the machine as numbers:\n\n" +
            "- MODE: DRIVE or DIG\n" +
            "- SWING: how far the cab is turned (degrees)\n" +
            "- BOOM, ARM, BUCKET: the angle of each part (degrees)\n" +
            "- BUCKET HEIGHT: height of the bucket teeth above the ground in metres (minus = below the ground, i.e. how deep you dig)\n" +
            "- BUCKET LOAD: how full the bucket is (%)\n" +
            "- TILT: how much the machine leans (degrees)\n" +
            "- SPEED: driving speed (km/h)\n\n" +
            "Try it: move the boom and watch BOOM and BUCKET HEIGHT change.");
        AddInfo("info_incidents", "Incidents", Area.Incidents,
            "The bottom line of the task panel (outlined in yellow) counts INCIDENTS. When one happens you hear a short warning buzz, " +
            "the counter goes up, and the line turns red for a few seconds and says what happened.\n\n" +
            "These count as incidents:\n" +
            "- Hitting a worker, an obstacle (barrel, gate post, barrier, fence) or the truck\n" +
            "- The bucket or arm hitting the truck (cab, side wall, bed floor or chassis)\n" +
            "- Striking a buried cable or pipe\n" +
            "- The boom, arm or bucket touching the power line\n" +
            "- Driving the tracks too close to the edge of the trench\n" +
            "- Digging deeper than the target depth\n" +
            "- Digging outside the marked dig area\n" +
            "- Moving the bucket over a worker\n" +
            "- Spilling soil, or dumping it outside the dumping area\n\n");

        // ---------------- PHASE 2: digging and dumping ----------------
        // Matches BucketLoad: soil enters only while the teeth drag/curl through soil in the dig zone; >= 25 % when
        // leaving the soil = loaded; dump = bucket opened to ~140 deg while >= 0.5 m above the ground.
        var intro = AddInfo("phase2_intro", "Digging and dumping", Area.None,
            "Phase 1 (basic controls) is complete.\n\n" +
            "Now you will dig one bucket of soil and dump it, step by step.\n\n" +
            "You can dig anywhere in front of the machine and dump anywhere.");
        intro.phase = 2;
        intro.redoPhase = 1;
        intro.onStart = () => { site.BuildDigSite(); ctl.Machine.EnableCuttingEdges(true); };

        AddTask("dig_in", "Teeth into the ground",
            "Reach out with the arm, open the bucket a little, then lower the boom until the teeth are in the ground.",
            () => TeethInSoil,
            () => $"Bucket height: {ctl.Machine.BucketHeightAboveGround:+0.00;-0.00} m",
            null, null, null, null);
        steps[steps.Count - 1].onStart = () => dumpsAtCycleStart = site.Dumps;
        AddTask("dig_scoop", "Scoop the soil",
            "Pull the arm in and curl the bucket at the same time, dragging the bucket through the soil.",
            () => site.Load.Fill >= 0.6F || site.Load.IsLoaded,
            () => $"Bucket load: {Mathf.RoundToInt(site.Load.Fill * 100F)} %",
            () => !TeethInSoil && site.Load.Fill < 0.05F ? "Lower the teeth into the ground first." : "",
            null, null, null);
        AddTask("dig_lift", "Lift the soil out",
            "Keep the bucket curled in and raise the boom to lift the bucket out of the ground.",
            () => site.Load.IsLoaded && ctl.Machine.BucketHeightAboveGround > 0.5F,
            () => $"Bucket load: {Mathf.RoundToInt(site.Load.Fill * 100F)} %    Bucket height: {ctl.Machine.BucketHeightAboveGround:+0.00;-0.00} m",
            null,
            () => !site.Load.IsLoaded && site.Load.Fill < 0.05F && !TeethInSoil, "dig_in",
            "The bucket was not full enough and the soil fell back. Scoop again.");
        AddTask("dump_open", "Dump the soil",
            "Keep the bucket curled, swing the cab to any spot you like, then open the bucket fully .",
            () => site.Dumps > dumpsAtCycleStart,
            () => $"Bucket angle: {ctl.Machine.bucketAngle:0}°  (opens fully at about 140°)",
            () => ctl.Machine.BucketHeightAboveGround < 0.5F ? "Lift the bucket a bit higher first." : "",
            () => !site.Load.IsLoaded && site.Dumps == dumpsAtCycleStart, "dig_in",
            "The soil fell out of the bucket. Dig again.");
        AddTask("cycle_own", "Now one more on your own",
            "Dig one more bucket of soil and dump it - anywhere you like.",
            () => site.Dumps > dumpsAtStepStart,
            () => $"Bucket load: {Mathf.RoundToInt(site.Load.Fill * 100F)} %",
            null, null, null, null);

        // ---------------- PHASE 3: free practice - instruction page first ----------------
        // The free-practice time starts only when this page is continued with A (Finish -> DoneAt).
        int minutes = Mathf.Max(1, Mathf.RoundToInt(SM.practiceDurationSeconds / 60F));
        var free = AddInfo("phase3_intro", "Free practice", Area.None,
            "Phase 2 (digging and dumping) is complete.\n\n" +
            $"Now you can practise on your own for up to {minutes} minutes: drive around, or dig and dump anywhere you like.\n\n" +
            "When you are done practicing and feel ready for the tasks, {HOLD} (you do not have to use all the time).");   // {HOLD} is filled in when drawn
        free.phase = 3;
        free.redoPhase = 2;

        AddSingleStickTexts();
    }

    // ONE flight stick: instructions and diagram targets. The completion checks stay the same
    // (they look at the machine inputs). Twist RIGHT = bucket open assumes the default twist direction;
    // if it is the other way round, tick FlightStickInput.invertBucket and swap the two bucket texts.
    void AddSingleStickTexts()
    {
        const float D = 0.7F;   // diagonal
        void One(string id, string how, Vector2 target, float twist = 0F, bool trig = false)
        {
            var st = steps.Find(q => q.id == id);
            if (st == null) return;
            st.how1 = how; st.target1 = target; st.twist1 = twist; st.trigger1 = trig;
        }
        One("drive_fwd", "Push the stick straight forward. Both tracks drive forward.", new Vector2(0, 1));
        One("drive_back", "Pull the stick straight back. Both tracks drive backward.", new Vector2(0, -1));
        One("turn_left_fwd", "Push the stick forward AND to the LEFT (diagonally).\nThe right track drives, so the machine turns to the left.", new Vector2(-D, D));
        One("turn_right_fwd", "Push the stick forward AND to the RIGHT (diagonally).\nThe left track drives, so the machine turns to the right.", new Vector2(D, D));
        One("turn_right_back", "Pull the stick back AND to the RIGHT (diagonally).\nThe machine reverses and its rear swings to the RIGHT.", new Vector2(D, -D));
        One("turn_left_back", "Pull the stick back AND to the LEFT (diagonally).\nThe machine reverses and its rear swings to the LEFT.", new Vector2(-D, -D));
        One("boom_down", "Push the stick forward, WITHOUT the trigger. The boom lowers.", new Vector2(0, 1));
        One("boom_up", "Pull the stick back, WITHOUT the trigger. The boom rises.", new Vector2(0, -1));
        One("arm_out", "HOLD the trigger and push the stick forward. The arm reaches out, away from the cab.", new Vector2(0, 1), 0F, true);
        One("arm_in", "HOLD the trigger and pull the stick back. The arm pulls in, towards the cab.", new Vector2(0, -1), 0F, true);
        One("bucket_open", "TWIST the grip to the RIGHT. The bucket opens, as when dumping soil.", Vector2.zero, 1F);
        One("bucket_curl", "TWIST the grip to the LEFT. The bucket curls in, as when scooping soil.", Vector2.zero, -1F);
        One("swing_left", "Push the stick to the LEFT. The cab and arm turn to the left.", new Vector2(-1, 0));
        One("swing_right", "Push the stick to the RIGHT. The cab and arm turn to the right.", new Vector2(1, 0));
        var dump = steps.Find(q => q.id == "dump_open");
        if (dump != null) dump.how1 = dump.how.Replace("(RIGHT stick right)", "(twist the grip to the right)");
    }

    // Instruction text for the device in use.
    static string HowText(Step s)
    {
        if (StickControls.SingleStick && s.how1 != null) return s.how1;
        if (StickControls.TwoSticks)
            return s.how.Replace("Press the B button on the gamepad.", "Press and release the trigger on either stick.")
                        .Replace("Press the B button again.", "Press and release a trigger again.");
        return s.how;
    }

    GamepadAndStickInput StickInput => ctl != null ? ctl.InputSource as GamepadAndStickInput : null;

    void AddDrive(string id, string t, string how, string keys, Vector2 l, Vector2 r, Func<InputSnapshot, bool> ok)
        => steps.Add(new Step { id = id, kind = Kind.Drive, title = t, how = how, keys = keys, targetL = l, targetR = r, ok = ok });

    // Dig step: the chosen axis must be pushed in the right direction and be the largest operation input.
    void AddDig(string id, string t, string how, string keys, bool leftStick, Vector2 target, Func<InputSnapshot, float> axis, int sign)
    {
        steps.Add(new Step
        {
            id = id, kind = Kind.Dig, title = t, how = how, keys = keys,
            targetL = leftStick ? target : Vector2.zero, targetR = leftStick ? Vector2.zero : target,
            ok = s =>
            {
                float v = axis(s) * sign;
                float maxOther = Mathf.Max(Mathf.Abs(s.swing), Mathf.Abs(s.boom), Mathf.Abs(s.arm), Mathf.Abs(s.bucket));
                return v >= StickOn && v >= maxOther - 0.001F;
            }
        });
    }

    Step AddInfo(string id, string t, Area area, string how)
    {
        var s = new Step { id = id, kind = Kind.Info, title = t, how = how, keys = "Gamepad A", area = area };
        steps.Add(s);
        return s;
    }

    void AddTask(string id, string t, string how, Func<bool> done, Func<string> status, Func<string> hintFn,
                 Func<bool> lost, string backTo, string lostText)
        => steps.Add(new Step { id = id, phase = 2, kind = Kind.Task, title = t, how = how, keys = "",
                                done = done, status = status, hint = hintFn, lost = lost, backTo = backTo, lostText = lostText });

    Vector3 Tip => ctl.Machine.BucketTipPosition;
    bool TeethInSoil => Tip.y - TerrainDigger.CurrentGroundHeight(Tip) < 0.15F;

    // ======================= progress =======================

    bool Active => !Done && SM != null && SM.IsTrialRunning && SM.CurrentTrial.index == trialIndex && ctl != null;
    bool FreeActive => Done && SM != null && SM.IsTrialRunning && SM.CurrentTrial.index == trialIndex;

    void Update()
    {
        // Joystick or gamepad buttons; keyboard only for the experimenter keys (StickControls.cs).
        var kb = StickControls.ExperimenterKeyboard;

        // Experimenter: 1 / 2 / 3 restarts that phase at any time during the practice (also in phase 3).
        if ((Active || FreeActive) && kb != null && allPhasesDone)
        {
            int p = kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame ? 1
                  : kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame ? 2
                  : kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame ? 3 : 0;
            if (p > 0) { JumpToPhase(p, "experimenter"); return; }
        }

        // Phase 3: hold A (gamepad) / the confirm button (joystick) to finish the practice.
        if (FreeActive)
        {
            if (menu) { MenuInput(kb); return; }
            bool aHeld = StickControls.ConfirmHeld();
            // Free-practice time over -> the menu (the practice does not end by itself any more).
            if (SM.TrialTime >= DoneAt + SM.practiceDurationSeconds) { OpenMenu("time_up"); return; }
            if (waitARelease) { if (!aHeld) waitARelease = false; holdA = 0F; return; }
            holdA = aHeld ? holdA + Time.unscaledDeltaTime : 0F;
            if (holdA >= HoldAToContinue) OpenMenu("participant_ready");
            return;
        }
        if (!Active) return;

        bool skip = StickControls.SkipStepPressed() || (kb != null && kb.nKey.wasPressedThisFrame);
        if (skip)
        {
            StudyLogger.Log("guide_step_skipped", $"{index + 1};{steps[index].id};held={steps[index].held:F1}");
            NextStep();
            return;
        }

        // Info page: A (or Enter for the experimenter) continues, once the page has been up for InfoMinSeconds.
        var st = steps[index];
        bool next = StickControls.ConfirmPressed() || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame));
        bool readyInfo = st.kind == Kind.Info && SM.TrialTime - st.startedAt >= InfoMinSeconds;
        if (readyInfo && next) CompleteStep();
    }

    void FixedUpdate()
    {
        if (!Active) return;
        if (SM.TrialTime - guideStartedAt >= GuideMaxSeconds) { Finish("time_cap"); return; }

        var s = steps[index];
        var input = ctl.LastInput;
        switch (s.kind)
        {
            case Kind.ModeDrive: if (ctl.IsTravelMode) CompleteStep(); return;
            case Kind.ModeDig: if (!ctl.IsTravelMode) CompleteStep(); return;
            case Kind.Info: return;   // continued with A (see Update)
            case Kind.Task:
                // Soil dumped already during the guided cycle (e.g. opened the bucket early over the dump area):
                // the cycle counts as done -> jump to the "one more on your own" step.
                if (s.id != "cycle_own" && site.Dumps > dumpsAtCycleStart)
                {
                    StudyLogger.Log("guide_step_done", $"{index + 1};{s.id};dumped_early");
                    index = steps.FindIndex(q => q.id == "cycle_own");
                    StartStep();
                    return;
                }
                if (s.lost != null && s.lost()) { GoBack(s); return; }
                lastOk = !ctl.IsTravelMode && s.done();
                s.held = lastOk ? s.held + Time.fixedDeltaTime : 0F;
                if (s.held >= TaskConfirmSeconds) CompleteStep();
                return;
        }

        bool rightMode = (s.kind == Kind.Drive) == ctl.IsTravelMode;
        lastOk = rightMode && s.ok(input);
        bool grace = SM.TrialTime - s.startedAt < HintGraceSeconds;
        wrongStreak = !lastOk && (input.Any || !rightMode) ? wrongStreak + Time.fixedDeltaTime : 0F;
        if (lastOk) s.held += Time.fixedDeltaTime;
        else if (input.Any && !grace) s.wrong += Time.fixedDeltaTime;
        if (s.held >= HoldFor(s.id)) CompleteStep();
    }

    void CompleteStep()
    {
        var s = steps[index];
        StudyLogger.Log("guide_step_done", $"{index + 1};{s.id};took={SM.TrialTime - s.startedAt:F1};held={s.held:F1};wrong={s.wrong:F1}");
        lastOk = false;
        NextStep();
    }

    void NextStep()
    {
        index++;
        // Repeating phase 1 or 2 after the menu: when that phase is finished, go straight back to the menu.
        if ((repeatOnly == 1 || repeatOnly == 2) && (index >= steps.Count || steps[index].phase != repeatOnly))
        {
            ReturnToMenu();
            return;
        }
        if (index >= steps.Count) { Finish("completed"); return; }
        StartStep();   // the next step starts straight away
    }

    // Phase 2: the soil was lost -> back to an earlier step, with a short explanation.
    void GoBack(Step s)
    {
        int to = steps.FindIndex(x => x.id == s.backTo);
        if (to < 0) return;
        StudyLogger.Log("guide_step_back", $"{s.id}->{s.backTo}");
        backMessage = s.lostText;
        backMessageUntil = SM.TrialTime + 5F;
        index = to;
        StartStep();
    }

    // ---- "Practice complete" menu (after all three phases) ----
    // End of a repeated phase 1 / 2: back to the menu (with the dig site and a diggable bucket, as after phase 3).
    void ReturnToMenu()
    {
        int p = repeatOnly;
        index = Mathf.Min(index, steps.Count - 1);
        Done = true;
        DoneAt = SM.TrialTime;
        if (site != null) site.BuildDigSite();
        if (ctl != null) ctl.Machine.EnableCuttingEdges(true);
        StudyLogger.Log("guide_repeat_done", $"phase={p};t={DoneAt:F1}");
        OpenMenu($"repeat_phase{p}_done");
    }

    void OpenMenu(string reason)
    {
        allPhasesDone = true;
        repeatOnly = 0;
        menu = true;
        menuSel = 0;
        menuOpenedAt = Time.unscaledTime;
        menuReason = reason;
        holdA = 0F;
        StudyLogger.Log("practice_menu", $"{reason};free_practice_s={SM.TrialTime - DoneAt:F1}");
    }

    void MenuInput(UnityEngine.InputSystem.Keyboard kb)
    {
        var pad = StickControls.Pad;
        bool up = StickControls.HatUp() || (pad != null && pad.dpad.up.wasPressedThisFrame) || (kb != null && kb.upArrowKey.wasPressedThisFrame);
        bool down = StickControls.HatDown() || (pad != null && pad.dpad.down.wasPressedThisFrame) || (kb != null && kb.downArrowKey.wasPressedThisFrame);
        if (up) menuSel = (menuSel + MenuItems.Length - 1) % MenuItems.Length;
        if (down) menuSel = (menuSel + 1) % MenuItems.Length;
        if (Time.unscaledTime - menuOpenedAt < MenuMinSeconds) return;
        bool ok = StickControls.ConfirmPressed() || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame));
        if (!ok) return;
        if (menuSel == 0)
        {
            StudyLogger.Log("practice_ready", $"free_practice_s={SM.TrialTime - DoneAt:F1};menu={menuReason}");
            SM.EndTrial("participant_ready");
        }
        else JumpToPhase(menuSel, "participant_menu");
    }


    // (Re)start phase p from its first step (phase 3: its instruction page, so the free-practice timer stops
    // and starts again when that page is continued). Works from any step and from the free practice.
    void JumpToPhase(int p, string by)
    {
        int to = p == 3 ? steps.FindIndex(q => q.id == "phase3_intro") : steps.FindIndex(q => q.phase == p);
        if (to < 0) return;
        string from = Done ? "free_practice" : steps[index].id;
        StudyLogger.Log("guide_phase_redo", $"phase={p};by={by};from={from}");
        Done = false;
        menu = false;
        repeatOnly = allPhasesDone ? p : 0;   // after the menu: play only this phase, then back to the menu
        guideStartedAt = SM.TrialTime;
        holdA = 0F;
        backMessageUntil = -1F;
        foreach (var q in steps) { q.held = 0F; q.wrong = 0F; }
        if (p == 1) ctl.Machine.EnableCuttingEdges(false);   // phase 1: solid bucket again, as the first time
        index = to;
        StartStep();
    }

    void StartStep()
    {
        var s = steps[index];
        s.startedAt = SM.TrialTime;
        s.held = 0F;
        lastOk = false;
        wrongStreak = 0F;
        s.onStart?.Invoke();
        if (site != null) dumpsAtStepStart = site.Dumps;
        StudyLogger.Log("guide_step_start", $"{index + 1};{s.id}");
    }

    void Finish(string reason)
    {
        if (Done) return;
        Done = true;
        DoneAt = SM.TrialTime;
        holdA = 0F;
        waitARelease = true;   // the A press that started phase 3 must not count towards "hold A when ready"
        index = Mathf.Min(index, steps.Count - 1);
        if (site != null) site.BuildDigSite();   // free practice always has the dig and dump areas
        if (ctl != null) ctl.Machine.EnableCuttingEdges(true);   // also after a phase-1 redo + jump to phase 3
        StudyLogger.Log("guide_complete", $"{reason};t={DoneAt:F1}");
    }

    // ======================= drawing (instruction display, or right-hand panel of Display 1) =======================

    void OnGUI()
    {
        if (SM == null || SM.CurrentState != StudyManager.State.Running || SM.CurrentTrial.index != trialIndex) return;
        GUI.depth = -10;                           // on top of the task panel (for the yellow outlines)
        if (InstructionDisplay.Begin())
        {
            try { DrawPanel(true); }
            finally { InstructionDisplay.End(); }
        }
        else DrawPanel(false);
        DrawInfoOutline();                         // always on Display 1 (marks a part of the driver screen)
        if (InstructionDisplay.Active && ShowProgressInDriverView) DrawDriverProgress();
    }

    GUIStyle dvOk;

    // Phase 1 drive / dig steps: the hold bar at the bottom centre of the driver view (Display 1), just above the
    // status bar - the same bar as on the instruction screen - and, while the correct control is held, the green
    // line "Correct - keep holding" above it. Nothing else.
    void DrawDriverProgress()
    {
        if (Done || index < 0 || index >= steps.Count) return;
        var s = steps[index];
        if (s.kind != Kind.Drive && s.kind != Kind.Dig) return;

        float k = Mathf.Clamp(Screen.height / 720F, 0.6F, 2F);
        Rect v = StudyLayout.DriverView;                       // viewport, y up
        float vx = v.x * Screen.width, vw = v.width * Screen.width;
        float vBottom = (1F - v.y) * Screen.height;            // top of the status bar (GUI y down)
        float w = Mathf.Min(480F * k, vw - 40F * k), h = 18F * k, border = 3F * k;
        var bar = new Rect(vx + (vw - w) / 2F, vBottom - h - 14F * k, w, h);
        Fill(new Rect(bar.x - border, bar.y - border, bar.width + 2F * border, bar.height + 2F * border), new Color(0.07F, 0.08F, 0.09F, 0.85F));
        Fill(bar, new Color(0.25F, 0.25F, 0.27F));
        Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(s.held / HoldFor(s.id)), bar.height),
             lastOk ? new Color(0.2F, 0.85F, 0.3F) : new Color(0.15F, 0.55F, 0.2F));

        if (lastOk)
        {
            if (dvOk == null)
            {
                dvOk = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                dvOk.normal.textColor = new Color(0.4F, 1F, 0.5F);
            }
            dvOk.fontSize = Mathf.RoundToInt(18 * k);
            float th = 28F * k;
            var tr = new Rect(bar.x, bar.y - border - th - 4F * k, bar.width, th);
            Fill(tr, new Color(0.07F, 0.08F, 0.09F, 0.85F));
            GUI.Label(tr, "Correct - keep holding", dvOk);
        }
    }

    void DrawPanel(bool ownDisplay)
    {
        Rect r;
        float k;
        if (ownDisplay)
        {
            // Instruction display: a centred column over the full height. k = 1 on a 720-pixel-high display
            // (times StudyLayout.InstructionTextScale).
            float sw = InstructionDisplay.Width, sh = InstructionDisplay.Height;
            k = InstructionDisplay.UiScale;
            float m = 20F * k, pw = Mathf.Min(sw - 2F * m, 1100F * k);
            r = new Rect((sw - pw) / 2F, m, pw, sh - 2F * m);
        }
        else
        {
            k = Screen.height / 1080F;
            Rect slot = StudyLayout.GuidePanel;    // right-hand part of Display 1 (viewport coords, y up)
            float top = 150F * k;                  // leave the mode icons (top right) visible
            r = new Rect(slot.x * Screen.width, (1F - slot.yMax) * Screen.height + top,
                         slot.width * Screen.width, slot.height * Screen.height - top);
        }
        MakeStyles(k);
        float pad = 20F * k, x = r.x + pad, w = r.width - 2 * pad, y = r.y + pad;

        if (Done) { if (menu) DrawMenu(r, x, y, w, k); else DrawFree(r, x, y, w, k); return; }

        Box(r, new Color(0.07F, 0.08F, 0.09F, 0.95F));
        var s = steps[index];

        // Header: phase + step within the phase, and one progress segment per step of this phase.
        int first = steps.FindIndex(q => q.phase == s.phase), count = steps.FindAll(q => q.phase == s.phase).Count;
        int pos = index - first;
        GUI.Label(new Rect(x, y, w, 24 * k), $"PHASE {s.phase} OF 3 - {PhaseName(s.phase).ToUpper()}   -   step {pos + 1} of {count}", small);
        y += 28 * k;
        float seg = w / count;
        for (int i = 0; i < count; i++)
        {
            Color c = i < pos ? new Color(0.2F, 0.8F, 0.3F) : i == pos ? new Color(1F, 0.8F, 0.1F) : new Color(0.3F, 0.3F, 0.32F);
            Fill(new Rect(x + i * seg + 1, y, seg - 2, 8 * k), c);
        }
        y += 22 * k;

        // Title.
        GUI.Label(new Rect(x, y, w, 40 * k), s.title, title);
        y += 44 * k;

        if (s.kind == Kind.Info) { DrawInfo(s, x, y, w, r, k); return; }

        // Mode badge.
        bool needDrive = s.kind == Kind.ModeDrive || s.kind == Kind.Drive;
        bool modeOk = s.kind == Kind.ModeDrive || s.kind == Kind.ModeDig || needDrive == ctl.IsTravelMode;
        string badge = s.kind == Kind.ModeDrive || s.kind == Kind.ModeDig
            ? "Now in " + (ctl.IsTravelMode ? "DRIVE" : "DIG") + " mode"
            : "Mode needed: " + (needDrive ? "DRIVE" : "DIG") + (modeOk ? "   (OK)" : "   - " + StickControls.ModeSwitch);
        Fill(new Rect(x, y, w, 30 * k), modeOk ? new Color(0.15F, 0.45F, 0.2F) : new Color(0.75F, 0.35F, 0.05F));
        GUI.Label(new Rect(x + 10 * k, y + 3 * k, w - 20 * k, 26 * k), badge, body);
        y += 42 * k;

        // Instructions.
        string how = HowText(s);
        float hh = body.CalcHeight(new GUIContent(how), w);
        GUI.Label(new Rect(x, y, w, hh), how, body);
        y += hh + 14 * k;

        if (s.kind == Kind.Task) { DrawTask(s, x, y, w, k, modeOk); return; }

        // Stick diagrams (or the B button / trigger for the mode steps).
        float d = Mathf.Min((w - 30 * k) / 2F, 170F * k);
        if (s.kind == Kind.ModeDrive || s.kind == Kind.ModeDig)
        {
            var b = new Rect(x + (w - d) / 2F, y + 22 * k, d, d);
            Fill(b, new Color(0.8F, 0.15F, 0.12F));
            GUI.Label(b, StickControls.ModeBox, bigCenter);
            y += d + 34 * k;
        }
        else if (StickControls.SingleStick)
        {
            y = DrawSingleStick(s, x, y, w, d, k);
        }
        else
        {
            var input = ctl.LastInput;
            Vector2 curL, curR;
            if (ctl.IsTravelMode) { curL = new Vector2(0, input.trackLeft); curR = new Vector2(0, input.trackRight); }
            else { curL = new Vector2(input.swing, input.arm); curR = new Vector2(input.bucket, input.boom); }
            float gap = w - 2 * d;
            Stick(new Rect(x, y + 22 * k, d, d), "LEFT STICK", s.targetL, curL, k);
            Stick(new Rect(x + d + gap, y + 22 * k, d, d), "RIGHT STICK", s.targetR, curR, k);
            y += d + 34 * k;
        }
        bool stickStep = s.kind == Kind.Drive || s.kind == Kind.Dig;
        // Keyboard keys (s.keys) are not shown: the study uses the gamepad only.
        if (stickStep) GUI.Label(new Rect(x, y, w, 24 * k), "Yellow = target,  white dot = your stick", small);
        y += 34 * k;

        // Hold bar.
        if (stickStep)
        {
            float need = HoldFor(s.id);
            GUI.Label(new Rect(x, y, w, 26 * k), "Hold the correct control until the bar is full", body);
            y += 30 * k;
            var bar = new Rect(x, y, w, 22 * k);
            Fill(bar, new Color(0.25F, 0.25F, 0.27F));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(s.held / need), bar.height), lastOk ? new Color(0.2F, 0.85F, 0.3F) : new Color(0.15F, 0.55F, 0.2F));
            y += 34 * k;

            // Hints wait HintGraceSeconds after the step starts and need WrongBeforeHint of wrong input,
            // so they do not flash up while the previous control is still being released.
            bool showWrong = SM.TrialTime - s.startedAt >= HintGraceSeconds && wrongStreak >= WrongBeforeHint;
            string h = lastOk ? "Correct - keep holding."
                     : !showWrong ? ""
                     : !modeOk ? $"You are in {(ctl.IsTravelMode ? "DRIVE" : "DIG")} mode. {StickControls.Cap(StickControls.ModeSwitch)} to switch to {(needDrive ? "DRIVE" : "DIG")} mode."
                     : "Not quite - move the white dot onto the yellow target.";
            hint.normal.textColor = lastOk ? new Color(0.4F, 1F, 0.5F) : new Color(1F, 0.65F, 0.25F);
            GUI.Label(new Rect(x, y, w, 60 * k), h, hint);
        }
    }

    // Phase 2 task: live value line + hint (soil lost / wrong mode / contextual).
    void DrawTask(Step s, float x, float y, float w, float k, bool modeOk)
    {
        if (s.status != null)
        {
            Fill(new Rect(x, y, w, 34 * k), new Color(0.16F, 0.17F, 0.19F));
            GUI.Label(new Rect(x + 10 * k, y + 5 * k, w - 20 * k, 28 * k), s.status(), body);
            y += 48 * k;
        }
        bool settled = SM.TrialTime - s.startedAt >= HintGraceSeconds;
        string h = SM.TrialTime < backMessageUntil ? backMessage
                 : lastOk ? "Good!"
                 : !settled ? ""
                 : !modeOk ? $"You are in DRIVE mode. {StickControls.Cap(StickControls.ModeSwitch)} to switch to DIG mode."
                 : s.hint != null ? s.hint() : "";
        hint.normal.textColor = lastOk ? new Color(0.4F, 1F, 0.5F) : new Color(1F, 0.65F, 0.25F);
        GUI.Label(new Rect(x, y, w, 80 * k), h, hint);
    }

    // Phase 3: small card - free practice, hold A when ready (time left only in the driver view's task panel).
    void DrawFree(Rect r, float x, float y, float w, float k)
    {
        var card = new Rect(r.x, r.y, r.width, 330F * k);
        Box(card, new Color(0.07F, 0.08F, 0.09F, 0.95F));
        GUI.Label(new Rect(x, y, w, 24 * k), "PHASE 3 OF 3 - FREE PRACTICE", small);
        y += 30 * k;
        GUI.Label(new Rect(x, y, w, 40 * k), "Practice on your own", title);
        y += 46 * k;
        string how = "Drive around, or dig and dump anywhere you like - whatever you want to practice.";
        float hh = body.CalcHeight(new GUIContent(how), w);
        GUI.Label(new Rect(x, y, w, hh), how, body);
        y += hh + 16 * k;

        // No timer here: the time left is shown in the task panel of the driver view (PracticeScenario.ProgressText).

        float bs = 46F * k;
        float bw = ButtonBoxWidth(bs, k);
        var a = new Rect(x, y, bw, bs);
        Fill(a, new Color(0.2F, 0.7F, 0.25F));
        GUI.Label(a, StickControls.ConfirmBox, bigCenter);
        hint.normal.textColor = Color.white;
        GUI.Label(new Rect(x + bw + 14 * k, y, w - bw - 14 * k, bs), $"Done practicing? {StickControls.HoldConfirm} to start the tasks", hint);
        y += bs + 8 * k;
        var bar = new Rect(x, y, w, 14 * k);
        Fill(bar, new Color(0.25F, 0.25F, 0.27F));
        Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(holdA / HoldAToContinue), bar.height), new Color(0.2F, 0.85F, 0.3F));
    }

    // "Practice complete" menu: start the tasks or repeat any phase.
    void DrawMenu(Rect r, float x, float y, float w, float k)
    {
        Box(r, new Color(0.07F, 0.08F, 0.09F, 0.95F));
        GUI.Label(new Rect(x, y, w, 24 * k), "PRACTICE COMPLETE - ALL 3 PHASES DONE", small);
        y += 30 * k;
        GUI.Label(new Rect(x, y, w, 40 * k), "What would you like to do next?", title);
        y += 48 * k;
        string intro = (menuReason == "time_up" ? "The free practice time is over. " : "") +
                       "You can start the tasks now, or first repeat any part of the practice.";
        float hh = body.CalcHeight(new GUIContent(intro), w);
        GUI.Label(new Rect(x, y, w, hh), intro, body);
        y += hh + 18 * k;

        float ih = 46F * k;
        for (int i = 0; i < MenuItems.Length; i++)
        {
            bool sel = i == menuSel;
            var row = new Rect(x, y, w, ih);
            Fill(row, sel ? new Color(1F, 0.8F, 0.1F) : new Color(0.18F, 0.19F, 0.21F));
            var st = new GUIStyle(body) { alignment = TextAnchor.MiddleLeft, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
            st.normal.textColor = sel ? new Color(0.08F, 0.08F, 0.08F) : new Color(0.9F, 0.9F, 0.92F);
            GUI.Label(new Rect(row.x + 16 * k, row.y, row.width - 32 * k, row.height), (sel ? "> " : "   ") + MenuItems[i], st);
            y += ih + 8 * k;
        }
        y += 10 * k;
        string how = StickControls.Active
            ? $"Move the hat switch up / down to choose, then press the {StickControls.ConfirmName}."
            : "Use the D-pad up / down to choose, then press A.";
        hint.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, w, 60 * k), how, hint);
    }

    // Info page: text and "press A" at the bottom of the panel (the yellow outline is drawn by DrawInfoOutline).
    void DrawInfo(Step s, float x, float y, float w, Rect panel, float k)
    {
        string how = HowText(s).Replace("{HOLD}", StickControls.HoldConfirm);   // joystick or gamepad, as connected now
        // Repeating a phase from the menu: drop the "Phase N is complete." lead-in of the phase start pages.
        if (repeatOnly > 0 && s.redoPhase > 0)
        {
            int cut = how.IndexOf("\n\n");
            if (cut >= 0) how = how.Substring(cut + 2);
        }
        float hh = body.CalcHeight(new GUIContent(how), w);
        GUI.Label(new Rect(x, y, w, hh), how, body);

        bool ready = SM.TrialTime - s.startedAt >= InfoMinSeconds;
        float bs = 54F * k, by = panel.yMax - bs - 24F * k;
        float bw = ButtonBoxWidth(bs, k);
        var a = new Rect(x, by, bw, bs);
        Fill(a, ready ? new Color(0.2F, 0.7F, 0.25F) : new Color(0.3F, 0.3F, 0.32F));
        GUI.Label(a, StickControls.ConfirmBox, bigCenter);
        string cont = StickControls.Active ? $"Press the {StickControls.ConfirmName} to continue" : "Press A to continue";
        GUI.Label(new Rect(x + bw + 16F * k, by, w - bw - 16F * k, bs), ready ? cont : "Read this first...", title);


    }

    // Info page: pulsing yellow outline around the explained part of Display 1 (task panel, status bar, incidents).
    void DrawInfoOutline()
    {
        if (Done || index < 0 || index >= steps.Count || steps[index].kind != Kind.Info) return;
        Rect target = AreaRect(steps[index].area);
        if (target.width <= 0F) return;
        float k = Screen.height / 1080F;
        float pulse = 0.55F + 0.45F * Mathf.Sin(Time.unscaledTime * 5F);
        Outline(target, new Color(1F, 0.85F, 0.1F, pulse), 4F * k);
    }

    // Screen rectangles (GUI coordinates, y down) of the parts explained by the info pages.
    // Task panel geometry matches ScenarioRunner.OnGUI (16 px margin, 520 px wide without map, text 118 + incident line 26).
    static Rect AreaRect(Area a)
    {
        const float m = 16F, textH = 118F, incH = 26F;
        float width = ScenarioRunner.Current != null && ScenarioRunner.Current.Map != null ? 640F : 520F;
        switch (a)
        {
            case Area.TaskPanel: return new Rect(m, m, width, textH + incH + 16F);
            case Area.Incidents: return new Rect(m, m + 8F + textH - 4F, width, incH + 8F);
            case Area.StatusBar:
                float h = StudyLayout.BarHeight * Screen.height;
                return new Rect(0F, Screen.height - h, Screen.width, h);
            default: return Rect.zero;
        }
    }

    // Width of the green button box: square for "A", wider for a joystick label such as "THUMB".
    float ButtonBoxWidth(float bs, float k) =>
        Mathf.Max(bs, bigCenter.CalcSize(new GUIContent(StickControls.ConfirmBox)).x + 16F * k);

    // ONE flight stick: a single stick diagram (or a twist bar for the bucket) and, in DIG mode, the trigger state.
    float DrawSingleStick(Step s, float x, float y, float w, float d, float k)
    {
        var gi = StickInput;
        Vector2 cur = gi != null ? gi.stick.StickOut : Vector2.zero;
        float tw = gi != null ? gi.stick.TwistOut : 0F;
        bool trig = gi != null && gi.stick.TriggerHeld;

        if (s.twist1 != 0F)
        {
            // Twist bar: yellow target at the end to twist to, white dot = current twist.
            GUI.Label(new Rect(x, y, w, 20 * k), "TWIST THE GRIP", center);
            var bar = new Rect(x, y + 26 * k, w, 44 * k);
            Fill(bar, new Color(0.16F, 0.17F, 0.19F));
            Fill(new Rect(bar.center.x - 1, bar.y, 2, bar.height), new Color(0.32F, 0.33F, 0.35F));
            float half = bar.width / 2F - 18F * k;
            float ts = 30F * k, tx = bar.center.x + Mathf.Sign(s.twist1) * half;
            Fill(new Rect(tx - ts / 2, bar.center.y - ts / 2, ts, ts), new Color(1F, 0.8F, 0.1F, 0.9F));
            GUI.Label(new Rect(bar.x + 6 * k, bar.yMax + 2 * k, 120 * k, 20 * k), "< left", small);
            GUI.Label(new Rect(bar.xMax - 60 * k, bar.yMax + 2 * k, 60 * k, 20 * k), "right >", small);
            float cs = 14F * k, cx = bar.center.x + Mathf.Clamp(tw, -1F, 1F) * half;
            Fill(new Rect(cx - cs / 2, bar.center.y - cs / 2, cs, cs), lastOk ? new Color(0.3F, 1F, 0.4F) : Color.white);
            y += 26 * k + 44 * k + 30 * k;
            GUI.Label(new Rect(x, y, w, 22 * k), "Keep the stick itself centred.", small);
            return y + 26 * k;
        }

        var r = new Rect(x + (w - d) / 2F, y + 22 * k, d, d);
        Stick(r, "STICK", s.target1, cur, k);
        y += d + 34 * k;
        if (s.kind == Kind.Dig)
        {
            string need = s.trigger1 ? "Trigger: HOLD it" : "Trigger: do NOT press";
            bool good = trig == s.trigger1;
            hint.normal.textColor = good ? new Color(0.4F, 1F, 0.5F) : new Color(1F, 0.65F, 0.25F);
            GUI.Label(new Rect(x, y - 6 * k, w, 24 * k), need + (trig ? "   (now: held)" : "   (now: not pressed)"), hint);
            y += 22 * k;
        }
        return y;
    }

    static void Outline(Rect r, Color c, float t)
    {
        r = new Rect(r.x - t, r.y - t, r.width + 2 * t, r.height + 2 * t);
        r.xMin = Mathf.Max(0F, r.xMin); r.yMin = Mathf.Max(0F, r.yMin);
        r.xMax = Mathf.Min(Screen.width, r.xMax); r.yMax = Mathf.Min(Screen.height, r.yMax);
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    // One stick: square area, cross-hair, yellow target marker, current position dot.
    void Stick(Rect r, string label, Vector2 target, Vector2 cur, float k)
    {
        GUI.Label(new Rect(r.x, r.y - 22 * k, r.width, 20 * k), label, center);
        Fill(r, new Color(0.16F, 0.17F, 0.19F));
        Fill(new Rect(r.center.x - 1, r.y, 2, r.height), new Color(0.32F, 0.33F, 0.35F));
        Fill(new Rect(r.x, r.center.y - 1, r.width, 2), new Color(0.32F, 0.33F, 0.35F));
        float rad = r.width / 2F - 14 * k;
        Vector2 P(Vector2 v) => new Vector2(r.center.x + v.x * rad, r.center.y - v.y * rad);   // stick up = screen up

        bool idle = target == Vector2.zero;
        Vector2 tp = P(target);
        float ts = (idle ? 22F : 30F) * k;
        Color tc = idle ? new Color(0.6F, 0.6F, 0.62F, 0.8F) : new Color(1F, 0.8F, 0.1F, 0.9F);
        Fill(new Rect(tp.x - ts / 2, tp.y - ts / 2, ts, ts), tc);
        if (idle) GUI.Label(new Rect(r.x, r.yMax - 22 * k, r.width, 20 * k), "keep centred", center);

        Vector2 cp = P(new Vector2(Mathf.Clamp(cur.x, -1, 1), Mathf.Clamp(cur.y, -1, 1)));
        float cs = 14F * k;
        Fill(new Rect(cp.x - cs / 2, cp.y - cs / 2, cs, cs), lastOk ? new Color(0.3F, 1F, 0.4F) : Color.white);
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void Box(Rect r, Color c) => Fill(r, c);

    void MakeStyles(float k)
    {
        if (small == null)
        {
            small = new GUIStyle(GUI.skin.label) { wordWrap = true };
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            body = new GUIStyle(GUI.skin.label) { wordWrap = true };
            hint = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            bigCenter = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            small.normal.textColor = new Color(0.75F, 0.75F, 0.78F);
            center.normal.textColor = new Color(0.8F, 0.8F, 0.82F);
            title.normal.textColor = body.normal.textColor = bigCenter.normal.textColor = Color.white;
        }
        small.fontSize = Mathf.RoundToInt(15 * k);
        title.fontSize = Mathf.RoundToInt(28 * k);
        body.fontSize = Mathf.RoundToInt(18 * k);
        hint.fontSize = Mathf.RoundToInt(18 * k);
        center.fontSize = Mathf.RoundToInt(14 * k);
        bigCenter.fontSize = Mathf.RoundToInt(26 * k);
    }
}
