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

    void OnDisable() { BucketSideCamera = null; }

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
            case ContextView.OverBucket: return "VIEW INTO BUCKET";
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
        BucketSideCamera = null;
        for (int i = 0; i < 3; i++)
        {
            bool slotOn = show && !(travel && PopupStyleForDriving && i > 0);   // popup style: bird's-eye only
            if (slotOn && set[i] == ContextView.OverBucket && !IntoBucketWanted()) slotOn = false;
            Show(slots[i], slotOn);
            if (slotOn)
            {
                Place(slots[i], set[i], LabelFor(set[i]));
                if (set[i] == ContextView.BucketSide) BucketSideCamera = slots[i].cam;
            }
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

        // Bucket-side overlays (C3 digging) are drawn only by the camera that shows the bucket side view.
        int sideOnly = 1 << CueKit.BucketSideOnlyLayer;
        w.cam.cullingMask = v == ContextView.BucketSide ? (w.cam.cullingMask | sideOnly) : (w.cam.cullingMask & ~sideOnly);
        w.cam.fieldOfView = v == ContextView.OverBucket ? 50F : v == ContextView.BucketSide ? fieldOfView : wideFieldOfView;
        Pose(v, out Vector3 pos, out Vector3 lookAt);
        if (switched) w.cam.transform.position = pos;   // jump straight to a new view (no camera flight)
        else w.cam.transform.position = Vector3.Lerp(w.cam.transform.position, pos, 1F - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));
        Vector3 up = v == ContextView.BirdsEye ? ctl.Machine.ForwardFlat : Vector3.up;   // top-down: machine forward is up
        w.cam.transform.rotation = Quaternion.LookRotation(lookAt - w.cam.transform.position, up);
    }

    // "View into bucket": a close camera that looks into the bucket opening, so the participant sees
    // how full the bucket is and where the teeth are - not the whole arm.
    // The opening side is found from the bucket's own geometry: the bucket shell (its renderers) lies on
    // one side of the line pivot -> teeth, the opening on the other. The camera sits ~2 m out on the
    // opening side (blended with "up" so it never goes below the ground), shifted a little sideways so
    // the arm is not in the middle of the picture.
    // The view into the bucket is only shown when the bucket is open (not curled): the bucket angle
    // (as on the measurement bar) must be above intoBucketMinAngle. 2 degree hysteresis + 0.4 s hold against flicker.
    public float intoBucketMinAngle = 5F;
    bool intoBucketOn;
    float intoBucketChanged = -10F;

    bool IntoBucketWanted()
    {
        var m = ctl.Machine;
        float a = m.bucketAngle;
        bool want = intoBucketOn ? a > intoBucketMinAngle - 2F : a > intoBucketMinAngle;
        if (want != intoBucketOn && Time.unscaledTime - intoBucketChanged > 0.4F)
        {
            intoBucketOn = want;
            intoBucketChanged = Time.unscaledTime;
            if (!want) StudyLogger.Log("view_hidden", $"OverBucket;bucket_angle={a:F0}");
        }
        return intoBucketOn;
    }

    // The camera currently showing the BUCKET SIDE VIEW (null if none). Used by the C3 overlays.
    public static Camera BucketSideCamera { get; private set; }

    Renderer[] bucketRenderers;
    public float intoBucketDistance = 2.0F;
    public float intoBucketSideShift = 0.7F;

    void IntoBucket(out Vector3 pos, out Vector3 lookAt)
    {
        var m = ctl.Machine;
        Vector3 piv = m.BucketPivotPosition, tip = m.BucketTipPosition;
        Vector3 mid = (piv + tip) / 2F;
        Vector3 axis = (tip - piv).normalized;

        if (bucketRenderers == null) bucketRenderers = m.BucketTransform.GetComponentsInChildren<Renderer>();
        Vector3 shell = Vector3.zero; int n = 0;
        foreach (var r in bucketRenderers) if (r != null && r.enabled) { shell += r.bounds.center; n++; }
        Vector3 open = Vector3.up;
        if (n > 0)
        {
            Vector3 s = shell / n - mid;
            s -= Vector3.Dot(s, axis) * axis;          // part across the pivot-teeth line
            if (s.sqrMagnitude > 1e-4F) open = -s.normalized;
        }

        Vector3 viewDir = (open + Vector3.up * 0.8F).normalized;   // from the bucket towards the camera
        if (viewDir.y < 0.35F) { viewDir.y = 0.35F; viewDir.Normalize(); }
        Vector3 side = m.BucketTransform.right;                    // bucket hinge axis (across the bucket)
        lookAt = mid - open * 0.15F;                               // a little inside the bucket
        pos = mid + viewDir * intoBucketDistance + side * intoBucketSideShift;
        float ground = TerrainDigger.CurrentGroundHeight(pos) + 0.4F;
        if (pos.y < ground) pos.y = ground;
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
                IntoBucket(out pos, out lookAt);
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
