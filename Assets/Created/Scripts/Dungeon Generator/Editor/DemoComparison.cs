using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Photographs the POLYGON Dungeon demo and a generated dungeon from matching viewpoints and lays
/// them side by side, so "does it look like the demo" is a picture rather than a memory.
///
/// Each demo area (Goblin_Cave, Cellar, ...) is paired with the generated area of the same kind.
/// Both sides find their viewpoints the same way - walkable floor inside the area, then a view
/// from the way in, a view from the far corner back across, and a view from the middle - so the
/// framing is comparable even though the layouts differ.
///
/// Output: Comparisons/&lt;seed&gt;/&lt;area&gt;.png in the project folder. Rows: the demo, ours as played (fog and torch), ours fully lit.
/// </summary>
public static class DemoComparison
{
    const string DemoScene = "Assets/Imports/PolygonDungeon/Scenes/Demo.unity";
    const string GameScene = "Assets/Created/Scenes/SampleScene.unity";
    const int Width = 640, Height = 360;

    /// <summary>Demo root object, and the generated labels that stand in for it.</summary>
    static readonly (string demo, string[] ours)[] Pairs =
    {
        ("Goblin_Cave", new[] { "Chasm cave" }),
        ("Crystal_Caves", new[] { "Lush cave", "Grotto cave" }),
        ("Cellar", new[] { "Cellar" }),
        ("Barracks", new[] { "Barracks" }),
        ("Torture_Room", new[] { "Torture room" }),
        ("Cells", new[] { "Cell" }),
        ("Furnace", new[] { "Furnace" }),
        ("Laboratory", new[] { "Chamber" }),
        ("Long_Hall", new[] { "Gallery" }),
        ("Center_Hall", new[] { "Great hall" }),
    };

    [MenuItem("Tools/Dungeon/Compare With Demo")]
    static void CompareMenu() => Run(202);

    /// <summary>Runs the whole comparison for one seed. Returns the output folder.</summary>
    public static string Run(int seed)
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Demo comparison runs in edit mode.");
            return null;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;

        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Comparisons", seed.ToString());
        Directory.CreateDirectory(folder);

