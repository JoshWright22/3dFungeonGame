using System.Collections.Generic;
using UnityEngine;

public partial class Generator3D
{
    readonly HashSet<Vector3Int> compositionReserved = new HashSet<Vector3Int>();

    HashSet<Vector3Int> GrowLobedChamber(BoundsInt footprint)
    {
        var cells = new HashSet<Vector3Int>();
        float phase = (float)random.NextDouble() * 100f;
        float cx = footprint.xMin + footprint.size.x * 0.5f;
        float cz = footprint.zMin + footprint.size.z * 0.5f;
        for (int x = footprint.xMin + 1; x < footprint.xMax - 1; x++)
        for (int z = footprint.zMin + 1; z < footprint.zMax - 1; z++)
        {
            float dx = (x + 0.5f - cx) / (footprint.size.x * 0.43f);
            float dz = (z + 0.5f - cz) / (footprint.size.z * 0.43f);
            float angle = Mathf.Atan2(dz, dx);
            float edge = 1f + 0.12f * Mathf.Sin(angle * 3f + phase)
                + 0.08f * Mathf.Sin(angle * 5f - phase);
            if (dx * dx + dz * dz < edge * edge)
                cells.Add(new Vector3Int(x, footprint.yMin, z));
        }
        return LargestComponentOf(cells);
    }

    bool NaturalColumnClear(Vector3Int cell)
    {
        if (cell.x < 1 || cell.z < 1 || cell.x >= size.x - 1 || cell.z >= size.z - 1) return false;
        for (int h = 0; h < Mathf.Max(1, caveTunnelHeight); h++)
        {
            var c = cell + Vector3Int.up * h;
            if (!grid.InBounds(c)) return false;
            if (h == 0 ? grid[c] != CellType.None && grid[c] != CellType.Cave
                : grid[c] != CellType.None && grid[c] != CellType.CaveOpen) return false;
        }
        return true;
    }

