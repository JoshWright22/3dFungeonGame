using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Names the areas of a generated dungeon so an editor tool can line them up against the demo
/// scene's areas - the Goblin Cave against a chasm, the Cellar against a cellar - and photograph
/// both from the same kind of viewpoint.
/// </summary>
public partial class Generator3D
{
    public struct ComparisonArea
    {
        public string Label;     // the archetype or cave kind: "Cellar", "Chasm cave", ...
        public Bounds Bounds;    // world space, floor to ceiling
        public Vector3 Entry;    // a point on the floor at a way in, for an eye-level view from the door
    }

    /// <summary>One area per room archetype and per cave kind, the largest of each.</summary>
    public List<ComparisonArea> ComparisonAreas()
    {
        var best = new Dictionary<string, (int size, ComparisonArea area)>();

        void Offer(string label, int size, ComparisonArea area)
        {
            area.Label = label;
            if (!best.TryGetValue(label, out var current) || size > current.size) best[label] = (size, area);
        }

        if (rooms != null)
            foreach (var room in rooms)
            {
                if (room.IsCave || room.archetype == null) continue;
                var min = room.bounds.min;
                var max = room.bounds.max;
                var bounds = new Bounds();
                bounds.SetMinMax(new Vector3(min.x * WorldUnitSize, min.y * WorldUnitSize + FloorSurfaceOffset, min.z * WorldUnitSize),
                    new Vector3(max.x * WorldUnitSize, max.y * WorldUnitSize + FloorSurfaceOffset, max.z * WorldUnitSize));
                Offer(room.archetype.name, room.bounds.size.x * room.bounds.size.z,
                    new ComparisonArea { Bounds = bounds, Entry = EntryOf(room.FloorCells()) });
            }

        foreach (var cave in caves)
        {
            if (cave.Shape != CaveShape.Chamber) continue;
            string label = chasms.Contains(cave) ? "Chasm cave" : IsLushCave(cave) ? "Lush cave" : "Grotto cave";

            var cells = new List<Vector3Int>();
            foreach (var cell in cave.Cells) if (grid[cell] == CellType.Cave && !chasmCells.Contains(cell)) cells.Add(cell);
            if (cells.Count == 0) continue;

            var bounds = new Bounds(CellCentre(cells[0]), Vector3.zero);
            foreach (var cell in cave.Cells)
            {
                bounds.Encapsulate(new Vector3(cell.x * WorldUnitSize, cell.y * WorldUnitSize + FloorSurfaceOffset, cell.z * WorldUnitSize));
                bounds.Encapsulate(new Vector3((cell.x + 1) * WorldUnitSize, (cell.y + 1) * WorldUnitSize + FloorSurfaceOffset, (cell.z + 1) * WorldUnitSize));
            }

            Offer(label, cave.CellCount, new ComparisonArea { Bounds = bounds, Entry = EntryOf(cells) });
        }

        var areas = new List<ComparisonArea>();
        foreach (var entry in best.Values) areas.Add(entry.area);
        areas.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        return areas;
    }

    /// <summary>The floor point of a cell beside a doorway or cave mouth, or the first cell if there is none.</summary>
    Vector3 EntryOf(IEnumerable<Vector3Int> cells)
    {
        Vector3Int first = default;
        bool any = false;
        foreach (var cell in cells)
        {
            if (!any) { first = cell; any = true; }
            foreach (var dir in Directions)
            {
                if (dir.y != 0) continue;
                if (caveEntrances.Contains((cell - dir, dir)) || doorways.Contains((cell - dir, dir)) || doorways.Contains((cell, -dir)))
                    return FloorPoint(cell);
            }
        }
        return FloorPoint(first);
    }

    bool NearDrop(Vector3Int cell, float dx, float dz)
    {
        foreach (var (sx, sz) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            float gap = sx < 0 ? dx : sx > 0 ? WorldUnitSize - dx : sz < 0 ? dz : WorldUnitSize - dz;
            if (gap > 0.6f) continue;
            var n = cell + new Vector3Int(sx, 0, sz);
            if (grid.InBounds(n) && grid[n] != CellType.Cave && grid[n] != CellType.None && !IsBuiltSpace(n)) return true;
            if (grid.InBounds(n) && grid[n] == CellType.Water) return true;
        }
        return false;
    }

    Vector3 FloorPoint(Vector3Int cell)
    {
        var c = CellCentre(cell);
        return new Vector3(c.x, cell.y * WorldUnitSize + FloorSurfaceOffset, c.z);
    }

