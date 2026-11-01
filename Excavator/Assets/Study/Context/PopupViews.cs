using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// POP-UP CAMERAS NEAR OBSTACLES  (C2/C3, travel mode, experiment switch)
// Active only when ContextViews.PopupStyleForDriving is true.
//
// When the machine comes close to an obstacle or a worker, a small camera window
// pops up INSIDE the driver view, placed next to where that object is on screen
// (or at the screen edge on its side if it is out of sight). The camera looks at
// the gap between the machine and the object, i.e. at the possible collision.
//   - Static obstacle (barrel, gate post): the pop-up sits beside it.
//   - Moving obstacle (worker): the pop-up follows the worker across the screen.
// The pop-up never covers the object itself or the centre of the driver view.
// At most two pop-ups at a time (the two closest). Logged as popup_open / popup_close.
//
// DRIVING scenario (ContextViews.UseDrivingLayout): "docked" mode instead. ONE obstacle camera in
// the right half of DISPLAY 5, next to the bird's-eye view (one-display layout: bottom half of the
// right-hand panel); the driver view stays free. It appears when an obstacle
// or worker comes within its opening distance, always shows the closest one (switches if another
// becomes clearly closer), and disappears again when everything is far away. The label says what it
// is, how far, and on which side of the machine.
// ---------------------------------------------------------------------------

public class PopupViews : MonoBehaviour
{
    public float openDistanceObstacle = 2F;   // metres from the machine (2026-11-01: was 4 m)
    public float openDistanceWorker = 8F;
    public float closeExtra = 1.5F;           // workers: must move this much further away to close (no flicker)
    public float closeExtraObstacle = 0F;     // obstacles / truck: the window closes as soon as nothing is within 2 m
    public float closeDelayObstacle = 0.3F;   // ... after this short time (only against flicker at the 2 m border;
                                              // no minimum open time for obstacles)
    public float closeDelay = 2F;             // seconds the object must stay far before the window closes
    public float minOpenSeconds = 4F;         // a window stays at least this long once opened
    public int maxPopups = 2;
    public Vector2 size = new Vector2(0.20F, 0.24F);   // fraction of the screen
    public float followSpeed = 8F;

    public float switchMargin = 0.5F;         // docked: switch to another object when it is this much closer
    // 2026-11-01: PEOPLE FIRST. For choosing (and switching) the one docked camera, a worker counts as this much
    // closer than it is. A worker 2 m away is shown before a barrel 1.6 m away (before, the barrel won and the worker
    // was only shown once it had walked away again). An obstacle still wins if it is clearly closer (< worker - 3 m).
    public float workerPriority = 3F;
    public float minSwitchSeconds = 0.5F;     // docked: ... but not more often than this (was minOpenSeconds = 4 s, so
                                              // an object that had moved away could stay on screen while a closer one
                                              // was ignored - e.g. the fence at 3.6 m while passing a barrel)

    // true = obstacle cameras are always placed in the right-hand panel, never inside the driver view.
    // One window in the bottom half of the panel, under the bird's-eye view (all scenarios). It shows the
    // closest obstacle and switches when another one becomes clearly closer.
    public static bool DockInPanel = true;

    ExcavatorController ctl;
    readonly List<Popup> popups = new List<Popup>();
    bool docked, drivingDock;
    // Dig and dump: the truck is the dump target, so it does not open the obstacle camera (unless touched),
    // and the camera only opens while the machine is moving.
    bool ignoreTruck, onlyWhileMoving;

    class Popup
    {
        public Hazard hazard;
        public Camera cam;
        public GameObject frame;
        public RectTransform box;
        public Text label;
        public Vector2 center;      // current screen position (0..1), smoothed
        public bool placed;
        public float farSince = -1F;
        public float openedAt;
    }

    public void Init(ExcavatorController controller)
    {
        ctl = controller;
        drivingDock = ContextViews.UseDrivingLayout;
        docked = drivingDock || DockInPanel;   // one obstacle camera, bottom half of the panel, under the bird's-eye view
        if (docked) maxPopups = 1;
        bool digDump = StudyManager.Instance != null && StudyManager.Instance.CurrentTrial.scenario == StudyScenario.Hard_DigAndDump;
        ignoreTruck = digDump;
        onlyWhileMoving = digDump;
    }

