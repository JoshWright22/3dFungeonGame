using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cave ground, laid the way the demo lays it.
///
/// The demo's caves are floored with a handful of huge cracked-rock plates - SM_Env_CrackedRock_01
/// and _03 at one to two and a half times their size, twelve to forty metres across, at any yaw -
/// with a few low ground mounds on top. Never a tile per grid cell: a plate the size of a room
/// is what stops the floor reading as a repeating pattern.
///
/// Two layers here. A flat base, one plain rock plate per cell cut exactly to the cell, sits a
/// few centimetres down and guarantees there is never a hole to fall through; it is what shows at
/// the very edge by a corridor mouth. Over it, the big plates are laid largest first, each as
/// large as fits without reaching into built space, until every cell is covered.
/// </summary>
public partial class Generator3D
{
    /// <summary>Plain plates for the hidden base layer, e.g. SM_Env_Rock_Flat_01-04.</summary>
    GameObject[] CaveBasePrefabs => profile.caveKitFloorPrefabs;

    readonly HashSet<Vector3Int> baseJunctions = new HashSet<Vector3Int>();

    void LayCaveGround()
    {
        baseJunctions.Clear();
        foreach (var cave in caves)
        {
            // Each storey the cave has floor on is its own ground - a chasm has two.
            var levels = new Dictionary<int, HashSet<Vector3Int>>();
            foreach (var cell in cave.Cells)
            {
                if (grid[cell] != CellType.Cave) continue;
                if (!levels.TryGetValue(cell.y, out var set)) levels[cell.y] = set = new HashSet<Vector3Int>();
                set.Add(cell);
            }

            foreach (var level in levels.Values)
            {
                LayBase(level);
                CoverWithPlates(level, chasms.Contains(cave));
            }
        }

        Physics.SyncTransforms();
    }

    /// <summary>One plain plate per cell, cut to the cell, just under the walking surface.</summary>
    void LayBase(HashSet<Vector3Int> floor)
    {
        if (CaveBasePrefabs == null || CaveBasePrefabs.Length == 0) return;

        foreach (var cell in Sorted(floor))
        {
            var prefab = PickPrefab(CaveBasePrefabs);
            if (prefab == null) continue;

            // A plate is an irregular blob in a square box, so it is oversized into rock to close its
            // corners - but cut exactly to the cell line wherever the neighbour is not rock.
            // Toward a corridor or room it still laps a little: it sits under that floor, and the
            // plate's ragged edge would otherwise leave slits of void along the join. Stairs
            // shape their own ground.
            float Lap(Vector3Int dir)
            {
                var n = cell + dir;
                if (IsGroundSpill(n, cell.y, floor)) return 1.2f;
                var kind = grid[n];
                if (kind == CellType.Stairs) return 0f;
                // Into a river only as far as its bank wall, which stands on the line.
                return kind == CellType.Water ? 0.35f : 0.7f;
            }
            float west = Lap(Vector3Int.left), east = Lap(Vector3Int.right);
            float south = Lap(new Vector3Int(0, 0, -1)), north = Lap(new Vector3Int(0, 0, 1));

            var size = new Vector3(WorldUnitSize + west + east, 0f, WorldUnitSize + south + north);
            var offset = new Vector3((east - west) * 0.5f, 0f, (north - south) * 0.5f);

            var plate = FitKitPiece(prefab, CellCentre(cell) + offset, Quaternion.identity, size, false);
            if (plate == null) continue;

            // Flattened so it can never reach the ceiling of a corridor under the cave.
            plate.transform.localScale = Vector3.Scale(plate.transform.localScale, new Vector3(1f, 0.6f, 1f));
            AlignSurface(plate, Floor(cell) - 0.06f, true);
            plate.name = "Cave floor base - " + prefab.name;
            RegisterGround(plate);

            // Every one of these is a blob in a square box, notched at its corners - and the
            // corners of four cells meet at one point, so the notches line up into a hole. A plate
            // centred on that point puts its solid middle over it. Each point once.
            for (int dx = 0; dx <= 1; dx++)
            for (int dz = 0; dz <= 1; dz++)
            {
            // Shared by every cave: two caves meeting at a corner would otherwise each lay one.
            if (!baseJunctions.Add(new Vector3Int(cell.x + dx, cell.y, cell.z + dz))) continue;
            var corner = FitKitPiece(PickPrefab(CaveBasePrefabs),
                CellCentre(cell) + new Vector3((dx - 0.5f) * WorldUnitSize, 0f, (dz - 0.5f) * WorldUnitSize),
                Quaternion.identity, new Vector3(WorldUnitSize + 0.6f, 0f, WorldUnitSize + 0.6f), false);
            if (corner != null)
            {
                corner.transform.localScale = Vector3.Scale(corner.transform.localScale, new Vector3(1f, 0.6f, 1f));
                AlignSurface(corner, Floor(cell) - 0.14f, true);
                if (ReachesIntoBuilt(corner, 0.4f)) DestroyImmediate(corner);
                else
                {
                    corner.name = "Cave floor base - " + corner.name.Replace("(Clone)", "");
                    RegisterGround(corner);
                }
            }
            }
        }
    }

