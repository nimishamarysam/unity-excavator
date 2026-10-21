using UnityEngine;

// ---------------------------------------------------------------------------
// STUDY SCREEN LAYOUT (identical in all three conditions)
//
//   +----------------------------------+-------------+
//   |                                  |  camera 1   |
//   |        DRIVER VIEW (cab)         |-------------|
//   |                                  |  camera 2   |
//   |                                  |-------------|
//   |                                  |  camera 3   |
//   +----------------------------------+-------------+
//   |            measurement bar (full width)        |
//   +------------------------------------------------+
//
// The camera panel on the right is used only in C2/C3; in C1 it stays empty.
// DRIVING scenario: the panel has two windows instead of three:
//   top    = BIRD'S-EYE VIEW (always on)
//   bottom = OBSTACLE CAMERA (only while something is close, see PopupViews)
// Keeping the driver view the same size in every condition avoids giving one
// condition a bigger "windshield" than another.
// All values are fractions of the screen (0..1), designed for a 16:9 screen.
//
// SEPARATE CAMERA MONITOR (lab setup, ContextViewsOnSecondDisplay = true):
//   Display 1: DRIVER VIEW over the full width + measurement bar.
//   Display 5 (2560 x 1080): the camera windows SIDE BY SIDE in the TOP THIRD only
//   (2560 x 360 px); the rest of that monitor stays black:
//     +-----------+-----------+-----------+   <- top 360 px
//     | camera 1  | camera 2  | camera 3  |
//     +-----------+-----------+-----------+
//     |              (black)              |
//     +-----------------------------------+
//   Driving scenario: bird's-eye (left half) | obstacle camera (right half) of that strip.
// In a build the monitor is switched on automatically; if it is not connected the
// single-screen layout above is used. In the Unity Editor, open a second Game view
// (Window > General > Game), set it to "Display 5" with a 2560x1080 resolution.
// ---------------------------------------------------------------------------

public class StudyLayout : MonoBehaviour
{
    public const float BarHeight = 72F / 1080F;     // measurement bar (same as MeasurementBar.barHeight)
    public const float PanelWidth = 0.28F;           // right-hand camera panel
    public const float Gap = 0.006F;

    // EXPERIMENT/LAB SWITCH: camera windows on a second monitor instead of the right-hand panel.
    public static bool ContextViewsOnSecondDisplay = true;

    // True when the camera windows really go to the camera monitor (Editor: always, using a "Display 5" Game view).
    public static bool SeparateDisplay =>
        ContextViewsOnSecondDisplay && (Application.isEditor || Display.displays.Length > CameraMonitorIndex);

    // Which monitor: Unity counts from 0, so "Display 5" in Windows/Unity's Game view = index 4.
    public const int CameraMonitorIndex = 4;
    // Part of that monitor used for the camera strip: top 360 of 1080 px.
    public const float StripHeight = 360F / 1080F;

    // Unity display index for the camera windows and their frames/labels (0 = main monitor).
    public static int PanelDisplay => SeparateDisplay ? CameraMonitorIndex : 0;

    public static Rect DriverView => SeparateDisplay
        ? new Rect(0F, BarHeight, 1F, 1F - BarHeight)
        : new Rect(0F, BarHeight, 1F - PanelWidth, 1F - BarHeight);

    // Smallest rect covering two slots (e.g. middle + bottom; works for stacked and side-by-side).
    public static Rect PanelSpan(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // Camera slot 0 = top (or left on the second display), 1 = middle, 2 = bottom (right).
    public static Rect PanelSlot(int i) => PanelSlot(i, 3);

    // Panel split into 'count' equal windows, slot 0 at the top.
    public static Rect PanelSlot(int i, int count)
    {
        if (SeparateDisplay)
        {
            // Camera monitor: windows side by side in the top strip, slot 0 on the left.
            float sw = (1F - (count + 1) * Gap) / count;
            float gy = Gap * 3F * StripHeight;   // vertical gap in the same pixel size as the horizontal one (2560 vs 1080 px)
            return new Rect(Gap + i * (sw + Gap), 1F - StripHeight + gy, sw, StripHeight - 2F * gy);
        }
        float x = 1F - PanelWidth + Gap, w = PanelWidth - 2F * Gap;
        float h = (1F - BarHeight - (count + 1) * Gap) / count;
        float y = 1F - Gap - (i + 1) * h - i * Gap;
        return new Rect(x, y, w, h);
    }

    Camera background;

    void Awake()
    {
        // A camera that draws nothing but a dark background, behind everything,
        // so the panel area is a clean dark surface when no camera window is shown.
        var go = new GameObject("LayoutBackground");
        go.transform.SetParent(transform, false);
        background = go.AddComponent<Camera>();
        background.depth = -100;
        background.clearFlags = CameraClearFlags.SolidColor;
        background.backgroundColor = new Color(0.10F, 0.11F, 0.12F);
        background.cullingMask = 0;

        if (SeparateDisplay)
        {
            // Switch on the camera monitor (only possible in a build; the Editor uses a "Display 5" Game view).
            if (!Application.isEditor && Display.displays.Length > CameraMonitorIndex && !Display.displays[CameraMonitorIndex].active)
                Display.displays[CameraMonitorIndex].Activate();
            var go2 = new GameObject("LayoutBackground_CameraMonitor");
            go2.transform.SetParent(transform, false);
            var bg2 = go2.AddComponent<Camera>();
            bg2.depth = -100;
            bg2.clearFlags = CameraClearFlags.SolidColor;
            bg2.backgroundColor = Color.black;   // unused bottom two thirds stay black
            bg2.cullingMask = 0;
            bg2.targetDisplay = CameraMonitorIndex;
        }
    }

    void LateUpdate()
    {
        // Give the cab camera its fixed area (re-applied after every scene reload).
        var sm = StudyManager.Instance;
        var cam = sm != null && sm.Controller != null && sm.Controller.Machine != null ? sm.Controller.Machine.OperatorCamera : null;
        if (cam == null) return;
        Rect r = DriverView;
        if (cam.rect != r) cam.rect = r;
    }
}
