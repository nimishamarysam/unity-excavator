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
// SECOND DISPLAY (lab setup, ContextViewsOnSecondDisplay = true):
//   Display 1: DRIVER VIEW over the full width + measurement bar.
//   Display 2 (landscape): the camera windows SIDE BY SIDE, full height:
//     +-----------+-----------+-----------+
//     | camera 1  | camera 2  | camera 3  |
//     +-----------+-----------+-----------+
//   Driving scenario: bird's-eye (left half) | obstacle camera (right half).
// In a build the second monitor is switched on automatically; if only one monitor is
// connected it falls back to the single-screen layout above. In the Unity Editor, open a
// second Game view (Window > General > Game), set it to "Display 2" and drag it to that monitor.
// ---------------------------------------------------------------------------

public class StudyLayout : MonoBehaviour
{
    public const float BarHeight = 72F / 1080F;     // measurement bar (same as MeasurementBar.barHeight)
    public const float PanelWidth = 0.28F;           // right-hand camera panel
    public const float Gap = 0.006F;

    // EXPERIMENT/LAB SWITCH: camera windows on a second monitor instead of the right-hand panel.
    public static bool ContextViewsOnSecondDisplay = true;

    // True when the camera windows really go to display 2 (Editor: always, using a "Display 2" Game view).
    public static bool SeparateDisplay =>
        ContextViewsOnSecondDisplay && (Application.isEditor || Display.displays.Length > 1);

    // Unity display index for the camera windows and their frames/labels (0 = main monitor).
    public static int PanelDisplay => SeparateDisplay ? 1 : 0;

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
            // Second monitor: windows side by side, slot 0 on the left, full height.
            float sw = (1F - (count + 1) * Gap) / count;
            return new Rect(Gap + i * (sw + Gap), Gap, sw, 1F - 2F * Gap);
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
            // Switch on the second monitor (only possible in a build; the Editor uses a "Display 2" Game view).
            if (!Application.isEditor && Display.displays.Length > 1 && !Display.displays[1].active)
                Display.displays[1].Activate();
            var go2 = new GameObject("LayoutBackground_Display2");
            go2.transform.SetParent(transform, false);
            var bg2 = go2.AddComponent<Camera>();
            bg2.depth = -100;
            bg2.clearFlags = CameraClearFlags.SolidColor;
            bg2.backgroundColor = background.backgroundColor;
            bg2.cullingMask = 0;
            bg2.targetDisplay = 1;
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
