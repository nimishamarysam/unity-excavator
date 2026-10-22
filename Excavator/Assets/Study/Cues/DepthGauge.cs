using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// DEPTH GAUGE  (C3, driver view, digging)  - replaces the floating "0.09 m deep (target 1.20)" text,
// which covered the pit. A vertical gauge like the grade indicator of a machine-control system, placed
// BESIDE the dig area (left of its far-left corner, pinned to the trench: it does not move with the arm),
// so it is read without looking away from the work and does not cover the pit. Falls back to the right
// edge of the driver view when that corner is not in view.
//   - top line = original ground (0 m), scale downwards to target + 0.5 m;
//   - GREEN band = target depth (from atGradeBelow above to overDigTolerance below the target);
//     RED zone below it = too deep;
//   - a cyan fill from the top down to the teeth and a white pointer at the teeth depth; the pointer turns
//     green in the target band and red when too deep. Above the ground the pointer waits at the top (grey);
//   - one small number below: the current depth ("0.09 m"); "+0.30 m" above the ground.
// ---------------------------------------------------------------------------

public class DepthGauge
{
    const float W = 22F, H = 160F;
    readonly GameObject root;
    readonly RectTransform rt, fill, band, red, pointer;
    readonly Image pointerImg;
    readonly Text value, targetText;

    public DepthGauge(Transform canvas, Font font)
    {
        root = new GameObject("CueDepthGauge", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas, false);
        rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(W, H);
        var bg = root.GetComponent<Image>(); bg.color = new Color(0.08F, 0.08F, 0.09F, 0.75F); bg.raycastTarget = false;
        red = Bar("TooDeep", new Color(1F, 0.15F, 0.1F, 0.55F), out _);
        band = Bar("Target", new Color(0.3F, 0.95F, 0.35F, 0.85F), out _);
        fill = Bar("Dug", new Color(0.1F, 0.9F, 1F, 0.45F), out _);
        pointer = Bar("Pointer", Color.white, out pointerImg);
        var ground = Bar("Ground", Color.white, out _);
        ground.anchoredPosition = Vector2.zero; ground.sizeDelta = new Vector2(W + 8F, 3F);
        value = Label(font, "Value", 14, new Vector2(0.5F, 1F));
        value.rectTransform.anchorMin = value.rectTransform.anchorMax = new Vector2(0.5F, 0F);
        value.rectTransform.anchoredPosition = new Vector2(0F, -4F);
        targetText = Label(font, "TargetValue", 12, new Vector2(1F, 0.5F));
        root.SetActive(false);
    }

    RectTransform Bar(string n, Color c, out Image img)
    {
        var g = new GameObject(n, typeof(RectTransform), typeof(Image));
        g.transform.SetParent(root.transform, false);
        var r = g.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5F, 1F);
        r.pivot = new Vector2(0.5F, 1F);
        img = g.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
        return r;
    }

    Text Label(Font font, string n, int size, Vector2 pivot)
    {
        var g = new GameObject(n, typeof(RectTransform), typeof(Text));
        g.transform.SetParent(root.transform, false);
        var t = g.GetComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = FontStyle.Bold; t.color = Color.white; t.raycastTarget = false;
        t.alignment = TextAnchor.MiddleCenter; t.horizontalOverflow = HorizontalWrapMode.Overflow;
        g.AddComponent<Outline>().effectColor = new Color(0F, 0F, 0F, 0.9F);
        var r = t.rectTransform; r.pivot = pivot; r.sizeDelta = new Vector2(70F, 18F);
        return t;
    }

    // Fallback position: fixed at the right edge of the driver view.
    public void Show(Rect view, float depth, float target, float atGradeBelow, float overDigTolerance) =>
        ShowAt(new Vector2(view.xMax - 0.012F, view.yMin + 0.55F * view.height), new Vector2(1F, 0.5F), depth, target, atGradeBelow, overDigTolerance);

    // At a screen point (0..1) with the given pivot, e.g. beside the dig area.
    // depth = teeth below the original ground (negative = above the ground).
    public void ShowAt(Vector2 p, Vector2 pivot, float depth, float target, float atGradeBelow, float overDigTolerance)
    {
        if (!root.activeSelf) root.SetActive(true);
        rt.anchorMin = rt.anchorMax = p;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;

        float max = target + 0.5F;
        float Y(float d) => -Mathf.Clamp01(d / max) * H;   // depth -> pixels from the top
        Place(band, Y(target - atGradeBelow), Y(target + overDigTolerance), W);
        Place(red, Y(target + overDigTolerance), -H, W);
        bool above = depth < 0F;
        Place(fill, 0F, above ? 0F : Y(depth), W - 10F);

        bool tooDeep = depth > target + overDigTolerance, atGrade = !tooDeep && depth >= target - atGradeBelow;
        pointerImg.color = above ? new Color(0.7F, 0.7F, 0.7F) : tooDeep ? new Color(1F, 0.15F, 0.1F) : atGrade ? new Color(0.3F, 1F, 0.35F) : Color.white;
        pointer.sizeDelta = new Vector2(W + 14F, 5F);
        pointer.anchoredPosition = new Vector2(0F, (above ? 0F : Y(depth)) + 2.5F);

        value.text = above ? $"+{-depth:0.00} m" : $"{depth:0.00} m";
        value.color = pointerImg.color;
        targetText.text = $"{target:0.0}";
        targetText.rectTransform.anchorMin = targetText.rectTransform.anchorMax = new Vector2(0F, 1F);
        targetText.rectTransform.anchoredPosition = new Vector2(-4F, Y(target));
    }

    static void Place(RectTransform r, float yTop, float yBottom, float width)
    {
        r.anchoredPosition = new Vector2(0F, yTop);
        r.sizeDelta = new Vector2(width, Mathf.Max(0F, yTop - yBottom));
    }

    public void Hide() { if (root.activeSelf) root.SetActive(false); }
}
