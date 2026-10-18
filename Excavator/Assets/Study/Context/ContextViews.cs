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
// Other scenarios in travel mode (with PopupStyleForDriving): the same two-part panel - bird's-eye view
// in the top half, obstacle camera below it. In operation mode the three windows above are used.
//
// DRIVING scenario (UseDrivingLayout): only ONE fixed window, the bird's-eye view in the top half
// of the panel. The bottom half is the obstacle camera, shown by PopupViews only when something is close.
//
// Side cameras are fixed to the tracks and look down along each side of the machine.
// Every window has a label so the participant knows what it shows.
// ---------------------------------------------------------------------------

public enum ContextView { BirdsEye, Rear, BucketSide, OverBucket, LeftSide, RightSide, PitTop }

public class ContextViews : MonoBehaviour
{
    // EXPERIMENT SWITCH. true = new style for travel mode: only the bird's-eye view is
    // always on, plus pop-up cameras near obstacles (see PopupViews.cs).
    // false = previous style: bird's-eye + left side + right side always on.
    public static bool PopupStyleForDriving = true;


    public float followSpeed = 6F;
    public float fieldOfView = 42F;     // close-up views (bucket side, over bucket)
    public float zoom = 1.0F;           // multiplies close-up camera distances: smaller = closer (was 0.7)
    // Bird's-eye, rear and side cameras keep the original wider setting.
    public float birdsEyeHeight = 20F;
    public float wideFieldOfView = 55F;

    // DIGGING: the middle window shows the DIG PIT TOP VIEW instead of the view into the bucket (C2 and C3).
    // A fixed camera straight above the trench, trench long side pointing up (like the bird's-eye view),
    // high enough to show the whole trench plus pitTopMargin around it. The buried pipe and cable are
    // drawn through the ground in this view (BuriedServicesXray).
    public static bool PitTopViewInDigging = false;   // off: Digging uses the standard bird's-eye | view into bucket | bucket side view
    public float pitTopFieldOfView = 40F;
    public float pitTopMargin = 0.4F;   // metres shown beyond each end of the trench (was 1.2 m: closer view)

    public static bool UsePitTopView
    {
        get
        {
            var sm = StudyManager.Instance;
            return PitTopViewInDigging && sm != null && sm.CurrentTrial.scenario == StudyScenario.Moderate_Digging;
        }
    }