    /// <summary>
    /// Covers a level with the big plates, deepest cells first: each plate is centred on whichever
    /// uncovered cell is furthest from anything a plate may not cross, and sized to that distance.
    /// </summary>
    void CoverWithPlates(HashSet<Vector3Int> floor, bool chasm)
    {
        var plates = profile.caveGroundPlates;
        if (plates == null || plates.Length == 0) return;

        var depth = ClearanceField(floor);
        var uncovered = new HashSet<Vector3Int>(floor);
        int index = 0;

        while (uncovered.Count > 0)
        {
            Vector3Int best = default;
            int bestDepth = -1;
            foreach (var cell in Sorted(uncovered))
                if (depth[cell] > bestDepth) { bestDepth = depth[cell]; best = cell; }

            // Radius out to the nearest forbidden cell, plus most of the cell the plate is centred on.
            float radius = Mathf.Clamp(bestDepth * WorldUnitSize + HalfWorldUnit * 0.9f, 2.4f, 16f);
            Vector3 centre = CellCentre(best);
            if (bestDepth > 0) centre += new Vector3(((float)random.NextDouble() - 0.5f) * 2f, 0f, ((float)random.NextDouble() - 0.5f) * 2f);

            GameObject plate = null;
            for (int attempt = 0; attempt < 5 && plate == null; attempt++, radius *= 0.8f)
            {
                var prefab = PickPrefab(plates);
                if (prefab == null) break;

                plate = FitKitPiece(prefab, centre, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f),
                    new Vector3(radius * 2f, 0f, radius * 2f), true);
                if (plate == null) break;

                // The demo's plates are a hand's width to half a metre thick at walking height.
                float thickness = Mathf.Min(0.5f, WorldBoundsHeight(plate));
                ScaleToHeight(plate, thickness);
                AlignSurface(plate, Floor(best) + 0.01f * (index % 4), true);

                if (ReachesIntoBuilt(plate, 0.4f) || HangsOverOtherSpace(plate, floor))
                {
                    DestroyImmediate(plate);
                    plate = null;
                }
            }

            uncovered.Remove(best);
            if (plate == null) continue;

            plate.name = "Cave ground - " + plate.name.Replace("(Clone)", "");
            RegisterGround(plate);
            index++;

            // The plate is an irregular blob: only cells well inside its inscribed circle count as covered.
            float covered = radius * 0.8f - HalfWorldUnit;
            foreach (var cell in Sorted(uncovered))
                if (FlatDistance(CellCentre(cell), centre) <= covered) uncovered.Remove(cell);
        }

