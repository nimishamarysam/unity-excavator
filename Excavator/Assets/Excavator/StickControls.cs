using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// ---------------------------------------------------------------------------
// JOYSTICK MODE
// When a flight stick is connected (and ExcavatorController.useFlightStick is on), the study is run with the
// joystick: every instruction, briefing and on-screen prompt names the joystick controls only, and the machine,
// the mode switch and all study buttons (start, continue, "hold to finish practice", pause, end trial,
// skip step) work from the joystick(s).
// The F310 gamepad and the keyboard are NOT part of the study setup, but they keep working in the background
// (for testing at home) unless BlockGamepadKeyboard is ticked (ExcavatorController inspector).
// With no joystick connected everything uses the F310 + keyboard as before.
// Only Thrustmaster TCA Sidestick Airbus (A320) sticks count as joysticks (FlightStickInput.OnlyA320, default on):
// other joysticks, wheels, pedals or throttles that are plugged in are ignored and do not switch on joystick mode.
//
// Joystick buttons (numbers as in Windows "Set up USB game controllers"; change them below if needed):
//   Trigger (1)        single stick: TAP = DRIVE/DIG mode, HOLD + forward/back = arm
//                      two sticks:   press + release either trigger = DRIVE/DIG mode
//   ConfirmButton (2)  = gamepad A: start / continue / hold to finish the practice
//   PauseButton (5)    = gamepad Start: pause / resume                 (experimenter)
//   EndTrialButton (6) = gamepad X: end the current trial early        (experimenter)
//   SkipStepButton (7) = gamepad Select: skip the current practice step (experimenter)
//   Hat left / right   = participant number -1 / +1 on the setup screen; hat up = practice on/off
//   In two-stick mode a button counts on EITHER stick.
// The live overlay at the bottom of the screen lists the buttons that are pressed ("pressed: button2 ...")
// so you can check which number a physical button has.
//
// Experimenter keyboard keys (Enter, P, End, N, C = calibrate, S = swap sticks) always work, unless
// BlockGamepadKeyboard is on AND AllowExperimenterKeyboard is off. They never move the machine.
// ---------------------------------------------------------------------------
public static class StickControls
{
    // Set by ExcavatorController.Awake from its inspector fields.
    public static bool Enabled = true;                  // useFlightStick
    public static StickLayout Layout = StickLayout.Auto;
    public static bool AllowExperimenterKeyboard = true;
    // true = in joystick mode the gamepad and keyboard are ignored completely (strict lab setting).
    // false (default) = they still work alongside the joystick (home testing), but are never mentioned on screen.
    public static bool BlockGamepadKeyboard = false;

    public static int ConfirmButton = 2;
    public static int PauseButton = 5;
    public static int EndTrialButton = 6;
    public static int SkipStepButton = 7;

    // Text used in the instructions for the confirm button (e.g. "thumb button", "button 2").
    public static string ConfirmName = "thumb button";
    public static string ConfirmShort = "THUMB";

    // ---------------- which device is in use ----------------
    static int cachedFrame = -1;
    static List<Joystick> cachedSticks = new List<Joystick>();
    static bool lastActive;

    public static List<Joystick> Sticks
    {
        get
        {
            if (cachedFrame != Time.frameCount) { cachedSticks = DualStickInput.Sticks(); cachedFrame = Time.frameCount; }
            return cachedSticks;
        }
    }

    // True = joystick mode: the study texts name the joystick controls only (see BlockGamepadKeyboard).
    public static bool Active
    {
        get
        {
            bool a = Enabled && Sticks.Count >= 1;
            if (a != lastActive)
            {
                lastActive = a;
                Debug.Log(a ? $"[StickControls] Joystick connected ({Sticks.Count}): joystick mode, gamepad + keyboard " +
                              (BlockGamepadKeyboard ? "OFF" : "still active (not shown on screen)")
                            : "[StickControls] No joystick: F310 gamepad + keyboard");
                if (StudyLogger.Instance != null) StudyLogger.Log("input_device", a ? $"joystick;sticks={Sticks.Count}" : "gamepad_keyboard");
            }
            return a;
        }
    }

    public static bool TwoSticks => Active && (Layout == StickLayout.TwoSticks || (Layout == StickLayout.Auto && Sticks.Count >= 2));
    public static bool SingleStick => Active && !TwoSticks;

    // Gamepad + keyboard ignored? Only in joystick mode with BlockGamepadKeyboard on.
    public static bool GamepadKeyboardBlocked => BlockGamepadKeyboard && Active;

