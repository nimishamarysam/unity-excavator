using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// MINI MAP
// A small top-down sketch drawn with Unity's IMGUI. Used twice in Dig and dump:
//   * DETAILED site map on the task instruction (briefing) screen: pit, DIG / DUMP boxes, truck, lane,
//     barriers, power line - so the participant knows the layout before starting.
//   * SIMPLE line map in the task panel while working: only the lane as a line, a "you are here" dot
//     and a pulsing destination marker (DUMP box when reversing, DIG box when driving forward).
// It is part of the task description, so it is the same in all conditions.
// Map orientation: "up" = the machine's forward direction at the start, "right" = its right side.
// ---------------------------------------------------------------------------

public class MiniMap
{
    public enum Kind { Fill, Outline, Line, Dot, Label }

    public struct Item
    {
        public Kind kind;
        public Vector3 a, b;          // centre (Fill/Outline/Dot/Label) or line ends
        public Vector2 size;          // length along "up", width along "right" (Fill/Outline); radius in x (Dot)
        public Color color;
        public string text;
    }

    public readonly List<Item> Items = new List<Item>();
    public Vector3 Up = Vector3.right;          // world direction drawn upwards
    public float LineWidth = 3F;
    public int FontSize = 12;
    public Color Background = new Color(0.16F, 0.17F, 0.15F, 0.9F);

    // Live layer (optional): machine position and current destination, read every frame.
    public System.Func<Vector3> Here;
    public System.Func<Vector3> Target;
    public System.Func<string> TargetLabel;

    Vector3 Right => Vector3.Cross(Vector3.up, Up);

    public void Rect(Vector3 c, float alongUp, float alongRight, Color col, bool filled = true) =>
        Items.Add(new Item { kind = filled ? Kind.Fill : Kind.Outline, a = c, size = new Vector2(alongUp, alongRight), color = col });
    public void Line(Vector3 a, Vector3 b, Color col) => Items.Add(new Item { kind = Kind.Line, a = a, b = b, color = col });
    public void Dot(Vector3 c, float radius, Color col) => Items.Add(new Item { kind = Kind.Dot, a = c, size = new Vector2(radius, 0), color = col });
    public void Label(Vector3 c, string text, Color col) => Items.Add(new Item { kind = Kind.Label, a = c, text = text, color = col });

    static GUIStyle labelStyle;
    static Texture2D disc, ring;