        if (!chasm) DressGround(floor, depth);
    }

    /// <summary>
    /// For each floor cell, how many cells out the nearest cell a plate may not lap over is.
    /// Rock may be lapped - the wall stands in it - but nothing else: a corridor, a room, the open
    /// air of a lower cave.
    /// </summary>
    Dictionary<Vector3Int, int> ClearanceField(HashSet<Vector3Int> floor)
    {
        var depth = new Dictionary<Vector3Int, int>();
        var queue = new Queue<Vector3Int>();

        foreach (var cell in floor)
        {
            bool edge = false;
            for (int dx = -1; dx <= 1 && !edge; dx++)
            for (int dz = -1; dz <= 1 && !edge; dz++)
            {
                var n = cell + new Vector3Int(dx, 0, dz);
                if (!floor.Contains(n) && !IsGroundSpill(n, cell.y, floor)) edge = true;
            }

            if (edge) { depth[cell] = 0; queue.Enqueue(cell); }
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var n = cell + new Vector3Int(dx, 0, dz);
                if (!floor.Contains(n) || depth.ContainsKey(n)) continue;
                depth[n] = depth[cell] + 1;
                queue.Enqueue(n);
            }
        }

        // A level with nothing it may not cross is one open field.
        foreach (var cell in floor)
            if (!depth.ContainsKey(cell)) depth[cell] = 3;

        return depth;
    }

    /// <summary>True when ground may lap into this cell: it is rock, or off the grid.</summary>
    bool IsGroundSpill(Vector3Int cell, int y, HashSet<Vector3Int> floor)
    {
        if (floor.Contains(cell)) return true;
        if (!grid.InBounds(cell)) return true;
        return grid[cell] == CellType.None;
    }

    /// <summary>True when part of a plate lies over a cell that is neither this level's floor nor rock.</summary>
    bool HangsOverOtherSpace(GameObject plate, HashSet<Vector3Int> floor)
    {
        if (!WorldBounds(plate, out Bounds b)) return false;

        int y = Mathf.FloorToInt((b.max.y - FloorSurfaceOffset + 0.1f) / WorldUnitSize);
        Vector3 c = b.center;
        float r = Mathf.Min(b.extents.x, b.extents.z) * 0.85f;

        // The blob's solid part: sampled on rings rather than over the whole box.
        for (int ring = 1; ring <= 3; ring++)
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6f;
                float d = r * ring / 3f;
                var cell = new Vector3Int(Mathf.FloorToInt((c.x + Mathf.Cos(a) * d) / WorldUnitSize), y,
                    Mathf.FloorToInt((c.z + Mathf.Sin(a) * d) / WorldUnitSize));
                if (!IsGroundSpill(cell, y, floor)) return true;
            }

        return false;
    }

    /// <summary>
    /// Like IntrudesOnBuiltSpace, but a piece may dip <paramref name="slack"/> metres into the
    /// storey below - a floor plate's underside does that over every corridor ceiling - and
    /// edges exactly on a cell line do not count.
    /// </summary>
    bool ReachesIntoBuilt(GameObject go, float slack)
    {
        if (!WorldBounds(go, out Bounds bounds)) return false;

        const float edge = 0.05f;
        int xMin = Mathf.FloorToInt((bounds.min.x + edge) / WorldUnitSize);
        int xMax = Mathf.FloorToInt((bounds.max.x - edge) / WorldUnitSize);
        int zMin = Mathf.FloorToInt((bounds.min.z + edge) / WorldUnitSize);
        int zMax = Mathf.FloorToInt((bounds.max.z - edge) / WorldUnitSize);

        for (int x = xMin; x <= xMax; x++)
        for (int z = zMin; z <= zMax; z++)
        for (int y = 0; y < size.y; y++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!grid.InBounds(cell) || !IsBuiltSpace(cell)) continue;

            float floor = y * WorldUnitSize + FloorSurfaceOffset;
            if (bounds.max.y <= floor + 0.02f || bounds.min.y >= floor + WorldUnitSize - slack) continue;
            return true;
        }

        return false;
    }

    float Floor(Vector3Int cell) => cell.y * WorldUnitSize + FloorSurfaceOffset;

    static float WorldBoundsHeight(GameObject go) => WorldBounds(go, out Bounds b) ? b.size.y : 0f;

    static void ScaleToHeight(GameObject go, float height)
    {
        float current = WorldBoundsHeight(go);
        if (current < 0.01f) return;
        var s = go.transform.localScale;
        go.transform.localScale = new Vector3(s.x, s.y * height / current, s.z);
    }

    // ------------------------------------------------------------------ mounds

    /// <summary>
    /// The demo crystal cave's ground detail: a few low mounds - a hand's width proud, never a
    /// step - on the open floor, and rough mound clusters and edge mounds against the walls where
    /// rock meets ground. Graded: a big mound, a couple of medium, a spray of small.
    /// </summary>
    void DressGround(HashSet<Vector3Int> floor, Dictionary<Vector3Int, int> depth)
    {
        var cells = Sorted(floor);

        if (profile.grottoMounds != null && profile.grottoMounds.Length > 0)
        {
            int mounds = Mathf.Max(1, cells.Count / 6);
            for (int i = 0; i < mounds; i++)
            {
                var cell = cells[random.Next(cells.Count)];
                if (depth[cell] < 1) continue;

                float span = i == 0 ? 10f + (float)random.NextDouble() * 4f : 5f + (float)random.NextDouble() * 4f;
                Vector3 at = CellCentre(cell) + new Vector3(((float)random.NextDouble() - 0.5f) * 3f, 0f, ((float)random.NextDouble() - 0.5f) * 3f);

                var mound = FitKitPiece(PickPrefab(profile.grottoMounds), at, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f),
                    new Vector3(span, 0f, span), true);
                if (mound == null) continue;

                ScaleToHeight(mound, 0.25f + (float)random.NextDouble() * 0.2f);
                AlignSurface(mound, Floor(cell) - 0.1f, false);

                if (ReachesIntoBuilt(mound, 0.4f) || HangsOverOtherSpace(mound, floor)) { DestroyImmediate(mound); continue; }
                mound.name = "Cave mound - " + mound.name.Replace("(Clone)", "");
                RegisterGround(mound);
            }
        }

        // Rough mounds banked against the foot of the wall.
        if (profile.caveWallMounds != null && profile.caveWallMounds.Length > 0)
            foreach (var cell in cells)
                foreach (var dir in Directions)
                {
                    if (dir.y != 0 || !IsKitWallFace(cell, dir) || random.NextDouble() > 0.4) continue;

                    Vector3 wall = CellCentre(cell) + new Vector3(dir.x, 0f, dir.z) * (HalfWorldUnit - 0.6f);
                    Vector3 along = new Vector3(dir.z, 0f, dir.x);

                    foreach (var tier in ScatterTiers(random.Next(1, 3), random.Next(1, 3)))
                    {
                        Vector3 at = wall + along * (((float)random.NextDouble() - 0.5f) * 4f);
                        var mound = Spawn(PickPrefab(profile.caveWallMounds), at,
                            Quaternion.LookRotation(-new Vector3(dir.x, 0f, dir.z)) * Quaternion.Euler(0f, ((float)random.NextDouble() - 0.5f) * 40f, 0f));
                        if (mound == null) continue;

                        mound.transform.localScale *= 1.4f * tier.scale + 0.6f;
                        AlignSurface(mound, Floor(cell) - 0.15f, false);
                        if (ReachesIntoBuilt(mound, 0.4f)) { DestroyImmediate(mound); continue; }
                        mound.name = "Cave wall mound - " + mound.name.Replace("(Clone)", "");
                    }
                }
    }

    // ------------------------------------------------------------------ ceilings

    /// <summary>
    /// Cave ceilings, as the demo hangs them: the same cracked-rock plates as the floor, turned
    /// upside down, two to six metres thick and overlapping at different depths, so the roof is a
    /// jumble of rock masses rather than one smooth slab. The flat roof slabs stay above as a
    /// backing that closes any gap between them.
    /// </summary>
    void LayCaveCeilings()
    {
        var plates = profile.caveGroundPlates;
        if (plates == null || plates.Length == 0) return;

        foreach (var cave in caves)
        {
            // Cells grouped by the height of the roof over them - a chasm's ledge and pit share one.
            var groups = new Dictionary<int, List<Vector3Int>>();
            foreach (var cell in Sorted(cave.Cells))
            {
                if (grid[cell] != CellType.Cave) continue;
                int top = cell.y + KitCavernHeight(cell);
                if (!groups.TryGetValue(top, out var list)) groups[top] = list = new List<Vector3Int>();
                list.Add(cell);
            }

            foreach (var group in groups)
            {
                float underside = group.Key * WorldUnitSize + FloorSurfaceOffset;
                // Never lower than three and a half metres over the highest floor under it.
                float lowestFloor = float.MinValue;
                foreach (var cell in group.Value) lowestFloor = Mathf.Max(lowestFloor, Floor(cell));
                float maxDrop = Mathf.Max(0.6f, underside - lowestFloor - 3.5f);

                var uncovered = new HashSet<Vector3Int>(group.Value);
                var order = new List<Vector3Int>(group.Value);
                int index = 0;
                foreach (var start in order)
                {
                    if (!uncovered.Contains(start)) continue;

                    Vector3 centre = CellCentre(start) + new Vector3(((float)random.NextDouble() - 0.5f) * 3f, 0f, ((float)random.NextDouble() - 0.5f) * 3f);
                    float radius = 7f + (float)random.NextDouble() * 5f;

                    GameObject plate = null;
                    for (int attempt = 0; attempt < 4 && plate == null; attempt++, radius *= 0.7f)
                    {
                        plate = FitKitPiece(PickPrefab(plates), centre + Vector3.up * underside,
                            Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 180f), new Vector3(radius * 2f, 0f, radius * 2f), true);
                        if (plate == null) break;

                        // Thick and lumpy, hanging a little lower or higher each time.
                        ScaleToHeight(plate, Mathf.Min(maxDrop + 1f, 1.5f + (float)random.NextDouble() * 3f));
                        float drop = Mathf.Min(maxDrop, (float)random.NextDouble() * 1.4f);
                        AlignSurface(plate, underside - drop, false);

                        if (ReachesIntoBuilt(plate, 0.4f) || HangsUnderHigherRoof(plate, underside))
                        {
                            DestroyImmediate(plate);
                            plate = null;
                        }
                    }

                    uncovered.Remove(start);
                    if (plate == null) continue;

                    plate.name = "Cave ceiling - " + plate.name.Replace("(Clone)", "");
                    RegisterRoof(plate);
                    index++;

                    float covered = radius * 0.8f - HalfWorldUnit;
                    foreach (var cell in group.Value)
                        if (FlatDistance(CellCentre(cell), centre) <= covered) uncovered.Remove(cell);
                }
            }
        }

        Physics.SyncTransforms();
        PatchOpenRoofs();
    }

    /// <summary>
    /// Wherever a player standing in a cave could look straight up into the void - the roof slab
    /// there was dropped for crowding a room above, and no ceiling plate fitted - a smaller roof
    /// piece is fitted to that one cell.
    /// </summary>
    void PatchOpenRoofs()
    {
        var roofs = profile.caveKitRoofPrefabs;
        if (roofs == null || roofs.Length == 0) return;
        int patched = 0, left = 0;

        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!IsCaveColumnBase(cell)) continue;

            float floor = Floor(cell);
            Vector3 c = CellCentre(cell);
            // Nine points over the cell; the patch is centred on the first one open to the sky.
            bool open = false;
            float ox = 0f, oz = 0f;
            foreach (float dx in new[] { 0f, -1.9f, 1.9f })
            foreach (float dz in new[] { 0f, -1.9f, 1.9f })
            {
                if (open) break;
                if (VisibleHit(new Vector3(c.x + dx, floor + 1.6f, c.z + dz), Vector3.up, 60f)) continue;
                open = true;
                ox = dx * 0.5f;
                oz = dz * 0.5f;
            }
            if (!open) continue;
            c += new Vector3(ox, 0f, oz);

            float underside = (cell.y + KitCavernHeight(cell)) * WorldUnitSize + FloorSurfaceOffset;
            GameObject slab = null;
            // Last rung: a thin plate cut to the cell with its top on the storey line, which fits
            // under a room or corridor beside the cave one storey up.
            foreach (var (span, depth, flush) in new[] { (7f, 0f, false), (5.6f, 0f, false), (5.6f, 0.5f, false), (5f, 0.3f, true) })
            {
                slab = FitKitPiece(PickPrefab(roofs), new Vector3(c.x, underside, c.z), Quaternion.Euler(0f, random.Next(4) * 90f, 0f),
                    new Vector3(span, depth, span), false);
                if (slab == null) break;
                if (flush) AlignSurface(slab, underside, true);
                else AlignSurface(slab, underside - 0.1f, false);
                if (!ReachesIntoBuilt(slab, 0.4f)) break;
                DestroyImmediate(slab);
                slab = null;
            }

            if (slab == null) { left++; continue; }
            slab.name = "Cave roof patch - " + slab.name.Replace("(Clone)", "");
            RegisterRoof(slab);
            patched++;
            Physics.SyncTransforms();
        }

        // Then at an angle: a player looks up across the cave, and a sight-line at sixty degrees
        // finds the slot between two roof pieces that one straight up never meets. A patch goes
        // where that sight-line crosses the roof line.
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!IsCaveColumnBase(cell)) continue;
            Vector3 eye = CellCentre(cell) + Vector3.up * (FloorSurfaceOffset + 1.6f);

            for (int k = 0; k < 8; k++)
            {
                var flat = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                var look = Quaternion.AngleAxis(-60f, Vector3.Cross(Vector3.up, flat)) * flat;
                if (VisibleHit(eye, look, 60f)) continue;

                // Up the sight-line to the first cell of rock above cave air: that is the roof line.
                Vector3Int last = cell;
                Vector3? crossing = null;
                for (float t = 0.25f; t < 40f; t += 0.25f)
                {
                    var p = eye + look * t;
                    var here = new Vector3Int(Mathf.FloorToInt(p.x / WorldUnitSize), Mathf.FloorToInt((p.y - FloorSurfaceOffset) / WorldUnitSize), Mathf.FloorToInt(p.z / WorldUnitSize));
                    if (here == last) continue;
                    if (IsRock(here))
                    {
                        if (here.y > last.y) crossing = p;
                        break;
                    }
                    last = here;
                }
                if (crossing == null) continue;

                float roofLine = (last.y + 1) * WorldUnitSize + FloorSurfaceOffset;
                var at = new Vector3(crossing.Value.x, roofLine, crossing.Value.z);
                GameObject slab = null;
                foreach (var (span, flush) in new[] { (5.6f, false), (4f, true) })
                {
                    slab = FitKitPiece(PickPrefab(roofs), at, Quaternion.Euler(0f, random.Next(4) * 90f, 0f),
                        new Vector3(span, flush ? 0.3f : 0f, span), false);
                    if (slab == null) break;
                    if (flush) AlignSurface(slab, roofLine + 0.25f, true);
                    else AlignSurface(slab, roofLine - 0.1f, false);
                    if (!ReachesIntoBuilt(slab, 0.4f)) break;
                    DestroyImmediate(slab);
                    slab = null;
                }

                if (slab == null) { left++; continue; }
                slab.name = "Cave roof patch - " + slab.name.Replace("(Clone)", "");
                RegisterRoof(slab);
                patched++;
                Physics.SyncTransforms();
            }
        }

        Debug.Log($"Cave roof patches: {patched} gaps closed, {left} could not be.", this);
    }

    /// <summary>
    /// True when part of a ceiling plate spreads under a stretch of cave whose roof is higher -
    /// where it would hang in mid-air across the taller space instead of against rock.
    /// </summary>
    bool HangsUnderHigherRoof(GameObject plate, float underside)
    {
        if (!WorldBounds(plate, out Bounds b)) return false;
        int storey = Mathf.FloorToInt((underside - 0.1f - FloorSurfaceOffset) / WorldUnitSize);
        float r = Mathf.Min(b.extents.x, b.extents.z) * 0.85f;

        for (int ring = 0; ring <= 3; ring++)
        for (int k = 0; k < (ring == 0 ? 1 : 12); k++)
        {
            float angle = k * Mathf.PI / 6f, d = r * ring / 3f;
            var column = new Vector3Int(Mathf.FloorToInt((b.center.x + Mathf.Cos(angle) * d) / WorldUnitSize), storey,
                Mathf.FloorToInt((b.center.z + Mathf.Sin(angle) * d) / WorldUnitSize));
            if (!grid.InBounds(column)) continue;
            if (grid[column] != CellType.Cave && grid[column] != CellType.CaveOpen) continue;

            // The roof over that column: up through its open air.
            var top = column;
            while (grid.InBounds(top + Vector3Int.up) && grid[top + Vector3Int.up] == CellType.CaveOpen) top += Vector3Int.up;
            float roof = (top.y + 1) * WorldUnitSize + FloorSurfaceOffset;
            if (roof > underside + 0.5f) return true;
        }

        return false;
    }

    /// <summary>
    /// Repairs exactly what a player could see out of. From crouched and standing eye height over
    /// every cave floor cell, the seal check's sight-lines go out; each one that reaches the void
    /// is followed to where it leaves the cave, and rock goes there - a roof piece if it left up
    /// through the roof, a wall piece if out through a wall or a corner, a floor plate if down.
    /// Runs once the whole dungeon is built, so a sight-line out through a doorway meets the room
    /// beyond rather than being taken for a leak.
    /// </summary>
    void RepairSightLeaks()
    {
        if (profile == null || !UsingCaveKit) return;
        Physics.SyncTransforms();
        drawnCache.Clear();

        var directions = new List<Vector3>();
        for (int k = 0; k < 16; k++)
        {
            var flat = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
            var side = Vector3.Cross(Vector3.up, flat);
            directions.Add(flat);
            directions.Add(Quaternion.AngleAxis(-30f, side) * flat);
            directions.Add(Quaternion.AngleAxis(-60f, side) * flat);
            directions.Add(Quaternion.AngleAxis(30f, side) * flat);
        }
        directions.Add(Vector3.up);
        directions.Add(Vector3.down);

        int repaired = 0, failed = 0;
        var done = new HashSet<Vector3Int>();
        // A second pass only if the first changed anything - a new patch can open the view to
        // a gap behind it.
        for (int pass = 0, before = -1; pass < 2 && repaired != before; pass++)
        {
        before = repaired;
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!IsCaveColumnBase(cell)) continue;
            float floor = Floor(cell);

            foreach (var (ox, oz) in new[] { (0f, 0f), (1.6f, 1.6f), (-1.6f, 1.6f), (1.6f, -1.6f), (-1.6f, -1.6f) })
            foreach (float h in new[] { 0.95f, 1.62f })
            {
                var eye = new Vector3((x + 0.5f) * WorldUnitSize + ox, floor + h, (z + 0.5f) * WorldUnitSize + oz);
                if (Physics.CheckSphere(eye, 0.15f, ~0, QueryTriggerInteraction.Ignore)) continue;

                foreach (var look in directions)
                {
                    if (VisibleHit(eye, look, 300f)) continue;
                    if (!FindExit(eye, look, out Vector3Int from, out Vector3Int step, out Vector3 crossing)) continue;

                    // One repair per crossing spot, metre by metre.
                    if (!done.Add(Vector3Int.RoundToInt(crossing))) continue;
                    if (PlugExit(from, step, crossing)) repaired++;
                    else failed++;
                }
            }
        }
        }

        Debug.Log($"Cave sight repair: {repaired} gaps closed where a player could see out, {failed} could not be.", this);
    }

    /// <summary>Follows a sight-line out of the cave to the first rock cell: where, and which way, it left.</summary>
    bool FindExit(Vector3 eye, Vector3 look, out Vector3Int from, out Vector3Int step, out Vector3 crossing)
    {
        from = step = default;
        crossing = default;
        Vector3Int last = CellOfPoint(eye);
        for (float t = 0.1f; t < 80f; t += 0.1f)
        {
            var p = eye + look * t;
            var here = CellOfPoint(p);
            if (here == last) continue;
            if (IsRock(here))
            {
                // Only gaps in the cave's own rock: built space closes its own sides.
                var kind = grid.InBounds(last) ? grid[last] : CellType.None;
                if (kind != CellType.Cave && kind != CellType.CaveOpen && kind != CellType.Water) return false;
                from = last;
                step = here - last;
                crossing = p;
                return true;
            }
            last = here;
        }
        return false;
    }

    Vector3Int CellOfPoint(Vector3 p) => new Vector3Int(
        Mathf.FloorToInt(p.x / WorldUnitSize), Mathf.FloorToInt((p.y - FloorSurfaceOffset) / WorldUnitSize), Mathf.FloorToInt(p.z / WorldUnitSize));

    /// <summary>Stands rock where a sight-line left the cave. False if nothing fits there.</summary>
    bool PlugExit(Vector3Int from, Vector3Int step, Vector3 crossing)
    {
        float bottom = from.y * WorldUnitSize + FloorSurfaceOffset;

        // Up through the roof: a roof piece over the spot.
        if (step.y > 0)
        {
            float roofLine = (from.y + 1) * WorldUnitSize + FloorSurfaceOffset;
            foreach (var (span, flush) in new[] { (5f, false), (3.5f, true) })
            {
                var slab = FitKitPiece(PickPrefab(profile.caveKitRoofPrefabs), new Vector3(crossing.x, roofLine, crossing.z),
                    Quaternion.Euler(0f, random.Next(4) * 90f, 0f), new Vector3(span, flush ? 0.3f : 0f, span), false);
                if (slab == null) return false;
                if (flush) AlignSurface(slab, roofLine + 0.25f, true);
                else AlignSurface(slab, roofLine - 0.1f, false);
                if (ReachesIntoBuilt(slab, 0.4f)) { DestroyImmediate(slab); continue; }
                slab.name = "Cave roof patch - " + slab.name.Replace("(Clone)", "");
                RegisterRoof(slab);
                Physics.SyncTransforms();
                return true;
            }
            return false;
        }

        // Down through the floor: a plate under the spot.
        if (step.y < 0)
        {
            var plate = FitKitPiece(PickPrefab(CaveBasePrefabs), new Vector3(crossing.x, bottom, crossing.z), Quaternion.identity,
                new Vector3(3.5f, 0f, 3.5f), false);
            if (plate == null) return false;
            plate.transform.localScale = Vector3.Scale(plate.transform.localScale, new Vector3(1f, 0.6f, 1f));
            AlignSurface(plate, bottom - 0.05f, true);
            if (ReachesIntoBuilt(plate, 0.4f)) { DestroyImmediate(plate); return false; }
            plate.name = "Cave floor patch - " + plate.name.Replace("(Clone)", "");
            RegisterGround(plate);
            Physics.SyncTransforms();
            return true;
        }

        // Out through a wall or a corner: a rock panel standing just outside the spot, facing
        // back into the cave along the way the sight-line left.
        var outward = new Vector3(step.x, 0f, step.z).normalized;
        foreach (var (width, height, thin) in new[] { (4f, 5f, false), (3f, 4f, false), (2.5f, 3f, true) })
        {
            var panel = FitKitPiece(PickPrefab(profile.caveKitWallPrefabs), crossing, Quaternion.LookRotation(-outward, Vector3.up),
                new Vector3(width, height, thin ? 0.6f : 2f), !thin);
            if (panel == null) return false;
            PushBehindPlane(panel, crossing - outward * 0.3f, -outward);
            if (ReachesIntoBuilt(panel, 0.35f)) { DestroyImmediate(panel); continue; }
            panel.name = "Cave wall sight patch - " + panel.name.Replace("(Clone)", "");
            Physics.SyncTransforms();
            return true;
        }
        return false;
    }

    /// <summary>
    /// The bottom of a column of cave air: a cave floor, or a river cut through one - which has
    /// cave air over it and needs a roof just the same.
    /// </summary>
    bool IsCaveColumnBase(Vector3Int cell)
    {
        if (grid[cell] == CellType.Cave) return true;
        var above = cell + Vector3Int.up;
        return grid[cell] == CellType.Water && grid.InBounds(above) && grid[above] == CellType.CaveOpen;
    }

    bool IsRock(Vector3Int cell) => !grid.InBounds(cell) || grid[cell] == CellType.None;

    // ------------------------------------------------------------------ settling

    /// <summary>
    /// The last word on cave props: anything in a cave that neither stands on something nor
    /// hangs from something is removed. Every placement pass casts for its ground, but a cast
    /// can land on a piece that a later pass threw away, or on the far side of a plate's edge -
    /// and one floating rock reads worse than ten missing ones.
    /// </summary>
    void SettleCaveProps()
    {
        Physics.SyncTransforms();
        int removed = 0;
        var doomed = new List<GameObject>();

        foreach (Transform child in dungeonRoot)
        {
            var go = child.gameObject;
            if (go.name.StartsWith("Cave ") || go.name.StartsWith("Chasm") || go.name.StartsWith("Cavern")) continue;
            if (go.GetComponentInChildren<Light>() != null) continue;
            if (!WorldBounds(go, out Bounds b)) continue;

            var cell = new Vector3Int(Mathf.FloorToInt(b.center.x / WorldUnitSize),
                Mathf.FloorToInt((b.min.y - FloorSurfaceOffset + 0.5f) / WorldUnitSize),
                Mathf.FloorToInt(b.center.z / WorldUnitSize));
            if (!grid.InBounds(cell) || (grid[cell] != CellType.Cave && grid[cell] != CellType.CaveOpen)) continue;

            var own = new HashSet<Collider>(go.GetComponentsInChildren<Collider>());
            if (Supported(b, own)) continue;

            doomed.Add(go);
        }

        foreach (var go in doomed) { DestroyImmediate(go); removed++; }

        // Whatever pass placed it, growth and litter are walked through, never around.
        foreach (Transform child in dungeonRoot)
            if (IsGroundClutter(child.name)) MakeWalkThrough(child.gameObject);
        if (removed > 0) Debug.Log($"Cave settle: removed {removed} unsupported props.", this);
    }

    /// <summary>True when something other than the object itself is just under its foot or just over its top.</summary>
    static bool Supported(Bounds b, HashSet<Collider> own)
    {
        foreach (var (sx, sz) in new[] { (0f, 0f), (0.4f, 0.4f), (-0.4f, 0.4f), (0.4f, -0.4f), (-0.4f, -0.4f) })
        {
            float x = b.center.x + sx * b.extents.x, z = b.center.z + sz * b.extents.z;
            // Cast from the middle of the object, so one sunk into the ground still finds it.
            if (HitsOther(new Ray(new Vector3(x, b.center.y, z), Vector3.down), b.extents.y + 0.6f, own)) return true;
            if (HitsOther(new Ray(new Vector3(x, b.center.y, z), Vector3.up), b.extents.y + 0.6f, own)) return true;
        }
        return false;
    }

    static bool HitsOther(Ray ray, float distance, HashSet<Collider> own)
    {
        foreach (var hit in Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore))
            if (!own.Contains(hit.collider)) return true;
        return false;
    }
}
