using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// BURIED SERVICES X-RAY in the VIEW INTO BUCKET  (C2 and C3, profile option intoBucketXray - Digging)
// The buried pipe and cable are drawn THROUGH the ground, but only by the camera that shows the
// VIEW INTO BUCKET (switched per camera in Camera.onPreCull), so the operator sees where they run
// below the bucket. Plain lines in the services' own colours: water pipe blue (as thick as the pipe),
// cable orange (as its markers). No distances and no warning colours - it is the same in C2 and C3.
// Logged as view_xray on / off.
// ---------------------------------------------------------------------------

public class BuriedServicesXray : MonoBehaviour
{
    public static readonly Color PipeColor = new Color(0.25F, 0.5F, 0.95F, 0.9F);
    public static readonly Color CableColor = new Color(1F, 0.45F, 0.05F, 0.9F);
    public float pipeWidth = 0.3F, cableWidth = 0.1F;

    AttentionCues root;
    Transform own;   // always-active parent (the cue world root is only active in C3)
    readonly Dictionary<Hazard, LineRenderer> lines = new Dictionary<Hazard, LineRenderer>();
    bool shown;

    public void Init(AttentionCues r)
    {
        root = r;
        own = new GameObject("BuriedServicesXray").transform;
        own.SetParent(transform, false);
        Camera.onPreCull += PreCull;
    }

    void OnDestroy() { Camera.onPreCull -= PreCull; }

    void PreCull(Camera cam)
    {
        if (cam == null) return;
        bool on = shown && cam == ContextViews.OverBucketCamera;
        foreach (var lr in lines.Values) if (lr != null) lr.enabled = on;
    }

    void LateUpdate()
    {
        var sm = StudyManager.Instance;
        bool want = root != null && root.Profile.intoBucketXray && sm != null && sm.ShowContextViews &&
                    (sm.CurrentState == StudyManager.State.Running || sm.CurrentState == StudyManager.State.Paused) &&
                    root.Ctl != null && !root.Ctl.IsTravelMode;
        if (want != shown)
        {
            shown = want;
            StudyLogger.Log("view_xray", want ? "on" : "off");
        }
        if (!want) { foreach (var lr in lines.Values) CueKit.Hide(lr); return; }

        foreach (var h in Hazard.All)
        {
            if (!(h is SegmentHazard seg) || !h.active) continue;
            if (h.kind != HazardKind.BuriedPipe && h.kind != HazardKind.BuriedCable) continue;
            if (!lines.TryGetValue(h, out var lr))
            {
                bool pipe = h.kind == HazardKind.BuriedPipe;
                lr = CueKit.Line(own, pipe ? "XrayPipe" : "XrayCable", pipe ? pipeWidth : cableWidth, true, false);
                lines[h] = lr;
            }
            lr.loop = false;
            lr.positionCount = 2;
            lr.SetPosition(0, seg.a);
            lr.SetPosition(1, seg.b);
            CueKit.Paint(lr, h.kind == HazardKind.BuriedPipe ? PipeColor : CableColor);
        }
    }
}
