using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// INSTRUCTION DISPLAY (default Display 2; 1920 x 720 in the Lab setup, 1280 x 720 in the Home setup)
//
// The participant instructions and guidance are shown on their own monitor:
//   * the practice guide (PracticeGuide: phases 1-3, stick diagrams, hold bars, hints)
//   * the task instruction (briefing) screen before every trial, incl. the dig-and-dump site map
//   * the "practice complete" / "task complete - questionnaires" screens and the pause screen
// The setup screen and the final "session complete" screen stay on Display 1 (experimenter screens,
// the setup screen needs keyboard / mouse input). The task panel and the measurement bar stay on Display 1.
//
// WHERE TO CHANGE IT (StudyLayout.cs):
//   StudyLayout.InstructionDisplayIndex          which monitor (0-based: Display 2 = 1)
//   StudyLayout.ApplySetup()                     resolution per setup (Lab / Home, menu Study > Setup)
//   StudyLayout.InstructionsOnSeparateDisplay    false = everything back on Display 1 (previous layout)
//
// HOW IT WORKS: Unity's IMGUI (OnGUI) can only draw on Display 1. So the instruction screens are drawn into
// a RenderTexture (size of the instruction monitor) and that texture is shown full screen on the instruction
// display by an overlay canvas. Scripts draw in that texture's pixel space:
//     if (InstructionDisplay.Begin()) { try { ...draw, use InstructionDisplay.Width / Height... } finally { InstructionDisplay.End(); } }
// Begin() returns false when there is no instruction display (switch off, or the monitor is not connected
// in a build) - then draw on Display 1 as before.
//
// In the Editor: open another Game view, set it to "Display 2" and its size to the instruction resolution.
// The Display 1 Game view must stay VISIBLE (not maximized away or hidden behind another tab) and at least
// as large as the instruction resolution - the instructions are drawn by Display 1's OnGUI, so if that
// Game view does not repaint, Display 2 stays empty (dark).
// In a build: the display is switched on automatically when Windows reports enough monitors.
// If the instructions appear upside down on some graphics API, set FlipY = true.
// Added automatically by StudyLayout.
// ---------------------------------------------------------------------------
public class InstructionDisplay : MonoBehaviour
{
    public static bool FlipY = false;
    public static Color ClearColor = new Color(0.10F, 0.11F, 0.12F, 1F);

    public static InstructionDisplay Instance { get; private set; }

    // True when the instruction screens go to their own display.
    public static bool Active => Instance != null && Instance.rt != null;

    // Drawing area (pixels) inside Begin() / End(); Display 1 size when not active.
    public static float Width => Active ? Instance.rt.width : Screen.width;
    public static float Height => Active ? Instance.rt.height : Screen.height;

    // Text / layout size factor on the instruction display: 1 at 720 pixels high, times StudyLayout.InstructionTextScale.
    public static float UiScale => Height / 720F * StudyLayout.InstructionTextScale;

    RenderTexture rt;
    RenderTexture prevRT;
    Matrix4x4 prevMatrix;
    bool repainting, warnedSmall;
    int depth;               // nested Begin() calls
    int lastDrawFrame = -10, clearedFrame = -1;

    void Awake()
    {
        Instance = this;
        int idx = StudyLayout.InstructionDisplayIndex;
        if (!StudyLayout.InstructionsOnSeparateDisplay || idx <= StudyLayout.DriverDisplayIndex) return;
        if (StudyLayout.ContextViewsOnSeparateDisplay && idx == StudyLayout.ContextDisplayIndex)
            Debug.LogWarning("[InstructionDisplay] Instruction display is the same as the context-view display - they will overlap.");

        int w = StudyLayout.InstructionWidth, h = StudyLayout.InstructionHeight;
        if (!Application.isEditor)
        {
            if (Display.displays.Length <= idx)
            {
                Debug.LogWarning($"[InstructionDisplay] Only {Display.displays.Length} display(s) found - instructions stay on Display 1.");
                return;
            }
            var d = Display.displays[idx];
            d.Activate();
            d.SetRenderingResolution(w, h);   // render at InstructionWidth x InstructionHeight (scaled to the monitor if its native size differs)
        }

        rt = new RenderTexture(Mathf.Max(64, w), Mathf.Max(64, h), 0, RenderTextureFormat.ARGB32) { name = "InstructionDisplayRT" };
        rt.Create();
        ClearTexture();

        // Dark background camera for the instruction display (nothing else renders there).
        var camGo = new GameObject("InstructionDisplayBackground");
        camGo.transform.SetParent(transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.depth = -100;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ClearColor;
        cam.cullingMask = 0;
        cam.targetDisplay = idx;

        // Full-screen overlay canvas showing the texture.
        var cgo = new GameObject("InstructionDisplayCanvas");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = idx;
        canvas.sortingOrder = 100;
        var igo = new GameObject("Instructions");
        igo.transform.SetParent(cgo.transform, false);
        var img = igo.AddComponent<RawImage>();
        img.texture = rt;
        img.raycastTarget = false;
        img.uvRect = FlipY ? new Rect(0F, 1F, 1F, -1F) : new Rect(0F, 0F, 1F, 1F);
        var rect = img.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;

        Debug.Log($"[InstructionDisplay] Instructions on Display {idx + 1} ({w} x {h})");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
    }

    // Nothing drawn last frame -> empty (dark) instruction display.
    void LateUpdate()
    {
        if (rt != null && lastDrawFrame < Time.frameCount - 1 && clearedFrame < lastDrawFrame + 1)
        {
            ClearTexture();
            clearedFrame = Time.frameCount;
        }
    }

    void ClearTexture()
    {
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, ClearColor);
        RenderTexture.active = old;
    }

    // Call inside OnGUI. true = draw now in Width x Height pixels (instruction display), then call End().
    public static bool Begin()
    {
        if (!Active) return false;
        var i = Instance;
        if (i.depth++ > 0) return true;

        // GUI coordinates = texture pixels (0..Width, 0..Height), 1:1 - no scaling, so the layout does not depend on
        // the size of Display 1 / the Editor Game view. (IMGUI clips to the Display 1 window, so that window must be at
        // least as large as the instruction display: 1920 x 720 >= 1280 x 720 in the lab build.)
        i.prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        if (!i.warnedSmall && (Screen.width < i.rt.width || Screen.height < i.rt.height))
        {
            i.warnedSmall = true;
            Debug.LogWarning($"[InstructionDisplay] Display 1 / Game view ({Screen.width} x {Screen.height}) is smaller than the instruction display ({i.rt.width} x {i.rt.height}) - instructions will be cut off. Make the Display 1 Game view at least that large.");
        }

        i.repainting = Event.current != null && Event.current.type == EventType.Repaint;
        if (i.repainting)
        {
            i.prevRT = RenderTexture.active;
            RenderTexture.active = i.rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0F, i.rt.width, i.rt.height, 0F);
            if (i.lastDrawFrame != Time.frameCount) GL.Clear(true, true, ClearColor);   // first drawing this frame
            i.lastDrawFrame = Time.frameCount;
        }
        return true;
    }

    public static void End()
    {
        if (!Active) return;
        var i = Instance;
        if (i.depth <= 0) return;
        if (--i.depth > 0) return;
        if (i.repainting)
        {
            GL.PopMatrix();
            RenderTexture.active = i.prevRT;
            i.repainting = false;
        }
        GUI.matrix = i.prevMatrix;
    }
}
