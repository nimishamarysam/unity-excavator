using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// ---------------------------------------------------------------------------
// Single flight-stick control of the excavator.
// Supported: Thrustmaster TCA Sidestick (Airbus / A320 edition) and Logitech WingMan Force 3D.
// With OnlyA320 = true (default, ExcavatorController "Only A320 Sticks") ONLY Thrustmaster TCA Sidestick
// Airbus (A320) sticks are used; every other joystick, wheel, pedal set or throttle is ignored.
//
//   Operation (dig) mode                 Travel (drive) mode
//   ---------------------------------    -----------------------------------
//   Stick X (left/right)  = swing        Stick Y (fwd/back) = drive both tracks
//   Stick Y (fwd/back)    = boom         Stick X (left/right) = steer
//   Trigger held + Y      = arm            (full X with no Y = spin on the spot)
//   Twist (rotate grip)   = bucket
//
//   Trigger TAP (press + release within tapMaxSeconds without moving the arm)
//   toggles travel / operation mode. The F310 B button and the keyboard also still work
//   (home testing) unless StickControls.BlockGamepadKeyboard is on.
//
// The throttle is NOT used: it does not spring back, so whatever it drove
// would keep moving after the hand lets go.
//
// Calibration: press C on the keyboard while a trial is running, move the stick
// to every edge and twist both ways for 6 s, then let go for 2 s. Saved per
// joystick model (PlayerPrefs). The TCA is spring-centred and reports its full
// range, so it normally works without calibrating.
// ---------------------------------------------------------------------------
public enum FlightStickChoice { Auto, ThrustmasterTCA, WingMan, AnyJoystick }

public class FlightStickInput : IExcavatorInput
{
    public FlightStickChoice choice = FlightStickChoice.Auto;

    public float stickDeadZone = 0.08F;
    public float twistDeadZone = 0.12F;
    public float smoothingSeconds = 0.05F; // low-pass filter on stick/twist against jitter
    public float twistScale = 1.0F;
    public float tapMaxSeconds = 0.4F;

    // Leave empty to auto-detect (twist usage / "rz"). Otherwise a control name from the overlay.
    public string twistAxisName = "";

    // Travel: when reversing, flip steering so "stick right" makes the back of the machine go right.
    public bool carStyleReverse = true;

    public bool invertSwing, invertBoom, invertArm, invertBucket, invertDrive, invertSteer;

    Joystick device;
    AxisControl twist;
    ButtonControl trigger;
    string model = "";          // "TCA", "WingMan" or the product name; used for calibration keys
    float rescanTimer;

    float triggerDownTime = -1F;
    bool armUsedDuringHold;
    bool boomLocked;

    float fx, fy, ftw;

    // Last processed values (after calibration + dead zone), for the practice-guide diagrams.
    public Vector2 StickOut { get; private set; }
    public float TwistOut { get; private set; }
    public bool TriggerHeld => trigger != null && trigger.isPressed;

    public string DeviceName => device != null ? device.displayName : "none";
    public bool IsConnected => Find() != null;

    static float Dz(float v, float dz)
    {
        float a = Mathf.Abs(v);
        if (a < dz) return 0F;
        return Mathf.Sign(v) * Mathf.Clamp01((a - dz) / (1F - dz));
    }

    // ---------------- device selection ----------------
    static string NameOf(Joystick j) =>
        ((j.description.manufacturer ?? "") + " " + (j.description.product ?? "") + " " + j.displayName).ToLowerInvariant();

    // Throttle quadrants etc. also show up as joysticks: never use them as a control stick.
    public static bool IsStick(Joystick j)
    {
        string n = NameOf(j);
        return !(n.Contains("q-eng") || n.Contains("quadrant") || n.Contains("throttle") || n.Contains("add-on"));
    }
    public static bool IsTCA(Joystick j) => IsThrustmaster(j) && IsStick(j);
    public static bool IsWingMan(Joystick j) => NameOf(j).Contains("wingman");

    // ---------------- A320 sticks only ----------------
    // true = only Thrustmaster TCA Sidestick Airbus (A320) sticks count as joysticks (set by ExcavatorController).
    public static bool OnlyA320 = true;
    // Words that identify an A320 stick in the device name (any of them, lower case, separated by ';').
    public static string A320NameMatch = "sidestick;airbus;a320";

    static bool IsThrustmaster(Joystick j)
    {
        string caps = j.description.capabilities ?? "";
        string n = NameOf(j);
        return caps.Contains("\"vendorId\":1103") || n.Contains("thrustmaster") || n.Contains("tca");   // 0x044F
    }

