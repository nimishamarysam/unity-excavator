using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// PROXIMITY WIDGET  (screen-space UI, used for buried services)
// A compact "parking sensor": short name, five segments and one number, e.g.  PIPE [#][#][#][ ][ ] 0.3 m
//   - the closer the bucket, the more segments light up (left to right);
//   - lit segments are coloured by their position, green -> yellow -> orange -> red, unlit ones are dark;
//   - at contact the lit segments blink.
// Replaces long written labels: the bar is read at a glance, the number is there if needed.
// ---------------------------------------------------------------------------

public class ProximityWidget
{
    const int Segments = 5;
    static readonly Color[] SegCol =
    {
        new Color(0.3F, 0.95F, 0.35F), new Color(0.85F, 0.95F, 0.2F), new Color(1F, 0.8F, 0.1F),
        new Color(1F, 0.5F, 0.05F), new Color(1F, 0.15F, 0.1F)
    };
    static readonly Color Off = new Color(0.25F, 0.25F, 0.27F, 0.9F);

    readonly GameObject root;
    readonly RectTransform rt;
    readonly Image bg;
    readonly Text name, dist;
    readonly Image[] seg = new Image[Segments];
    readonly RectTransform arrowRt;   // optional direction arrow (e.g. where a worker walks), at the right end
    static Sprite arrowSprite;

    public const float Width = 190F, Height = 24F;

    public ProximityWidget(Transform canvas, Font font, string objName)
    {
        root = new GameObject(objName, typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas, false);
        rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(Width, Height);
        bg = root.GetComponent<Image>();
        bg.color = new Color(0.05F, 0.05F, 0.06F, 0.8F);
        bg.raycastTarget = false;

        name = MakeText(font, "Name", TextAnchor.MiddleLeft, 6F, 62F);
        for (int i = 0; i < Segments; i++)
        {
            var g = new GameObject("Seg" + i, typeof(RectTransform), typeof(Image));
            g.transform.SetParent(root.transform, false);
            var r = g.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0F, 0.5F);
            r.pivot = new Vector2(0F, 0.5F);
            r.sizeDelta = new Vector2(12F, 14F);
            r.anchoredPosition = new Vector2(68F + i * 15F, 0F);
            seg[i] = g.GetComponent<Image>();
            seg[i].raycastTarget = false;
        }
        dist = MakeText(font, "Dist", TextAnchor.MiddleRight, 145F, 40F);
        var ag = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
        ag.transform.SetParent(root.transform, false);
        arrowRt = ag.GetComponent<RectTransform>();
        arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(0F, 0.5F);
        arrowRt.sizeDelta = new Vector2(16F, 16F);
        arrowRt.anchoredPosition = new Vector2(Width + 10F, 0F);
        var ai = ag.GetComponent<Image>();
        ai.sprite = ArrowSprite; ai.raycastTarget = false;
        ag.SetActive(false);
        root.SetActive(false);
    }

    Text MakeText(Font font, string n, TextAnchor align, float x, float w)
    {
        var g = new GameObject(n, typeof(RectTransform), typeof(Text));
        g.transform.SetParent(root.transform, false);
        var r = g.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0F, 0.5F);
        r.pivot = new Vector2(0F, 0.5F);
        r.sizeDelta = new Vector2(w, Height);
        r.anchoredPosition = new Vector2(x, 0F);
        var t = g.GetComponent<Text>();
        t.font = font; t.fontSize = 14; t.fontStyle = FontStyle.Bold; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
        return t;
    }

    // p = screen point (0..1), pivot as for a RectTransform. range = distance at which the first segment lights.
    // showBar = false: only the name (condition C2, which has no proximity guidance).
    // arrowDeg: screen direction of an arrow (0 = right, 90 = up), NaN = no arrow.
    public void Show(Vector2 p, Vector2 pivot, string label, Color labelCol, float distance, float range, bool contact,
                     bool showBar = true, float arrowDeg = float.NaN)
    {
        if (!root.activeSelf) root.SetActive(true);
        bool arrow = !float.IsNaN(arrowDeg) && showBar;
        rt.sizeDelta = new Vector2(showBar ? Width + (arrow ? 20F : 0F) : 66F, Height);
        arrowRt.gameObject.SetActive(arrow);
        if (arrow) { arrowRt.localEulerAngles = new Vector3(0F, 0F, arrowDeg); arrowRt.GetComponent<Image>().color = new Color(labelCol.r, labelCol.g, labelCol.b, 1F); }
        dist.gameObject.SetActive(showBar);
        foreach (var sg in seg) sg.gameObject.SetActive(showBar);
        rt.anchorMin = rt.anchorMax = p;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        name.text = label; name.color = new Color(labelCol.r, labelCol.g, labelCol.b, 1F);
        dist.text = $"{distance:0.0} m"; dist.color = Color.white;
        int lit = distance >= range ? 0 : Mathf.Clamp(Mathf.CeilToInt((1F - distance / range) * Segments), 1, Segments);
        float blink = contact ? CueKit.Pulse(10F, 0.25F) : 1F;
        for (int i = 0; i < Segments; i++)
        {
            Color c = i < lit ? SegCol[i] : Off;
            if (i < lit) c.a = blink;
            seg[i].color = c;
        }
    }

    public void Hide() { if (root.activeSelf) root.SetActive(false); }

    // A white arrow pointing right (rotated by Show), drawn in code.
    static Sprite ArrowSprite
    {
        get
        {
            if (arrowSprite != null) return arrowSprite;
            const int N = 32;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5F, fy = y + 0.5F - N / 2F;
                    bool head = fx >= 16F && fx <= 30F && Mathf.Abs(fy) <= (30F - fx) * 0.85F;
                    bool shaft = fx >= 3F && fx < 17F && Mathf.Abs(fy) <= 3.5F;
                    px[y * N + x] = head || shaft ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply();
            return arrowSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5F, 0.5F), 100F);
        }
    }
}
