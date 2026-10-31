using UnityEditor;

// Menu  Study > Setup > Lab / Home  - picks the display values in StudyLayout.ApplySetup().
// Saved per computer (EditorPrefs), so the lab PC and the home PC each remember their own setup.
// Takes effect the next time Play is pressed.
static class StudySetupMenu
{
    const string LabItem = "Study/Setup/Lab";
    const string HomeItem = "Study/Setup/Home";

    static StudyLayout.SetupProfile Current =>
        (StudyLayout.SetupProfile)EditorPrefs.GetInt(StudyLayout.SetupPrefKey, (int)StudyLayout.SetupProfile.Lab);

    [MenuItem(LabItem, false, 1)] static void SetLab() => Set(StudyLayout.SetupProfile.Lab);
    [MenuItem(HomeItem, false, 2)] static void SetHome() => Set(StudyLayout.SetupProfile.Home);

    [MenuItem(LabItem, true)] static bool LabValidate() { Menu.SetChecked(LabItem, Current == StudyLayout.SetupProfile.Lab); return !EditorApplication.isPlaying; }
    [MenuItem(HomeItem, true)] static bool HomeValidate() { Menu.SetChecked(HomeItem, Current == StudyLayout.SetupProfile.Home); return !EditorApplication.isPlaying; }

    static void Set(StudyLayout.SetupProfile p)
    {
        EditorPrefs.SetInt(StudyLayout.SetupPrefKey, (int)p);
        UnityEngine.Debug.Log($"[StudyLayout] Setup set to {p} (applies on next Play).");
    }
}
