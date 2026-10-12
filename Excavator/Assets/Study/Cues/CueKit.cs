using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// CUE KIT
// Small drawing helpers shared by the C3 attention cues:
//   - coloured lines in the 3D world (LineRenderer), flat on the ground or facing the camera
//   - a rectangle with a label in the driver view (ScreenBox, Unity UI)
// Colours have one meaning everywhere:
//   yellow = something to look at,  orange = warning,  red = contact / stop,
//   cyan   = guidance (where to drive, where the bucket should go),  green = OK here.
// ---------------------------------------------------------------------------

public static class CueKit
{
    // Objects on this layer are drawn ONLY by the context-aware cameras (side panel and pop-ups),
    // not by the cab camera. Used for the 3D highlights around obstacles. Layer 31 is unused in this project.
    public const int ContextOnlyLayer = 31;
    // Objects on this layer are drawn ONLY by the BUCKET SIDE VIEW camera (C3 digging overlays:
    // bucket trajectory, height arrow, bucket angle). Layer 30 is unused in this project.
    public const int BucketSideOnlyLayer = 30;

    public static readonly Color Look   = new Color(1F, 0.85F, 0.1F);
    public static readonly Color Warn   = new Color(1F, 0.5F, 0.05F);
    public static readonly Color Danger = new Color(1F, 0.12F, 0.1F);
    public static readonly Color Guide  = new Color(0.1F, 0.9F, 1F);
    public static readonly Color Good   = new Color(0.3F, 1F, 0.35F);
    public static readonly Color Bright = new Color(1F, 1F, 1F);

    static Material depthMat, xrayMat;

    // xray = true: drawn on top of everything (also through the ground and the machine).
    public static Material Mat(bool xray)
    {
        if (xray) { if (xrayMat == null) xrayMat = Make(CompareFunction.Always, 3110); return xrayMat; }
        if (depthMat == null) depthMat = Make(CompareFunction.LessEqual, 3100);
        return depthMat;
    }

    static Material Make(CompareFunction z, int queue)
    {
        Shader s = Shader.Find("Study/CueOverlay");
        if (s == null) s = Shader.Find("Sprites/Default");   // fallback: still visible, just without x-ray
        var m = new Material(s) { renderQueue = queue };
        if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", (int)z);
        return m;
    }

