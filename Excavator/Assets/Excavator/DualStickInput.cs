using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// ---------------------------------------------------------------------------
// Two flight sticks, one in each hand: the same layout as the F310 gamepad
// (and as the ISO control pattern of a real excavator's two levers).
//
//   Operation (dig) mode                    Travel (drive) mode
//   ------------------------------------    ---------------------------------
//   LEFT  stick X (left/right)  = swing     LEFT  stick Y = left track
//   LEFT  stick Y (fwd/back)    = arm       RIGHT stick Y = right track
//   RIGHT stick X (left/right)  = bucket
//   RIGHT stick Y (fwd/back)    = boom
//
//   Trigger on EITHER stick (press + release) toggles travel / operation mode,
//   like the F310 B button. Twist and throttle are not used.
//   The F310 and the keyboard still work alongside (home testing) unless StickControls.BlockGamepadKeyboard is on.
//
// WHICH STICKS: with FlightStickInput.OnlyA320 (default) only Thrustmaster TCA Sidestick Airbus (A320)
// sticks are used as LEFT and RIGHT, however many other joysticks, wheels, pedals or throttles are connected.
// Every joystick is listed once in the Console as USED or IGNORED.
//
// WHICH IS LEFT (experimenter keys, overlay shows the result):
//   Hold the TRIGGER of the stick that is on the left + press S  -> that stick becomes LEFT (remembered).
//   S alone                                                      -> swap left and right (remembered).
//   If the sticks report a USB serial number the choice follows the physical stick; otherwise it follows
//   the order in which Windows lists them (keep both sticks in the same USB ports).
//
// CALIBRATION - separate for the LEFT and the RIGHT stick, each stored for that stick:
//   Hold the TRIGGER of one stick + press C -> calibrate only that stick.
//   C alone                                 -> calibrate both sticks.
//   Then: move the stick(s) to every edge for 6 s, let go for 2 s. The TCA is spring-centred and reports
//   its full range, so it normally works without calibrating.
// ---------------------------------------------------------------------------
public class DualStickInput : IExcavatorInput
{
    public float deadZone = 0.08F;
    public float smoothingSeconds = 0.05F;
    public bool invertSwing, invertArm, invertBucket, invertBoom, invertTracks;

    // Last processed values (after calibration + dead zone), for the practice-guide diagrams.
    public Vector2 LeftOut { get; private set; }
    public Vector2 RightOut { get; private set; }

    struct AxisCal { public float min, centre, max; }
    static AxisCal Def(float mn, float c, float mx) => new AxisCal { min = mn, centre = c, max = mx };

    class Side
    {
        public string name;                 // "left" / "right"
        public Joystick dev;
        public ButtonControl trigger;
        public string model;
        public AxisCal x, y;
        public bool haveCal;
        public float fx, fy, lastRawX, lastRawY;
        // calibration in progress for this side
        public bool cal;
        public float cX, cY;
    }

    readonly Side L = new Side { name = "left" };
    readonly Side R = new Side { name = "right" };
    bool swapped;
    string leftSerial = "";
    bool loadedSwap;
    bool ignoreTriggerRelease;               // trigger was used for S / C: its release must not switch the mode
    bool useSerials;                         // both sticks report different serial numbers -> identify them by serial

    int calPhase; float calTimer; int cenN;
    const float CalMoveSeconds = 6F, CalCentreSeconds = 2F;
    public bool IsCalibrating => calPhase != 0;

    // The usable sticks (A320 only by default), in a stable order: by serial number if they have one, else by device id.
    public static List<Joystick> Sticks()
    {
        var list = new List<Joystick>();
        foreach (var j in Joystick.all)
        {
            FlightStickInput.Report(j);
            if (FlightStickInput.IsUsable(j)) list.Add(j);
        }
        list.Sort((a, b) =>
        {
            string sa = a.description.serial ?? "", sb = b.description.serial ?? "";
            if (sa != "" && sb != "" && sa != sb) return string.CompareOrdinal(sa, sb);
            return a.deviceId.CompareTo(b.deviceId);
        });
        return list;
    }
    public static int StickCount() => Sticks().Count;

    static string ModelOf(Joystick j) =>
        FlightStickInput.IsTCA(j) ? "TCA" : FlightStickInput.IsWingMan(j) ? "WingMan" : (j.description.product ?? "joystick");

