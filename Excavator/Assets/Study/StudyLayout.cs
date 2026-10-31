using UnityEngine;

// ---------------------------------------------------------------------------
// STUDY SCREEN LAYOUT (identical in all three conditions)
//
// TWO DISPLAYS (ContextViewsOnSeparateDisplay = true; currently OFF - see ONE DISPLAY below):
//
//   DISPLAY 1 (driver)                          DISPLAY 5 (context-aware views, C2/C3), 2560 x 1080
//   +------------------------------------+      +-----------+-----------+-----------+
//   |                                    |      |  slot 0   |  slot 1   |  slot 2   |  top third (2560 x 360)
//   |      DRIVER VIEW (cab), full width |      +-----------+-----------+-----------+
//   |                                    |      |                                   |
//   +------------------------------------+      |          (dark, unused)           |
//   |   measurement bar (full width)     |      |                                   |
//   +------------------------------------+      +-----------------------------------+
//
//   The context-aware camera windows sit SIDE BY SIDE in the TOP THIRD of Display 5 (slot 0 = left),
//   about 850 x 360 pixels each (ContextAreaTop / ContextAreaHeight).
//   DRIVING scenario / travel mode: two windows - left = BIRD'S-EYE VIEW, right = OBSTACLE CAMERA (pop-up).
//   Display 5 stays dark in C1 and while no window is shown.
//   Task panel and measurement bar stay on Display 1.
//
// INSTRUCTION DISPLAY (InstructionsOnSeparateDisplay = true, default DISPLAY 2 at 1280 x 720, see InstructionDisplay.cs):
//   practice guide, task instruction (briefing) screens, practice/task complete and pause screens.
//   Change the monitor with InstructionDisplayIndex and the resolution with InstructionWidth / InstructionHeight below. Without that monitor (build) or with the switch off,
//   they are drawn on Display 1 as before (practice instructions over the right-hand part of the driver view).
//
// ONE DISPLAY (default now: ContextViewsOnSeparateDisplay = false, or a build with fewer than 5 displays connected):
//   the previous layout - driver view on the left, the camera windows stacked in a panel on the right.
//
// Display 1 in a build: a borderless 1920 x 720 window pinned to the top of the monitor (TopAnchoredWindow.cs),
// so the layout above fills that window, not the whole monitor.
// Display numbers: Unity counts from 0, so "Display 1" = 0 and "Display 5" = 4.
// In the Editor: open a second Game view and set it to "Display 5" to see the context views.
// In a build: Display 5 is activated automatically when Windows reports at least 5 monitors.
// Keeping the driver view the same size in every condition avoids giving one condition a bigger
// "windshield" than another. All values are fractions of the display (0..1).
// ---------------------------------------------------------------------------

public class StudyLayout : MonoBehaviour
{
    public const float BarHeight = 72F / 1080F;     // measurement bar (same as MeasurementBar.barHeight)
    public const float PanelWidth = 0.28F;           // right-hand panel (one-display layout, practice instructions)
    public const float Gap = 0.006F;

    // EXPERIMENT SWITCH: context-aware views on their own display (Display 5) and a full-width driver view on Display 1.
    // false = context views back on Display 1, stacked vertically in the right-hand panel (current lab setup).
    public static bool ContextViewsOnSeparateDisplay = false;
    public const int DriverDisplayIndex = 0;          // Display 1
    public const int ContextDisplayIndex = 4;         // Display 5
    // EXPERIMENT SWITCH: participant instructions and guidance on their own display (see InstructionDisplay.cs).
    public static bool InstructionsOnSeparateDisplay = true;
    // >>> INSTRUCTION DISPLAY NUMBER: 0-based, so Display 2 = 1 (Display 3 = 2, Display 4 = 3, ...). <<<
    public const int InstructionDisplayIndex = 1;     // Display 2
    // >>> INSTRUCTION DISPLAY RESOLUTION (Editor and build). Set per setup in ApplySetup() below - do not change here. <<<
    public static int InstructionWidth = 1920;
    public static int InstructionHeight = 720;

    // >>> LAB / HOME SETUP SWITCH <<<
    //   Editor: menu  Study > Setup > Lab  or  Home.  Remembered per computer (EditorPrefs), so the lab PC
    //           stays on Lab and the home PC on Home - nothing to change in code or commit.
    //   Build:  start the .exe with  -setup home  or  -setup lab  (default Lab).
    //   The values used by each setup are in ApplySetup() - change them there.
    public enum SetupProfile { Lab, Home }
    public static SetupProfile Setup { get; private set; } = SetupProfile.Lab;
    public const string SetupPrefKey = "StudyLayout.Setup";

    public static void ApplySetup()
    {
        Setup = ReadSetup();
        switch (Setup)
        {
            case SetupProfile.Home:
                // Smaller instruction screen: the Display 1 Game view on a home monitor / laptop is usually
                // narrower than 1920 px, and IMGUI clips the instructions to that Game view.
                InstructionsOnSeparateDisplay = true;
                InstructionWidth = 1280;
                InstructionHeight = 720;
                break;
            default: // Lab
                InstructionsOnSeparateDisplay = true;
                InstructionWidth = 1920;
                InstructionHeight = 720;
                break;
        }
        Debug.Log($"[StudyLayout] Setup: {Setup} - instructions {(InstructionsOnSeparateDisplay ? $"on Display {InstructionDisplayIndex + 1}" : "on Display 1")} ({InstructionWidth} x {InstructionHeight})");
    }

    static SetupProfile ReadSetup()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "-setup", System.StringComparison.OrdinalIgnoreCase)
                && System.Enum.TryParse(args[i + 1], true, out SetupProfile fromArgs))
                return fromArgs;