    void LateUpdate()
    {
        var sm = StudyManager.Instance;
        bool running = sm != null && sm.ShowContextViews && ctl != null && ctl.Machine != null &&
                       (sm.CurrentState == StudyManager.State.Running || sm.CurrentState == StudyManager.State.Paused);
        // Docked (driving scenario): in any mode. Floating: travel mode with the pop-up experiment switch on.
        bool active = running && (drivingDock || (ContextViews.PopupStyleForDriving && ctl.IsTravelMode));
        if (!active) { CloseAll(); return; }

        // Candidates: visible obstacles and workers inside their opening distance, closest first.
        var near = new List<Hazard>();
        var input = ctl.LastInput;
        bool moving = ctl.Machine.SpeedKmh > 0.3F || input.trackLeft != 0F || input.trackRight != 0F;
        foreach (var h in Hazard.All)
        {
            if (!h.active || !h.gameObject.activeInHierarchy || !IsPopupKind(h) || h.CurrentDistance > OpenDistance(h)) continue;
            if (ignoreTruck && h.kind == HazardKind.Truck && h.CurrentLevel != Hazard.Level.Contact) continue;
            near.Add(h);
        }
        near.Sort((a, b) => Rank(a).CompareTo(Rank(b)));

        // Docked: always show the CLOSEST object. Switch as soon as another one is clearly closer (switchMargin),
        // at most every minSwitchSeconds - also while standing still, so the window never keeps showing an object
        // that is farther away than another one nearby.
        bool switched = false;   // switching to a closer object: the replacement may open while standing still
        if (docked && popups.Count == 1 && near.Count > 0 && near[0] != popups[0].hazard &&
            Time.unscaledTime - popups[0].openedAt > minSwitchSeconds &&
            (popups[0].hazard == null || Rank(near[0]) < Rank(popups[0].hazard) - switchMargin
             || popups[0].hazard.CurrentDistance > OpenDistance(popups[0].hazard)))   // shown one is out of range: take the one in range
        {
            Close(popups[0]);
            popups.Clear();
            switched = true;
        }

        // Open pop-ups for new close hazards ("only while moving" decides when a window may OPEN when none is shown;
        // switching to a closer object above works also while standing still).
        foreach (var h in near)
        {
            if (onlyWhileMoving && !moving && !switched) break;
            if (popups.Count >= maxPopups) break;
            if (popups.Exists(p => p.hazard == h)) continue;
            popups.Add(Open(h));
        }

        // Update or close existing ones.
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            bool gone = p.hazard == null || !p.hazard.active || !p.hazard.gameObject.activeInHierarchy;
            // Closing depends only on distance (and the truck rule). "Only while moving" decides when a
            // window may OPEN, not when it closes, so a window does not vanish when the machine pauses.
            float extra = p.hazard != null && p.hazard.kind == HazardKind.Worker ? closeExtra : closeExtraObstacle;
            bool far = gone || p.hazard.CurrentDistance > OpenDistance(p.hazard) + extra
                       || (ignoreTruck && p.hazard.kind == HazardKind.Truck && p.hazard.CurrentLevel != Hazard.Level.Contact);
            if (far) { if (p.farSince < 0) p.farSince = Time.unscaledTime; }
            else p.farSince = -1F;
            bool worker = p.hazard != null && p.hazard.kind == HazardKind.Worker;
            bool minTimeOver = !worker || Time.unscaledTime - p.openedAt > minOpenSeconds;   // obstacles: no minimum
            float delay = worker ? closeDelay : closeDelayObstacle;
            if (gone || (far && minTimeOver && Time.unscaledTime - p.farSince > delay)) { Close(p); popups.RemoveAt(i); continue; }
            UpdatePopup(p, i);
        }
    }

    static bool IsPopupKind(Hazard h) => h.kind == HazardKind.Worker || h.kind == HazardKind.Obstacle || h.kind == HazardKind.Truck;
    // Ranking distance for the docked camera: workers count as workerPriority closer (see above).
    float Rank(Hazard h) => h.kind == HazardKind.Worker ? h.CurrentDistance - workerPriority : h.CurrentDistance;
    float OpenDistance(Hazard h) => h.kind == HazardKind.Worker ? openDistanceWorker : openDistanceObstacle;

    Popup Open(Hazard h)
    {
        var p = new Popup { hazard = h, openedAt = Time.unscaledTime };
        var camGo = new GameObject("PopupCamera_" + h.hazardId);
        camGo.transform.SetParent(transform, false);
        p.cam = camGo.AddComponent<Camera>();
        p.cam.depth = 60;
        p.cam.fieldOfView = 50F;
        p.cam.nearClipPlane = 0.2F;
        p.cam.cullingMask &= ~((1 << CueKit.BucketSideOnlyLayer) | (1 << CueKit.DriverOnlyLayer));
        bool dock = docked || DockInPanel;
        // Docked obstacle camera: with the other context views on Display 5. Floating pop-ups stay over the driver view.
        p.cam.targetDisplay = dock ? StudyLayout.ContextDisplay : StudyLayout.DriverDisplayIndex;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        p.frame = new GameObject("PopupFrame_" + h.hazardId);
        p.frame.transform.SetParent(transform, false);
        var canvas = p.frame.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;
        canvas.targetDisplay = p.cam.targetDisplay;

        var boxGo = new GameObject("Box", typeof(RectTransform));
        boxGo.transform.SetParent(p.frame.transform, false);
        p.box = boxGo.GetComponent<RectTransform>();
        p.box.offsetMin = p.box.offsetMax = Vector2.zero;
        Color edge = new Color(1F, 1F, 1F, 0.95F);
        Side(p.box, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 3), new Vector2(0.5F, 0), edge);   // top
        Side(p.box, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 3), new Vector2(0.5F, 1), edge);   // bottom
        Side(p.box, new Vector2(0, 0), new Vector2(0, 1), new Vector2(3, 0), new Vector2(1, 0.5F), edge);   // left
        Side(p.box, new Vector2(1, 0), new Vector2(1, 1), new Vector2(3, 0), new Vector2(0, 0.5F), edge);   // right

        var tag = new GameObject("Label", typeof(RectTransform), typeof(Image));
        tag.transform.SetParent(p.box, false);
        var rt = tag.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.sizeDelta = new Vector2(150, 26);
        tag.GetComponent<Image>().color = new Color(0, 0, 0, 0.7F);
        var tGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        tGo.transform.SetParent(tag.transform, false);
        var trt = tGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(8, 0); trt.offsetMax = Vector2.zero;
        p.label = tGo.GetComponent<Text>();
        p.label.font = font; p.label.fontSize = 16; p.label.color = Color.white; p.label.alignment = TextAnchor.MiddleLeft;
        p.label.text = KindLabel(h);
        if (docked || DockInPanel) { p.label.fontSize = 18; p.label.rectTransform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(330, 30); }

        StudyLogger.Log("popup_open", $"{h.hazardId};{h.kind};dist={h.CurrentDistance:F2}");
        return p;
    }

    static void Side(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 pivot, Color c)
    {
        var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot; rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
        go.GetComponent<Image>().color = c;
    }

    void UpdatePopup(Popup p, int index)
    {
        var h = p.hazard;
        var m = ctl.Machine;
        Vector3 hp = h.HazardPoint, mp = h.MachinePoint;

        // 1. Camera: looks at the gap between machine and object, from outside and above.
        Vector3 gap = (hp + mp) / 2F;
        Vector3 outward = hp - m.SwingCenter; outward.y = 0F;
        outward = outward.sqrMagnitude > 0.01F ? outward.normalized : m.ForwardFlat;
        Vector3 camPos = gap + outward * 3.5F + Vector3.up * 3.5F;
        p.cam.transform.position = camPos;
        p.cam.transform.rotation = Quaternion.LookRotation(gap - camPos, Vector3.up);

        if (docked || DockInPanel)
        {
            Rect dock = StudyLayout.PanelSlot(1, 2);
            p.cam.rect = dock;
            p.box.anchorMin = dock.min;
            p.box.anchorMax = dock.max;
            p.box.offsetMin = p.box.offsetMax = Vector2.zero;
            p.label.text = $"{KindLabel(h)}  {h.CurrentDistance:0.0} m  {SideText(hp)}";
            return;
        }

        // 2. Screen position: next to the object as the driver sees it, inside the driver view.
        Rect view = StudyLayout.DriverView;
        Vector2 want = ScreenAnchor(m.OperatorCamera, hp, view);
        // Put the pop-up beside the object (towards the screen edge), never over the object or the centre.
        float side = want.x < view.center.x ? -1F : 1F;
        Vector2 c = new Vector2(want.x + side * (size.x / 2F + 0.03F), want.y + size.y * 0.15F);
        c.y -= index * 0.02F;   // slight offset if two pop-ups land in the same place
        c.x = Mathf.Clamp(c.x, view.xMin + size.x / 2F + 0.005F, view.xMax - size.x / 2F - 0.005F);
        c.y = Mathf.Clamp(c.y, view.yMin + size.y / 2F + 0.005F, view.yMax - size.y / 2F - 0.08F);
        // Keep out of the central third of the driver view (where the driver looks ahead).
        float midL = view.xMin + view.width / 3F, midR = view.xMin + view.width * 2F / 3F;
        if (c.x + size.x / 2F > midL && c.x - size.x / 2F < midR)
            c.x = side < 0 ? midL - size.x / 2F : midR + size.x / 2F;

        if (!p.placed) { p.center = c; p.placed = true; }
        else p.center = Vector2.Lerp(p.center, c, 1F - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));

        Rect r = new Rect(p.center.x - size.x / 2F, p.center.y - size.y / 2F, size.x, size.y);
        p.cam.rect = r;
        p.box.anchorMin = new Vector2(r.xMin, r.yMin);
        p.box.anchorMax = new Vector2(r.xMax, r.yMax);
        p.box.offsetMin = p.box.offsetMax = Vector2.zero;
    }

    // Where a world point appears in the driver view (0..1 screen coords). Points behind or
    // outside the view are pushed to the nearest edge of the driver view on their side.
    static Vector2 ScreenAnchor(Camera driverCam, Vector3 world, Rect view)
    {
        if (driverCam == null) return view.center;
        Vector3 vp = driverCam.WorldToViewportPoint(world);   // relative to the camera's own rect
        Vector2 s = new Vector2(view.xMin + vp.x * view.width, view.yMin + vp.y * view.height);
        if (vp.z < 0F)   // behind the driver: mirror to the matching side, bottom edge
            s = new Vector2(view.xMin + (1F - vp.x) * view.width, view.yMin);
        s.x = Mathf.Clamp(s.x, view.xMin, view.xMax);
        s.y = Mathf.Clamp(s.y, view.yMin, view.yMax);
        return s;
    }

    static string KindLabel(Hazard h) => h.kind == HazardKind.Worker ? "WORKER" : h.kind == HazardKind.Truck ? "TRUCK" : "OBSTACLE";

    // Where the object is relative to the machine: FRONT / REAR and LEFT / RIGHT.
    string SideText(Vector3 hp)
    {
        var m = ctl.Machine;
        Vector3 d = hp - m.SwingCenter; d.y = 0F;
        float ahead = Vector3.Dot(d, m.ForwardFlat), right = Vector3.Dot(d, Vector3.Cross(Vector3.up, m.ForwardFlat));
        string fb = ahead > 1.5F ? "FRONT" : ahead < -1.5F ? "REAR" : "";
        string lr = right > 0.8F ? "RIGHT" : right < -0.8F ? "LEFT" : "";
        string s = (fb + " " + lr).Trim();
        return s == "" ? "" : "- " + s;
    }

    void Close(Popup p)
    {
        if (p.hazard != null) StudyLogger.Log("popup_close", $"{p.hazard.hazardId};min_dist_logged_by_tracker");
        if (p.cam != null) Destroy(p.cam.gameObject);
        if (p.frame != null) Destroy(p.frame);
    }

    void CloseAll()
    {
        foreach (var p in popups) Close(p);
        popups.Clear();
    }
}
