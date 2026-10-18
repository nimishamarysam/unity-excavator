using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ---------------------------------------------------------------------------
// INCIDENT FEEDBACK  (all scenarios, ALL conditions - it is the consequence of an error, not a cue)
// When something happens that should not happen, the participant gets immediate feedback:
//   - a red flash around the driver view,
//   - a red message in the middle of the driver view (e.g. "PIPE CUT", "WORKER HIT"), 2.5 s.
// (No sound.)
// Triggered by the study's own log events (contacts, strikes, spills, over-digging, ...), so what the
// participant sees is exactly what is recorded. Each message is also logged as "incident_shown".
// The same incident is not repeated within 2 s (e.g. a bucket resting against the truck wall).
// ---------------------------------------------------------------------------

public class IncidentFeedback : MonoBehaviour
{
    public float showSeconds = 2.5F;
    public float repeatBlockSeconds = 2F;

    GameObject canvasGo;
    readonly Image[] border = new Image[4];
    Image banner;
    Text bannerText;
    float shownAt = -100F;
    string current = "";
    int repeats;
    readonly Dictionary<string, float> lastShown = new Dictionary<string, float>();

    void OnEnable() { StudyLogger.Logged += OnLogged; }
    void OnDisable() { StudyLogger.Logged -= OnLogged; }

    void Start()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvasGo = new GameObject("IncidentCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 70;   // above everything else

        Rect v = StudyLayout.DriverView;
        float t = 0.012F;
        border[0] = Box(new Vector2(v.xMin, v.yMax - t), new Vector2(v.xMax, v.yMax));   // top
        border[1] = Box(new Vector2(v.xMin, v.yMin), new Vector2(v.xMax, v.yMin + t));   // bottom
        border[2] = Box(new Vector2(v.xMin, v.yMin), new Vector2(v.xMin + t * 0.6F, v.yMax));   // left
        border[3] = Box(new Vector2(v.xMax - t * 0.6F, v.yMin), new Vector2(v.xMax, v.yMax));   // right

        banner = Box(new Vector2(v.center.x - 0.2F, v.yMin + v.height * 0.55F), new Vector2(v.center.x + 0.2F, v.yMin + v.height * 0.55F + 0.075F));
        var tGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        tGo.transform.SetParent(banner.transform, false);
        var rt = tGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        bannerText = tGo.GetComponent<Text>();
        bannerText.font = font; bannerText.fontSize = 34; bannerText.fontStyle = FontStyle.Bold;
        bannerText.alignment = TextAnchor.MiddleCenter; bannerText.color = Color.white;
        bannerText.horizontalOverflow = HorizontalWrapMode.Overflow;

        canvasGo.SetActive(false);
    }

    Image Box(Vector2 min, Vector2 max)
    {
        var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvasGo.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go.GetComponent<Image>();
    }

    // Which log events are incidents, and the message to show.
    static string Message(string evt, string detail)
    {
        switch (evt)
        {
            case "hazard_contact":
            {
                string[] p = detail.Split(';');
                string kind = p.Length > 1 ? p[1] : "";
                switch (kind)
                {
                    case "Worker": return "WORKER HIT!";
                    case "Obstacle": return "OBSTACLE HIT";
                    case "BuriedCable": return "CABLE STRUCK";
                    case "BuriedPipe": return "PIPE CUT";
                    case "PowerLine": return "POWER LINE CONTACT!";
                    case "Truck": return "TRUCK HIT";
                    case "TrenchEdge": return "TOO CLOSE TO THE TRENCH EDGE";
                    case "Boulder": return "BOULDER HIT";
                    case "Wall": return "WALL HIT";
                    default: return "CONTACT";
                }
            }
            case "bucket_blocked": return "BUCKET HIT THE TRUCK";
            case "spill": return detail.StartsWith("too_close") ? "SOIL DUMPED TOO CLOSE TO THE TRENCH" : "SOIL SPILLED";
            case "over_dig": return "DUG TOO DEEP";
            case "cut_outside_zone": return "DIGGING OUTSIDE THE MARKED AREA";
            case "bucket_over_worker": return "BUCKET OVER A WORKER";
            default: return null;
        }
    }

    void OnLogged(string evt, string detail)
    {
        var sm = StudyManager.Instance;
        if (sm == null || !sm.IsTrialRunning || canvasGo == null) return;
        string msg = Message(evt, detail);
        if (msg == null) return;

        float now = Time.unscaledTime;
        if (lastShown.TryGetValue(msg, out float last) && now - last < repeatBlockSeconds) return;
        lastShown[msg] = now;

        repeats = msg == current && now - shownAt < showSeconds ? repeats + 1 : 1;
        current = msg;
        shownAt = now;
        bannerText.text = repeats > 1 ? $"{msg}  (x{repeats})" : msg;
        float w = Mathf.Max(0.22F, bannerText.text.Length * 0.0115F);
        var brt = banner.rectTransform;
        Rect v = StudyLayout.DriverView;
        brt.anchorMin = new Vector2(v.center.x - w / 2F, brt.anchorMin.y);
        brt.anchorMax = new Vector2(v.center.x + w / 2F, brt.anchorMax.y);
        canvasGo.SetActive(true);
        StudyLogger.Log("incident_shown", msg);
    }

    void Update()
    {
        if (canvasGo == null || !canvasGo.activeSelf) return;
        var sm = StudyManager.Instance;
        float age = Time.unscaledTime - shownAt;
        if (age > showSeconds || sm == null || !sm.IsTrialRunning) { canvasGo.SetActive(false); return; }
        // Border blinks for the first second, then fades with the message.
        float fade = Mathf.Clamp01((showSeconds - age) / 0.6F);
        float blink = age < 1F ? (Mathf.Repeat(age * 6F, 1F) < 0.5F ? 1F : 0.35F) : 1F;
        Color red = new Color(0.9F, 0.08F, 0.06F);
        foreach (var b in border) b.color = new Color(red.r, red.g, red.b, 0.9F * fade * blink);
        banner.color = new Color(red.r, red.g, red.b, 0.88F * fade);
        bannerText.color = new Color(1F, 1F, 1F, fade);
    }
}
