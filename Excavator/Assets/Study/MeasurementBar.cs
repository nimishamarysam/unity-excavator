using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// STATIC MEASUREMENT BAR (shown in all three conditions)
// A dark strip along the bottom of the screen with plain numeric readouts.
// "Static" means it never changes colour, flashes or moves - it only shows numbers,
// so it is the same in every condition.
// The whole UI is built from code, so nothing has to be set up in the scene.
// ---------------------------------------------------------------------------

public class MeasurementBar : MonoBehaviour
{
    public float barHeight = 72F;          // in pixels on a 1920x1080 reference screen
    public float refreshRateHz = 10F;      // numbers update 10 times per second (easier to read)

    GameObject canvasGo;
    Text mode, swing, boom, arm, bucket, height, tilt, speed, loadText;
    BucketLoad load;   // the scenario's bucket load (found when needed; changes every trial)
    float nextRefresh;
    static readonly CultureInfo inv = CultureInfo.InvariantCulture;

    void Start()
    {
        BuildUI();
    }

    void BuildUI()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Canvas = the "sheet" all UI is drawn on. Overlay = drawn on top of every camera.
        canvasGo = new GameObject("MeasurementBarCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5F;

        // Background strip anchored to the bottom edge, full width.
        var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        bar.transform.SetParent(canvasGo.transform, false);
        var rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5F, 0);
        rt.sizeDelta = new Vector2(0, barHeight);
        bar.GetComponent<Image>().color = new Color(0.08F, 0.09F, 0.10F, 0.85F);
        var layout = bar.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 8, 8);
        layout.spacing = 12;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        mode   = Cell(bar.transform, font, "MODE");
        swing  = Cell(bar.transform, font, "SWING");
        boom   = Cell(bar.transform, font, "BOOM");
        arm    = Cell(bar.transform, font, "ARM");
        bucket = Cell(bar.transform, font, "BUCKET");
        height = Cell(bar.transform, font, "BUCKET HEIGHT");
        loadText = Cell(bar.transform, font, "BUCKET LOAD");
        tilt   = Cell(bar.transform, font, "TILT");
        speed  = Cell(bar.transform, font, "SPEED");

        canvasGo.SetActive(false);
    }

    // One readout: a small grey label on top and a large white value below. Returns the value text.
    Text Cell(Transform parent, Font font, string label)
    {
        var cell = new GameObject(label, typeof(RectTransform), typeof(VerticalLayoutGroup));
        cell.transform.SetParent(parent, false);
        var v = cell.GetComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.MiddleCenter;
        v.childForceExpandHeight = false;

        MakeText(cell.transform, font, label, 15, new Color(0.65F, 0.68F, 0.72F));
        return MakeText(cell.transform, font, "-", 28, Color.white);
    }

    Text MakeText(Transform parent, Font font, string s, int size, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.text = s;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    void Update()
    {
        var sm = StudyManager.Instance;
        bool visible = sm != null &&
            (sm.CurrentState == StudyManager.State.Running || sm.CurrentState == StudyManager.State.Paused);
        if (canvasGo != null && canvasGo.activeSelf != visible) canvasGo.SetActive(visible);
        if (!visible) return;

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1F / refreshRateHz;

        var ctl = sm.Controller;
        if (ctl == null || ctl.Machine == null) return;
        var m = ctl.Machine;

        mode.text   = ctl.IsTravelMode ? "DRIVE" : "DIG";
        swing.text  = Deg(m.swingAngle);
        boom.text   = Deg(m.boomAngle);
        arm.text    = Deg(m.armAngle);
        bucket.text = Deg(m.bucketAngle);
        height.text = m.BucketHeightAboveGround.ToString("+0.00;-0.00", inv) + " m";
        tilt.text   = Deg(m.TiltAngle);
        speed.text  = m.SpeedKmh.ToString("0.0", inv) + " km/h";

        // How full the bucket is, 0-100 % (same scale the scenarios use to count a scoop / a load).
        if (load == null) load = FindAnyObjectByType<BucketLoad>();
        loadText.text = load == null ? "-" : Mathf.RoundToInt(load.Fill * 100F).ToString(inv) + " %";

    }

    static string Deg(float v) => v.ToString("0", inv) + "°";
}