    // Thrustmaster TCA Sidestick Airbus / Sidestick X Airbus ("A320" stick). Throttle quadrants are excluded.
    public static bool IsA320(Joystick j)
    {
        if (j == null || !IsStick(j) || !IsThrustmaster(j)) return false;
        string n = NameOf(j);
        foreach (var w in A320NameMatch.Split(';'))
            if (w.Trim().Length > 0 && n.Contains(w.Trim().ToLowerInvariant())) return true;
        return false;
    }

    // May this joystick be used as a control stick?
    public static bool IsUsable(Joystick j) => IsStick(j) && (!OnlyA320 || IsA320(j));

    static readonly HashSet<int> reported = new HashSet<int>();
    // Logs each connected joystick once: used or ignored (helps when several peripherals are plugged in).
    public static void Report(Joystick j)
    {
        if (j == null || !reported.Add(j.deviceId)) return;
        Debug.Log($"[FlightStick] Joystick '{j.displayName}' (manufacturer '{j.description.manufacturer}', product '{j.description.product}', " +
                  $"serial '{j.description.serial}') -> {(IsUsable(j) ? "USED" : OnlyA320 ? "IGNORED (not an A320 sidestick)" : "IGNORED (throttle / add-on)")}");
    }

    Joystick Pick()
    {
        Joystick tca = null, wing = null, any = null;
        foreach (var j in Joystick.all)
        {
            Report(j);
            if (!IsUsable(j)) continue;
            if (tca == null && IsTCA(j)) tca = j;
            else if (wing == null && IsWingMan(j)) wing = j;
            else if (any == null) any = j;
        }
        switch (choice)
        {
            case FlightStickChoice.ThrustmasterTCA: return tca;
            case FlightStickChoice.WingMan: return wing;
            case FlightStickChoice.AnyJoystick: return tca ?? wing ?? any;
            default: return tca ?? wing ?? any;   // Auto: TCA first (spring-centred, full range)
        }
    }

    Joystick Find()
    {
        // In Auto, look again now and then so a TCA plugged in mid-session takes over from a WingMan.
        if (device != null && device.added)
        {
            if (choice != FlightStickChoice.Auto || IsTCA(device)) return device;
            rescanTimer += Time.unscaledDeltaTime;
            if (rescanTimer < 2F) return device;
            rescanTimer = 0F;
            var better = Pick();
            if (better == null || better == device) return device;
        }

        device = Pick();
        twist = null; trigger = null;
        if (device == null) return null;

        model = IsTCA(device) ? "TCA" : IsWingMan(device) ? "WingMan" : (device.description.product ?? "joystick");
        // Two identical A320 sticks: keep a separate calibration per physical stick when it reports a serial number.
        if (model == "TCA" && !string.IsNullOrEmpty(device.description.serial)) model += "_" + device.description.serial;
        if (!string.IsNullOrEmpty(twistAxisName)) twist = device.TryGetChildControl<AxisControl>(twistAxisName);
        if (twist == null) twist = device.twist;
        if (twist == null) twist = device.TryGetChildControl<AxisControl>("rz");
        trigger = device.trigger ?? device.TryGetChildControl<ButtonControl>("trigger");
        LoadCal();
        fx = fy = ftw = 0F;

        var sb = new System.Text.StringBuilder();
        sb.Append($"[FlightStick] Using '{device.displayName}' (manufacturer '{device.description.manufacturer}', product '{device.description.product}', model {model}). Controls:");
        foreach (var c in device.allControls) if (c.parent == device) sb.Append(' ').Append(c.name);
        sb.Append($"\n  twist={(twist != null ? twist.name : "MISSING")}, trigger={(trigger != null ? trigger.name : "MISSING")}, calibrated={haveCal}");
        Debug.Log(sb.ToString());
        return device;
    }

    // ---------------- calibration ----------------
    struct AxisCal { public float min, centre, max; }
    static AxisCal Def(float mn, float c, float mx) => new AxisCal { min = mn, centre = c, max = mx };
    AxisCal calX, calY, calTw;
    bool haveCal;
    int calPhase; float calTimer, cenSumX, cenSumY, cenSumTw; int cenN;
    const float CalMoveSeconds = 6F, CalCentreSeconds = 2F;
    float lastRawX, lastRawY, lastRawTw;
    public bool IsCalibrating => calPhase != 0;

    string Key(string axis, string part) => $"flightstick_{model}_{axis}_{part}";