#if UNITY_EDITOR
        return (SetupProfile)UnityEditor.EditorPrefs.GetInt(SetupPrefKey, (int)SetupProfile.Lab);
#else
        return SetupProfile.Lab;
#endif
    }
    // >>> INSTRUCTION TEXT SIZE: 1 = default, 1.2 = 20 % bigger, 0.9 = 10 % smaller (practice guide and instruction screens). <<<
    public static float InstructionTextScale = 1F;
    // Part of Display 5 used for the views (fractions, y up): the top third, i.e. 2560 x 360 on a 2560 x 1080 display.
    public static float ContextAreaTop = 1F;
    public static float ContextAreaHeight = 1F / 3F;

    // True when the context views really go to Display 5 (in a build only if that display exists).
    public static bool SeparateDisplay =>
        ContextViewsOnSeparateDisplay && (Application.isEditor || Display.displays.Length > ContextDisplayIndex);

    // Display (0-based) for the context-aware camera windows and their frames / overlays.
    public static int ContextDisplay => SeparateDisplay ? ContextDisplayIndex : DriverDisplayIndex;

    public static Rect DriverView => SeparateDisplay
        ? new Rect(0F, BarHeight, 1F, 1F - BarHeight)
        : new Rect(0F, BarHeight, 1F - PanelWidth, 1F - BarHeight);

    // Practice instructions (PracticeGuide): right-hand part of Display 1 above the measurement bar.
    public static Rect GuidePanel => new Rect(1F - PanelWidth + Gap, BarHeight + Gap, PanelWidth - 2F * Gap, 1F - BarHeight - 2F * Gap);

    // Camera slot 0 = left (Display 5) / top (one display), 1 = middle, 2 = right / bottom.
    public static Rect PanelSlot(int i) => PanelSlot(i, 3);

    // Context-view area split into 'count' equal windows.
    public static Rect PanelSlot(int i, int count)
    {
        if (SeparateDisplay)
        {
            // Side by side in the top third of Display 5 (ContextAreaTop / ContextAreaHeight).
            float w = (1F - (count + 1) * Gap) / count;
            float yMin = ContextAreaTop - ContextAreaHeight;
            return new Rect(Gap + i * (w + Gap), yMin + Gap, w, ContextAreaHeight - 2F * Gap);
        }
        float x = 1F - PanelWidth + Gap, pw = PanelWidth - 2F * Gap;
        float h = (1F - BarHeight - (count + 1) * Gap) / count;
        float y = 1F - Gap - (i + 1) * h - i * Gap;
        return new Rect(x, y, pw, h);
    }

    // Two neighbouring slots joined into one window (e.g. the tall/wide BUCKET SIDE VIEW).
    public static Rect Join(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // Pixel size of a display (for converting pixels to 0..1 display units). Falls back to the main window.
    public static float PixelWidth(int display) =>
        display > 0 && display < Display.displays.Length && Display.displays[display].active ? Display.displays[display].renderingWidth : Screen.width;
    public static float PixelHeight(int display) =>
        display > 0 && display < Display.displays.Length && Display.displays[display].active ? Display.displays[display].renderingHeight : Screen.height;

    // Puts a camera and/or an overlay canvas on the context-view display.
    public static void ToContextDisplay(Camera cam) { if (cam != null) cam.targetDisplay = ContextDisplay; }
    public static void ToContextDisplay(Canvas canvas) { if (canvas != null) canvas.targetDisplay = ContextDisplay; }

    Camera background, contextBackground;

    void Awake()
    {
        ApplySetup();   // Lab / Home values (must run before InstructionDisplay is added below)
        // Display 1 (build): borderless 1920 x 720 window at the top-left of the monitor (see TopAnchoredWindow).
        if (GetComponent<TopAnchoredWindow>() == null) gameObject.AddComponent<TopAnchoredWindow>();
        // Instructions and guidance on their own display (Display 2 by default, see InstructionDisplayIndex).
        if (GetComponent<InstructionDisplay>() == null) gameObject.AddComponent<InstructionDisplay>();

        // In a build, the extra monitor must be switched on once (Display 1 is always on).
        if (ContextViewsOnSeparateDisplay && !Application.isEditor)
        {
            if (Display.displays.Length > ContextDisplayIndex) Display.displays[ContextDisplayIndex].Activate();
            else Debug.LogWarning($"[StudyLayout] Only {Display.displays.Length} display(s) found - context views stay on Display 1 (one-display layout).");
        }

        // A camera that draws nothing but a dark background, behind everything,
        // so empty areas are a clean dark surface when no camera window is shown.
        background = MakeBackground("LayoutBackground", DriverDisplayIndex);
        if (SeparateDisplay) contextBackground = MakeBackground("LayoutBackgroundContext", ContextDisplayIndex);
    }

    Camera MakeBackground(string name, int display)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var cam = go.AddComponent<Camera>();
        cam.depth = -100;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10F, 0.11F, 0.12F);
        cam.cullingMask = 0;
        cam.targetDisplay = display;
        return cam;
    }

    void LateUpdate()
    {
        // Give the cab camera its fixed area on Display 1 (re-applied after every scene reload).
        var sm = StudyManager.Instance;
        var cam = sm != null && sm.Controller != null && sm.Controller.Machine != null ? sm.Controller.Machine.OperatorCamera : null;
        if (cam == null) return;
        Rect r = DriverView;
        if (cam.rect != r) cam.rect = r;
        if (cam.targetDisplay != DriverDisplayIndex) cam.targetDisplay = DriverDisplayIndex;
    }
}