    static Texture2D Circle(bool filled)
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        float rOut = n / 2F - 1F, rIn = filled ? -1F : rOut - 9F;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x + 0.5F - n / 2F, y + 0.5F - n / 2F).magnitude;
                float a = Mathf.Clamp01(rOut - d + 0.5F) * (filled ? 1F : Mathf.Clamp01(d - rIn + 0.5F));
                px[y * n + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // Draws the map fitted into r (screen pixels).
    public void Draw(Rect r)
    {
        if (Items.Count == 0) return;
        if (disc == null) disc = Circle(true);
        if (ring == null) ring = Circle(false);
        Vector3 up = new Vector3(Up.x, 0, Up.z).normalized, right = Right;
        Vector2 Map(Vector3 p) => new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up));

        // Bounds of the static items (in map coordinates, metres). The live dot does not move the map.
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        void Grow(Vector2 q) { x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x); y0 = Mathf.Min(y0, q.y); y1 = Mathf.Max(y1, q.y); }
        foreach (var it in Items)
        {
            var c = Map(it.a);
            switch (it.kind)
            {
                case Kind.Fill: case Kind.Outline:
                    Grow(c + new Vector2(-it.size.y / 2, -it.size.x / 2)); Grow(c + new Vector2(it.size.y / 2, it.size.x / 2)); break;
                case Kind.Line: Grow(c); Grow(Map(it.b)); break;
                default: Grow(c); break;
            }
        }
        float pad = 10F + FontSize * 0.5F;
        float s = Mathf.Min((r.width - 2 * pad) / Mathf.Max(1F, x1 - x0), (r.height - 2 * pad) / Mathf.Max(1F, y1 - y0));
        Vector2 mid = new Vector2((x0 + x1) / 2F, (y0 + y1) / 2F);
        Vector2 Screen(Vector3 p) { var q = Map(p) - mid; return new Vector2(r.center.x + q.x * s, r.center.y - q.y * s); }

        GUI.color = Background;
        GUI.DrawTexture(r, Texture2D.whiteTexture);

        if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
        labelStyle.fontSize = FontSize;

        foreach (var it in Items)
        {
            GUI.color = it.color;
            switch (it.kind)
            {
                case Kind.Fill:
                case Kind.Outline:
                {
                    Vector2 c = Screen(it.a);
                    float w = it.size.y * s, h = it.size.x * s;
                    var box = new Rect(c.x - w / 2, c.y - h / 2, w, h);
                    if (it.kind == Kind.Fill) GUI.DrawTexture(box, Texture2D.whiteTexture);
                    else
                    {
                        float t = 2F;
                        GUI.DrawTexture(new Rect(box.x, box.y, box.width, t), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(box.x, box.yMax - t, box.width, t), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(box.x, box.y, t, box.height), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(box.xMax - t, box.y, t, box.height), Texture2D.whiteTexture);
                    }
                    break;
                }
                case Kind.Line:
                {
                    Vector2 a = Screen(it.a), b = Screen(it.b);
                    float len = (b - a).magnitude, ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                    var m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(ang, a);
                    GUI.DrawTexture(new Rect(a.x, a.y - LineWidth / 2F, len, LineWidth), Texture2D.whiteTexture);
                    GUI.matrix = m;
                    // round joints
                    GUI.DrawTexture(new Rect(a.x - LineWidth / 2F, a.y - LineWidth / 2F, LineWidth, LineWidth), disc);
                    GUI.DrawTexture(new Rect(b.x - LineWidth / 2F, b.y - LineWidth / 2F, LineWidth, LineWidth), disc);
                    break;
                }
                case Kind.Dot:
                {
                    Vector2 c = Screen(it.a);
                    float d = Mathf.Max(6F, it.size.x * 2F * s);
                    GUI.DrawTexture(new Rect(c.x - d / 2, c.y - d / 2, d, d), disc);
                    break;
                }
                case Kind.Label:
                {
                    Vector2 c = Screen(it.a);
                    labelStyle.normal.textColor = it.color;
                    GUI.color = Color.white;
                    GUI.Label(new Rect(c.x - 80, c.y - FontSize, 160, FontSize * 2), it.text, labelStyle);
                    break;
                }
            }
        }

        // Destination: pulsing ring + its name.
        if (Target != null)
        {
            Vector2 c = Screen(Target());
            float d = 16F + 6F * Mathf.Sin(Time.unscaledTime * 5F);
            GUI.color = new Color(1F, 0.85F, 0.1F);
            GUI.DrawTexture(new Rect(c.x - d / 2, c.y - d / 2, d, d), ring);
            if (TargetLabel != null)
            {
                labelStyle.normal.textColor = new Color(1F, 0.85F, 0.1F);
                GUI.color = Color.white;
                GUI.Label(new Rect(c.x - 80, c.y + 9F, 160, FontSize * 2), TargetLabel(), labelStyle);
            }
        }

        // You are here: white-rimmed cyan dot (kept inside the map if the machine is off the lane area).
        if (Here != null)
        {
            Vector2 c = Screen(Here());
            c.x = Mathf.Clamp(c.x, r.x + 6F, r.xMax - 6F);
            c.y = Mathf.Clamp(c.y, r.y + 6F, r.yMax - 6F);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(c.x - 7F, c.y - 7F, 14F, 14F), disc);
            GUI.color = new Color(0.1F, 0.8F, 1F);
            GUI.DrawTexture(new Rect(c.x - 5F, c.y - 5F, 10F, 10F), disc);
            labelStyle.normal.textColor = Color.white;
            GUI.color = Color.white;
            GUI.Label(new Rect(c.x - 80, c.y - 9F - FontSize * 1.6F, 160, FontSize * 2), "YOU", labelStyle);
        }
        GUI.color = Color.white;
    }
}
