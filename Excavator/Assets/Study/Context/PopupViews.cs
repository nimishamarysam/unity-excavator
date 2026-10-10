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
// ---------------------------------------------------------------------------

public class PopupViews : MonoBehaviour
{
    public float openDistanceObstacle = 4F;   // metres from the machine
    public float openDistanceWorker = 8F;
    public float closeExtra = 1F;             // must move this much further away to close (no flicker)
    public float closeDelay = 1F;             // seconds
    public int maxPopups = 2;
    public Vector2 size = new Vector2(0.20F, 0.24F);   // fraction of the screen
    public float followSpeed = 8F;

    ExcavatorController ctl;
    readonly List<Popup> popups = new List<Popup>();

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
    }

    public void Init(ExcavatorController controller) { ctl = controller; }

    void LateUpdate()
    {
        var sm = StudyManager.Instance;
        bool active = ContextViews.PopupStyleForDriving && sm != null && sm.ShowContextViews && ctl != null && ctl.Machine != null &&
                      ctl.IsTravelMode && (sm.CurrentState == StudyManager.State.Running || sm.CurrentState == StudyManager.State.Paused);
        if (!active) { CloseAll(); return; }

        // Candidates: visible obstacles and workers inside their opening distance, closest first.
        var near = new List<Hazard>();
        foreach (var h in Hazard.All)
            if (h.active && h.gameObject.activeInHierarchy && IsPopupKind(h) && h.CurrentDistance <= OpenDistance(h)) near.Add(h);
        near.Sort((a, b) => a.CurrentDistance.CompareTo(b.CurrentDistance));

        // Open pop-ups for new close hazards.
        foreach (var h in near)
        {
            if (popups.Count >= maxPopups) break;
            if (popups.Exists(p => p.hazard == h)) continue;
            popups.Add(Open(h));
        }

        // Update or close existing ones.
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            bool gone = p.hazard == null || !p.hazard.active || !p.hazard.gameObject.activeInHierarchy;
            bool far = gone || p.hazard.CurrentDistance > OpenDistance(p.hazard) + closeExtra;
            if (far) { if (p.farSince < 0) p.farSince = Time.unscaledTime; }
            else p.farSince = -1F;
            if (gone || (far && Time.unscaledTime - p.farSince > closeDelay)) { Close(p); popups.RemoveAt(i); continue; }
            UpdatePopup(p, i);
        }
    }

    static bool IsPopupKind(Hazard h) => h.kind == HazardKind.Worker || h.kind == HazardKind.Obstacle || h.kind == HazardKind.Truck;
    float OpenDistance(Hazard h) => h.kind == HazardKind.Worker ? openDistanceWorker : openDistanceObstacle;

    Popup Open(Hazard h)
    {
        var p = new Popup { hazard = h };
        var camGo = new GameObject("PopupCamera_" + h.hazardId);
        camGo.transform.SetParent(transform, false);
        p.cam = camGo.AddComponent<Camera>();
        p.cam.depth = 60;
        p.cam.fieldOfView = 50F;
        p.cam.nearClipPlane = 0.2F;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        p.frame = new GameObject("PopupFrame_" + h.hazardId);
        p.frame.transform.SetParent(transform, false);
        var canvas = p.frame.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;

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
        p.label.text = h.kind == HazardKind.Worker ? "WORKER" : h.kind == HazardKind.Truck ? "TRUCK" : "OBSTACLE";

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