        // The demo first, in its own scene so its lighting and fog are its own.
        var demoShots = new Dictionary<string, List<Texture2D>>();
        var demo = EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Single);
        foreach (var root in demo.GetRootGameObjects())
        {
            if (!System.Array.Exists(Pairs, p => p.demo == root.name)) continue;
            if (!AreaOf(root, out Bounds area)) continue;
            demoShots[root.name] = Shoot(area, null, root.transform, false);
        }

        // Then ours, generated fresh in the game scene, lit as a player would see it.
        var oursShots = new Dictionary<string, List<Texture2D>>();
        var oursLit = new Dictionary<string, List<Texture2D>>();
        EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
        var generator = Object.FindFirstObjectByType<Generator3D>();
        if (generator != null)
        {
            // The game's own fog and ambient, which it applies on Start.
            var atmosphere = Object.FindFirstObjectByType<Delver.Game.DungeonAtmosphere>();
            if (atmosphere != null)
                typeof(Delver.Game.DungeonAtmosphere).GetMethod("Apply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(atmosphere, null);

            generator.Generate(seed);
            Physics.SyncTransforms();
            var areas = generator.ComparisonAreas();
            foreach (var area in areas)
                oursShots[area.Label] = Shoot(area.Bounds, area.Entry, null, true);

            // And again with the fog lifted and the ambient up, so the build itself can be judged
            // past the torch's reach - the demo is lit all over, the game is not.
            RenderSettings.fog = false;
            RenderSettings.ambientLight = new Color(0.45f, 0.42f, 0.38f);
            foreach (var area in areas)
                oursLit[area.Label] = Shoot(area.Bounds, area.Entry, null, true);
        }

        foreach (var (demoName, labels) in Pairs)
        {
            demoShots.TryGetValue(demoName, out var top);
            List<Texture2D> played = null, lit = null;
            string used = "none";
            foreach (var label in labels)
                if (oursShots.TryGetValue(label, out played)) { used = label; oursLit.TryGetValue(label, out lit); break; }

            if (top == null && played == null) continue;
            var sheet = Sheet(top, played, lit);
            File.WriteAllBytes(Path.Combine(folder, $"{demoName} vs {used}.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }

        foreach (var list in demoShots.Values) foreach (var t in list) Object.DestroyImmediate(t);
        foreach (var list in oursShots.Values) foreach (var t in list) Object.DestroyImmediate(t);
        foreach (var list in oursLit.Values) foreach (var t in list) Object.DestroyImmediate(t);

        // Back to the game scene as it was saved - the generated dungeon is not kept.
        EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);

        Debug.Log($"Demo comparison written to {folder}");
        return folder;
    }

    // ------------------------------------------------------------------ viewpoints

    /// <summary>The demo area's extent: every renderer under its root.</summary>
    static bool AreaOf(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool first = true;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }
        return !first;
    }

    /// <summary>
    /// Three eye-level shots of an area, each a long unobstructed look across it: from the way in,
    /// along the longest clear sight line, and from the middle.
    /// </summary>
    static List<Texture2D> Shoot(Bounds area, Vector3? entry, Transform owner, bool torch)
    {
        var floor = WalkablePoints(area, owner);
        floor.RemoveAll(p => !InTheOpen(p));
        var shots = new List<Texture2D>();
        if (floor.Count == 0) return shots;

        Vector3 middle = Vector3.zero;
        foreach (var p in floor) middle += p;
        middle /= floor.Count;
        Vector3 centre = Nearest(floor, middle);
        Vector3 start = entry.HasValue ? Nearest(floor, entry.Value) : Furthest(floor, centre);

        var random = new System.Random(floor.Count);
        Vector3 longA = start, longB = centre;
        float longest = 0f;
        for (int i = 0; i < 600; i++)
        {
            var a = floor[random.Next(floor.Count)];
            var b = floor[random.Next(floor.Count)];
            float d = (a - b).sqrMagnitude;
            if (d > longest && CanSee(a, b)) { longest = d; longA = a; longB = b; }
        }

        shots.Add(Render(start, FurthestVisible(floor, start), torch));
        shots.Add(Render(longA, longB, torch));
        shots.Add(Render(centre, FurthestVisible(floor, centre), torch));
        return shots;
    }

    /// <summary>Not standing inside a rock: clear for a metre all round at eye height.</summary>
    static bool InTheOpen(Vector3 foot)
    {
        Vector3 eye = foot + Vector3.up * 1.7f;
        if (Physics.CheckSphere(eye, 0.4f, ~0, QueryTriggerInteraction.Ignore)) return false;
        for (int k = 0; k < 8; k++)
        {
            var dir = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
            if (Physics.Raycast(eye, dir, 1.2f, ~0, QueryTriggerInteraction.Ignore)) return false;
        }
        return true;
    }

    static bool CanSee(Vector3 a, Vector3 b)
    {
        Vector3 from = a + Vector3.up * 1.7f, to = b + Vector3.up * 1.4f;
        return !Physics.Linecast(from, to, ~0, QueryTriggerInteraction.Ignore);
    }

    static Vector3 FurthestVisible(List<Vector3> points, Vector3 from)
    {
        Vector3 best = from + Vector3.forward;
        float bestD = -1f;
        foreach (var p in points)
        {
            float d = (p - from).sqrMagnitude;
            if (d > bestD && CanSee(from, p)) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>
    /// Floor points with head room, on a two-metre grid across the area. Under a roof the first
    /// thing a downward cast meets is the roof, so every hit down the column is considered.
    /// </summary>
    static List<Vector3> WalkablePoints(Bounds area, Transform owner)
    {
        var points = new List<Vector3>();
        for (float x = area.min.x + 1f; x < area.max.x; x += 2f)
        for (float z = area.min.z + 1f; z < area.max.z; z += 2f)
        {
            var hits = Physics.RaycastAll(new Vector3(x, area.max.y + 1f, z), Vector3.down, area.size.y + 2f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.normal.y < 0.8f) continue;
                if (owner != null && !hit.collider.transform.IsChildOf(owner)) continue;
                // Head room, but under a roof: the top of a roof slab has head room too.
                Vector3 foot = hit.point + Vector3.up * 0.1f;
                if (Physics.Raycast(foot, Vector3.up, 2.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(foot, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore)) continue;
                points.Add(hit.point);
            }
        }
        return points;
    }

    static Vector3 Nearest(List<Vector3> points, Vector3 to)
    {
        Vector3 best = points[0];
        foreach (var p in points) if ((p - to).sqrMagnitude < (best - to).sqrMagnitude) best = p;
        return best;
    }

    static Vector3 Furthest(List<Vector3> points, Vector3 from)
    {
        Vector3 best = points[0];
        foreach (var p in points) if ((p - from).sqrMagnitude > (best - from).sqrMagnitude) best = p;
        return best;
    }

    // ------------------------------------------------------------------ rendering

    static Texture2D Render(Vector3 standing, Vector3 lookingAt, bool torch)
    {
        var go = new GameObject("Comparison camera");
        go.hideFlags = HideFlags.HideAndDontSave;
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 400f;
        if (torch) Delver.Game.DungeonAtmosphere.ApplyToCamera(cam);
        // The game pulls the far plane in to the fog's end; with the fog lifted for the lit row,
        // the whole space should be drawn.
        if (!RenderSettings.fog) cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        Vector3 eye = standing + Vector3.up * 1.7f;
        Vector3 target = lookingAt + Vector3.up * 1.4f;
        if ((target - eye).sqrMagnitude < 1f) target = eye + Vector3.forward;
        go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));

        // The carried torch, roughly as CarriedTorch lights it - ours is played by torchlight.
        if (torch)
        {
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.4f);
            light.intensity = 9f;
            light.range = 20f;
        }

        var rt = RenderTexture.GetTemporary(Width, Height, 24);
        cam.targetTexture = rt;
        cam.Render();

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        // Kept alive across the scene switch between the demo and ours.
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
        shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;

        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(go);
        return shot;
    }

    /// <summary>Rows top to bottom: the demo, ours as played (fog and torch), ours fully lit.</summary>
    static Texture2D Sheet(params List<Texture2D>[] rows)
    {
        const int columns = 3, bar = 6;
        int height = Height * rows.Length + bar * (rows.Length - 1);
        var sheet = new Texture2D(Width * columns, height, TextureFormat.RGB24, false);
        var fill = new Color32[sheet.width * sheet.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(20, 20, 20, 255);
        sheet.SetPixels32(fill);

        for (int r = 0; r < rows.Length; r++)
        {
            // Texture rows run bottom-up, so the first row goes at the top.
            int y = height - (r + 1) * Height - r * bar;
            if (r > 0)
                for (int x = 0; x < sheet.width; x++)
                    for (int b = 0; b < bar; b++)
                        sheet.SetPixel(x, y + Height + b, r == 1 ? new Color(0.7f, 0.1f, 0.1f) : new Color(0.3f, 0.3f, 0.3f));

            if (rows[r] == null) continue;
            for (int i = 0; i < rows[r].Count && i < columns; i++)
                sheet.SetPixels(i * Width, y, Width, Height, rows[r][i].GetPixels());
        }

        sheet.Apply();
        return sheet;
    }
}