    static string Serial(Joystick j) => j != null ? (j.description.serial ?? "") : "";

    bool Find()
    {
        if (!loadedSwap)
        {
            swapped = PlayerPrefs.GetInt("dualstick_swapped", 0) == 1;
            leftSerial = PlayerPrefs.GetString("dualstick_left_serial", "");
            loadedSwap = true;
        }
        var s = Sticks();
        if (s.Count < 2) { L.dev = R.dev = null; return false; }
        bool serials = Serial(s[0]) != "" && Serial(s[1]) != "" && Serial(s[0]) != Serial(s[1]);
        if (serials != useSerials) { useSerials = serials; L.dev = R.dev = null; }   // calibration keys change

        Joystick left, right;
        int bySerial = useSerials && leftSerial != "" ? s.FindIndex(j => Serial(j) == leftSerial) : -1;
        if (bySerial >= 0 && bySerial < 2)
        {   // the remembered physical stick is LEFT
            left = s[bySerial];
            right = s[1 - bySerial];
        }
        else
        {
            left = swapped ? s[1] : s[0];
            right = swapped ? s[0] : s[1];
        }
        if (L.dev != left) Assign(L, left);
        if (R.dev != right) Assign(R, right);
        return true;
    }

    void Assign(Side side, Joystick j)
    {
        side.dev = j;
        side.trigger = j.trigger ?? j.TryGetChildControl<ButtonControl>("trigger");
        side.model = ModelOf(j);
        side.fx = side.fy = 0F;
        LoadCal(side);
        Debug.Log($"[DualStick] {side.name.ToUpper()} stick = '{j.displayName}' (model {side.model}, serial '{Serial(j)}', calibrated={side.haveCal})");
    }

    // Make 'j' the LEFT stick (and remember it).
    void SetLeft(Joystick j)
    {
        if (j == null) return;
        var s = Sticks();
        int i = s.IndexOf(j);
        if (i < 0 || i > 1) return;
        bool newSwapped = i == 1;
        if (newSwapped != swapped && !useSerials) SwapSideCalibration();   // calibration follows the physical stick
        swapped = newSwapped;
        leftSerial = useSerials ? Serial(j) : "";
        PlayerPrefs.SetInt("dualstick_swapped", swapped ? 1 : 0);
        PlayerPrefs.SetString("dualstick_left_serial", leftSerial);
        PlayerPrefs.Save();
        L.dev = R.dev = null;   // re-assign (loads each stick's own calibration)
        Find();
        StudyLogger.Log("dualstick_left", $"{j.displayName};serial={leftSerial}");
    }

    // ---------------- calibration (stored per stick) ----------------
    // Key: the stick's serial number if it has one (follows the physical stick), else the side (left / right).
    string Key(Side s, string axis, string part)
    {
        string id = useSerials ? "sn_" + Serial(s.dev) : s.name + "_" + s.model;
        return $"dualstick_{id}_{axis}_{part}";
    }

    void LoadCal(Side s)
    {
        s.haveCal = PlayerPrefs.HasKey(Key(s, "x", "min"));
        if (s.haveCal)
        {
            s.x = Def(PlayerPrefs.GetFloat(Key(s, "x", "min")), PlayerPrefs.GetFloat(Key(s, "x", "c")), PlayerPrefs.GetFloat(Key(s, "x", "max")));
            s.y = Def(PlayerPrefs.GetFloat(Key(s, "y", "min")), PlayerPrefs.GetFloat(Key(s, "y", "c")), PlayerPrefs.GetFloat(Key(s, "y", "max")));
        }
        else if (s.model == "WingMan") { s.x = Def(-0.33F, -0.03F, 0.27F); s.y = Def(-0.535F, 0F, 0.398F); }
        else { s.x = Def(-1F, 0F, 1F); s.y = Def(-1F, 0F, 1F); }
    }

    void SaveCal(Side s)
    {
        PlayerPrefs.SetFloat(Key(s, "x", "min"), s.x.min); PlayerPrefs.SetFloat(Key(s, "x", "c"), s.x.centre); PlayerPrefs.SetFloat(Key(s, "x", "max"), s.x.max);
        PlayerPrefs.SetFloat(Key(s, "y", "min"), s.y.min); PlayerPrefs.SetFloat(Key(s, "y", "c"), s.y.centre); PlayerPrefs.SetFloat(Key(s, "y", "max"), s.y.max);
        s.haveCal = true;
    }