    // A line in the world. flat = lies on the ground (width is horizontal), otherwise it faces the camera.
    public static LineRenderer Line(Transform parent, string name, float width, bool xray, bool flat, int layer = 0)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = Mat(xray);
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = 0;
        if (flat)
        {
            lr.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);   // Z up = width lies flat
        }
        return lr;
    }

    public static void Paint(LineRenderer lr, Color c)
    {
        lr.startColor = c;
        lr.endColor = c;
    }

    public static void Hide(LineRenderer lr)
    {
        if (lr != null && lr.positionCount != 0) lr.positionCount = 0;
    }

    public static Color Alpha(Color c, float a) { c.a = a; return c; }

    // Blinks between 'low' and 1 (unscaled time, so it also blinks while paused).
    public static float Pulse(float speed = 6F, float low = 0.45F) =>
        Mathf.Lerp(low, 1F, 0.5F + 0.5F * Mathf.Sin(Time.unscaledTime * speed));

    public static Vector3 Flat(Vector3 v) { v.y = 0F; return v; }

    // A point on the current (possibly dug) ground, lifted a little so it is not hidden in the terrain.
    public static Vector3 OnSurface(Vector3 p, float lift = 0.06F)
    {
        p.y = TerrainDigger.CurrentGroundHeight(p) + lift;
        return p;
    }

    static readonly List<Vector3> tmp = new List<Vector3>();

    // A circle on the ground (follows the terrain).
    public static void Circle(LineRenderer lr, Vector3 center, float radius, float lift = 0.06F, int n = 32)
    {
        tmp.Clear();
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2F / n;
            tmp.Add(OnSurface(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius, lift));
        }
        lr.loop = true;
        lr.positionCount = tmp.Count;
        lr.SetPositions(tmp.ToArray());
    }

    // A flat circle at a fixed height (used under the bucket teeth).
    public static void FlatCircle(LineRenderer lr, Vector3 center, float radius, int n = 28)
    {
        tmp.Clear();
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2F / n;
            tmp.Add(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius);
        }
        lr.loop = true;
        lr.positionCount = tmp.Count;
        lr.SetPositions(tmp.ToArray());
    }

    // The 12 edges of a box drawn as one continuous line (vertical edges are walked twice).
    public static void WireBox(LineRenderer lr, Bounds b)
    {
        Vector3 mn = b.min, mx = b.max;
        Vector3 b0 = new Vector3(mn.x, mn.y, mn.z), b1 = new Vector3(mx.x, mn.y, mn.z), b2 = new Vector3(mx.x, mn.y, mx.z), b3 = new Vector3(mn.x, mn.y, mx.z);
        Vector3 t0 = new Vector3(mn.x, mx.y, mn.z), t1 = new Vector3(mx.x, mx.y, mn.z), t2 = new Vector3(mx.x, mx.y, mx.z), t3 = new Vector3(mn.x, mx.y, mx.z);
        var p = new[] { b0, b1, b2, b3, b0, t0, t1, b1, t1, t2, b2, t2, t3, b3, t3, t0 };
        lr.loop = false;
        lr.positionCount = p.Length;
        lr.SetPositions(p);
    }

    // An arrow head ">" at 'tip' pointing along 'dir', lying in the plane spanned by dir and 'side'.
    public static void Chevron(LineRenderer lr, Vector3 tip, Vector3 dir, Vector3 side, float length, float halfWidth)
    {
        lr.loop = false;
        lr.positionCount = 3;
        lr.SetPosition(0, tip - dir * length + side * halfWidth);
        lr.SetPosition(1, tip);
        lr.SetPosition(2, tip - dir * length - side * halfWidth);
    }

    public static Canvas MakeCanvas(Transform parent, string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = order;
        return c;
    }

    // Projects a world box into the camera's part of the screen (0..1 screen coordinates).
    // Returns false if the box is not inside the camera's view. 'centerVp' is the box centre in the
    // camera's own viewport space (z < 0 = behind the camera), used to place an edge marker instead.
    public static bool ProjectBox(Camera cam, Bounds b, out Rect r, out Vector3 centerVp)
    {
        Rect view = cam.rect;
        centerVp = cam.WorldToViewportPoint(b.center);
        r = default;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        int front = 0;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 vp = cam.WorldToViewportPoint(corner);
            if (vp.z <= 0.05F) continue;
            front++;
            x0 = Mathf.Min(x0, vp.x); y0 = Mathf.Min(y0, vp.y);
            x1 = Mathf.Max(x1, vp.x); y1 = Mathf.Max(y1, vp.y);
        }
        if (front == 0 || centerVp.z <= 0F) return false;

        Rect s = Rect.MinMaxRect(view.xMin + x0 * view.width, view.yMin + y0 * view.height,
                                 view.xMin + x1 * view.width, view.yMin + y1 * view.height);
        // Small padding, and a minimum size so far-away objects still get a readable box.
        float padX = 6F / Mathf.Max(1, Screen.width), padY = 6F / Mathf.Max(1, Screen.height);
        float minW = 0.025F, minH = 0.04F;
        float cx = s.center.x, cy = s.center.y;
        float w = Mathf.Max(s.width + 2 * padX, minW), h = Mathf.Max(s.height + 2 * padY, minH);
        s = new Rect(cx - w / 2F, cy - h / 2F, w, h);

        Rect c = Rect.MinMaxRect(Mathf.Max(s.xMin, view.xMin), Mathf.Max(s.yMin, view.yMin),
                                 Mathf.Min(s.xMax, view.xMax), Mathf.Min(s.yMax, view.yMax));
        if (c.width < 0.004F || c.height < 0.004F) return false;
        r = c;
        return true;
    }
}

