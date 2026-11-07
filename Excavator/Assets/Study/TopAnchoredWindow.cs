using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

// ---------------------------------------------------------------------------
// DISPLAY 1 WINDOW: borderless, WindowWidth x WindowHeight (default 1920 x 720), placed on the monitor
// according to Anchor (TopLeft, TopCenter, Center, or Custom = CustomX / CustomY pixels from the
// monitor's top-left corner).
//
// Because the window itself is WindowWidth x WindowHeight, Screen.width / Screen.height match it and the
// whole Display 1 layout (driver view, measurement bar, task panel, practice instructions) fits inside it.
// The rest of the monitor is not used (dark desktop background).
//
// LAB PROJECTOR (curved screen, 2026-11-07): the participant view must stay on the FLAT middle part of the
// projection. Its size is not known in advance, so the window can be positioned and sized at run time:
//
//   WINDOW CALIBRATION (build only) - press F8 (CalibrationKey), best on the setup screen:
//     Arrow keys            move the window        (10 px; Shift = 50 px; Alt = 1 px)
//     Ctrl + Arrow keys     resize it (the top-left corner stays; Right/Down = bigger, Left/Up = smaller)
//     G                     show / hide a 10 % grid to judge where the flat area ends
//     Backspace             back to the default values from this file
//     F8 again              SAVE and close. The values are stored on this computer (PlayerPrefs) and used
//                           at every start, so it only has to be done once per lab setup.
//   While calibrating, a yellow frame marks the window edges and the values are shown in the middle.
//
//   Precedence at start: command line  -window-rect X Y WIDTH HEIGHT  >  saved calibration  >  values below.
//   Turn off with TopAnchoredWindow.Enabled = false (then Player Settings decide, e.g. Fullscreen Window).
//
// Only in a Windows BUILD (the Editor keeps its Game view - set its size by hand).
// Added automatically by StudyLayout.
// ---------------------------------------------------------------------------
public class TopAnchoredWindow : MonoBehaviour
{
    public static bool Enabled = true;
    public static int WindowWidth = 1920;
    public static int WindowHeight = 720;

    public enum WindowAnchor { TopLeft, TopCenter, Center, Custom }
    // Where the window sits on the monitor (Custom = CustomX / CustomY).
    public static WindowAnchor Anchor = WindowAnchor.TopCenter;
    public static int CustomX = 0;     // pixels from the monitor's left edge (Anchor = Custom)
    public static int CustomY = 0;     // pixels from the monitor's top edge  (Anchor = Custom)

    // Use the values saved with the F8 calibration (false = always the values above / command line).
    public static bool UseSavedCalibration = true;
    public static Key CalibrationKey = Key.F8;
    public static bool Calibrating { get; private set; }

    const string PrefX = "Window.X", PrefY = "Window.Y", PrefW = "Window.W", PrefH = "Window.H";
    const int MinWidth = 640, MinHeight = 360;

    // Code defaults (for Backspace in the calibration).
    static int defW, defH, defX, defY;
    static WindowAnchor defAnchor;
    static bool defaultsKept;

    int monX, monY, monW = 1920, monH = 1080;   // monitor of the window (desktop pixels)
    bool showGrid;
    Coroutine applying;

    // Top-left corner of the window for a monitor with the given position and size.
    Vector2Int Corner(int x0, int y0, int w, int h)
    {
        int cx = x0 + (w - WindowWidth) / 2, cy = y0 + (h - WindowHeight) / 2;
        switch (Anchor)
        {
            case WindowAnchor.TopLeft: return new Vector2Int(x0, y0);
            case WindowAnchor.TopCenter: return new Vector2Int(cx, y0);
            case WindowAnchor.Custom: return new Vector2Int(x0 + CustomX, y0 + CustomY);
            default: return new Vector2Int(cx, cy);
        }
    }

