using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// CONTEXT-AWARE CAMERA VIEWS  (conditions C2 and C3 only)
// Three camera windows in the panel to the right of the driver view (see
// StudyLayout). All three are always on during a trial; WHICH cameras they show
// depends on the operation:
//
//   Travel mode (driving or reversing):  BIRD'S-EYE | LEFT SIDE  | RIGHT SIDE
//   Operation mode (digging, carrying,
//   dumping):                            BIRD'S-EYE | OVER BUCKET | BUCKET SIDE
//
// DRIVING scenario (UseDrivingLayout): only ONE fixed window, the bird's-eye view in the top half
// of the panel. The bottom half is the obstacle camera, shown by PopupViews only when something is close.
//
// Side cameras are fixed to the tracks and look down along each side of the machine.
// Every window has a label so the participant knows what it shows.
// ---------------------------------------------------------------------------

public enum ContextView { BirdsEye, Rear, BucketSide, OverBucket, LeftSide, RightSide }

public class ContextViews : MonoBehaviour
{
    // EXPERIMENT SWITCH. true = new style for travel mode: only the bird's-eye view is
    // always on, plus pop-up cameras near obstacles (see PopupViews.cs).
    // false = previous style: bird's-eye + left side + right side always on.
    public static bool PopupStyleForDriving = true;


    public float followSpeed = 6F;
    public float fieldOfView = 42F;     // close-up views (bucket side, over bucket)
    public float zoom = 0.7F;           // multiplies close-up camera distances: smaller = closer
    // Bird's-eye, rear and side cameras keep the original wider setting.
    public float birdsEyeHeight = 20F;
    public float wideFieldOfView = 55F;

    ExcavatorController ctl;
    Window[] slots;
    bool drivingLayout;

    // True in the driving scenario: bird's-eye (top) + obstacle camera on proximity (bottom).
    public static bool UseDrivingLayout
    {
        get
        {
            var sm = StudyManager.Instance;
            return sm != null && sm.CurrentTrial.scenario == StudyScenario.Easy_Driving;
        }
    }

    // One camera + its frame and label.
    class Window
    {
        public Camera cam;
        public GameObject frame;
        public Text label;
        public ContextView view;
        public string labelText;
        public bool hasView, visible = true;
        public string slot;
    }

    public void Init(ExcavatorController controller)
    {
        ctl = controller;
        drivingLayout = UseDrivingLayout;
        int n = drivingLayout ? 1 : 3;
        slots = new Window[n];
        for (int i = 0; i < n; i++)
        {
            Rect r = drivingLayout ? StudyLayout.PanelSlot(0, 2) : StudyLayout.PanelSlot(i);
            slots[i] = MakeWindow("Slot" + (i + 1), r, 50 + i);
            Show(slots[i], false);
        }
    }

