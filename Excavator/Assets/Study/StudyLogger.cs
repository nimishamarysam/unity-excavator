using System;
using System.Globalization;
using System.IO;
using UnityEngine;

// ---------------------------------------------------------------------------
// STUDY LOGGER
// Writes two CSV files per participant into a "StudyData" folder:
//   P01_<date>_events.csv   one row per event (trial start/end, hazards, probes...)
//   P01_<date>_samples.csv  10 rows per second with the machine state and inputs
// Numbers always use a dot as decimal separator (InvariantCulture), so the files
// open correctly in Excel/R/Python on Finnish computers too.
// In the Unity editor the folder is next to the "Assets" folder; in a build it
// is next to the .exe.
// ---------------------------------------------------------------------------

public class StudyLogger : MonoBehaviour
{
    public static StudyLogger Instance { get; private set; }

    public float sampleRateHz = 10F;

    StreamWriter events;
    StreamWriter samples;
    float nextSampleTime;
    static readonly CultureInfo inv = CultureInfo.InvariantCulture;

    public string FolderPath { get; private set; }
    public bool IsOpen => events != null;

    void Awake()
    {
        Instance = this;
        FolderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StudyData"));
    }

    public void OpenSession(int participantId)
    {
        CloseSession();
        Directory.CreateDirectory(FolderPath);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", inv);
        baseName = $"P{participantId:00}_{stamp}";
        questionnaire = null;

        events = new StreamWriter(Path.Combine(FolderPath, baseName + "_events.csv"));
        events.WriteLine("wall_clock,trial_time_s,participant,trial,condition,scenario,event,detail");

        samples = new StreamWriter(Path.Combine(FolderPath, baseName + "_samples.csv"));
        samples.WriteLine("trial_time_s,participant,trial,condition,scenario,mode," +
                          "swing_deg,boom_deg,arm_deg,bucket_deg,bucket_height_m,tilt_deg,speed_kmh,pos_x,pos_z," +
                          "in_swing,in_boom,in_arm,in_bucket,in_track_left,in_track_right");
    }

    public void CloseSession()
    {
        questionnaire?.Flush(); questionnaire?.Close(); questionnaire = null;
        events?.Flush(); events?.Close(); events = null;
        samples?.Flush(); samples?.Close(); samples = null;
    }

    // Call this from anywhere: StudyLogger.Log("hazard_onset", "worker_behind");
    // Raised for every logged event (e.g. IncidentFeedback shows a warning when something goes wrong).
    public static event Action<string, string> Logged;

    public static void Log(string eventName, string detail = "")
    {
        Logged?.Invoke(eventName, detail);
        if (Instance == null || Instance.events == null) return;
        var sm = StudyManager.Instance;
        var t = sm != null ? sm.CurrentTrial : default;
        float trialTime = sm != null ? sm.TrialTime : 0F;

        Instance.events.WriteLine(string.Join(",",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", inv),
            trialTime.ToString("F3", inv),
            sm != null ? sm.ParticipantId.ToString(inv) : "",
            t.index.ToString(inv),
            t.condition,
            t.scenario,
            Csv(eventName),
            Csv(detail)));
    }

    // Questionnaire answers: one row per completed questionnaire in P01_<date>_questionnaire.csv
    // (columns: wall_clock, participant, trial, condition, scenario, questionnaire, then one column per item).
    string baseName;
    StreamWriter questionnaire;
    public void WriteQuestionnaire(string name, string[] itemNames, int[] values)
    {
        if (events == null || string.IsNullOrEmpty(baseName)) return;
        if (questionnaire == null)
        {
            string path = Path.Combine(FolderPath, baseName + "_questionnaire.csv");
            bool isNew = !File.Exists(path);
            questionnaire = new StreamWriter(path, true);
            if (isNew) questionnaire.WriteLine("wall_clock,participant,trial,condition,scenario,questionnaire," + string.Join(",", itemNames) + ",mean");
        }
        var sm = StudyManager.Instance;
        var t = sm != null ? sm.CurrentTrial : default;
        float mean = 0F;
        foreach (int v in values) mean += v;
        mean /= Mathf.Max(1, values.Length);
        questionnaire.WriteLine(string.Join(",",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", inv),
            sm != null ? sm.ParticipantId.ToString(inv) : "",
            t.index.ToString(inv), t.condition, t.scenario, name,
            string.Join(",", Array.ConvertAll(values, v => v.ToString(inv))),
            mean.ToString("F2", inv)));
        questionnaire.Flush();
    }

    public void Flush()
    {
        events?.Flush();
        samples?.Flush();
    }

    void Update()
    {
        var sm = StudyManager.Instance;
        if (samples == null || sm == null || !sm.IsTrialRunning) return;
        if (Time.time < nextSampleTime) return;
        nextSampleTime = Time.time + 1F / sampleRateHz;

        var ctl = sm.Controller;
        if (ctl == null || ctl.Machine == null) return;
        var m = ctl.Machine;
        var i = ctl.LastInput;
        var t = sm.CurrentTrial;
        Vector3 pos = m.position;

        samples.WriteLine(string.Join(",",
            F(sm.TrialTime), sm.ParticipantId.ToString(inv), t.index.ToString(inv), t.condition, t.scenario,
            ctl.IsTravelMode ? "travel" : "operation",
            F(m.swingAngle), F(m.boomAngle), F(m.armAngle), F(m.bucketAngle),
            F(m.BucketHeightAboveGround), F(m.TiltAngle), F(m.SpeedKmh), F(pos.x), F(pos.z),
            F(i.swing), F(i.boom), F(i.arm), F(i.bucket), F(i.trackLeft), F(i.trackRight)));
    }

    void OnApplicationQuit() => CloseSession();

    static string F(float v) => v.ToString("F3", inv);

    static string Csv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