    void ConnectNaturalCaverns()
    {
        var chambers = caves.FindAll(c => c.Shape == CaveShape.Chamber);
        for (int i = 1; i < chambers.Count; i++)
        {
            CaveRegion target = null;
            int distance = int.MaxValue;
            for (int j = 0; j < i; j++)
            {
                if (chambers[j].Anchor.y != chambers[i].Anchor.y) continue;
                int d = (chambers[j].Anchor - chambers[i].Anchor).sqrMagnitude;
                if (d < distance) { target = chambers[j]; distance = d; }
            }
            if (target == null) continue;
            var start = chambers[i].Anchor;
            var end = target.Anchor;
            var queue = new Queue<Vector3Int>();
            var parent = new Dictionary<Vector3Int, Vector3Int>();
            parent[start] = start; queue.Enqueue(start);
            // A fixed shuffled neighbour order keeps each route reproducible without a
            // random draw for every visited cell.
            var directions = new List<Vector3Int>(Shuffled(Directions));
            while (queue.Count > 0 && !parent.ContainsKey(end))
            {
                var cell = queue.Dequeue();
                foreach (var dir in directions)
                {
                    var next = cell + dir;
                    if (parent.ContainsKey(next) || !NaturalColumnClear(next)) continue;
                    parent[next] = cell; queue.Enqueue(next);
                }
            }
            if (!parent.ContainsKey(end)) continue;
            var carved = new HashSet<Vector3Int>();
            var cursor = end;
            while (cursor != start)
            {
                int radius = Mathf.Max(1, caveTunnelRadius);
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    var cell = cursor + new Vector3Int(dx, 0, dz);
                    if (NaturalColumnClear(cell) && grid[cell] == CellType.None) carved.Add(cell);
                }
                cursor = parent[cursor];
            }
            if (carved.Count > 0)
                CommitCave(carved, start.y, caveTunnelHeight, CaveShape.Tunnel, false);
        }
    }

    /// <summary>
    /// Holds every cave mouth, and the cave immediately behind it, clear of dressing.
    ///
    /// A stalagmite or a rock module standing in an approach reads as a wall however open the
    /// grid says the opening is, so this runs whichever way the cave was built.
    /// </summary>
    void ReserveCaveMouths()
    {
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (grid[cell] != CellType.Cave) continue;

            foreach (var dir in Directions)
            {
                var built = cell + dir;
                if (!grid.InBounds(built) || !IsBuiltSpace(built)
                    || !HasFloor(grid[built]) || grid[built] == CellType.Water
                    || grid[built] == CellType.Stairs) continue;

                // Two cells of cave behind the mouth, one cell to either hand.
                var flank = new Vector3Int(dir.z, 0, dir.x);
                for (int back = -1; back <= 2; back++)
                for (int across = -1; across <= 1; across++)
                    compositionReserved.Add(cell - dir * back + flank * across);
            }
        }
    }

    // A portal is an open-ended collar. Its four sides overlap both the masonry and
    // cave skin, hiding the numerical cut edge without sealing the walkable opening.
    void BuildCavePortals()
    {
        // The kit's own rock ground and wall panels already overlap the boundary, so a collar
        // there would be a third surface competing with them. Collars are for the contoured mesh
        // only - but the mouth reservations they carry are needed either way, so those are made
        // in ReserveCaveMouths before this returns.
        if (!UsingCaveSurface || caveSurfaceCollider == null) return;
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (grid[cell] != CellType.Cave) continue;
            foreach (var dir in Directions)
            {
                var built = cell + dir;
                if (!grid.InBounds(built) || !IsBuiltSpace(built)
                    || !HasFloor(grid[built]) || grid[built] == CellType.Water
                    || grid[built] == CellType.Stairs) continue;
                var outward = new Vector3(dir.x, 0f, dir.z);
                var right = Vector3.Cross(Vector3.up, outward);
                var boundary = CellCentre(cell) + outward * HalfWorldUnit + Vector3.up * FloorSurfaceOffset;
                float depth = Mathf.Min(WorldUnitSize, 2f * WorldUnitSize / caveVoxelsPerCell + caveSurfaceNoise + 0.5f);
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                // Floor lies just above the floor line; roof sits just below the ceiling.
                for (int end = 0; end < 2; end++)
                {
                    Vector3 p = boundary + outward * (end == 0 ? 0.2f : -depth);
                    vertices.Add(p - right * HalfWorldUnit + Vector3.up * 0.02f);
                    vertices.Add(p + right * HalfWorldUnit + Vector3.up * 0.02f);
                    vertices.Add(p + right * HalfWorldUnit + Vector3.up * (WorldUnitSize - 0.02f));
                    vertices.Add(p - right * HalfWorldUnit + Vector3.up * (WorldUnitSize - 0.02f));
                }
                // A corner cell carries a collar per opening, and a collar's flank sits exactly
                // where its neighbour's mouth is. Only raise the faces that front onto rock:
                // ring order is 0 = -right low, 1 = +right low, 2 = +right high, 3 = -right high,
                // so quad 0 is the floor, 1 and 3 the flanks, and 2 the roof.
                var lateral = new Vector3Int(Mathf.RoundToInt(right.x), 0, Mathf.RoundToInt(right.z));
                for (int side = 0; side < 4; side++)
                {
                    if (side == 1 && OpenAcross(cell, lateral)) continue;
                    if (side == 3 && OpenAcross(cell, -lateral)) continue;
                    if (side == 2 && OpenAcross(cell, Vector3Int.up)) continue;
                    int next = (side + 1) % 4;
                    triangles.Add(side); triangles.Add(next); triangles.Add(next + 4);
                    triangles.Add(side); triangles.Add(next + 4); triangles.Add(side + 4);
                }
                if (triangles.Count == 0) continue;
                var mesh = new Mesh { name = "Open cave transition collar" };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
                Delver.Dungeon.CaveSurface.Finish(mesh, caveWallPrefabs, true, true);
                caveGeneratedMeshes.Add(mesh);
                var go = new GameObject("Cave transition collar");
                go.transform.SetParent(dungeonRoot, false); go.transform.position = Vector3.zero;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                var source = PickFirst(caveWallPrefabs);
                if (source != null) renderer.sharedMaterial = source.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }
    }

    /// <summary>
    /// True when the collar face on <paramref name="side"/> would stand across open space rather
    /// than back onto rock. Nearly all of a collar sits on the cave side of the boundary, so the
    /// cave-side neighbour decides it: a corner cell's second mouth is exactly where the first
    /// collar's flank would otherwise fall, and a cave neighbour already has its own rock skin.
    /// </summary>
    bool OpenAcross(Vector3Int cell, Vector3Int side)
    {
        return IsOpenSpace(cell + side);
    }

    bool IsOpenSpace(Vector3Int cell)
    {
        if (!grid.InBounds(cell)) return false;
        return IsBuiltSpace(cell) || grid[cell] == CellType.Cave || grid[cell] == CellType.CaveOpen;
    }

    void ComposeCaverns()
    {
        if (profile == null || !HasCaveGeometry) return;
        foreach (var cave in caves)
        {
            if (cave.Shape != CaveShape.Chamber) continue;
            bool lush = IsLushCave(cave);

            // A chasm's anchor is as likely to be over the pit as on an island.
            if (cave.CellCount >= profile.landmarkMinFloorArea && !chasms.Contains(cave))
            {
                ReserveFocalArea(cave.Anchor, cave.Cells);
                PlaceLandmark(PickPrefab(profile.caveLandmarkPrefabs), cave.Anchor, lush);
            }
            CladCavern(cave);
            SetCaveVignettes(cave, lush);
            foreach (var cell in cave.Cells)
            {
                if (grid[cell] != CellType.Cave || compositionReserved.Contains(cell)) continue;
                if (chasmCells.Contains(cell) && !chasmGround.Contains(cell)) continue;
                if (random.NextDouble() >= profile.caveThemePropChance) continue;
                var prefab = PickPrefab(lush ? profile.lushCavePrefabs : profile.inhabitedCavePrefabs);
                SpawnGrounded(prefab, CellCentre(cell) + Vector3.up * FloorSurfaceOffset,
                    Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
            }
        }
        foreach (var room in rooms)
        {
            if (room.IsCave || room.FloorArea < profile.landmarkMinFloorArea) continue;

            // A cell block or a cellar is not the kind of room that gets a centrepiece, however
            // large it happens to come out.
            if (room.archetype != null && !room.archetype.allowLandmark) continue;
            if (grid[room.anchor] != CellType.BottomFloorRoom) continue;
            ReserveFocalArea(room.anchor, room.FloorCells());
            PlaceLandmark(PickPrefab(profile.roomLandmarkPrefabs), room.anchor, false);
        }
    }

    void ReserveFocalArea(Vector3Int anchor, IEnumerable<Vector3Int> floor)
    {
        float radius = profile.landmarkClearRadius / WorldUnitSize;
        foreach (var cell in floor)
            // The cross keeps clear approach lanes from every side, as well as a focal clearing.
            // Only a few cells past the clearing: run the full width of a cavern, the lanes
            // reserved a whole row and column of it, and the biggest caves came out emptiest.
            if ((cell - anchor).sqrMagnitude <= radius * radius
                || ((cell.x == anchor.x || cell.z == anchor.z) && (cell - anchor).sqrMagnitude <= (radius + 3f) * (radius + 3f)))
                compositionReserved.Add(cell);
    }

    void PlaceLandmark(GameObject prefab, Vector3Int cell, bool cool)
    {
        if (prefab == null) return;
        // Offset inside the reserved area so the navigation anchor remains walkable.
        Vector3 point = CellCentre(cell) + new Vector3(WorldUnitSize * 0.65f, FloorSurfaceOffset, WorldUnitSize * 0.65f);
        var landmark = SpawnGrounded(prefab, point, Quaternion.Euler(0f, random.Next(4) * 90f, 0f));
        if (landmark == null) return;
        var bounds = landmark.GetComponentInChildren<MeshRenderer>();
        if (bounds != null)
        {
            // A landmark is meant to be seen across a cavern; the old 3.5 m cap shrank thrones to
            // footstools.
            float scale = Mathf.Min(1f, 7f / Mathf.Max(bounds.bounds.size.x, bounds.bounds.size.z));
            landmark.transform.localScale *= scale;
            AlignSurface(landmark, point.y, false);
        }
        landmark.name = "Cavern landmark - " + prefab.name;
        var lightObject = new GameObject("Landmark accent light");
        lightObject.transform.SetParent(landmark.transform, true);
        lightObject.transform.position = landmark.transform.position;

        // A TorchLight rather than a bare Light, so it is culled with the rest when far away.
        // Cool landmarks glow the rulebook plates' magenta; warm ones burn orange.
        var light = lightObject.AddComponent<Delver.Game.TorchLight>();
        light.LocalOffset = new Vector3(-2f, 3f, -2f);
        light.Range = 24f;
        light.BaseIntensity = 3.5f;
        light.FlickerDepth = cool ? 0.05f : 0.2f;
        light.FlameColor = cool ? new Color(0.85f, 0.3f, 1f) : new Color(1f, 0.58f, 0.25f);
    }

    void CladCavern(CaveRegion cave)
    {
        // The asset kit rings and roofs every cavern itself. This older cladding hung squashed,
        // single-sided roof slabs at each cell's own height on top of it - see-through slabs
        // floating mid-air wherever a cavern's ceiling height changes.
        if (UsingCaveKit) return;

        foreach (var cell in cave.Cells)
        {
            if (grid[cell] != CellType.Cave || compositionReserved.Contains(cell) || TouchesStairs(cell)) continue;
            foreach (var dir in Directions)
            {
                var neighbour = cell + dir;
                if (!grid.InBounds(neighbour) || grid[neighbour] != CellType.None) continue;
                // One module every two cells. Overlap with the solid shell supplies continuity;
                // the imported rock is the visible relief, not the only collision boundary.
                if ((cell.x + cell.z) % 2 != 0) continue;
                var prefab = PickPrefab(profile.caveCladdingPrefabs);
                if (prefab == null) continue;
                var outward = new Vector3(dir.x, 0f, dir.z);
                float height = CaveColumnHeight(cell) * WorldUnitSize;
                Vector3 centre = CellCentre(cell) + outward * (HalfWorldUnit + 0.2f)
                    + Vector3.up * (FloorSurfaceOffset + height * 0.5f);
                FitModule(prefab, centre, Quaternion.LookRotation(-outward),
                    dir.x == 0 ? new Vector3(WorldUnitSize, height, 1.8f) : new Vector3(1.8f, height, WorldUnitSize), "Authored cave wall");
            }
            if (cell.x % 2 != 0 || cell.z % 2 != 0) continue;
            var roof = PickPrefab(profile.caveRoofPrefabs);
            if (roof == null) continue;
            float roofHeight = CaveColumnHeight(cell) * WorldUnitSize;
            FitModule(roof, CellCentre(cell) + Vector3.up * (FloorSurfaceOffset + roofHeight - 0.35f),
                Quaternion.identity, new Vector3(WorldUnitSize, 0.7f, WorldUnitSize), "Authored cave roof");
        }
    }

    int CaveColumnHeight(Vector3Int floor)
    {
        int height = 1;
        while (grid.InBounds(floor + Vector3Int.up * height)
            && grid[floor + Vector3Int.up * height] == CellType.CaveOpen) height++;
        return height;
    }

    void FitModule(GameObject prefab, Vector3 centre, Quaternion rotation, Vector3 targetSize, string label)
    {
        var go = Spawn(prefab, centre, rotation);
        var renderers = go.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.x < 0.001f || bounds.size.y < 0.001f || bounds.size.z < 0.001f) return;
        Vector3 worldScale = new Vector3(targetSize.x / bounds.size.x, targetSize.y / bounds.size.y, targetSize.z / bounds.size.z);
        // Cardinal rotations only: map world-axis size back to local scale axes.
        Vector3 localScale = rotation * Vector3.right;
        if (Mathf.Abs(localScale.z) > 0.5f) worldScale = new Vector3(worldScale.z, worldScale.y, worldScale.x);
        go.transform.localScale = Vector3.Scale(go.transform.localScale, worldScale);
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        go.transform.position += centre - bounds.center;
        go.name = label + " - " + prefab.name;
    }
}
