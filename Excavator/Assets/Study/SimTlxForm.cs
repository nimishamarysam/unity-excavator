using UnityEngine;
using UnityEngine.InputSystem;

// ---------------------------------------------------------------------------
// SIM-TLX FORM (instruction screen, after every measured task)
// The Simulation Task Load Index (Harris, Wilson & Vine, 2020): nine workload dimensions, each rated on a
// 21-step scale from "Very low" (0) to "Very high" (20). Filled in by the participant with the same controls
// they drive with - no mouse is needed (the instruction display cannot take mouse clicks):
//   Joystick: hat up/down = choose the question, stick or hat left/right = set the rating,
//             thumb button (confirm) = next question / submit.
//   Gamepad:  D-pad up/down, left stick or D-pad left/right, A.     Keyboard (testing): arrow keys.
// Every question must be rated (moved at least once) before the form can be submitted. Results go to the
// events log ("sim_tlx") and to P01_<date>_questionnaire.csv (raw 0-20 values, plus the mean).
// The experimenter can skip the form with Enter (logged as "sim_tlx_skipped").
// CHECK the item wording below against the version of the SIM-TLX your study cites before collecting data.
// Turn the on-screen form off with SimTlxForm.Enabled = false (then the screen asks for paper questionnaires).
// ---------------------------------------------------------------------------
public class SimTlxForm
{
    public static bool Enabled = false;   // off: SIM-TLX is collected separately, outside the simulator
    public const int Steps = 20;   // 0..20 = 21 positions

    public static readonly string[] ItemKeys =
        { "mental", "physical", "temporal", "frustration", "complexity", "stress", "distraction", "perceptual", "control" };
    static readonly string[] Titles =
        { "Mental demands", "Physical demands", "Temporal demands", "Frustration", "Task complexity",
          "Situational stress", "Distraction", "Perceptual strain", "Task control" };
    static readonly string[] Questions =
    {
        "How mentally fatiguing was the task?",
        "How physically fatiguing was the task?",
        "How hurried or rushed did you feel during the task?",
        "How insecure, discouraged, irritated, stressed or annoyed were you?",
        "How complex was the task?",
        "How stressed did you feel while performing the task?",
        "How distracting was the task environment?",
        "How uncomfortable or irritating were the visual and auditory aspects of the task?",
        "How difficult was the task to control and operate?",
    };

    public int[] Values { get; } = new int[ItemKeys.Length];
    readonly bool[] rated = new bool[ItemKeys.Length];
    int selected;                 // 0..8 = question, 9 = Submit
    bool armed;                   // inputs must be released once after the form appears (stick still deflected from driving)
    float armTime, nextRepeat;
    Vector2Int heldDir;
    float startTime;

    bool AllRated { get { foreach (bool r in rated) if (!r) return false; return true; } }

    public void Begin()
    {
        for (int i = 0; i < Values.Length; i++) { Values[i] = Steps / 2; rated[i] = false; }
        selected = 0; armed = false; heldDir = Vector2Int.zero;
        armTime = Time.unscaledTime + 0.8F;   // short lock-out right after the task ended
        startTime = Time.unscaledTime;
    }

    // Call every frame. Returns true when the participant submits the completed form.
    public bool Tick()
    {
        Vector2Int dir = ReadDirection();
        bool confirm = StickControls.ConfirmPressed();
        var kb = StickControls.ExperimenterKeyboard;
        if (kb != null && kb.spaceKey.wasPressedThisFrame) confirm = true;

        if (!armed)
        {
            if (Time.unscaledTime >= armTime && dir == Vector2Int.zero) armed = true;
            return false;
        }

        // Direction with auto-repeat (Time.timeScale is 0 on this screen, so unscaled time is used).
        float now = Time.unscaledTime;
        bool step = false;
        if (dir != heldDir) { heldDir = dir; step = dir != Vector2Int.zero; nextRepeat = now + 0.35F; }
        else if (dir != Vector2Int.zero && now >= nextRepeat) { step = true; nextRepeat = now + 0.08F; }
        if (step)
        {
            if (dir.y != 0) selected = Mathf.Clamp(selected - dir.y, 0, ItemKeys.Length);
            else if (dir.x != 0 && selected < ItemKeys.Length)
            {
                if (!rated[selected]) rated[selected] = true;          // first move rates from the middle
                Values[selected] = Mathf.Clamp(Values[selected] + dir.x, 0, Steps);
            }
        }

        if (confirm)
        {
            if (selected < ItemKeys.Length)
            {
                // Next question (or the first one still unrated / Submit when all are rated).
                selected = NextTarget(selected + 1);
            }
            else if (AllRated) return true;
            else selected = NextTarget(0);
        }
        return false;
    }

    int NextTarget(int from)
    {
        for (int i = from; i < ItemKeys.Length; i++) if (!rated[i]) return i;
        for (int i = 0; i < ItemKeys.Length; i++) if (!rated[i]) return i;
        return ItemKeys.Length;   // Submit
    }

    public string Detail()
    {
        var sb = new System.Text.StringBuilder();
        float mean = 0F;
        for (int i = 0; i < ItemKeys.Length; i++) { sb.Append(ItemKeys[i]).Append('=').Append(Values[i]).Append(';'); mean += Values[i]; }
        sb.Append($"mean={mean / ItemKeys.Length:F2};seconds={Time.unscaledTime - startTime:F0}");
        return sb.ToString();
    }