    void LoadCal()
    {
        haveCal = PlayerPrefs.HasKey(Key("x", "min"));
        if (haveCal) { calX = LoadAxis("x"); calY = LoadAxis("y"); calTw = LoadAxis("tw"); return; }
        if (model == "WingMan")
        {   // measured on the lab WingMan (it reports only part of its range)
            calX = Def(-0.33F, -0.03F, 0.27F); calY = Def(-0.535F, 0F, 0.398F); calTw = Def(-1F, -0.045F, 0.09F);
        }
        else { calX = Def(-1F, 0F, 1F); calY = Def(-1F, 0F, 1F); calTw = Def(-1F, 0F, 1F); }
    }
    AxisCal LoadAxis(string n) => new AxisCal
    {
        min = PlayerPrefs.GetFloat(Key(n, "min")), centre = PlayerPrefs.GetFloat(Key(n, "c")), max = PlayerPrefs.GetFloat(Key(n, "max"))
    };
    void SaveAxis(string n, AxisCal a)
    {
        PlayerPrefs.SetFloat(Key(n, "min"), a.min); PlayerPrefs.SetFloat(Key(n, "c"), a.centre); PlayerPrefs.SetFloat(Key(n, "max"), a.max);
    }

    static float Apply(float v, AxisCal a)
    {
        if (v >= a.centre) { float r = a.max - a.centre; return r > 0.02F ? Mathf.Clamp01((v - a.centre) / r) : 0F; }
        float l = a.centre - a.min; return l > 0.02F ? -Mathf.Clamp01((a.centre - v) / l) : 0F;
    }

    public void StartCalibration()
    {
        if (Find() == null) return;
        calPhase = 1; calTimer = 0F;
        calX = Def(lastRawX, lastRawX, lastRawX); calY = Def(lastRawY, lastRawY, lastRawY); calTw = Def(lastRawTw, lastRawTw, lastRawTw);
        Debug.Log("[FlightStick] Calibration started");
    }

    void UpdateCalibration(float x, float y, float tw)
    {
        calTimer += Time.deltaTime; // called from FixedUpdate
        if (calPhase == 1)
        {
            calX.min = Mathf.Min(calX.min, x); calX.max = Mathf.Max(calX.max, x);
            calY.min = Mathf.Min(calY.min, y); calY.max = Mathf.Max(calY.max, y);
            calTw.min = Mathf.Min(calTw.min, tw); calTw.max = Mathf.Max(calTw.max, tw);
            if (calTimer >= CalMoveSeconds) { calPhase = 2; calTimer = 0F; cenSumX = cenSumY = cenSumTw = 0F; cenN = 0; }
        }
        else
        {
            if (calTimer > 0.7F) { cenSumX += x; cenSumY += y; cenSumTw += tw; cenN++; }
            if (calTimer >= CalCentreSeconds && cenN > 0)
            {
                calX.centre = cenSumX / cenN; calY.centre = cenSumY / cenN; calTw.centre = cenSumTw / cenN;
                SaveAxis("x", calX); SaveAxis("y", calY); SaveAxis("tw", calTw); PlayerPrefs.Save();
                haveCal = true; calPhase = 0;
                Debug.Log($"[FlightStick] Calibration saved for {model}. x {calX.min:0.00}/{calX.centre:0.00}/{calX.max:0.00}  y {calY.min:0.00}/{calY.centre:0.00}/{calY.max:0.00}  twist {calTw.min:0.00}/{calTw.centre:0.00}/{calTw.max:0.00}");
            }
        }
    }

    public string DebugText()
    {
        if (Find() == null) return "Flight stick: not connected (C = calibrate once connected)";
        var sb = new System.Text.StringBuilder();
        if (calPhase == 1) sb.Append($"CALIBRATING ({CalMoveSeconds - calTimer:0}s): push the stick to every edge (circle it), twist fully left AND right\n");
        else if (calPhase == 2) sb.Append("CALIBRATING: let go of the stick completely\n");
        else sb.Append($"{device.displayName} [{model}]  {(haveCal ? "calibrated" : "default calibration")}  (C = calibrate)\n");
        sb.Append($"output  x {fx:+0.00;-0.00}  y {fy:+0.00;-0.00}  twist {ftw:+0.00;-0.00}   trigger {(trigger != null && trigger.isPressed ? "HELD (Y = arm)" : "-")}\n");
        sb.Append("raw  ");
        foreach (var c in device.allControls)
            if (c is AxisControl ax && !(c is ButtonControl) && c.parent == device) sb.Append($"{c.name} {ax.ReadValue():+0.00;-0.00}   ");
        sb.Append($"stick {device.stick.x.ReadValue():+0.00;-0.00},{device.stick.y.ReadValue():+0.00;-0.00}   twist axis = {(twist != null ? twist.name : "NONE")}");
        sb.Append("\npressed:");
        foreach (var c in device.allControls)
            if (c is ButtonControl b && b.isPressed && c.parent == device) sb.Append(' ').Append(c.name);
        return sb.ToString();
    }

