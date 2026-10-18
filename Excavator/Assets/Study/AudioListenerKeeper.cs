using UnityEngine;

// Keeps exactly ONE audio listener (on the persistent StudySystems object). The scene's own listener sits
// on a camera the study switches off, which made Unity report "There are no audio listeners in the scene".
// The study itself plays no sound.
public class AudioListenerKeeper : MonoBehaviour
{
    AudioListener listener;
    float next;

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 1F;
        if (listener == null) listener = gameObject.AddComponent<AudioListener>();
        listener.enabled = true;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l != listener && l.enabled) l.enabled = false;
    }
}