    static Vector2Int ReadDirection()
    {
        var d = StickControls.HatHeld();
        float sx = StickControls.StickX();
        if (d.x == 0 && Mathf.Abs(sx) > 0.6F) d.x = sx > 0F ? 1 : -1;
        var pad = StickControls.Pad;
        if (pad != null)
        {
            Vector2 v = pad.dpad.ReadValue();
            Vector2 ls = pad.leftStick.ReadValue();
            if (d.x == 0) d.x = v.x > 0.5F || ls.x > 0.6F ? 1 : v.x < -0.5F || ls.x < -0.6F ? -1 : 0;
            if (d.y == 0) d.y = v.y > 0.5F || ls.y > 0.7F ? 1 : v.y < -0.5F || ls.y < -0.7F ? -1 : 0;
        }
        var kb = StickControls.ExperimenterKeyboard;
        if (kb != null)
        {
            if (d.x == 0) d.x = kb.rightArrowKey.isPressed ? 1 : kb.leftArrowKey.isPressed ? -1 : 0;
            if (d.y == 0) d.y = kb.upArrowKey.isPressed ? 1 : kb.downArrowKey.isPressed ? -1 : 0;
        }
        if (d.x != 0 && d.y != 0) d.x = 0;   // one direction at a time
        return d;
    }

    // ---------- drawing (inside OnGUI, in the instruction display's pixel space; k = size factor) ----------
    GUIStyle title, text, small, bold, center;

    void Styles(float k)
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            text = new GUIStyle(GUI.skin.label) { wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { wordWrap = true };
            bold = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            center = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
        title.fontSize = Mathf.RoundToInt(24F * k);
        text.fontSize = Mathf.RoundToInt(15F * k);
        small.fontSize = Mathf.RoundToInt(12F * k);
        bold.fontSize = Mathf.RoundToInt(15F * k);
        center.fontSize = Mathf.RoundToInt(17F * k);
    }

    public void Draw(Rect area, float k)
    {
        Styles(k);
        float y = area.y;
        GUI.Label(new Rect(area.x, y, area.width, 32F * k), "Task complete - how did this task feel?", title);
        y += 32F * k;
        string controls = StickControls.Active
            ? $"Hat up / down: choose a question.   Stick or hat left / right: set your rating.   {StickControls.ConfirmShort.ToLower()} button: next."
            : "D-pad up / down: choose a question.   Left stick or D-pad left / right: set your rating.   A: next.";
        GUI.Label(new Rect(area.x, y, area.width, 22F * k), controls, small);
        y += 24F * k;

        float bottomH = 44F * k;
        float rowH = Mathf.Min(58F * k, (area.yMax - y - bottomH) / ItemKeys.Length);
        float labelW = area.width * 0.44F, scaleX = area.x + labelW + 10F * k, scaleW = area.width - labelW - 10F * k;

        for (int i = 0; i < ItemKeys.Length; i++)
        {
            var row = new Rect(area.x - 6F * k, y, area.width + 12F * k, rowH - 3F * k);
            if (i == selected) Fill(row, new Color(1F, 0.85F, 0.2F, 0.28F));
            else if (i % 2 == 0) Fill(row, new Color(1F, 1F, 1F, 0.05F));

            GUI.Label(new Rect(area.x, y + 1F * k, labelW, 20F * k), $"{i + 1}. {Titles[i]}" + (rated[i] ? "" : "   (not rated)"), bold);
            GUI.Label(new Rect(area.x, y + 19F * k, labelW, rowH - 20F * k), Questions[i], small);
            DrawScale(new Rect(scaleX, y + 4F * k, scaleW, rowH - 10F * k), Values[i], rated[i], i == selected, k);
            y += rowH;
        }

        // Submit row
        var sub = new Rect(area.x + area.width / 2F - 160F * k, area.yMax - bottomH + 6F * k, 320F * k, bottomH - 10F * k);
        bool ready = AllRated;
        Fill(sub, selected == ItemKeys.Length ? (ready ? new Color(0.2F, 0.65F, 0.3F, 1F) : new Color(0.6F, 0.6F, 0.6F, 1F))
                                              : new Color(0.35F, 0.35F, 0.35F, 0.8F));
        var old = GUI.color;
        GUI.color = Color.white;
        string label = ready ? (selected == ItemKeys.Length ? $"Submit - press {(StickControls.Active ? StickControls.ConfirmShort.ToLower() : "A")}" : "Submit (at the bottom)")
                             : $"Rate all questions first ({Count()} of {ItemKeys.Length})";
        GUI.Label(sub, label, center);
        GUI.color = old;
    }

    int Count() { int n = 0; foreach (bool r in rated) if (r) n++; return n; }

    void DrawScale(Rect r, int value, bool isRated, bool active, float k)
    {
        float lineY = r.y + r.height * 0.42F;
        Fill(new Rect(r.x, lineY - 1.5F * k, r.width, 3F * k), new Color(0.88F, 0.88F, 0.88F, 0.9F));
        for (int s = 0; s <= Steps; s++)
        {
            float x = r.x + r.width * s / Steps;
            float th = s % 10 == 0 ? 14F : s % 5 == 0 ? 10F : 6F;
            Fill(new Rect(x - 1F * k, lineY - th * k / 2F, 2F * k, th * k), new Color(0.88F, 0.88F, 0.88F, 0.9F));
        }
        GUI.Label(new Rect(r.x - 2F * k, lineY + 6F * k, 100F * k, 16F * k), "Very low", small);
        var right = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
        GUI.Label(new Rect(r.xMax - 100F * k, lineY + 6F * k, 102F * k, 16F * k), "Very high", right);

        if (isRated || active)
        {
            float x = r.x + r.width * value / Steps;
            float sz = 14F * k;
            Color c = isRated ? new Color(0.1F, 0.45F, 0.9F, 1F) : new Color(0.5F, 0.5F, 0.5F, 0.6F);
            Fill(new Rect(x - sz / 2F, lineY - sz / 2F, sz, sz), c);
        }
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }
}
