using System.Collections.Generic;
using Delver.Dungeon;
using UnityEngine;

/// <summary>
/// Lines the foot of every cave wall with clusters, the way the demo dresses its caves.
///
/// The demo's caves are full at the edges and open in the middle: you walk down a clear floor
/// between banks of stalagmites, crystals and mushrooms, or past goblin fences and bone heaps,
/// with a light every few metres. A cluster here is one big piece against the wall, a couple of
/// medium round it and a spray of small spilling out - and every piece is cast onto real ground.
/// </summary>
public partial class Generator3D
{
    void DressCaveEdges()
    {
        if (profile == null || profile.caveEdges == null || profile.caveEdges.Length == 0) return;
        Physics.SyncTransforms();

        int faces = 0, placed = 0;
        foreach (var cave in caves)
        {
            CaveTheme theme = chasms.Contains(cave) ? CaveTheme.Inhabited : IsLushCave(cave) ? CaveTheme.Lush : CaveTheme.Any;
            var set = PickEdgeSet(theme);
            if (set == null) continue;

            int clusters = 0;
            foreach (var cell in Sorted(cave.Cells))
            {
                if (grid[cell] != CellType.Cave || chasmCells.Contains(cell)) continue;
                // Clearings and their lanes run through the middle of a cave; its edges are still
                // edges. Only a way in, and a stairwell, must be left clear.
                if (NearMouth(cell) || TouchesStairs(cell)) continue;

                foreach (var dir in Directions)
                {
                    if (dir.y != 0 || !IsKitWallFace(cell, dir)) continue;
                    faces++;
                    if (random.NextDouble() > set.chance) continue;
                    if (!EdgeCluster(set, cell, dir)) continue;

                    placed++;
                    if (set.lightEvery > 0 && ++clusters % set.lightEvery == 0) LightCluster(set, cell, dir);
                }
            }
        }

        LogEdges(faces, placed);
    }