    void Start()
    {
        if (!Enabled || Application.isEditor) return;
        if (!defaultsKept) { defW = WindowWidth; defH = WindowHeight; defX = CustomX; defY = CustomY; defAnchor = Anchor; defaultsKept = true; }
        string source = "code";
        if (UseSavedCalibration && PlayerPrefs.HasKey(PrefW))
        {
            Anchor = WindowAnchor.Custom;
            CustomX = PlayerPrefs.GetInt(PrefX); CustomY = PlayerPrefs.GetInt(PrefY);
            WindowWidth = PlayerPrefs.GetInt(PrefW); WindowHeight = PlayerPrefs.GetInt(PrefH);
            source = "saved calibration";
        }
        if (ReadArgs()) source = "command line";
        Debug.Log($"[TopAnchoredWindow] Window from {source}: {Describe()}");
        Apply();
    }

    // -window-rect X Y WIDTH HEIGHT (pixels, X/Y from the monitor's top-left corner).
    static bool ReadArgs()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 4; i++)
        {
            if (!string.Equals(args[i], "-window-rect", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(args[i + 1], out int x) && int.TryParse(args[i + 2], out int y)
                && int.TryParse(args[i + 3], out int w) && int.TryParse(args[i + 4], out int h))
            {
                Anchor = WindowAnchor.Custom; CustomX = x; CustomY = y;
                WindowWidth = Mathf.Max(MinWidth, w); WindowHeight = Mathf.Max(MinHeight, h);
                return true;
            }
            Debug.LogWarning("[TopAnchoredWindow] -window-rect needs four whole numbers: X Y WIDTH HEIGHT");
        }
        return false;
    }

    static string Describe() =>
        Anchor == WindowAnchor.Custom ? $"{WindowWidth} x {WindowHeight} at ({CustomX}, {CustomY})" : $"{WindowWidth} x {WindowHeight}, anchor {Anchor}";

    void Apply()
    {
        if (applying != null) StopCoroutine(applying);
        applying = StartCoroutine(ApplyRoutine());
    }

    IEnumerator ApplyRoutine()
    {
        Screen.SetResolution(WindowWidth, WindowHeight, FullScreenMode.Windowed);
        yield return null;                       // let Unity resize the window first
        yield return null;
        Place();
        yield return new WaitForSecondsRealtime(0.5F);
        Place();                                 // once more, in case Unity re-applied its own style
        applying = null;
    }

    // ======================= calibration =======================

    void Update()
    {
        if (!Enabled || Application.isEditor) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[CalibrationKey].wasPressedThisFrame)
        {
            if (Calibrating) { Save(); Calibrating = false; }
            else
            {
                // Switch to Custom at the current position, so moving starts from where the window is now.
                var c = Corner(monX, monY, monW, monH);
                Anchor = WindowAnchor.Custom; CustomX = c.x - monX; CustomY = c.y - monY;
                Calibrating = true;
                Debug.Log("[TopAnchoredWindow] Calibration started");
            }
            return;
        }
        if (!Calibrating) return;

        if (kb.gKey.wasPressedThisFrame) showGrid = !showGrid;
        if (kb.backspaceKey.wasPressedThisFrame)
        {
            WindowWidth = defW; WindowHeight = defH; Anchor = defAnchor; CustomX = defX; CustomY = defY;
            PlayerPrefs.DeleteKey(PrefX); PlayerPrefs.DeleteKey(PrefY); PlayerPrefs.DeleteKey(PrefW); PlayerPrefs.DeleteKey(PrefH);
            var c = Corner(monX, monY, monW, monH);
            Anchor = WindowAnchor.Custom; CustomX = c.x - monX; CustomY = c.y - monY;
            Apply();
            return;
        }

        int step = kb.shiftKey.isPressed ? 50 : kb.altKey.isPressed ? 1 : 10;
        int dx = 0, dy = 0;
        if (kb.leftArrowKey.wasPressedThisFrame) dx -= step;
        if (kb.rightArrowKey.wasPressedThisFrame) dx += step;
        if (kb.upArrowKey.wasPressedThisFrame) dy -= step;
        if (kb.downArrowKey.wasPressedThisFrame) dy += step;
        if (dx == 0 && dy == 0) return;

        if (kb.ctrlKey.isPressed)
        {
            WindowWidth = Mathf.Max(MinWidth, WindowWidth + dx);
            WindowHeight = Mathf.Max(MinHeight, WindowHeight + dy);
        }
        else { CustomX += dx; CustomY += dy; }
        Apply();
    }

    void Save()
    {
        PlayerPrefs.SetInt(PrefX, CustomX); PlayerPrefs.SetInt(PrefY, CustomY);
        PlayerPrefs.SetInt(PrefW, WindowWidth); PlayerPrefs.SetInt(PrefH, WindowHeight);
        PlayerPrefs.Save();
        Debug.Log($"[TopAnchoredWindow] Calibration saved: {Describe()}");
    }

    GUIStyle infoStyle;

    void OnGUI()
    {
        if (!Calibrating) return;
        GUI.depth = -1000;                       // on top of everything else on Display 1
        float w = Screen.width, h = Screen.height;
        Color frame = new Color(1F, 0.85F, 0.1F);

        if (showGrid)
            for (int i = 1; i < 10; i++)
            {
                Fill(new Rect(w * i / 10F - 1F, 0F, 2F, h), new Color(1F, 1F, 1F, i == 5 ? 0.7F : 0.35F));
                Fill(new Rect(0F, h * i / 10F - 1F, w, 2F), new Color(1F, 1F, 1F, i == 5 ? 0.7F : 0.35F));
            }

        const float b = 6F;                      // frame on the window edges
        Fill(new Rect(0, 0, w, b), frame); Fill(new Rect(0, h - b, w, b), frame);
        Fill(new Rect(0, 0, b, h), frame); Fill(new Rect(w - b, 0, b, h), frame);
        Fill(new Rect(w / 2F - 30F, h / 2F - 1.5F, 60F, 3F), frame);   // centre cross
        Fill(new Rect(w / 2F - 1.5F, h / 2F - 30F, 3F, 60F), frame);

        if (infoStyle == null) infoStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, wordWrap = true };
        infoStyle.normal.textColor = Color.white;
        var r = new Rect(w / 2F - 330F, h / 2F + 40F, 660F, 150F);
        Fill(r, new Color(0F, 0F, 0F, 0.8F));
        GUI.Label(r,
            "WINDOW CALIBRATION - fit the yellow frame to the flat part of the screen\n" +
            $"Position {CustomX}, {CustomY}   Size {WindowWidth} x {WindowHeight}   (monitor {monW} x {monH})\n" +
            "Arrows: move (Shift 50 px, Alt 1 px)   Ctrl + Arrows: size\n" +
            "G: grid   Backspace: defaults   F8: SAVE and close", infoStyle);
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    // ======================= window placement =======================

