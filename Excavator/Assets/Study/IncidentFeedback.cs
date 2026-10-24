using UnityEngine;

// ---------------------------------------------------------------------------
// INCIDENT FEEDBACK  (all scenarios, ALL conditions - it is the consequence of an error, not a cue)
// When something happens that should not happen, the participant hears a short warning sound and the
// INCIDENTS counter in the status bar goes up (and turns red for a moment). No text, no flash.
//
// Triggered by the study's own log events (contacts, strikes, spills, over-digging, ...), so what the
// participant hears is exactly what is recorded. Each incident is logged as "incident" with its type.
// The same incident type is not counted again within 2 s (e.g. a bucket resting against the truck wall).
// The counter starts at 0 in every trial.
// ---------------------------------------------------------------------------

public class IncidentFeedback : MonoBehaviour
{
    public float repeatBlockSeconds = 2F;
    [Range(0, 1)] public float volume = 0.6F;

    // Read by the task panel (ScenarioRunner).
    public static int Count { get; private set; }
    public static string LastDescription { get; private set; } = "";
    public static float LastIncidentTime { get; private set; } = -100F;   // unscaled time

    readonly System.Collections.Generic.Dictionary<string, float> lastByType = new System.Collections.Generic.Dictionary<string, float>();
    AudioSource audioSource;

    void OnEnable()
    {
        StudyLogger.Logged += OnLogged;
        StudyManager.TrialStarted += OnTrialStarted;
    }

    void OnDisable()
    {
        StudyLogger.Logged -= OnLogged;
        StudyManager.TrialStarted -= OnTrialStarted;
    }

    void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0F;
        audioSource.clip = AlertClip();
    }

    void OnTrialStarted(TrialInfo t)
    {
        Count = 0;
        LastDescription = "";
        LastIncidentTime = -100F;
        lastByType.Clear();
    }

    // Which log events are incidents (short type name for the log), null = not an incident.
    static string IncidentType(string evt, string detail)
    {
        switch (evt)
        {
            case "hazard_contact":
            {
                string[] p = detail.Split(';');
                return "contact_" + (p.Length > 1 ? p[1] : "unknown");   // e.g. contact_Worker, contact_BuriedPipe
            }
            case "bucket_blocked":
            {   // only real impacts (BucketBlocker: fast enough), not slow touches; type names the part hit
                if (!detail.Contains("impact=1")) return null;
                int i = detail.IndexOf("part=");
                string part = i >= 0 ? detail.Substring(i + 5).Split(';')[0] : "truck";
                return "truck_hit_" + part;
            }
            case "spill": return detail.StartsWith("too_close") ? "spill_too_close" : "spill";
            case "over_dig": return "over_dig";
            case "cut_outside_zone": return "dig_outside_area";
            case "bucket_over_worker": return "bucket_over_worker";
            default: return null;
        }
    }

    void OnLogged(string evt, string detail)
    {
        var sm = StudyManager.Instance;
        if (sm == null || !sm.IsTrialRunning) return;
        string type = IncidentType(evt, detail);
        if (type == null) return;

        float now = Time.unscaledTime;
        if (lastByType.TryGetValue(type, out float last) && now - last < repeatBlockSeconds) return;
        lastByType[type] = now;

        Count++;
        LastIncidentTime = now;
        LastDescription = Describe(type);
        if (audioSource != null) audioSource.PlayOneShot(audioSource.clip, volume);
        StudyLogger.Log("incident", $"{type};count={Count}");
    }

    // Short readable text for the task panel.
    static string Describe(string type)
    {
        switch (type)
        {
            case "contact_Worker": return "Worker hit";
            case "contact_Obstacle": return "Obstacle hit";
            case "contact_BuriedCable": return "Buried cable struck";
            case "contact_BuriedPipe": return "Pipe struck";
            case "contact_PowerLine": return "Power line contact";
            case "contact_TrenchEdge": return "Too close to trench edge";
            case "contact_Truck": return "Truck hit";
            case "truck_hit_cab": return "Bucket/arm hit the truck cab";
            case "truck_hit_wall": return "Bucket/arm hit the truck wall";
            case "truck_hit_bed_floor": return "Bucket hit the truck bed floor";
            case "truck_hit_chassis": return "Bucket/arm hit the truck chassis";
            case "truck_hit_truck": return "Bucket hit the truck";
            case "spill": return "Soil spilled";
            case "spill_too_close": return "Soil dumped otside the dumping area";
            case "over_dig": return "Dug too deep";
            case "dig_outside_area": return "Digging outside the area";
            case "bucket_over_worker": return "Bucket over a worker";
            default: return type.Replace('_', ' ');
        }
    }

    // A classic "error" buzz: two short, low, harsh pulses (square wave + slightly detuned copy), generated in code.
    static AudioClip AlertClip()
    {
        const int rate = 44100;
        const float pulse = 0.14F, gap = 0.07F;
        float len = pulse * 2F + gap;
        int n = (int)(rate * len);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float local = t < pulse ? t : t - pulse - gap;
            bool on = t < pulse || t > pulse + gap;
            if (!on) { d[i] = 0F; continue; }
            float env = Mathf.Clamp01(local / 0.005F) * Mathf.Clamp01((pulse - local) / 0.02F);
            float a = Mathf.Sign(Mathf.Sin(2F * Mathf.PI * 196F * t));     // G3 square
            float b = Mathf.Sign(Mathf.Sin(2F * Mathf.PI * 207F * t));     // detuned -> rough beating
            d[i] = 0.32F * env * (a + 0.7F * b) / 1.7F;
        }
        var clip = AudioClip.Create("IncidentError", n, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