    bool NearMouth(Vector3Int cell)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var n = cell + new Vector3Int(dx, 0, dz);
            if (grid.InBounds(n) && IsBuiltSpace(n)) return true;
        }
        return false;
    }

    void LogEdges(int faces, int placed) => Debug.Log($"Cave edges: {placed} clusters on {faces} wall faces.", this);

    CaveEdgeSet PickEdgeSet(CaveTheme theme)
    {
        CaveEdgeSet fallback = null;
        foreach (var set in profile.caveEdges)
        {
            if (set == null) continue;
            if (set.theme == theme) return set;
            if (set.theme == CaveTheme.Any) fallback = set;
        }
        return fallback;
    }

    /// <summary>One graded cluster against the wall face. False if nothing could be stood.</summary>
    bool EdgeCluster(CaveEdgeSet set, Vector3Int cell, Vector3Int dir)
    {
        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 along = new Vector3(dir.z, 0f, dir.x);
        Vector3 foot = CellCentre(cell) + outward * (HalfWorldUnit - 1.3f) + along * (((float)random.NextDouble() - 0.5f) * 3f);
        float floor = Floor(cell);

        bool any = EdgePiece(set.big, foot, outward, floor, 0.9f + (float)random.NextDouble() * 0.4f, false);

        int mediums = random.Next(1, 3);
        for (int i = 0; i < mediums; i++)
        {
            Vector3 at = foot + along * (((float)random.NextDouble() - 0.5f) * 3.5f) - outward * (float)random.NextDouble() * 1.2f;
            any |= EdgePiece(set.medium, at, outward, floor, 0.7f + (float)random.NextDouble() * 0.5f, false);
        }

        int smalls = random.Next(2, 5);
        for (int i = 0; i < smalls; i++)
        {
            Vector3 at = foot + along * (((float)random.NextDouble() - 0.5f) * 4.5f) - outward * (0.5f + (float)random.NextDouble() * 2f);
            any |= EdgePiece(set.small, at, outward, floor, 0.6f + (float)random.NextDouble() * 0.6f, true);
        }

        return any;
    }

    bool EdgePiece(GameObject[] pool, Vector3 at, Vector3 outward, float floor, float scale, bool walkThrough)
    {
        var prefab = PickPrefab(pool);
        if (prefab == null) return false;

        var ray = new Ray(new Vector3(at.x, floor + 1.5f, at.z), Vector3.down);
        if (!CaveGroundCast(ray, out RaycastHit hit, 2.5f) || hit.normal.y < 0.7f) return false;
        if (Mathf.Abs(hit.point.y - floor) > 0.6f) return false;

        // Facing out of the wall, give or take - a fence or a tent reads wrong side-on to it.
        var rotation = Quaternion.LookRotation(-outward) * Quaternion.Euler(0f, ((float)random.NextDouble() - 0.5f) * 70f, 0f);
        var piece = Spawn(prefab, hit.point, rotation);
        if (piece == null) return false;

        piece.transform.localScale *= scale;
        AlignSurface(piece, hit.point.y - 0.05f, false);

        if (ReachesIntoBuilt(piece, 0.4f))
        {
            DestroyImmediate(piece);
            return false;
        }

        if (walkThrough || IsGroundClutter(prefab.name)) MakeWalkThrough(piece);
        return true;
    }

    void LightCluster(CaveEdgeSet set, Vector3Int cell, Vector3Int dir)
    {
        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 at = CellCentre(cell) + outward * (HalfWorldUnit - 2f);
        float floor = Floor(cell);
        if (!CaveGroundCast(new Ray(new Vector3(at.x, floor + 1.5f, at.z), Vector3.down), out RaycastHit hit, 2.5f)) return;

        GameObject holder;
        if (set.lightProp != null)
        {
            holder = Spawn(set.lightProp, hit.point, Quaternion.LookRotation(-outward));
            if (holder == null) return;
            AlignSurface(holder, hit.point.y - 0.05f, false);
        }
        else
        {
            holder = new GameObject("Cave edge light");
            holder.transform.SetParent(dungeonRoot, false);
            holder.transform.position = hit.point;
        }

        var light = holder.AddComponent<Delver.Game.TorchLight>();
        light.FlameColor = set.lightColor;
        // Darker deeper, as everywhere - but never out, or a cave is a black box past the torch.
        light.BaseIntensity = set.lightIntensity * Mathf.Max(0.4f, LitFactor(cell));
        light.Range = set.lightRange;
        light.LocalOffset = new Vector3(0f, 1.5f, 0f);
    }

    // ------------------------------------------------------------------ columns

    /// <summary>
    /// Stands rock columns in the open middle of each floored cave. A generated cavern is a blob
    /// forty metres across; the demo's caves are passages ten or fifteen wide winding between
    /// masses of rock. Columns turn the one into the other.
    /// </summary>
    int columnSites, columnBlocked, columnIntruding;

    void RaiseCaveColumns()
    {
        columnSites = columnBlocked = columnIntruding = 0;
        if (profile == null || profile.caveColumnMasses == null || profile.caveColumnMasses.Length == 0) return;
        Physics.SyncTransforms();
        int raised = 0;

        foreach (var cave in caves)
        {
            if (cave.Shape != CaveShape.Chamber || chasms.Contains(cave)) continue;

            var floor = new HashSet<Vector3Int>();
            foreach (var cell in cave.Cells) if (grid[cell] == CellType.Cave) floor.Add(cell);
            var wallDistance = WallDistance(floor);

            var set = PickEdgeSet(IsLushCave(cave) ? CaveTheme.Lush : CaveTheme.Any);
            var placed = new List<Vector3Int>();
            int spacing = profile.caveColumnSpacing;

            while (true)
            {
                Vector3Int best = default;
                float bestScore = -1f;
                foreach (var cell in Sorted(floor))
                {
                    if (wallDistance[cell] < spacing - 1 || compositionReserved.Contains(cell)) continue;
                    float gap = float.MaxValue;
                    foreach (var p in placed) gap = Mathf.Min(gap, Vector3Int.Distance(p, cell));
                    if (gap < spacing) continue;
                    // Furthest from the other columns, then from the walls.
                    float score = Mathf.Min(gap, 99f) * 10f + wallDistance[cell];
                    if (score > bestScore) { bestScore = score; best = cell; }
                }

                if (bestScore < 0f) break;
                placed.Add(best);
                columnSites++;
                if (RaiseColumn(best, set)) raised++;
            }
        }

        Debug.Log($"Cave columns: {raised} raised; {columnSites} sites, {columnBlocked} blocked by something standing there, {columnIntruding} reaching into built space.", this);
    }

    /// <summary>Cells from each floor cell to the nearest cell that is not floor, counting diagonals.</summary>
    Dictionary<Vector3Int, int> WallDistance(HashSet<Vector3Int> floor)
    {
        var distance = new Dictionary<Vector3Int, int>();
        var queue = new Queue<Vector3Int>();
        foreach (var cell in floor)
        {
            bool edge = false;
            for (int dx = -1; dx <= 1 && !edge; dx++)
            for (int dz = -1; dz <= 1 && !edge; dz++)
                if (!floor.Contains(cell + new Vector3Int(dx, 0, dz))) edge = true;
            if (edge) { distance[cell] = 0; queue.Enqueue(cell); }
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var n = cell + new Vector3Int(dx, 0, dz);
                if (!floor.Contains(n) || distance.ContainsKey(n)) continue;
                distance[n] = distance[cell] + 1;
                queue.Enqueue(n);
            }
        }

        foreach (var cell in floor) if (!distance.ContainsKey(cell)) distance[cell] = 0;
        return distance;
    }

    /// <summary>One column: a mass of rock floor to roof, spires round it, the cave's own growth at its foot.</summary>
    bool RaiseColumn(Vector3Int cell, CaveEdgeSet set)
    {
        float floor = Floor(cell);
        float height = KitCavernHeight(cell) * WorldUnitSize + 1.5f;
        float width = 6f + (float)random.NextDouble() * 3f;
        Vector3 centre = CellCentre(cell) + new Vector3(((float)random.NextDouble() - 0.5f) * 2f, 0f, ((float)random.NextDouble() - 0.5f) * 2f);
        centre.y = floor;

        // Anything already standing there - a scene, a landmark - keeps its ground.
        var clear = new Vector3(width * 0.5f, 1f, width * 0.5f);
        foreach (var hit in Physics.OverlapBox(centre + Vector3.up * 1.5f, clear, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (!caveKitGround.Contains(hit) && !caveKitRoof.Contains(hit)) { columnBlocked++; return false; }

        var prefab = PickPrefab(profile.caveColumnMasses);
        float yaw = (float)random.NextDouble() * 360f;
        var pieces = new List<GameObject>();
        foreach (float turn in new[] { 0f, 180f })
        {
            var mass = FitKitPiece(prefab, centre + Vector3.up * (height * 0.5f), Quaternion.Euler(0f, yaw + turn, 0f), new Vector3(width, height, 0f), true);
            if (mass == null) continue;
            AlignSurface(mass, floor - 0.5f, false);
            pieces.Add(mass);
        }

        foreach (var mass in pieces)
            if (ReachesIntoBuilt(mass, 0f))
            {
                foreach (var m in pieces) DestroyImmediate(m);
                columnIntruding++;
                return false;
            }

        foreach (var mass in pieces) mass.name = "Cave column - " + prefab.name;
        Physics.SyncTransforms();

        // Spires against it, then the cave's growth - all cast onto the ground round the foot.
        int spires = random.Next(2, 4);
        for (int i = 0; i < spires; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var spire = PickPrefab(profile.caveColumnSpires);
            if (spire == null) break;
            EdgePiece(new[] { spire }, centre + dir * (width * 0.5f + 0.5f), -dir, floor, 2.5f + (float)random.NextDouble() * 2f, false);
        }

        if (set != null)
            for (int i = 0; i < 6; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                bool small = i >= 2;
                EdgePiece(small ? set.small : set.medium, centre + dir * (width * 0.5f + (small ? 1.5f : 0.8f)), -dir, floor,
                    0.7f + (float)random.NextDouble() * 0.5f, small);
            }

        return true;
    }
}