// A rectangle with a small label tag, drawn on top of the screen (driver view cues).
// The same object can also show only the tag (for an off-screen marker or a text banner).
public class ScreenBox
{
    readonly GameObject root;
    readonly RectTransform box, tag;
    readonly Image[] edges = new Image[4];
    readonly Image tagBg;
    readonly Text text;
    readonly int fontSize;

    public ScreenBox(Transform canvas, Font font, string name, int fontSize = 16, float thickness = 3F)
    {
        this.fontSize = fontSize;
        root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(canvas, false);
        box = root.GetComponent<RectTransform>();
        edges[0] = Edge(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thickness), new Vector2(0.5F, 0.5F));   // top
        edges[1] = Edge(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thickness), new Vector2(0.5F, 0.5F));   // bottom
        edges[2] = Edge(new Vector2(0, 0), new Vector2(0, 1), new Vector2(thickness, 0), new Vector2(0.5F, 0.5F));   // left
        edges[3] = Edge(new Vector2(1, 0), new Vector2(1, 1), new Vector2(thickness, 0), new Vector2(0.5F, 0.5F));   // right

        var tagGo = new GameObject("Tag", typeof(RectTransform), typeof(Image));
        tagGo.transform.SetParent(box, false);
        tag = tagGo.GetComponent<RectTransform>();
        tagBg = tagGo.GetComponent<Image>();
        var tGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        tGo.transform.SetParent(tag, false);
        var trt = tGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8, 2); trt.offsetMax = new Vector2(-8, -2);
        text = tGo.GetComponent<Text>();
        text.font = font; text.fontSize = fontSize; text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        root.SetActive(false);
    }

    Image Edge(Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 pivot)
    {
        var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(box, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot; rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
        return go.GetComponent<Image>();
    }

    // Box around screen rect r (0..1 screen coordinates) with the label above its top-left corner.
    public void ShowBox(Rect r, Color c, string label)
    {
        if (!root.activeSelf) root.SetActive(true);
        box.anchorMin = r.min; box.anchorMax = r.max;
        box.offsetMin = box.offsetMax = Vector2.zero;
        foreach (var e in edges) { e.enabled = true; e.color = c; }
        // Label above the box; inside the box if there is no room above.
        bool below = r.yMax > 0.93F;
        tag.anchorMin = tag.anchorMax = new Vector2(0, 1);
        tag.pivot = below ? new Vector2(0, 1) : new Vector2(0, 0);
        tag.anchoredPosition = below ? new Vector2(3, -3) : new Vector2(-2, 2);
        SetText(c, label);
    }

    // Only the label, placed at screen point p (0..1) with the given pivot (e.g. (0,0.5) = left-middle).
    public void ShowTag(Vector2 p, Vector2 pivot, Color c, string label)
    {
        if (!root.activeSelf) root.SetActive(true);
        box.anchorMin = box.anchorMax = p;
        box.offsetMin = box.offsetMax = Vector2.zero;
        foreach (var e in edges) e.enabled = false;
        tag.anchorMin = tag.anchorMax = Vector2.zero;
        tag.pivot = pivot;
        tag.anchoredPosition = Vector2.zero;
        SetText(c, label);
    }

    void SetText(Color c, string label)
    {
        tagBg.color = new Color(c.r, c.g, c.b, 0.9F * c.a);
        text.color = new Color(0, 0, 0, Mathf.Max(0.6F, c.a));
        if (text.text != label) text.text = label;
        string[] lines = label.Split('\n');
        int longest = 0;
        foreach (var l in lines) longest = Mathf.Max(longest, l.Length);
        tag.sizeDelta = new Vector2(longest * fontSize * 0.62F + 18F, lines.Length * (fontSize + 5F) + 6F);
    }

    public void Hide() { if (root.activeSelf) root.SetActive(false); }
    public void Destroy() { if (root != null) Object.Destroy(root); }
}
