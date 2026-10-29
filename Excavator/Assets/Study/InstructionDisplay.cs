using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// INSTRUCTION DISPLAY (default Display 2, 1280 x 720)
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
//   StudyLayout.InstructionWidth / Height        resolution (default 1280 x 720, Editor and build)
//   StudyLayout.InstructionsOnSeparateDisplay    false = everything back on Display 1 (previous layout)
//
// HOW IT WORKS: Unity's IMGUI (OnGUI) can only draw on Display 1. So the instruction screens are drawn into
// a RenderTexture (size of the instruction monitor) and that texture is shown full screen on the instruction
// display by an overlay canvas. Scripts draw in that texture's pixel space:
//     if (InstructionDisplay.Begin()) { try { ...draw, use InstructionDisplay.Width / Height... } finally { InstructionDisplay.End(); } }
// Begin() returns false when there is no instruction display (switch off, or the monitor is not connected
// in a build) - then draw on Display 1 as before.
//
// In the Editor: open another Game view, set it to "Display 2" and its size to 1280 x 720 to see the instructions.
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

    RenderTexture rt;
    RenderTexture prevRT;
    Matrix4x4 prevMatrix;
    bool repainting;
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

        // GUI coordinates 0..Width / 0..Height are mapped onto the whole texture.
        i.prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / (float)i.rt.width, Screen.height / (float)i.rt.height, 1F)) * i.prevMatrix;

        i.repainting = Event.current != null && Event.current.type == EventType.Repaint;
        if (i.repainting)
        {
            i.prevRT = RenderTexture.active;
            RenderTexture.active = i.rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0F, Screen.width, Screen.height, 0F);
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