    void ClearCal(Side s)
    {
        foreach (var a in new[] { "x", "y" })
            foreach (var p in new[] { "min", "c", "max" }) PlayerPrefs.DeleteKey(Key(s, a, p));
    }

    // No serial numbers: calibration is stored per side, so swapping sides must swap the stored values too.
    void SwapSideCalibration()
    {
        if (L.dev == null || R.dev == null) return;
        bool hl = L.haveCal, hr = R.haveCal;
        AxisCal lx = L.x, ly = L.y, rx = R.x, ry = R.y;
        ClearCal(L); ClearCal(R);
        if (hr) { L.x = rx; L.y = ry; SaveCal(L); }
        if (hl) { R.x = lx; R.y = ly; SaveCal(R); }
        PlayerPrefs.Save();
    }

    static float Apply(float v, AxisCal a)
    {
        if (v >= a.centre) { float r = a.max - a.centre; return r > 0.02F ? Mathf.Clamp01((v - a.centre) / r) : 0F; }
        float l = a.centre - a.min; return l > 0.02F ? -Mathf.Clamp01((a.centre - v) / l) : 0F;
    }

    float Dz(float v)
    {
        float a = Mathf.Abs(v);
        if (a < deadZone) return 0F;
        return Mathf.Sign(v) * Mathf.Clamp01((a - deadZone) / (1F - deadZone));
    }

    void StartCalibration(bool left, bool right)
    {
        calPhase = 1; calTimer = 0F; cenN = 0;
        L.cal = left; R.cal = right;
        foreach (var s in new[] { L, R })
        {
            if (!s.cal) continue;
            s.x = Def(s.lastRawX, s.lastRawX, s.lastRawX); s.y = Def(s.lastRawY, s.lastRawY, s.lastRawY);
            s.cX = s.cY = 0F;
        }
        string which = left && right ? "both" : left ? "LEFT" : "RIGHT";
        Debug.Log($"[DualStick] Calibration started: {which}");
        StudyLogger.Log("dualstick_calibration_start", which);
    }

    void UpdateCalibration()
    {
        calTimer += Time.deltaTime; // FixedUpdate
        if (calPhase == 1)
        {
            foreach (var s in new[] { L, R })
            {
                if (!s.cal) continue;
                s.x.min = Mathf.Min(s.x.min, s.lastRawX); s.x.max = Mathf.Max(s.x.max, s.lastRawX);
                s.y.min = Mathf.Min(s.y.min, s.lastRawY); s.y.max = Mathf.Max(s.y.max, s.lastRawY);
            }
            if (calTimer >= CalMoveSeconds) { calPhase = 2; calTimer = 0F; cenN = 0; L.cX = L.cY = R.cX = R.cY = 0F; }
        }
        else
        {
            if (calTimer > 0.7F) { foreach (var s in new[] { L, R }) { s.cX += s.lastRawX; s.cY += s.lastRawY; } cenN++; }
            if (calTimer >= CalCentreSeconds && cenN > 0)
            {
                foreach (var s in new[] { L, R })
                {
                    if (!s.cal) continue;
                    s.x.centre = s.cX / cenN; s.y.centre = s.cY / cenN;
                    SaveCal(s);
                    Debug.Log($"[DualStick] Calibration saved for {s.name.ToUpper()} ({s.dev.displayName}): " +
                              $"x {s.x.min:0.00}/{s.x.centre:0.00}/{s.x.max:0.00}  y {s.y.min:0.00}/{s.y.centre:0.00}/{s.y.max:0.00}");
                    StudyLogger.Log("dualstick_calibration_saved", $"{s.name};x={s.x.min:0.00}/{s.x.centre:0.00}/{s.x.max:0.00};y={s.y.min:0.00}/{s.y.centre:0.00}/{s.y.max:0.00}");
                    s.cal = false;
                }
                PlayerPrefs.Save();
                calPhase = 0;
            }
        }
    }

    // Stick whose trigger is held (null = none or both).
    Joystick TriggerHeldStick()
    {
        bool l = L.trigger != null && L.trigger.isPressed, r = R.trigger != null && R.trigger.isPressed;
        return l && !r ? L.dev : r && !l ? R.dev : null;
    }