    // Keyboard for the experimenter shortcuts (null when it must not be used).
    public static Keyboard ExperimenterKeyboard => (!GamepadKeyboardBlocked || AllowExperimenterKeyboard) ? Keyboard.current : null;

    // Gamepad for the study prompts (null when blocked).
    public static Gamepad Pad => GamepadKeyboardBlocked ? null : Gamepad.current;

    // ---------------- buttons ----------------
    static readonly Dictionary<Joystick, List<ButtonControl>> buttonLists = new Dictionary<Joystick, List<ButtonControl>>();

    // Button n (1 = trigger) of one joystick. Looks for "button{n}" / "Button {n}", else the n-th button of the device.
    public static ButtonControl Button(Joystick j, int n)
    {
        if (j == null || n < 1) return null;
        if (n == 1 && j.trigger != null) return j.trigger;
        var b = j.TryGetChildControl<ButtonControl>("button" + n);
        if (b != null) return b;
        if (!buttonLists.TryGetValue(j, out var list))
        {
            list = new List<ButtonControl>();
            foreach (var c in j.allControls)
                if (c is ButtonControl bc && c.parent == j && !c.synthetic) list.Add(bc);
            buttonLists[j] = list;
        }
        return n - 1 < list.Count ? list[n - 1] : null;
    }

    public static bool Pressed(int n)
    {
        if (!Active) return false;
        foreach (var j in Sticks) { var b = Button(j, n); if (b != null && b.wasPressedThisFrame) return true; }
        return false;
    }

    public static bool Held(int n)
    {
        if (!Active) return false;
        foreach (var j in Sticks) { var b = Button(j, n); if (b != null && b.isPressed) return true; }
        return false;
    }

    // Joystick.hatswitch is typed as Vector2Control; on HID joysticks it is a DpadControl at runtime.
    static DpadControl Hat(Joystick j) => (j.hatswitch as DpadControl) ?? j.TryGetChildControl<DpadControl>("hat");

    public static bool HatLeft() => HatPressed(h => h.left);
    public static bool HatRight() => HatPressed(h => h.right);
    public static bool HatUp() => HatPressed(h => h.up);

    static bool HatPressed(System.Func<DpadControl, ButtonControl> dir)
    {
        if (!Active) return false;
        foreach (var j in Sticks) { var h = Hat(j); if (h != null && dir(h).wasPressedThisFrame) return true; }
        return false;
    }

    // ---------------- study prompts: joystick button OR gamepad button (unless the gamepad is blocked) ----------------
    public static bool ConfirmPressed() => Pressed(ConfirmButton) || (Pad != null && Pad.buttonSouth.wasPressedThisFrame);
    public static bool ConfirmHeld() => Held(ConfirmButton) || (Pad != null && Pad.buttonSouth.isPressed);
    public static bool PausePressed() => Pressed(PauseButton) || (Pad != null && Pad.startButton.wasPressedThisFrame);
    public static bool EndTrialPressed() => Pressed(EndTrialButton) || (Pad != null && Pad.buttonWest.wasPressedThisFrame);
    public static bool SkipStepPressed() => Pressed(SkipStepButton) || (Pad != null && Pad.selectButton.wasPressedThisFrame);

    // ---------------- texts for the instructions ----------------
    public static string ConfirmLabel => Active ? "the " + ConfirmName : "A";
    public static string ConfirmBox
    {
        get
        {
            if (!Active) return "A";
            string n = ConfirmName.Trim();
            if (n.StartsWith("button ")) return n.Substring(7).ToUpper();      // "button 2" -> "2"
            return n == "thumb button" ? ConfirmShort : n.Split(' ')[0].ToUpper();
        }
    }
    public static string PressConfirm => Active ? $"Press the {ConfirmName} on the joystick" : "Press A on the gamepad";
    public static string HoldConfirm => Active ? $"Hold the {ConfirmName}" : "Hold A";

    public static string ModeBox => Active ? "TRIGGER" : "B";
    public static string ModeSwitch =>
        SingleStick ? "tap the trigger (press and let go quickly)"
        : TwoSticks ? "press and release the trigger on either stick"
        : "press B";
    public static string PauseLabel => Active ? $"joystick button {PauseButton}" : "Start (gamepad)";

    public static string DriveForward => SingleStick ? "push the stick forward" : "push both travel levers forward";
    public static string DriveBack => SingleStick ? "pull the stick back" : "pull both travel levers back";

    public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}
