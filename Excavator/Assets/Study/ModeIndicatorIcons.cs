using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// MODE INDICATOR ICONS (all scenarios)
// The two mode squares (TravelIndicator / OperationIndicator, top right) were plain black / green squares.
// Each now carries an icon so the mode can be told apart without remembering which square is which:
//     TRAVEL = two crawler tracks seen from above with an arrow (driving)
//     DIG    = excavator bucket with teeth on the end of the arm (digging)
// Active mode: green square, dark icon. Inactive: dark square, grey icon. The icons are drawn in code
// (no image assets). ExcavatorController calls Setup once and Apply when the mode changes.
//
// 2026-11-01: the two squares in the scene (top right) are HIDDEN. The mode is shown only as text
// (DRIVE / DIG) in the MODE cell of the status bar (MeasurementBar), no icons.
// Set ShowSceneIcons = true to bring the top-right squares back.
// ---------------------------------------------------------------------------

public static class ModeIndicatorIcons
{
    const int N = 64;
    static Sprite travelSprite, digSprite;
    static readonly Color ActiveBg = new Color(0.15F, 0.85F, 0.25F), InactiveBg = new Color(0.12F, 0.12F, 0.13F);
    static readonly Color ActiveIcon = new Color(0.05F, 0.1F, 0.05F), InactiveIcon = new Color(0.6F, 0.6F, 0.62F);

    // false = the top-right squares in the scene are hidden (mode shown in the status bar instead).
    public static bool ShowSceneIcons = false;

    // Icons for other UI (status bar MODE cell).
    public static Sprite TravelIcon => TravelSprite;
    public static Sprite DigIcon => DigSprite;

    // Colours one square (background) and its icon for the active / inactive state.
    public static void PaintSquare(Image bg, Image icon, bool active)
    {
        if (bg != null) bg.color = active ? ActiveBg : InactiveBg;
        if (icon != null) icon.color = active ? ActiveIcon : InactiveIcon;
    }

    public static void Setup(Image travel, Image operation)
    {
        if (!ShowSceneIcons)
        {
            if (travel != null) travel.gameObject.SetActive(false);
            if (operation != null) operation.gameObject.SetActive(false);
            return;
        }
        AddIcon(travel, TravelSprite);
        AddIcon(operation, DigSprite);
    }

    public static void Apply(Image travel, Image operation, bool travelMode)
    {
        Paint(travel, travelMode);
        Paint(operation, !travelMode);
    }

    static void Paint(Image bg, bool active)
    {
        if (bg == null) return;
        bg.color = active ? ActiveBg : InactiveBg;
        var icon = bg.transform.Find("ModeIcon");
        if (icon != null) icon.GetComponent<Image>().color = active ? ActiveIcon : InactiveIcon;
    }

    static void AddIcon(Image bg, Sprite sprite)
    {
        if (bg == null || bg.transform.Find("ModeIcon") != null) return;
        var go = new GameObject("ModeIcon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(bg.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.1F, 0.1F); rt.anchorMax = new Vector2(0.9F, 0.9F);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    // ---- icon drawing (white shapes on transparent, y = 0 at the bottom) ----

    static Sprite TravelSprite
    {
        get
        {
            if (travelSprite != null) return travelSprite;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool on = false;
                    // two tracks with tread gaps
                    foreach (int x0 in new[] { 6, 44 })
                        if (x >= x0 && x < x0 + 14 && y >= 4 && y < 60 && !((y - 4) % 8 >= 6 && x > x0 + 2 && x < x0 + 11)) on = true;
                    // arrow between them: shaft + head (pointing forward / up)
                    if (x >= 29 && x < 35 && y >= 10 && y < 40) on = true;
                    if (y >= 38 && y < 56 && Mathf.Abs(x - 31.5F) <= (56 - y) * 0.55F) on = true;
                    px[y * N + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            return travelSprite = Make(px, "TravelIcon");
        }
    }

    static Sprite DigSprite
    {
        get
        {
            if (digSprite != null) return digSprite;
            var px = new Color32[N * N];
            Vector2 c = new Vector2(38F, 36F);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool on = false;
                    Vector2 p = new Vector2(x + 0.5F, y + 0.5F);
                    // bucket: lower-left quarter disc (back and bottom curve), open at the top right
                    float r = (p - c).magnitude;
                    if (p.x <= c.x && p.y <= c.y && r <= 28F) on = true;
                    // a hollow (the inside of the bucket) so it reads as a scoop, not a blob
                    if (p.x <= c.x - 5F && p.y <= c.y - 1F && p.y >= c.y - 17F && r <= 19F && p.x > c.x - 17F) on = false;
                    // three teeth sticking out to the right from the cutting edge (bottom right)
                    for (int k = 0; k < 3; k++)
                    {
                        float ty = 9F + k * 7F;
                        if (p.x > c.x && p.x < c.x + 10F && Mathf.Abs(p.y - ty) <= (c.x + 10F - p.x) * 0.3F) on = true;
                    }
                    // arm: thick line from the bucket pivot up to the top right corner
                    Vector2 a = c, b = new Vector2(60F, 60F), ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    if ((a + ab * t - p).magnitude <= 4F) on = true;
                    px[y * N + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            return digSprite = Make(px, "DigIcon");
        }
    }

    static Sprite Make(Color32[] px, string name)
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5F, 0.5F), 100F);
    }
}
