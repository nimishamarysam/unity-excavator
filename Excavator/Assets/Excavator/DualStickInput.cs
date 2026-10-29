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
// Which stick is left: the overlay shows it. Press S on the keyboard to swap
// (remembered). C calibrates both sticks at once: move both to every edge for
// 6 s, then let go for 2 s.
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
    }

    readonly Side L = new Side { name = "left" };
    readonly Side R = new Side { name = "right" };
    bool swapped;
    bool loadedSwap;

    int calPhase; float calTimer; float cLX, cLY, cRX, cRY; int cenN;
    const float CalMoveSeconds = 6F, CalCentreSeconds = 2F;
    public bool IsCalibrating => calPhase != 0;

    public static List<Joystick> Sticks()
    {
        var list = new List<Joystick>();
        foreach (var j in Joystick.all) if (FlightStickInput.IsStick(j)) list.Add(j);
        list.Sort((a, b) => a.deviceId.CompareTo(b.deviceId)); // stable order between runs
        return list;
    }
    public static int StickCount() => Sticks().Count;

    static string ModelOf(Joystick j) =>
        FlightStickInput.IsTCA(j) ? "TCA" : FlightStickInput.IsWingMan(j) ? "WingMan" : (j.description.product ?? "joystick");

    bool Find()
    {
        if (!loadedSwap) { swapped = PlayerPrefs.GetInt("dualstick_swapped", 0) == 1; loadedSwap = true; }
        var s = Sticks();
        if (s.Count < 2) { L.dev = R.dev = null; return false; }
        var left = swapped ? s[1] : s[0];
        var right = swapped ? s[0] : s[1];
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
        Debug.Log($"[DualStick] {side.name} stick = '{j.displayName}' (model {side.model}, calibrated={side.haveCal})");
    }

    string Key(Side s, string axis, string part) => $"dualstick_{s.name}_{s.model}_{axis}_{part}";

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

    void StartCalibration()
    {
        calPhase = 1; calTimer = 0F;
        foreach (var s in new[] { L, R }) { s.x = Def(s.lastRawX, s.lastRawX, s.lastRawX); s.y = Def(s.lastRawY, s.lastRawY, s.lastRawY); }
        Debug.Log("[DualStick] Calibration started");
    }

    void UpdateCalibration()
    {
        calTimer += Time.deltaTime; // FixedUpdate
        if (calPhase == 1)
        {
            foreach (var s in new[] { L, R })
            {
                s.x.min = Mathf.Min(s.x.min, s.lastRawX); s.x.max = Mathf.Max(s.x.max, s.lastRawX);
                s.y.min = Mathf.Min(s.y.min, s.lastRawY); s.y.max = Mathf.Max(s.y.max, s.lastRawY);
            }
            if (calTimer >= CalMoveSeconds) { calPhase = 2; calTimer = 0F; cLX = cLY = cRX = cRY = 0F; cenN = 0; }
        }
        else
        {
            if (calTimer > 0.7F) { cLX += L.lastRawX; cLY += L.lastRawY; cRX += R.lastRawX; cRY += R.lastRawY; cenN++; }
            if (calTimer >= CalCentreSeconds && cenN > 0)
            {
                L.x.centre = cLX / cenN; L.y.centre = cLY / cenN; R.x.centre = cRX / cenN; R.y.centre = cRY / cenN;
                SaveCal(L); SaveCal(R); PlayerPrefs.Save();
                calPhase = 0;
                Debug.Log($"[DualStick] Calibration saved. left x {L.x.min:0.00}/{L.x.centre:0.00}/{L.x.max:0.00} y {L.y.min:0.00}/{L.y.centre:0.00}/{L.y.max:0.00} | right x {R.x.min:0.00}/{R.x.centre:0.00}/{R.x.max:0.00} y {R.y.min:0.00}/{R.y.centre:0.00}/{R.y.max:0.00}");
            }
        }
    }

    public bool ModeTogglePressedThisFrame()
    {
        if (!Find()) return false;
        var kb = StickControls.ExperimenterKeyboard;
        if (kb != null && calPhase == 0)
        {
            if (kb.cKey.wasPressedThisFrame) StartCalibration();
            if (kb.sKey.wasPressedThisFrame)
            {
                swapped = !swapped;
                PlayerPrefs.SetInt("dualstick_swapped", swapped ? 1 : 0); PlayerPrefs.Save();
                Find();
            }
        }
        bool l = L.trigger != null && L.trigger.wasReleasedThisFrame;
        bool r = R.trigger != null && R.trigger.wasReleasedThisFrame;
        return l || r;
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
        if (!Find()) return "Two-stick layout: need 2 joysticks connected";
        string head = calPhase == 1 ? $"CALIBRATING ({CalMoveSeconds - calTimer:0}s): move BOTH sticks to every edge (circle them)\n"
                    : calPhase == 2 ? "CALIBRATING: let go of both sticks completely\n"
                    : "Two sticks (gamepad layout)   S = swap left/right   C = calibrate   trigger = drive/dig\n";
        string Line(Side s) =>
            $"{s.name.ToUpper()}: {s.dev.displayName} [{s.model}{(s.haveCal ? ", calibrated" : "")}]  x {s.fx:+0.00;-0.00}  y {s.fy:+0.00;-0.00}" +
            $"{(s.trigger != null && s.trigger.isPressed ? "  TRIGGER" : "")}\n";
        return head + Line(L) + Line(R) + "Left: X swing, Y arm   Right: X bucket, Y boom   Drive: left Y / right Y = tracks";
    }
}