    public bool ModeTogglePressedThisFrame()
    {
        if (!Find()) return false;
        var kb = StickControls.ExperimenterKeyboard;
        if (kb != null && calPhase == 0)
        {
            if (kb.cKey.wasPressedThisFrame)
            {
                var held = TriggerHeldStick();
                if (held == L.dev && held != null) StartCalibration(true, false);
                else if (held == R.dev && held != null) StartCalibration(false, true);
                else StartCalibration(true, true);
                if (held != null) ignoreTriggerRelease = true;
            }
            if (kb.sKey.wasPressedThisFrame)
            {
                var held = TriggerHeldStick();
                if (held != null) { SetLeft(held); ignoreTriggerRelease = true; }
                else SetLeft(R.dev);   // swap: the current right stick becomes left
            }
        }
        bool lUp = L.trigger != null && L.trigger.wasReleasedThisFrame;
        bool rUp = R.trigger != null && R.trigger.wasReleasedThisFrame;
        if (ignoreTriggerRelease)
        {
            bool anyHeld = (L.trigger != null && L.trigger.isPressed) || (R.trigger != null && R.trigger.isPressed);
            if (!anyHeld) ignoreTriggerRelease = false;
            return false;
        }
        if (calPhase != 0) return false;
        return lUp || rUp;
    }

    void ReadSide(Side s, float k)
    {
        s.lastRawX = s.dev.stick.x.ReadValue();
        s.lastRawY = s.dev.stick.y.ReadValue();
        s.fx += (Apply(s.lastRawX, s.x) - s.fx) * k;
        s.fy += (Apply(s.lastRawY, s.y) - s.fy) * k;
    }

    public void Read(bool travelMode, InputEvents e)
    {
        if (!Find()) return;
        float k = smoothingSeconds > 0F ? 1F - Mathf.Exp(-Time.deltaTime / smoothingSeconds) : 1F;
        ReadSide(L, k); ReadSide(R, k);
        // While calibrating, NEITHER stick moves the machine (also the one that is not being calibrated).
        if (calPhase != 0) { UpdateCalibration(); L.fx = L.fy = R.fx = R.fy = 0F; LeftOut = RightOut = Vector2.zero; return; }

        float lx = Dz(L.fx), ly = Dz(L.fy), rx = Dz(R.fx), ry = Dz(R.fy);
        LeftOut = new Vector2(lx, ly); RightOut = new Vector2(rx, ry);

        if (!travelMode)
        {
            if (lx != 0) e.swing = invertSwing ? -lx : lx;
            if (ly != 0) e.arm = invertArm ? -ly : ly;
            if (rx != 0) e.bucket = invertBucket ? -rx : rx;
            if (ry != 0) e.boom = invertBoom ? -ry : ry;
        }
        else
        {
            if (ly != 0) e.trackLeft = invertTracks ? -ly : ly;
            if (ry != 0) e.trackRight = invertTracks ? -ry : ry;
        }
    }

    public string DebugText()
    {
        if (!Find())
            return FlightStickInput.OnlyA320 ? "Two-stick layout: need 2 A320 sidesticks (other joysticks are ignored)"
                                             : "Two-stick layout: need 2 joysticks connected";
        string which = L.cal && R.cal ? "BOTH sticks" : L.cal ? "the LEFT stick" : "the RIGHT stick";
        string head = calPhase == 1 ? $"CALIBRATING {which.ToUpper()} ({CalMoveSeconds - calTimer:0}s): move it to every edge (circle it)\n"
                    : calPhase == 2 ? $"CALIBRATING {which.ToUpper()}: let go completely\n"
                    : "Two A320 sticks   trigger+S = this stick is LEFT, S = swap   trigger+C = calibrate this stick, C = both   trigger = drive/dig\n";
        string Line(Side s) =>
            $"{s.name.ToUpper()}: {s.dev.displayName}{(Serial(s.dev) != "" ? " #" + Serial(s.dev) : "")} [{(s.haveCal ? "calibrated" : "default calibration")}]" +
            $"  x {s.fx:+0.00;-0.00}  y {s.fy:+0.00;-0.00}{(s.trigger != null && s.trigger.isPressed ? "  TRIGGER" : "")}\n";
        return head + Line(L) + Line(R) + "Left: X swing, Y arm   Right: X bucket, Y boom   Drive: left Y / right Y = tracks";
    }
}
