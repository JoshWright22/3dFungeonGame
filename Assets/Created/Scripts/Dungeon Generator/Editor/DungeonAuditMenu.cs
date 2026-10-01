using UnityEditor;
using UnityEngine;

/// <summary>Runs every dungeon audit on whatever is generated right now - in edit mode or mid play-test.</summary>
public static class DungeonAuditMenu
{
    [MenuItem("Tools/Dungeon/Audit Current Dungeon")]
    static void AuditCurrent()
    {
        var generator = Object.FindFirstObjectByType<Generator3D>();
        if (generator == null)
        {
            Debug.LogWarning("No Generator3D in the open scene.");
            return;
        }

        string report = generator.AuditAll(out int problems);
        if (problems > 0) Debug.LogWarning(report, generator);
        else Debug.Log(report, generator);
    }
}
