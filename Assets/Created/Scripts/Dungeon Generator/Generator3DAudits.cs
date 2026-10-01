using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Checks a finished dungeon for the faults play-testing keeps finding, so a change is judged by
/// a number rather than by walking around: can a player get out of the map, and what is stuck
/// inside what.
/// </summary>
public partial class Generator3D
{
    // The player's own capsule (Player.prefab), at its most capable: crouched, so it fits through
    // whatever a crouching player fits through; a 0.45 m step; a jump of about a metre
    // (JumpSpeed 7.2 against gravity 26); slopes up to 60 degrees.
    const float AuditRadius = 0.35f, AuditHeight = 1.05f, AuditStep = 0.45f, AuditJump = 1.0f, AuditStride = 1f;
    const float AuditMinGroundNormal = 0.5f;   // cos 60

    /// <summary>The cells the last escape walk reached, for drawing a coverage map.</summary>
    public readonly HashSet<Vector3Int> LastWalkCells = new HashSet<Vector3Int>();

    /// <summary>The layout type of a cell, for audit tools. None outside the grid.</summary>
    public string CellKind(Vector3Int cell) => grid != null && grid.InBounds(cell) ? grid[cell].ToString() : "None";

    public Vector3Int GridSize => size;

    /// <summary>
    /// Walks the dungeon the way a player can: a player-sized capsule starts on every floor cell
    /// it fits on, steps out in eight directions, climbs what the controller climbs and drops off
    /// ledges, until everywhere it can get to has been visited. Anywhere it reaches that is out in
    /// solid rock, or a drop with no ground under it at all, is an escape.
    /// </summary>
    public string AuditEscape(out int escapes)
    {
        Physics.SyncTransforms();
        escapes = 0;
        int falls = 0;
        var report = new List<string>();

        var seen = new HashSet<Vector3Int>();
        var visitedCells = LastWalkCells;
        visitedCells.Clear();
        int blocked = 0, tooHigh = 0, cramped = 0;
        var blockers = new Dictionary<string, int>();
        var queue = new Queue<Vector3>();

        // Started from every floor cell at once, not just the spawn: the question is whether a
        // player standing anywhere can get out, and one awkward staircase must not hide the rest
        // of the map from the test.
        int starts = 0;
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!IsAuditFloor(grid[cell])) continue;
            var above = CellCentre(cell) + Vector3.up * (FloorSurfaceOffset + 1.5f);
            if (!GroundUnder(above, 3.5f, out Vector3 start, out _) || OverlapsDungeon(start)) continue;
            if (!seen.Add(AuditKey(start))) continue;
            queue.Enqueue(start);
            visitedCells.Add(AuditCell(start));
            starts++;
        }

        var directions = new Vector3[8];
        for (int k = 0; k < 8; k++) directions[k] = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;

        while (queue.Count > 0 && seen.Count < 600000)
        {
            var at = queue.Dequeue();

            if (InSolidRock(at))
            {
                escapes++;
                if (report.Count < 10) report.Add($"out in rock at {at:F1}");
                continue;   // do not keep walking around outside
            }

            foreach (var heading in directions)
            {
                var dir = heading;
                // Where the ground is a stride ahead decides how high the body is carried: a
                // step's height on the flat, the full rise on a ramp or a staircase.
                // Probed from the highest a foot could reach in one stride, so the top of a
                // bookcase is never mistaken for the floor ahead.
                // Snapped to a half-metre grid. Doorways sit on cell centres, which are on it; a
                // walk that drifts off-grid after a few diagonal steps misses a 1.5 m doorway's
                // narrow line and reports the door as a wall.
                Vector3 next = at + dir * AuditStride;
                next.x = Snap(next.x);
                next.z = Snap(next.z);
                dir = new Vector3(next.x - at.x, 0f, next.z - at.z);
                float reach = dir.magnitude;
                if (reach < 0.1f) continue;
                dir /= reach;
                // A ramp or stair may rise as steeply as the controller climbs (60 degrees); a flat
                // top only as high as a jump.
                float slopeRise = reach * 1.73f;
                if (!GroundUnder(next + Vector3.up * (Mathf.Max(AuditJump, slopeRise) + 0.2f), 40f, out Vector3 ground, out Vector3 normal))
                {
                    // Nothing below - but only a fall if the body can actually get there; the far
                    // side of a wall has no floor either.
                    if (CastsIntoDungeon(at + Vector3.up * (AuditStep + AuditRadius),
                            at + Vector3.up * Mathf.Max(AuditStep + AuditRadius, AuditHeight - AuditRadius), dir, reach, out _)) { blocked++; continue; }

                    // Off an edge with nothing below at all: falling out of the world.
                    falls++;
                    if (report.Count < 10) report.Add($"drop into the void at {next:F1}");
                    continue;
                }

                // Up a step, a slope, or anything it can jump onto; nothing higher.
                float rise = ground.y - at.y;
                bool slope = normal.y < 0.97f;
                if (normal.y < AuditMinGroundNormal || rise > (slope ? Mathf.Max(AuditJump, slopeRise) : AuditJump)) { tooHigh++; continue; }

                // The feet clear the rise; the body above them is the crouched capsule.
                float lift = Mathf.Max(AuditStep, rise);
                Vector3 bottom = at + Vector3.up * (lift + AuditRadius);
                Vector3 top = at + Vector3.up * Mathf.Max(lift + AuditRadius, lift + AuditHeight - AuditRadius);
                if (CastsIntoDungeon(bottom, top, dir, reach, out string what))
                {
                    blocked++;
                    blockers[what] = blockers.TryGetValue(what, out int b) ? b + 1 : 1;
                    continue;
                }

                // And it has to fit where it lands: a spot half inside a wall is not one a
                // player can stand on, and walking on from it would pass through the wall.
                if (OverlapsDungeon(ground)) { cramped++; continue; }

                if (seen.Add(AuditKey(ground)))
                {
                    queue.Enqueue(ground);
                    visitedCells.Add(AuditCell(ground));
                }
            }
        }

        escapes += falls;

        // How much of the dungeon the walk reached. A walk that never left the first room finds
        // no escapes because it never went anywhere - this says how far to trust the zero.
        int floorCells = 0, reached = 0;
        var missed = new Dictionary<CellType, int>();
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!IsAuditFloor(grid[cell])) continue;
            floorCells++;
            if (visitedCells.Contains(cell)) reached++;
            else missed[grid[cell]] = missed.TryGetValue(grid[cell], out int m) ? m + 1 : 1;
        }

        var text = new StringBuilder($"ESCAPE - can you walk out? A player-sized capsule (crouched, 0.45 m step, 1 m jump, 60 degree slopes) started on every floor cell "
            + $"it fits on ({starts} starts) and walked everywhere it could, covering {reached} of {floorCells} floor cells "
            + $"({100f * reached / Mathf.Max(1, floorCells):F0}% - floor it could not stand on was not tested); "
            + $"{escapes - falls} spots it reached are outside the dungeon in solid rock, and {falls} steps drop off an edge with no floor anywhere below. "
            + $"Steps refused: {blocked} walked into something, {tooHigh} too high to climb, {cramped} landed somewhere the capsule does not fit.");
        if (missed.Count > 0)
        {
            var parts = new List<string>();
            foreach (var kv in missed) parts.Add($"{kv.Key} {kv.Value}");
            text.Append(" Not covered, by kind: " + string.Join(", ", parts) + ".");
        }

        var worst = new List<KeyValuePair<string, int>>(blockers);
        worst.Sort((a, b) => b.Value.CompareTo(a.Value));
        if (worst.Count > 0) text.Append(" Most walked into: " + string.Join(", ", worst.GetRange(0, Mathf.Min(5, worst.Count)).ConvertAll(kv => $"{kv.Key} x{kv.Value}")) + ".");
        foreach (var line in report) text.AppendLine().Append("  ").Append(line);
        return text.ToString();
    }

    Vector3Int AuditCell(Vector3 p) => new Vector3Int(
        Mathf.FloorToInt(p.x / WorldUnitSize), Mathf.FloorToInt((p.y + 1.5f - FloorSurfaceOffset) / WorldUnitSize), Mathf.FloorToInt(p.z / WorldUnitSize));

    // A metre grid offset by half, so every cell centre - and with it every doorway's centre
    // line - lies on it.
    static float Snap(float v) => Mathf.Round(v - 0.5f) + 0.5f;

    static Vector3Int AuditKey(Vector3 p) => new Vector3Int(
        Mathf.RoundToInt(p.x - 0.5f), Mathf.RoundToInt(p.y * 2f), Mathf.RoundToInt(p.z - 0.5f));

    /// <summary>Out of the dungeon's space: in a rock cell with rock all round it, or off the grid.</summary>
    bool InSolidRock(Vector3 p)
    {
        foreach (var (dx, dz) in new[] { (0f, 0f), (1.5f, 1.5f), (-1.5f, 1.5f), (1.5f, -1.5f), (-1.5f, -1.5f) })
        {
            var cell = new Vector3Int(Mathf.FloorToInt((p.x + dx) / WorldUnitSize),
                Mathf.FloorToInt((p.y + 1.5f - FloorSurfaceOffset) / WorldUnitSize),
                Mathf.FloorToInt((p.z + dz) / WorldUnitSize));
            if (grid.InBounds(cell) && grid[cell] != CellType.None) return false;
        }
        return true;
    }

    /// <summary>Capsule sweep that only counts the dungeon's own colliders, not the player's.</summary>
    bool CastsIntoDungeon(Vector3 bottom, Vector3 top, Vector3 dir, float distance, out string what)
    {
        what = null;
        // Distance zero means the capsule starts in contact; landing spots are checked to fit, so
        // that is only ever the ground lip at its feet.
        foreach (var hit in Physics.CapsuleCastAll(bottom, top, AuditRadius, dir, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance <= 0f || !hit.collider.transform.IsChildOf(dungeonRoot)) continue;
            // Doors open; a player walks through them.
            if (hit.collider.GetComponentInParent<Delver.Dungeon.DungeonDoor>() != null) continue;
            what = System.Text.RegularExpressions.Regex.Replace(hit.collider.name, @"\(Clone\)|\s\(\d+\)", "");
            return true;
        }
        return false;
    }

    // Room is the headroom of a tall room above its BottomFloorRoom floor - there is nothing to stand on.
    static bool IsAuditFloor(CellType kind) =>
        kind == CellType.BottomFloorRoom || kind == CellType.Hallway
        || kind == CellType.Cave || kind == CellType.Stairs || kind == CellType.Water;

    bool OverlapsDungeon(Vector3 feet)
    {
        // Clear of a step's height at the feet: the next stair, a kerb, a root - the controller
        // steps onto anything that low rather than stopping against it.
        Vector3 bottom = feet + Vector3.up * (AuditStep + AuditRadius);
        Vector3 top = feet + Vector3.up * Mathf.Max(AuditStep + AuditRadius, AuditHeight - AuditRadius);
        foreach (var hit in Physics.OverlapCapsule(bottom, top, AuditRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(dungeonRoot)) continue;
            if (hit.GetComponentInParent<Delver.Dungeon.DungeonDoor>() != null) continue;
            return true;
        }
        return false;
    }

    bool GroundUnder(Vector3 from, float distance, out Vector3 ground, out Vector3 normal)
    {
        ground = default;
        normal = Vector3.up;
        float nearest = float.MaxValue;
        // A small sphere rather than a ray: a ray down the exact seam between two floor tiles
        // slips between their colliders where no foot ever could.
        foreach (var hit in Physics.SphereCastAll(from, 0.15f, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            // distance 0 is a sphere that started inside something; Unity reports no real point for it.
            if (hit.distance <= 0f || !hit.collider.transform.IsChildOf(dungeonRoot) || hit.normal.y < AuditMinGroundNormal) continue;
            if (hit.distance < nearest) { nearest = hit.distance; ground = hit.point; normal = hit.normal; }
        }
        return nearest < float.MaxValue;
    }

    /// <summary>
    /// What is stuck in what: props sunk into walls, solid props inside each other, the same piece
    /// spawned twice in one place, and cave rock reaching into rooms and corridors.
    /// </summary>
    public string AuditOverlaps(out int overlaps)
    {
        Physics.SyncTransforms();
        var props = new List<(GameObject go, Bounds b, HashSet<Collider> own)>();
        var structure = new HashSet<Collider>();
        var seenPieces = new Dictionary<string, int>();
        int duplicates = 0, caveInBuilt = 0;
        var examples = new List<string>();

        foreach (Transform child in dungeonRoot)
        {
            var go = child.gameObject;
            string name = go.name;

            // The same prefab at the same spot and turn: a flickering double.
            var key = $"{name}|{Vector3Int.RoundToInt(child.position * 20f)}|{Vector3Int.RoundToInt(child.eulerAngles)}";
            seenPieces[key] = seenPieces.TryGetValue(key, out int n) ? n + 1 : 1;
            if (n == 1)
            {
                duplicates++;
                if (examples.Count < 12) examples.Add($"duplicate {name} at {child.position:F1}");
            }

            if (name.StartsWith("Cave ") && !name.StartsWith("Cave containment") && !name.StartsWith("Cave floor base")
                && ReachesIntoBuilt(go, 0.4f))
            {
                caveInBuilt++;
                if (examples.Count < 12) examples.Add($"cave rock in built space: {name} at {child.position:F1}");
            }

            var colliders = go.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0 || !WorldBounds(go, out Bounds bounds)) continue;

            // Masonry only. A cave prop banked against cave rock half buried in it is how the
            // demo dresses its caves; a barrel half into a brick wall is a mistake.
            if (IsStructure(name) && !name.StartsWith("Cave ")) foreach (var c in colliders) structure.Add(c);
            else if (!IsSurface(name) && !IsWallMounted(name) && bounds.size.magnitude < 8f) props.Add((go, bounds, new HashSet<Collider>(colliders)));
        }

        var propOf = new Dictionary<Collider, int>();
        for (int i = 0; i < props.Count; i++) foreach (var c in props[i].own) propOf[c] = i;

        int inWall = 0, inProp = 0;
        for (int i = 0; i < props.Count; i++)
        {
            var (go, b, own) = props[i];
            // Only the core of the prop: a hand's width of contact is just standing against it.
            bool wall = false, other = false;
            foreach (var hit in Physics.OverlapBox(b.center, b.extents * 0.35f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (own.Contains(hit)) continue;
                if (structure.Contains(hit)) wall = true;
                // Cave rocks are laid in graded heaps on purpose; only room furniture counts.
                else if (propOf.TryGetValue(hit, out int j) && j > i && !InCave(b.center)) other = true;
            }

            if (wall)
            {
                inWall++;
                if (examples.Count < 12) examples.Add($"in a wall: {go.name} at {b.center:F1}");
            }
            if (other)
            {
                inProp++;
                if (examples.Count < 12) examples.Add($"inside another prop: {go.name} at {b.center:F1}");
            }
        }

        overlaps = inWall + inProp + duplicates + caveInBuilt;
        var text = new StringBuilder($"OVERLAP - is anything stuck in anything? Of {props.Count} solid props: {inWall} have their middle inside a masonry wall, "
            + $"{inProp} room props have their middle inside another; {duplicates} pieces are spawned twice in the same spot (flicker); "
            + $"{caveInBuilt} cave rock pieces reach into a room or corridor.");
        foreach (var line in examples) text.AppendLine().Append("  ").Append(line);
        return text.ToString();
    }

    bool InCave(Vector3 p)
    {
        var cell = new Vector3Int(Mathf.FloorToInt(p.x / WorldUnitSize), Mathf.FloorToInt((p.y - FloorSurfaceOffset) / WorldUnitSize), Mathf.FloorToInt(p.z / WorldUnitSize));
        return grid.InBounds(cell) && (grid[cell] == CellType.Cave || grid[cell] == CellType.CaveOpen);
    }

    /// <summary>Hung on a wall on purpose - its bracket is meant to be in the masonry.</summary>
    static bool IsWallMounted(string name) =>
        name.Contains("Torch") || name.Contains("Banner") || name.Contains("Shield") || name.Contains("Sconce")
        || name.Contains("Chain") || name.Contains("Shelf") || name.Contains("Shelves") || name.Contains("Standards")
        // The room kits' timber framing - posts, beams, braces, bands - is set into the masonry.
        || name.Contains("Pole") || name.Contains("Beam") || name.Contains("Brace") || name.Contains("Post") || name.Contains("Band")
        || name.Contains("Support") || name.Contains("Trim");

    static bool IsStructure(string name) =>
        name.Contains("Wall") || name.Contains("Door") || name.Contains("Pillar") || name.Contains("Cave wall") || name.Contains("Cave column");

    static bool IsSurface(string name) =>
        name.Contains("Floor") || name.Contains("Tiles") || name.Contains("Ceiling") || name.Contains("Stair")
        || name.StartsWith("Cave ") || name.StartsWith("Chasm") || name.Contains("Rug") || name.Contains("River");

    /// <summary>Every audit, one report - what Tools &gt; Dungeon &gt; Audit Current Dungeon prints.</summary>
    public string AuditAll(out int problems) => AuditAll(true, out problems);

    /// <param name="walk">Include the escape walk, which takes several seconds on a full dungeon.</param>
    public string AuditAll(bool walk, out int problems)
    {
        // The layout grid lives only in memory: a script recompile since the dungeon was built
        // throws it away while the geometry stays in the scene.
        if (grid == null || dungeonRoot == null)
        {
            problems = 0;
            return "No layout in memory for the dungeon in the scene (scripts recompiled since it was built). Generate again, then audit.";
        }

        // Only the dungeon is audited. Anything else in the scene with a collider - a stray floor
        // tile under the map, the player - would otherwise catch rays the dungeon lets through.
        var outsiders = new List<Collider>();
        foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            if (c.enabled && !c.transform.IsChildOf(dungeonRoot)) { c.enabled = false; outsiders.Add(c); }

        try
        {
            var text = new StringBuilder();
            text.AppendLine(AuditSeal(out int leaks));
            int escapes = 0;
            text.AppendLine(walk ? AuditEscape(out escapes) : "ESCAPE - not run on generate (slow); Tools > Dungeon > Audit Current Dungeon runs it.");
            text.AppendLine(AuditOverlaps(out int overlaps));
            if (HasCaveGeometry) text.AppendLine(AuditCaves());
            problems = leaks + escapes + overlaps;
            return text.ToString();
        }
        finally
        {
            foreach (var c in outsiders) c.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