    Window MakeWindow(string name, Rect r, int depth)
    {
        var w = new Window { slot = name };
        var camGo = new GameObject("ContextCamera_" + name);
        camGo.transform.SetParent(transform, false);
        w.cam = camGo.AddComponent<Camera>();
        w.cam.rect = r;
        w.cam.depth = depth;              // drawn on top of the operator's view
        w.cam.nearClipPlane = 0.2F;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        w.frame = new GameObject("ContextFrame_" + name);
        w.frame.transform.SetParent(transform, false);
        var canvas = w.frame.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;

        float t = 3F;
        Edge(w.frame, new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(0, t), new Vector2(0, 0));
        Edge(w.frame, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(0, t), new Vector2(0, 1));
        Edge(w.frame, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(t, 0), new Vector2(1, 0));
        Edge(w.frame, new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(t, 0), new Vector2(0, 0));

        var tag = new GameObject("Label", typeof(RectTransform), typeof(Image));
        tag.transform.SetParent(w.frame.transform, false);
        var rt = tag.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(r.xMin, r.yMax);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(250, 30);
        tag.GetComponent<Image>().color = new Color(0, 0, 0, 0.7F);
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(tag.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(10, 0); trt.offsetMax = Vector2.zero;
        w.label = textGo.GetComponent<Text>();
        w.label.font = font; w.label.fontSize = 18; w.label.color = Color.white; w.label.alignment = TextAnchor.MiddleLeft;
        return w;
    }

    static void Edge(GameObject parent, Vector2 a, Vector2 b, Vector2 size, Vector2 pivot)
    {
        var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.Min(a, b); rt.anchorMax = Vector2.Max(a, b);
        rt.pivot = pivot; rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(1, 1, 1, 0.9F);
    }

    static void Show(Window w, bool v)
    {
        if (w.visible == v) return;
        w.visible = v;
        w.cam.enabled = v;
        w.frame.SetActive(v);
        if (!v) w.hasView = false;
    }

    static readonly ContextView[] TravelSet = { ContextView.BirdsEye, ContextView.LeftSide, ContextView.RightSide };
    static readonly ContextView[] OperationSet = { ContextView.BirdsEye, ContextView.OverBucket, ContextView.BucketSide };

    static string LabelFor(ContextView v)
    {
        switch (v)
        {
            case ContextView.Rear: return "REAR VIEW";
            case ContextView.BucketSide: return "BUCKET SIDE VIEW";
            case ContextView.OverBucket: return "VIEW OVER BUCKET";
            case ContextView.LeftSide: return "LEFT SIDE";
            case ContextView.RightSide: return "RIGHT SIDE";
            default: return "BIRD'S-EYE VIEW";
        }
    }

    void LateUpdate()
    {
        var sm = StudyManager.Instance;
        bool show = sm != null && sm.ShowContextViews && ctl != null && ctl.Machine != null &&
                    (sm.CurrentState == StudyManager.State.Running || sm.CurrentState == StudyManager.State.Paused);
        if (drivingLayout)
        {
            Show(slots[0], show);
            if (show) Place(slots[0], ContextView.BirdsEye, LabelFor(ContextView.BirdsEye));
            return;
        }

        bool travel = ctl != null && ctl.IsTravelMode;
        var set = travel ? TravelSet : OperationSet;
        for (int i = 0; i < 3; i++)
        {
            bool slotOn = show && !(travel && PopupStyleForDriving && i > 0);   // popup style: bird's-eye only
            Show(slots[i], slotOn);
            if (slotOn) Place(slots[i], set[i], LabelFor(set[i]));
        }
    }

    void Place(Window w, ContextView v, string text)
    {
        bool switched = !w.hasView || v != w.view || text != w.labelText;
        if (switched)
        {
            w.view = v; w.hasView = true; w.labelText = text;
            w.label.text = text;
            StudyLogger.Log("view_shown", w.slot + ":" + v);
        }

        w.cam.fieldOfView = (v == ContextView.BucketSide || v == ContextView.OverBucket) ? fieldOfView : wideFieldOfView;
        Pose(v, out Vector3 pos, out Vector3 lookAt);
        if (switched) w.cam.transform.position = pos;   // jump straight to a new view (no camera flight)
        else w.cam.transform.position = Vector3.Lerp(w.cam.transform.position, pos, 1F - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));
        Vector3 up = v == ContextView.BirdsEye ? ctl.Machine.ForwardFlat : Vector3.up;   // top-down: machine forward is up
        w.cam.transform.rotation = Quaternion.LookRotation(lookAt - w.cam.transform.position, up);
    }

    void Pose(ContextView v, out Vector3 pos, out Vector3 lookAt)
    {
        var m = ctl.Machine;
        Vector3 c = m.SwingCenter, f = m.ForwardFlat, tip = m.BucketTipPosition;
        Vector3 reachDir = tip - c; reachDir.y = 0F;
        reachDir = reachDir.sqrMagnitude > 0.01F ? reachDir.normalized : f;
        Vector3 side = Vector3.Cross(Vector3.up, reachDir);
        float z = zoom;

        switch (v)
        {
            case ContextView.Rear:
                pos = c - f * 3F + Vector3.up * 3.2F;
                lookAt = c - f * 10F;
                break;
            case ContextView.BucketSide:
                Vector3 ground = WorldBuilder.OnGround(tip);
                pos = ground - side * (7F * z) + Vector3.up * 1.2F - reachDir * 1F;
                lookAt = new Vector3(tip.x, Mathf.Min(tip.y, ground.y), tip.z);
                break;
            case ContextView.OverBucket:
                pos = tip + Vector3.up * (9F * z) - reachDir * 1.5F;
                lookAt = tip + Vector3.down * 2F;
                break;
            case ContextView.LeftSide:
            case ContextView.RightSide:
                // Mounted on the side of the machine (fixed to the tracks), looking down along that side.
                Vector3 r = Vector3.Cross(Vector3.up, f) * (v == ContextView.RightSide ? 1F : -1F);
                pos = c + r * 1.8F + Vector3.up * 3F + f * 1.5F;
                lookAt = WorldBuilder.OnGround(c + r * 4.5F - f * 2F);
                break;
            default: // BirdsEye
                pos = c + Vector3.up * birdsEyeHeight - f * 0.01F;
                lookAt = c;
                break;
        }
    }
}
