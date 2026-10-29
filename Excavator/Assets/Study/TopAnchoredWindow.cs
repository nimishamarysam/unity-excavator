using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

// ---------------------------------------------------------------------------
// DISPLAY 1 WINDOW: borderless, WindowWidth x WindowHeight (default 1920 x 720), placed on the monitor
// according to Anchor (default CENTER of the monitor; also TopLeft, TopCenter).
//
// Because the window itself is 1920 x 720, Screen.width / Screen.height are 1920 / 720 and the whole
// Display 1 layout (driver view, measurement bar, task panel, practice instructions, briefing screens)
// fits inside it automatically. The part of the monitor below the window is not used by the application.
//
// Only in a Windows BUILD (the Editor keeps its Game view). Display 5 is not affected (secondary displays
// always run fullscreen; the context views are drawn in its top third, see StudyLayout).
// Turn off with TopAnchoredWindow.Enabled = false (then Player Settings decide, e.g. Fullscreen Window).
// Added automatically by StudyLayout.
// ---------------------------------------------------------------------------
public class TopAnchoredWindow : MonoBehaviour
{
    public static bool Enabled = true;
    public static int WindowWidth = 1920;
    public static int WindowHeight = 720;

    public enum WindowAnchor { TopLeft, TopCenter, Center }
    // Where the window sits on the monitor.
    public static WindowAnchor Anchor = WindowAnchor.TopCenter;

    // Top-left corner of the window for a monitor with the given position and size.
    static Vector2Int Corner(int monX, int monY, int monW, int monH)
    {
        int cx = monX + (monW - WindowWidth) / 2, cy = monY + (monH - WindowHeight) / 2;
        switch (Anchor)
        {
            case WindowAnchor.TopLeft: return new Vector2Int(monX, monY);
            case WindowAnchor.TopCenter: return new Vector2Int(cx, monY);
            default: return new Vector2Int(cx, cy);
        }
    }

    IEnumerator Start()
    {
        if (!Enabled || Application.isEditor) yield break;

        Screen.SetResolution(WindowWidth, WindowHeight, FullScreenMode.Windowed);
        yield return null;                       // let Unity resize the window first
        yield return null;
        Place();
        yield return new WaitForSecondsRealtime(0.5F);
        Place();                                 // once more, in case Unity re-applied its own style
        Debug.Log($"[TopAnchoredWindow] Display 1 window {Screen.width} x {Screen.height}, borderless, anchor {Anchor}");
    }

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
        int x = 0, y = 0;
        if (GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY), ref mi))
        {
            var r = mi.rcMonitor;
            var c = Corner(r.left, r.top, r.right - r.left, r.bottom - r.top);
            x = c.x; y = c.y;
        }

        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));   // no title bar, no border
        SetWindowPos(hwnd, IntPtr.Zero, x, y, WindowWidth, WindowHeight, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }
#else
    void Place()
    {
        // Other platforms: move the window (position relative to the display); the border cannot be removed from here.
        var d = Screen.mainWindowDisplayInfo;
        Screen.MoveMainWindowTo(d, Corner(0, 0, d.width, d.height));
    }
#endif
}