    /// <summary>
    /// Measures the faults that have been reported in caves, so a change can be judged by a
    /// number: holes in cave ground, cave ground standing proud of a corridor or room floor, and
    /// the positions of the first few of each.
    /// </summary>
    public string AuditCaves()
    {
        Physics.SyncTransforms();
        int samples = 0, holes = 0, proud = 0, builtSamples = 0;
        var holeAt = new List<Vector3>();
        var proudAt = new List<Vector3>();

        foreach (var cave in caves)
            foreach (var cell in cave.Cells)
            {
                if (grid[cell] != CellType.Cave) continue;
                // Pit floor is only seen from above; it is checked like any other ground.
                float floor = cell.y * WorldUnitSize + FloorSurfaceOffset;
                for (float dx = 0.5f; dx < WorldUnitSize; dx += 1f)
                for (float dz = 0.5f; dz < WorldUnitSize; dz += 1f)
                {
                    // Within a step of an edge that drops away - a ledge over a pit, a river bank -
                    // the rock ends raggedly by design. Only holes in the middle of the floor count.
                    if (NearDrop(cell, dx, dz)) continue;

                    var from = new Vector3(cell.x * WorldUnitSize + dx, floor + 0.6f, cell.z * WorldUnitSize + dz);
                    samples++;
                    // Visible ground only - the invisible floor seal under it does not count.
                    if (NearestOf(caveKitGround, new Ray(from, Vector3.down), out _, 1.6f)) continue;
                    holes++;
                    if (holeAt.Count < 8) holeAt.Add(from);
                }

                // Every built cell beside the cave: nothing of the cave's ground above its floor.
                foreach (var dir in Directions)
                {
                    if (dir.y != 0) continue;
                    var built = cell + dir;
                    if (!grid.InBounds(built) || !IsBuiltSpace(built)) continue;
                    float builtFloor = built.y * WorldUnitSize + FloorSurfaceOffset;

                    for (float t = 0.25f; t < WorldUnitSize; t += 0.5f)
                    for (float depth = 0.1f; depth < 1.6f; depth += 0.5f)
                    {
                        // Just inside the built cell, along the shared face.
                        Vector3 edge = CellCentre(built) - new Vector3(dir.x, 0f, dir.z) * (HalfWorldUnit - depth);
                        Vector3 along = new Vector3(dir.z, 0f, dir.x) * (t - HalfWorldUnit);
                        var from = new Vector3(edge.x + along.x, builtFloor + 2f, edge.z + along.z);
                        builtSamples++;
                        if (!NearestOf(caveKitGround, new Ray(from, Vector3.down), out RaycastHit hit, 2.5f)) continue;
                        if (hit.point.y <= builtFloor + 0.05f) continue;
                        proud++;
                        if (proudAt.Count < 8) proudAt.Add(hit.point);
                    }
                }
            }

        var report = new System.Text.StringBuilder();
        report.Append($"CAVE GROUND - {holes} of {samples} spots in the middle of cave floors have no visible rock (a hole you look into); "
            + $"{proud} of {builtSamples} spots on room and corridor floors beside caves have cave rock standing above them (clipping into the hallway).");
        if (holeAt.Count > 0) report.Append(" Holes at " + string.Join(" ", holeAt));
        if (proudAt.Count > 0) report.Append(" Proud at " + string.Join(" ", proudAt));
        return report.ToString();
    }

    /// <summary>
    /// Checks the whole dungeon is sealed: from points in every walkable cell, rays go out in
    /// every direction, and any that hits nothing has found a way out into the void - a gap a
    /// player can see through and, if it is big enough, walk through. Reports the count per kind
    /// of space and where the first few are.
    /// </summary>
    public string AuditSeal(out int leaks)
    {
        Physics.SyncTransforms();
        leaks = 0;
        int rays = 0;
        var byKind = new Dictionary<CellType, int>();
        var leakAt = new List<string>();
        float reach = Mathf.Max(size.x, size.y, size.z) * WorldUnitSize;

        // Every way a player can look: level all round, up at 30 and 60 degrees (upper walls and
        // roofs), down at 30 (the foot of walls and floor edges), and straight up and down.
        var directions = new List<Vector3>();
        for (int k = 0; k < 16; k++)
        {
            var flat = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
            var side = Vector3.Cross(Vector3.up, flat);
            directions.Add(flat);
            directions.Add(Quaternion.AngleAxis(-30f, side) * flat);
            if (k % 2 == 0)
            {
                directions.Add(Quaternion.AngleAxis(-60f, side) * flat);
                directions.Add(Quaternion.AngleAxis(30f, side) * flat);
            }
        }
        directions.Add(Vector3.up);
        directions.Add(Vector3.down);

        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            // From where a player's eyes can be: crouched and standing, over every floor cell.
            var cell = new Vector3Int(x, y, z);
            var kind = grid[cell];
            if (!IsAuditFloor(kind)) continue;

            float floor = y * WorldUnitSize + FloorSurfaceOffset;
            var heights = new[] { 0.95f, 1.62f };

            foreach (var (ox, oz) in new[] { (0f, 0f), (1.6f, 1.6f), (-1.6f, 1.6f), (1.6f, -1.6f), (-1.6f, -1.6f) })
            foreach (float h in heights)
            {
                var origin = new Vector3((x + 0.5f) * WorldUnitSize + ox, floor + h, (z + 0.5f) * WorldUnitSize + oz);

                // An origin inside solid geometry - a pillar, a rock - is not somewhere anyone stands.
                if (Physics.CheckSphere(origin, 0.15f, ~0, QueryTriggerInteraction.Ignore)) continue;

                foreach (var dir in directions)
                {
                    rays++;
                    // Only drawn geometry closes a sight-line.
                    if (VisibleHit(origin, dir, reach)) continue;

                    leaks++;
                    byKind[kind] = byKind.TryGetValue(kind, out int n) ? n + 1 : 1;
                    if (leakAt.Count < 12) leakAt.Add($"{kind} {cell} from {origin:F1} toward {dir:F1}");
                }
            }
        }

        var report = new System.Text.StringBuilder($"SEAL - can you see out? From crouched and standing eye height over every floor cell, looking level, up 30 and 60 degrees, down 30 and straight up and down: "
            + $"{leaks} of {rays} sight-lines reach the void - drawn geometry only (each is a hole in a wall, floor or roof a player can see).");
        foreach (var kv in byKind) report.Append($" {kv.Key}: {kv.Value}.");
        foreach (var at in leakAt) report.AppendLine().Append("  ").Append(at);
        return report.ToString();
    }
}