    // ---------------- IExcavatorInput ----------------
    public bool ModeTogglePressedThisFrame()
    {
        if (Find() == null) return false;
        var kb = StickControls.ExperimenterKeyboard;
        if (kb != null && kb.cKey.wasPressedThisFrame && calPhase == 0) StartCalibration();
        if (trigger == null) return false;
        if (trigger.wasPressedThisFrame) { triggerDownTime = Time.unscaledTime; armUsedDuringHold = false; }
        if (trigger.wasReleasedThisFrame)
        {
            bool tap = triggerDownTime >= 0F && Time.unscaledTime - triggerDownTime <= tapMaxSeconds && !armUsedDuringHold;
            triggerDownTime = -1F;
            return tap;
        }
        return false;
    }

    public void Read(bool travelMode, InputEvents e)
    {
        if (Find() == null) return;

        float rawX = device.stick.x.ReadValue();
        float rawY = device.stick.y.ReadValue();
        float rawTw = twist != null ? twist.ReadValue() : 0F;
        lastRawX = rawX; lastRawY = rawY; lastRawTw = rawTw;

        if (calPhase != 0) { UpdateCalibration(rawX, rawY, rawTw); fx = fy = ftw = 0F; StickOut = Vector2.zero; TwistOut = 0F; return; }

        float k = smoothingSeconds > 0F ? 1F - Mathf.Exp(-Time.deltaTime / smoothingSeconds) : 1F;
        fx += (Apply(rawX, calX) - fx) * k;
        fy += (Apply(rawY, calY) - fy) * k;
        ftw += (Apply(rawTw, calTw) - ftw) * k;

        float x = Dz(fx, stickDeadZone);
        float y = Dz(fy, stickDeadZone);
        StickOut = new Vector2(x, y);
        TwistOut = Mathf.Clamp(Dz(ftw, twistDeadZone) * twistScale, -1F, 1F);

        if (!travelMode)
        {
            float tw = TwistOut;

            bool armHeld = trigger != null && trigger.isPressed;
            if (armHeld)
            {
                if (y != 0) { e.arm = invertArm ? -y : y; armUsedDuringHold = true; boomLocked = true; }
                y = 0F;
            }
            else if (boomLocked)
            {
                // Released the trigger with Y still pushed: wait for the stick to centre before it drives the boom.
                if (y == 0F) boomLocked = false; else y = 0F;
            }

            if (x != 0) e.swing = invertSwing ? -x : x;
            if (y != 0) e.boom = invertBoom ? -y : y;
            if (tw != 0) e.bucket = invertBucket ? -tw : tw;
        }
        else
        {
            float drive = invertDrive ? -y : y;
            float steer = invertSteer ? -x : x;
            if (carStyleReverse && drive < 0F) steer = -steer;
            float left = drive + steer, right = drive - steer;
            float m = Mathf.Max(1F, Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)));
            left /= m; right /= m;
            if (left != 0) e.trackLeft = left;
            if (right != 0) e.trackRight = right;
        }
    }
}

public enum StickLayout { Auto, SingleStick, TwoSticks }

// F310 + keyboard and flight stick(s). The sticks are read last, so they win when moved.
// The study texts name only the joystick while one is connected (StickControls.Active); the F310 + keyboard
// keep working for testing at home, unless StickControls.BlockGamepadKeyboard is on (then joystick only).
// Layout Auto: two sticks connected -> TwoSticks (gamepad-style), otherwise SingleStick.
public class GamepadAndStickInput : IExcavatorInput
{
    public readonly GamepadKeyboardInput gamepad = new GamepadKeyboardInput();
    public readonly FlightStickInput stick = new FlightStickInput();
    public readonly DualStickInput dual = new DualStickInput();

    public StickLayout layout
    {
        get => StickControls.Layout;
        set => StickControls.Layout = value;
    }

    public bool UsingStick => StickControls.Active;
    public bool UsingTwoSticks => StickControls.TwoSticks;

    public bool IsCalibrating => UsingStick && (UsingTwoSticks ? dual.IsCalibrating : stick.IsCalibrating);
    bool GamepadOn => !StickControls.GamepadKeyboardBlocked;

    public string DebugText() =>
        !UsingStick ? "No joystick connected: F310 gamepad + keyboard in use"
        : (GamepadOn ? "JOYSTICK (gamepad + keyboard also active)   " : "JOYSTICK ONLY (gamepad + keyboard off)   ")
          + (UsingTwoSticks ? dual.DebugText() : stick.DebugText());

    public bool ModeTogglePressedThisFrame()
    {
        bool a = GamepadOn && gamepad.ModeTogglePressedThisFrame();
        if (!UsingStick) return a;
        bool b = UsingTwoSticks ? dual.ModeTogglePressedThisFrame() : stick.ModeTogglePressedThisFrame();
        return a || b;
    }

    public void Read(bool travelMode, InputEvents e)
    {
        if (GamepadOn) gamepad.Read(travelMode, e);
        if (!UsingStick) return;
        if (UsingTwoSticks) dual.Read(travelMode, e); else stick.Read(travelMode, e);
    }
}