#if UNITY_STANDALONE_WIN
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    const int GWL_STYLE = -16;
    const long WS_POPUP = 0x80000000L, WS_VISIBLE = 0x10000000L;
    const uint SWP_SHOWWINDOW = 0x0040, SWP_FRAMECHANGED = 0x0020;
    const uint MONITOR_DEFAULTTOPRIMARY = 1;

    void Place()
    {
        IntPtr hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero) { Debug.LogWarning("[TopAnchoredWindow] Window handle not found (application not in focus?)"); return; }

        // Position on the monitor the window is on (Display 1), according to Anchor.
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        if (GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY), ref mi))
        {
            var m = mi.rcMonitor;
            monX = m.left; monY = m.top; monW = m.right - m.left; monH = m.bottom - m.top;
        }
        var c = Corner(monX, monY, monW, monH);

        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));   // no title bar, no border
        SetWindowPos(hwnd, IntPtr.Zero, c.x, c.y, WindowWidth, WindowHeight, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }
#else
    void Place()
    {
        // Other platforms: move the window (position relative to the display); the border cannot be removed from here.
        var d = Screen.mainWindowDisplayInfo;
        monX = 0; monY = 0; monW = d.width; monH = d.height;
        Screen.MoveMainWindowTo(d, Corner(0, 0, d.width, d.height));
    }
#endif
}