    ExcavatorController ctl;
    Window[] slots;
    Window travelBirdsEye;   // other scenarios, travel mode: bird's-eye in the TOP HALF (obstacle camera below it)
    Window sideTall;         // operation mode: BUCKET SIDE VIEW over the middle + bottom slots while the view into the bucket is hidden
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
        if (!drivingLayout)
        {
            travelBirdsEye = MakeWindow("TravelTop", StudyLayout.PanelSlot(0, 2), 53);
            Show(travelBirdsEye, false);
            Rect mid = StudyLayout.PanelSlot(1), bottom = StudyLayout.PanelSlot(2);
            sideTall = MakeWindow("SideTall", new Rect(bottom.xMin, bottom.yMin, bottom.width, mid.yMax - bottom.yMin), 54);
            Show(sideTall, false);
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
        w.cam.cullingMask &= ~(1 << CueKit.DriverOnlyLayer);   // driver-view-only cues (teeth line)

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

    void OnDisable()
    {
        BucketSideCamera = null; OverBucketCamera = null; PitTopCamera = null;
        Camera.onPreCull -= PitPreCull; Camera.onPostRender -= PitPostRender; RestoreMachine();
    }

    // ---- DIG PIT TOP VIEW: the machine (cab, boom, arm) is not drawn, only the bucket, so it does not hide the trench ----
    public bool hideMachineInPitView = true;
    Renderer[] machineRenderers;
    readonly System.Collections.Generic.List<Renderer> hiddenForPit = new System.Collections.Generic.List<Renderer>();

    void OnEnable() { Camera.onPreCull += PitPreCull; Camera.onPostRender += PitPostRender; }

    void PitPreCull(Camera cam)
    {
        if (!hideMachineInPitView || cam == null || cam != PitTopCamera || ctl == null || ctl.Machine == null) return;
        var m = ctl.Machine;
        if (machineRenderers == null) machineRenderers = m.transform.GetComponentsInChildren<Renderer>(true);
        Transform keep = m.BucketTransform;
        foreach (var r in machineRenderers)
        {
            if (r == null || !r.enabled || (keep != null && r.transform.IsChildOf(keep))) continue;
            r.enabled = false;
            hiddenForPit.Add(r);
        }
    }

    void PitPostRender(Camera cam) { if (cam == PitTopCamera) RestoreMachine(); }

    void RestoreMachine()
    {
        foreach (var r in hiddenForPit) if (r != null) r.enabled = true;
        hiddenForPit.Clear();
    }

    static void Show(Window w, bool v)
    {
        if (w == null || w.visible == v) return;
        w.visible = v;
        w.cam.enabled = v;
        w.frame.SetActive(v);
        if (!v) w.hasView = false;
    }

    static readonly ContextView[] TravelSet = { ContextView.BirdsEye, ContextView.LeftSide, ContextView.RightSide };
    static readonly ContextView[] OperationSet = { ContextView.BirdsEye, ContextView.OverBucket, ContextView.BucketSide };
    static readonly ContextView[] DiggingOperationSet = { ContextView.BirdsEye, ContextView.PitTop, ContextView.BucketSide };

    static string LabelFor(ContextView v)
    {
        switch (v)
        {
            case ContextView.Rear: return "REAR VIEW";
            case ContextView.BucketSide: return "BUCKET SIDE VIEW";
            case ContextView.OverBucket: return "VIEW INTO BUCKET";
            case ContextView.PitTop: return "DIG PIT (TOP VIEW)";
            case ContextView.LeftSide: return "LEFT SIDE";
            case ContextView.RightSide: return "RIGHT SIDE";
            default: return "BIRD'S-EYE VIEW";
        }
    }

    void LateUpdate()
    {
        // Windows are made in Init. After a script reload in Play mode (code saved while playing) they are lost:
        // do nothing until the next trial builds them again.
        if (slots == null || (!drivingLayout && travelBirdsEye == null)) return;
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
        BucketSideCamera = null;
        OverBucketCamera = null;
        PitTopCamera = null;

        // Travel mode with obstacle cameras: same panel as the driving scenario - bird's-eye view in the
        // top half, the obstacle camera (PopupViews) in the bottom half when something is close.
        if (travel && PopupStyleForDriving)
        {
            for (int i = 0; i < 3; i++) Show(slots[i], false);
            Show(sideTall, false);
            Show(travelBirdsEye, show);
            if (show) Place(travelBirdsEye, ContextView.BirdsEye, LabelFor(ContextView.BirdsEye));
            return;
        }
        Show(travelBirdsEye, false);

        var set = travel ? TravelSet : UsePitTopView ? DiggingOperationSet : OperationSet;
        // Operation mode: while the view into the bucket is hidden (bucket angle 0), the BUCKET SIDE VIEW takes
        // the middle AND bottom slots (one tall window) instead of leaving the middle slot empty.
        bool intoOn = !(set[1] == ContextView.OverBucket) || IntoBucketWanted();
        bool tall = show && !travel && !intoOn && set[2] == ContextView.BucketSide;
        Show(sideTall, tall);
        if (tall)
        {
            Place(sideTall, ContextView.BucketSide, LabelFor(ContextView.BucketSide));
            BucketSideCamera = sideTall.cam;
        }
        for (int i = 0; i < 3; i++)
        {
            bool slotOn = show && !(travel && PopupStyleForDriving && i > 0);   // popup style: bird's-eye only
            if (slotOn && set[i] == ContextView.OverBucket && !intoOn) slotOn = false;
            if (tall && i == 2) slotOn = false;                               // shown in the tall window instead
            Show(slots[i], slotOn);
            if (slotOn)
            {
                Place(slots[i], set[i], LabelFor(set[i]));
                if (set[i] == ContextView.BucketSide) BucketSideCamera = slots[i].cam;
                if (set[i] == ContextView.OverBucket) OverBucketCamera = slots[i].cam;
                if (set[i] == ContextView.PitTop) PitTopCamera = slots[i].cam;
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
        // The bucket side view is for depth and curl only: it does NOT draw the obstacle / worker highlights
        // (context-only layer) - those stay in the bird's-eye view, the view into the bucket and the driver view.
        int sideOnly = 1 << CueKit.BucketSideOnlyLayer, ctxOnly = 1 << CueKit.ContextOnlyLayer;
        w.cam.cullingMask = v == ContextView.BucketSide ? ((w.cam.cullingMask | sideOnly) & ~ctxOnly)
                                                        : ((w.cam.cullingMask & ~sideOnly) | ctxOnly);
        w.cam.fieldOfView = v == ContextView.OverBucket ? 55F : v == ContextView.BucketSide ? fieldOfView
                          : v == ContextView.PitTop ? pitTopFieldOfView : wideFieldOfView;
        Pose(v, out Vector3 pos, out Vector3 lookAt);
        if (switched) w.cam.transform.position = pos;   // jump straight to a new view (no camera flight)
        else w.cam.transform.position = Vector3.Lerp(w.cam.transform.position, pos, 1F - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));
        Vector3 up = v == ContextView.BirdsEye ? ctl.Machine.ForwardFlat
                   : v == ContextView.PitTop ? PitAxis() : Vector3.up;   // top-down: machine forward / trench long side is up
        w.cam.transform.rotation = Quaternion.LookRotation(lookAt - w.cam.transform.position, up);
        // Cut-away: clip everything closer to the camera than a thin slab in front of the bucket.
        float near = 0.2F;
        if (v == ContextView.BucketSide && UndergroundSideView.Active)
            near = Mathf.Max(0.2F, Vector3.Dot(lookAt - w.cam.transform.position, w.cam.transform.forward) - cutAwaySlab);
        w.cam.nearClipPlane = near;
    }

    // The view into the bucket is shown when the BUCKET angle (as on the measurement bar) is above
    // intoBucketMinAngle (2 degree hysteresis, 0.4 s hold against flicker).
    public float intoBucketMinAngle = 1F;          // shown when the bucket angle is above 0 (above 1 deg, as the bar rounds)
    public float intoBucketHysteresis = 0.5F;      // hidden again at or below 0.5 deg
    bool intoBucketOn;
    float intoBucketChanged = -10F;

    bool IntoBucketWanted()
    {
        float a = ctl.Machine.bucketAngle;
        bool want = intoBucketOn ? a > intoBucketMinAngle - intoBucketHysteresis : a > intoBucketMinAngle;
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
    // The camera currently showing the VIEW INTO BUCKET (null if none). The C3 curl advice is shown there.
    public static Camera OverBucketCamera { get; private set; }
    // The camera currently showing the DIG PIT TOP VIEW (null if none). The buried pipe/cable x-ray is drawn there.
    public static Camera PitTopCamera { get; private set; }

    Vector3 PitAxis()
    {
        var d = TerrainDigger.Instance;
        return d != null && d.HasZone ? d.ZoneAxisU : ctl.Machine.ForwardFlat;
    }

    // Straight above the middle of the dig zone, high enough for the whole zone + margin.
    void PitTop(out Vector3 pos, out Vector3 lookAt)
    {
        var d = TerrainDigger.Instance;
        if (d == null || !d.HasZone) { IntoBucket(out pos, out lookAt); return; }
        Vector3 c = d.ZoneCenter;
        c.y = TerrainDigger.ReferenceGroundHeight(c);
        float half = Mathf.Max(d.ZoneHalfLength, d.ZoneHalfWidth) + pitTopMargin;
        float h = half / Mathf.Tan(pitTopFieldOfView * 0.5F * Mathf.Deg2Rad);
        lookAt = c;
        pos = c + Vector3.up * h - d.ZoneAxisU * 0.01F;   // tiny offset so "up" is well defined
    }

    // Cut-away: only things within this distance IN FRONT of the bucket's plane are drawn (near clip plane),
    // so a worker or stakes between the camera and the bucket do not cover the section.
    public float cutAwaySlab = 1.2F;

    // "View into bucket": a close camera that looks down into the bucket from a FIXED vantage point
    // relative to the arm's working plane: above the bucket, a little towards the cab and to the right
    // of the arm, so the arm does not cover the picture. It does not depend on how the bucket is rotated,
    // so the perspective stays the same while the bucket curls or opens (it only follows the bucket's
    // position, smoothly).
    public float intoBucketDistance = 3.2F;
    public float bucketSideDistance = 5F;     // bucket side view: distance from the bucket   // camera height-ish distance from the bucket (was 2.0)
    public float intoBucketSideShift = 1.3F;  // to the right of the arm

    void IntoBucket(out Vector3 pos, out Vector3 lookAt)
    {
        var m = ctl.Machine;
        Vector3 piv = m.BucketPivotPosition, tip = m.BucketTipPosition;
        Vector3 mid = (piv + tip) / 2F;
        Vector3 reachDir = mid - m.SwingCenter; reachDir.y = 0F;
        reachDir = reachDir.sqrMagnitude > 0.01F ? reachDir.normalized : m.ForwardFlat;
        Vector3 side = Vector3.Cross(Vector3.up, reachDir);         // right of the arm, seen from the cab
        // Fixed vantage relative to the arm plane: above the bucket, a little towards the cab and to the right
        // of the arm (always above the bucket, so never inside the truck bed or the ground).
        lookAt = mid;
        pos = mid + Vector3.up * (0.8F * intoBucketDistance) - reachDir * (0.35F * intoBucketDistance) + side * intoBucketSideShift;
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
            {
                // Side-on view of the BUCKET (not the ground): camera level with the bucket, a little above it,
                // looking at the bucket and slightly down so the surface below it (ground or truck bed) is in view.
                Vector3 bmid = (m.BucketPivotPosition + tip) / 2F;
                if (UndergroundSideView.Active)
                {
                    // C3 cut-away: terrain hidden for this camera, so it can sit level with the bucket below ground.
                    pos = bmid - side * (bucketSideDistance * z) + Vector3.up * 0.3F - reachDir * 0.5F;
                    lookAt = bmid;
                    break;
                }
                float below = Mathf.Clamp(bmid.y - TerrainDigger.CurrentGroundHeight(bmid), 0F, 3F);
                pos = bmid - side * (bucketSideDistance * z) + Vector3.up * 0.6F - reachDir * 0.5F;
                float minY = TerrainDigger.CurrentGroundHeight(pos) + 0.5F;
                if (pos.y < minY) pos.y = minY;
                lookAt = bmid + Vector3.down * (0.35F * below);
                break;
            }
            case ContextView.OverBucket:
                IntoBucket(out pos, out lookAt);
                break;
            case ContextView.PitTop:
                PitTop(out pos, out lookAt);
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
