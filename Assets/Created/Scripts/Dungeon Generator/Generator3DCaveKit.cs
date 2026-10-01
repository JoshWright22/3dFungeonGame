using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds caves out of the POLYGON pack's own rock modules instead of a contoured mesh.
///
/// The demo scene is the reference. It does not fit one small piece per wall face - it rings a
/// space with a handful of large rock masses that overlap each other heavily, caps them with
/// axis-aligned roof slabs, and lays a separate rock ground under the lot. Overlap is the whole
/// technique: two solid rock masses intersecting are invisible from inside, while two pieces
/// butted edge to edge leave a seam you can see through. Everything here places pieces larger
/// than the space they cover and lets them collide.
/// </summary>
public partial class Generator3D
{
    /// <summary>Floor pieces built by the kit, for the raycasts that drop props onto the ground.</summary>
    readonly HashSet<Collider> caveKitGround = new HashSet<Collider>();

    /// <summary>Roof slabs, for the upward cast that hangs stalactites.</summary>
    readonly HashSet<Collider> caveKitRoof = new HashSet<Collider>();

    bool UsingCaveKit => profile != null && profile.caveUseAssetKit;

    /// <summary>A straight run of cave boundary facing one way, in cells.</summary>
    struct WallRun
    {
        public Vector3Int Start;
        public Vector3Int Dir;      // the direction the wall faces, into the cave
        public Vector3Int Along;    // the direction the run travels
        public int Length;
        public int Height;          // storeys of rock face, from KitWallHeight
    }

    void BuildCaveKit()
    {
        caveKitGround.Clear();
        caveKitRoof.Clear();
        if (!UsingCaveKit) return;

        BuildKitFloors();
        BuildKitWalls();
        Physics.SyncTransforms();
        SealWallGaps();
        BuildKitRoofs();
        LayCaveCeilings();
        BuildKitBackdrops();
        BuildChasms();

        Physics.SyncTransforms();
    }

    // ------------------------------------------------------------------ floor

    /// <summary>The cave floor. See Generator3DCaveGround.</summary>
    void BuildKitFloors() => LayCaveGround();

    bool IsBuiltEdge(Vector3Int cell, Vector3Int dir)
    {
        var neighbour = cell + dir;
        return grid.InBounds(neighbour) && IsBuiltSpace(neighbour);
    }

    // ------------------------------------------------------------------ walls

    /// <summary>
    /// Rings each cavern with large wall panels along its straight boundary runs.
    ///
    /// Pieces are spaced at less than their own width so consecutive panels always overlap, and
    /// a run shorter than one piece still gets one - a two-metre nook is walled by a panel that
    /// buries itself in the rock either side of it.
    /// </summary>
    void BuildKitWalls()
    {
        if (profile.caveKitWallPrefabs == null || profile.caveKitWallPrefabs.Length == 0) return;

        foreach (var run in CollectWallRuns())
        {
            float spacing = Mathf.Max(2f, profile.caveKitWallSpacing);
            int pieces = Mathf.Max(1, Mathf.CeilToInt(run.Length * WorldUnitSize / spacing));

            for (int i = 0; i < pieces; i++)
            {
                var prefab = PickPrefab(profile.caveKitWallPrefabs);
                if (prefab == null) continue;

                // Spread the pieces evenly along the run, measured from its centre line.
                float t = pieces == 1 ? 0.5f : i / (float)(pieces - 1);
                float distance = t * (run.Length - 1) * WorldUnitSize;

                Vector3 start = CellCentre(run.Start);
                Vector3 along = new Vector3(run.Along.x, 0f, run.Along.z);
                Vector3 inward = new Vector3(run.Dir.x, 0f, run.Dir.z);
                Vector3 at = start + along * distance;

                // Under a ledge the face runs right up to the underside of the ledge's ground,
                // sunk a metre and a half below the pit floor; anywhere else the roof caps it.
                float height = LedgeAbove(run.Start, run.Dir * -1, run.Height)
                    ? (run.Height * WorldUnitSize + 1.4f) / 1.15f
                    : run.Height * WorldUnitSize;
                Vector3 centre = at + Vector3.up * (run.Start.y * WorldUnitSize + FloorSurfaceOffset);

                var rotation = Quaternion.LookRotation(inward, Vector3.up)
                    * Quaternion.Euler(0f, profile.caveKitWallJitter * ((float)random.NextDouble() - 0.5f), 0f);

                // These pieces run from a 2.5m slab to an 8m curved mass. Behind the boundary
                // there may only be one cell of rock before a room starts, so the piece's depth
                // is clamped to what is actually there rather than left at its natural size.
                Vector3 boundaryCell = at - inward * WorldUnitSize;
                float availableDepth = RockDepthBehind(run.Start, -run.Dir, 3) * WorldUnitSize;

                // Width ladder. A full-width panel is 11m and will often overhang a run's end
                // into a room; discarding it outright is what left holes in the ring. Narrower
                // rock against the boundary is a far better answer than no rock at all, so the
                // panel is retried at decreasing widths and only abandoned if even the smallest
                // still reaches into built space.
                GameObject panel = null;
                foreach (float share in new[] { 1.6f, 1.0f, 0.65f, 0.4f })
                {
                    panel = FitKitPiece(prefab, centre, rotation,
                        new Vector3(spacing * share, height * 1.15f, Mathf.Max(1.5f, availableDepth)), true);
                    if (panel == null) break;

                    Vector3 face = at - inward * HalfWorldUnit;
                    PushBehindPlane(panel, face + inward * profile.caveKitWallInset, inward);
                    AlignSurface(panel, run.Start.y * WorldUnitSize + FloorSurfaceOffset - 1.5f, false);

                    if (!IntrudesOnBuiltSpace(panel)) break;

                    DestroyImmediate(panel);
                    panel = null;
                }

                if (panel == null) continue;

                panel.name = "Cave wall - " + prefab.name;
            }
        }
    }

    /// <summary>
    /// Looks for daylight and patches it.
    ///
    /// Run-based placement covers a boundary well but not perfectly: a panel narrowed to keep
    /// clear of a room, or a run one cell long tucked in a corner, can leave a face open, and an
    /// open face in a cave wall is a hole you see the world through. Rather than try to predict
    /// those cases, this casts the same ray a player's eye would follow and stands a panel
    /// wherever one escapes. Runs after the walls and before the roof, so the patches are capped
    /// with everything else.
    /// </summary>
    void SealWallGaps()
    {
        if (profile.caveKitWallPrefabs == null || profile.caveKitWallPrefabs.Length == 0) return;

        int sealed_ = 0;
        int faces = 0, open_ = 0, dropped = 0;

        foreach (var cave in caves)
        foreach (var cell in cave.Cells)
        {
            // Upper storeys too: a cave can be wider up top than at its floor, and those faces
            // have no floor cell beneath them for the wall runs to find.
            if (grid[cell] != CellType.Cave) continue;



            foreach (var dir in Directions)
            {
                if (dir.y != 0) continue;
                if (!IsKitWallFace(cell, dir)) continue;
                faces++;

                var inward = new Vector3(-dir.x, 0f, -dir.z);
                var outward = -inward;
                Vector3 centre = CellCentre(cell);
                float floor = cell.y * WorldUnitSize + FloorSurfaceOffset;

                // Three heights, because a panel can close the ankles and leave the head open.
                // And three points across the face, because the seam between two panels is
                // rarely in the middle of the cell.
                bool open = false;
                var across = new Vector3(dir.z, 0f, dir.x);
                foreach (float h in new[] { 0.5f, 1.7f, 3.5f })
                foreach (float a in new[] { -1.8f, 0f, 1.8f })
                {
                    var from = new Vector3(centre.x, floor + h, centre.z) + across * a;
                    if (!Physics.Raycast(new Ray(from, outward), WorldUnitSize + 1f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        open = true;
                        break;
                    }
                }

                var prefab = PickPrefab(profile.caveKitWallPrefabs);
                if (prefab == null) continue;

                int storeys = KitWallHeight(cell, dir);
                float height = LedgeAbove(cell, dir, storeys)
                    ? (storeys * WorldUnitSize + 1.4f) / 1.15f
                    : storeys * WorldUnitSize;
                float depth = Mathf.Max(1.5f, RockDepthBehind(cell, dir, 3) * WorldUnitSize);
                if (open) open_++;

                // Narrow the patch until it fits. A face left open beside a room is the worst of
                // both faults - it is exactly where a player stands and sees through the world -
                // so the ladder goes right down to a panel barely wider than the cell itself.
                GameObject patch = null;
                if (open)
                foreach (var step in new[]
                {
                    new Vector2(1.5f, 1.15f), new Vector2(1.15f, 1.15f), new Vector2(0.9f, 1.15f),
                    new Vector2(0.9f, 0.8f), new Vector2(0.7f, 0.8f), new Vector2(0.7f, 0.55f),
                    new Vector2(0.55f, 0.55f), new Vector2(0.55f, 0.35f),
                })
                {
                    // A tall panel behind thin rock reaches into whatever room sits above it, so
                    // the ladder gives up height as well as width. The last rung is a short slab
                    // that plugs the walking band and nothing more - still better than daylight.
                    patch = FitKitPiece(prefab, centre + Vector3.up * floor,
                        Quaternion.LookRotation(inward, Vector3.up),
                        new Vector3(WorldUnitSize * step.x, height * step.y, depth), true);
                    if (patch == null) break;

                    PushBehindPlane(patch, centre + outward * HalfWorldUnit + inward * profile.caveKitWallInset, inward);
                    AlignSurface(patch, floor - 1.5f, false);

                    if (!IntrudesOnBuiltSpace(patch)) break;

                    DestroyImmediate(patch);
                    patch = null;
                }

                // Last resort where no real panel fits: a slab barely deeper than a wall, kept to
                // the walking band. Thin rock reads far better
                // than a window onto the void.
                if (patch == null && open)
                    foreach (float share in new[] { 0.9f, 0.7f })
                    {
                        patch = FitKitPiece(prefab, centre + Vector3.up * floor, Quaternion.LookRotation(inward, Vector3.up),
                            new Vector3(WorldUnitSize * share, Mathf.Min(height, 4.5f), 0.6f), false);
                        if (patch == null) break;

                        // Stood on the floor line, not sunk into it: these are mostly caves sitting
                        // directly over a corridor, where sinking the foot reaches into the
                        // corridor below and gets every patch thrown away.
                        PushBehindPlane(patch, centre + outward * HalfWorldUnit + inward * profile.caveKitWallInset, inward);
                        AlignSurface(patch, floor + 0.02f, false);

                        if (!IntrudesOnBuiltSpace(patch)) break;

                        DestroyImmediate(patch);
                        patch = null;
                    }

                if (patch != null)
                {
                    patch.name = "Cave wall patch - " + prefab.name;
                    sealed_++;
                }
                else if (open) dropped++;

                // The walking band is only the start. A patch shortened to keep out of a room
                // above leaves the upper reaches of a tall cavern open, and from across the
                // cavern you look up straight through them - so every height the cavern
                // reaches is tested, and any band still open gets a strip of rock of its own.
                Physics.SyncTransforms();
                for (float h = 5.5f; h < height - 0.5f; h += 2.5f)
                {
                    var from = new Vector3(centre.x, floor + h, centre.z);
                    if (Physics.Raycast(new Ray(from, outward), WorldUnitSize + 1f, ~0, QueryTriggerInteraction.Ignore)) continue;

                    GameObject band = null;
                    foreach (float share in new[] { 1.5f, 1.15f, 0.9f, 0.7f })
                    {
                        band = FitKitPiece(PickPrefab(profile.caveKitWallPrefabs), centre + Vector3.up * (floor + h),
                            Quaternion.LookRotation(inward, Vector3.up),
                            new Vector3(WorldUnitSize * share, 4f, depth), true);
                        if (band == null) break;

                        PushBehindPlane(band, centre + outward * HalfWorldUnit + inward * profile.caveKitWallInset, inward);
                        AlignSurface(band, floor + h - 2f, false);

                        if (!IntrudesOnBuiltSpace(band)) break;

                        DestroyImmediate(band);
                        band = null;
                    }

                    if (band == null) continue;

                    band.name = "Cave wall band - " + band.name.Replace("(Clone)", "");
                    sealed_++;
                    Physics.SyncTransforms();
                }
            }
        }

        // Upper storeys: every cell of cave air in the grid. Walked from the grid rather than from
        // the regions, which list floor cells only - and a river cut through a cave floor leaves
        // open air standing over water, belonging to no floor cell at all.
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var air = new Vector3Int(x, y, z);
            if (grid[air] == CellType.CaveOpen) SealUpperFaces(air, ref sealed_);
        }

        // Last, a dense pass over every rock face, floor storey and air alike. The tests above
        // look along the face's centre line; a seam between two panels, or the slot beside a
        // corridor mouth's frame, is seen at an angle from wherever the player happens to stand.
        Physics.SyncTransforms();
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (grid[cell] != CellType.Cave && grid[cell] != CellType.CaveOpen) continue;
            foreach (var dir in Directions)
            {
                if (dir.y != 0 || !IsRock(cell + dir)) continue;
                PatchFace(cell, dir, ref sealed_);
            }
        }

        LayMouthThresholds();

        Debug.Log($"Cave kit seal: {faces} wall faces, {open_} open, {sealed_} sealed, {dropped} dropped.", this);
        if (sealed_ > 0) Physics.SyncTransforms();
    }

    /// <summary>
    /// Looks through one rock face the way a player might: from nine points across it, at seven
    /// heights, straight out and thirty-five degrees either side. Returns where a sight-line
    /// escapes, if one does.
    /// </summary>
    bool FindFaceLeak(Vector3Int cell, Vector3Int dir, out float across, out float height)
    {
        float bottom = cell.y * WorldUnitSize + FloorSurfaceOffset;
        Vector3 centre = CellCentre(cell);
        var outward = new Vector3(dir.x, 0f, dir.z);
        var side = new Vector3(dir.z, 0f, dir.x);

        foreach (float h in new[] { 0.3f, 0.9f, 1.6f, 2.4f, 3.2f, 4.0f, 4.7f })
        foreach (float a in new[] { -2.3f, -1.7f, -1.1f, -0.5f, 0f, 0.5f, 1.1f, 1.7f, 2.3f })
        foreach (float yaw in new[] { 0f, -35f, 35f })
        foreach (float pitch in new[] { 0f, -30f, 30f })
        {
            // Pitched too: a gap under the foot of a wall or over its top is only ever seen
            // looking down or up at it.
            var from = new Vector3(centre.x, bottom + h, centre.z) + side * a;
            var look = Quaternion.Euler(0f, yaw, 0f) * (Quaternion.AngleAxis(pitch, side) * outward);
            if (VisibleHit(from, look, WorldUnitSize * 2f)) continue;

            across = a;
            height = h;
            return true;
        }

        // And the body: a crouched player capsule pushed straight out through the face. One that
        // gets a metre past the cave's edge has found a way behind the rock - round the open end
        // of a curved panel, say - even where no light comes through.
        foreach (float a in new[] { -2.1f, -1.4f, -0.7f, 0f, 0.7f, 1.4f, 2.1f })
        foreach (float yaw in new[] { 0f, -35f, 35f })
        {
            // Straight out and slanting, since a body slips diagonally between two panels.
            var feet = new Vector3(centre.x, bottom, centre.z) + side * a;
            var low = feet + Vector3.up * 0.85f;
            var high = feet + Vector3.up * 1.4f;
            var push = Quaternion.Euler(0f, yaw, 0f) * outward;
            float reach = (HalfWorldUnit + 1f) / Mathf.Cos(yaw * Mathf.Deg2Rad);
            if (Physics.CapsuleCast(low, high, 0.35f, push, reach, ~0, QueryTriggerInteraction.Ignore)) continue;

            across = Mathf.Clamp(a + Mathf.Tan(yaw * Mathf.Deg2Rad) * HalfWorldUnit, -2.5f, 2.5f);
            height = 1f;
            return true;
        }

        across = height = 0f;
        return false;
    }

    /// <summary>True when a ray meets something that is drawn. A collider nobody can see closes nothing.</summary>
    static bool VisibleHit(Vector3 from, Vector3 dir, float distance)
    {
        // Nearly every ray stops on drawn rock, so ask the cheap question first.
        if (!Physics.Raycast(from, dir, out RaycastHit first, distance, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (IsDrawn(first.collider)) return true;

        foreach (var hit in Physics.RaycastAll(from, dir, distance, ~0, QueryTriggerInteraction.Ignore))
            if (IsDrawn(hit.collider)) return true;
        return false;
    }

    static readonly Dictionary<Collider, bool> drawnCache = new Dictionary<Collider, bool>();

    static bool IsDrawn(Collider collider)
    {
        if (drawnCache.TryGetValue(collider, out bool drawn)) return drawn;
        drawn = collider.GetComponentInParent<MeshRenderer>() != null;
        drawnCache[collider] = drawn;
        return drawn;
    }

    /// <summary>
    /// Stands rock over a face until no sight-line gets through it, a panel centred on wherever the
    /// last leak was. False if the face still shows daylight after three tries.
    /// </summary>
    bool PatchFace(Vector3Int cell, Vector3Int dir, ref int sealedCount)
    {
        float bottom = cell.y * WorldUnitSize + FloorSurfaceOffset;
        Vector3 centre = CellCentre(cell);
        var outward = new Vector3(dir.x, 0f, dir.z);
        var side = new Vector3(dir.z, 0f, dir.x);
        float depth = Mathf.Max(1.5f, RockDepthBehind(cell, dir, 3) * WorldUnitSize);

        var tried = new HashSet<float>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!FindFaceLeak(cell, dir, out float a, out float h)) return true;

            // Rock is already standing at that point across the face and the leak is still
            // there: this face cannot close it, and a second copy of the same patch will not.
            if (!tried.Add(a)) return false;

            // A leak near the end of the face is usually the corner seam with the next face, so
            // the patch is pushed out past the corner rather than centred inside the cell.
            bool nearCorner = Mathf.Abs(a) > 1.5f;
            Vector3 at = new Vector3(centre.x, bottom + HalfWorldUnit, centre.z) + side * (nearCorner ? a * 1.3f : a);
            GameObject patch = null;
            // Tallest first - sunk 2.5 m under the floor and reaching 1.5 m past the storey, so
            // it closes the slots under the wall's foot and over its top as well - then shorter
            // and thinner wherever a room or corridor is in the way.
            foreach (var (baseWidth, tall, thin) in new[] { (4.5f, true, false), (4.5f, false, false), (3f, false, false), (3f, false, true), (2f, false, true) })
            {
                float width = nearCorner ? baseWidth + 1.5f : baseWidth;
                float height = tall ? WorldUnitSize + 4f : WorldUnitSize * 1.2f;
                patch = FitKitPiece(PickPrefab(profile.caveKitWallPrefabs), at, Quaternion.LookRotation(-outward, Vector3.up),
                    new Vector3(width, height, thin ? 0.6f : depth), !thin);
                if (patch == null) break;

                PushBehindPlane(patch, centre + outward * HalfWorldUnit - outward * profile.caveKitWallInset, -outward);
                AlignSurface(patch, bottom - (tall ? 2.5f : 0.3f), false);
                if (!ReachesIntoBuilt(patch, 0.35f)) break;

                DestroyImmediate(patch);
                patch = null;
            }

            if (patch == null) return false;
            patch.name = "Cave wall seam - " + patch.name.Replace("(Clone)", "");
            sealedCount++;
            Physics.SyncTransforms();
        }

        return !FindFaceLeak(cell, dir, out _, out _);
    }

    /// <summary>
    /// A rock plate across every seam between cave ground and a corridor or room floor, tucked a
    /// hair under the built floor, so the join is rock on the cave side and flagstone on the
    /// other with nothing to see between.
    /// </summary>
    void LayMouthThresholds()
    {
        if (CaveBasePrefabs == null || CaveBasePrefabs.Length == 0) return;

        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (grid[cell] != CellType.Cave) continue;

            foreach (var dir in Directions)
            {
                if (dir.y != 0) continue;
                var built = cell + dir;
                if (!grid.InBounds(built) || !IsBuiltSpace(built)) continue;
                var kind = grid[built];
                if (kind == CellType.Stairs || kind == CellType.Water) continue;

                var outward = new Vector3(dir.x, 0f, dir.z);
                Vector3 seam = CellCentre(cell) + outward * HalfWorldUnit;
                var span = dir.x != 0 ? new Vector3(3f, 0f, WorldUnitSize + 0.4f) : new Vector3(WorldUnitSize + 0.4f, 0f, 3f);

                var plate = FitKitPiece(PickPrefab(CaveBasePrefabs), seam, Quaternion.identity, span, false);
                if (plate == null) continue;
                plate.transform.localScale = Vector3.Scale(plate.transform.localScale, new Vector3(1f, 0.6f, 1f));
                AlignSurface(plate, Floor(cell) - 0.02f, true);
                plate.name = "Cave mouth threshold - " + plate.name.Replace("(Clone)", "");
                RegisterGround(plate);
            }
        }
    }

    /// <summary>
    /// Closes the rock faces of a cave's open air where the floor storey below does not reach -
    /// tested the same way as the walking band, three points across at three heights, and
    /// patched with a panel standing on the storey line.
    /// </summary>
    void SealUpperFaces(Vector3Int cell, ref int sealedCount)
    {
        float bottom = cell.y * WorldUnitSize + FloorSurfaceOffset;
        Vector3 centre = CellCentre(cell);

        foreach (var dir in Directions)
        {
            if (dir.y != 0) continue;
            var beside = cell + dir;
            if (grid.InBounds(beside) && grid[beside] != CellType.None) continue;

            var outward = new Vector3(dir.x, 0f, dir.z);
            var across = new Vector3(dir.z, 0f, dir.x);
            bool open = false;
            foreach (float h in new[] { 0.8f, 2.5f, 4.2f })
            foreach (float a in new[] { -1.8f, 0f, 1.8f })
            {
                var from = new Vector3(centre.x, bottom + h, centre.z) + across * a;
                if (!Physics.Raycast(from, outward, WorldUnitSize + 1f, ~0, QueryTriggerInteraction.Ignore)) { open = true; break; }
            }
            if (!open) continue;

            // These faces often sit right over a corridor or room on the storey below, so the
            // panel stands on the storey line and may only graze the ceiling under it. The ladder
            // ends in a thin slab, which fits wherever a panel does not.
            float depth = Mathf.Max(1.5f, RockDepthBehind(cell, dir, 3) * WorldUnitSize);
            foreach (var (share, thin) in new[] { (1.5f, false), (1.15f, false), (0.9f, false), (1.1f, true), (0.9f, true) })
            {
                var band = FitKitPiece(PickPrefab(profile.caveKitWallPrefabs), centre + Vector3.up * (bottom + HalfWorldUnit),
                    Quaternion.LookRotation(-outward, Vector3.up),
                    new Vector3(WorldUnitSize * share, WorldUnitSize * 1.15f, thin ? 0.6f : depth), !thin);
                if (band == null) break;

                PushBehindPlane(band, centre + outward * HalfWorldUnit - outward * profile.caveKitWallInset, -outward);
                AlignSurface(band, bottom - 0.15f, false);

                if (!ReachesIntoBuilt(band, 0.3f))
                {
                    band.name = "Cave wall upper - " + band.name.Replace("(Clone)", "");
                    sealedCount++;
                    Physics.SyncTransforms();
                    break;
                }

                DestroyImmediate(band);
            }
        }
    }

    /// <summary>
    /// Walks the cave boundary and merges each straight stretch facing the same way into one run.
    ///
    /// Walling face by face is what makes a generated cave read as a room with rock wallpaper:
    /// every 5m the silhouette repeats. A run lets one piece span several cells.
    /// </summary>
    List<WallRun> CollectWallRuns()
    {
        var runs = new List<WallRun>();
        var claimed = new HashSet<(Vector3Int, Vector3Int)>();

        foreach (var dir in Directions)
        {
            if (dir.y != 0) continue;
            var along = new Vector3Int(dir.z, 0, dir.x);

            foreach (var cave in caves)
            foreach (var cell in cave.Cells)
            {
                if (grid[cell] != CellType.Cave) continue;
                if (!IsKitWallFace(cell, dir)) continue;
                if (claimed.Contains((cell, dir))) continue;

                // Only start a run at its first cell, so each stretch is collected once. A run
                // also breaks where the face changes height - a pit wall under a ledge is one
                // storey of rock, beside a full-height cavern wall.
                int height = KitWallHeight(cell, dir);
                var before = cell - along;
                if (grid.InBounds(before) && grid[before] == CellType.Cave && IsKitWallFace(before, dir)
                    && KitWallHeight(before, dir) == height) continue;

                int length = 0;
                var cursor = cell;
                while (grid.InBounds(cursor) && grid[cursor] == CellType.Cave && IsKitWallFace(cursor, dir)
                    && KitWallHeight(cursor, dir) == height)
                {
                    claimed.Add((cursor, dir));
                    length++;
                    cursor += along;
                }

                runs.Add(new WallRun { Start = cell, Dir = -dir, Along = along, Length = length, Height = height });
            }
        }

        return runs;
    }

    /// <summary>
    /// True when this cell's face in <paramref name="dir"/> needs rock standing against it.
    ///
    /// Faces onto built space are cave mouths and stay open - the room's own wall course and its
    /// tunnel entrance piece close those.
    /// </summary>
    bool IsKitWallFace(Vector3Int cell, Vector3Int dir)
    {
        var neighbour = cell + dir;
        if (!grid.InBounds(neighbour)) return true;
        return grid[neighbour] == CellType.None;
    }

    /// <summary>
    /// Storeys of rock face on one side of a cave floor cell: as far up as the cave stays open
    /// and the rock beside it stays solid. Under a chasm's ledge that is the one storey of the pit
    /// - a full-height panel there would stand up through the ledge.
    /// </summary>
    int KitWallHeight(Vector3Int floor, Vector3Int dir)
    {
        int height = 1;
        while (true)
        {
            var above = floor + Vector3Int.up * height;
            if (!grid.InBounds(above) || grid[above] != CellType.CaveOpen) break;
            var beside = above + dir;
            if (grid.InBounds(beside) && grid[beside] != CellType.None) break;
            height++;
        }
        return height;
    }

    /// <summary>True when the rock face stops under a floor - a chasm's ledge - rather than at the roof.</summary>
    bool LedgeAbove(Vector3Int floor, Vector3Int dir, int storeys)
    {
        var beside = floor + Vector3Int.up * storeys + dir;
        return grid.InBounds(beside) && grid[beside] == CellType.Cave;
    }

    /// <summary>Open cells above a cave floor cell, counting the floor itself.</summary>
    int KitCavernHeight(Vector3Int floor)
    {
        int height = 1;
        while (grid.InBounds(floor + Vector3Int.up * height)
            && grid[floor + Vector3Int.up * height] == CellType.CaveOpen) height++;
        return height;
    }

    // ------------------------------------------------------------------ roof

    /// <summary>
    /// Caps each cavern with roof slabs on a coarse grid, stepped so they overlap.
    ///
    /// The slab is 23m by 20m and the grid is deliberately tighter than that in both axes. A
    /// slab is kept only where its footprint actually covers cave, so the ceiling follows the
    /// blob rather than squaring it off.
    /// </summary>
    void BuildKitRoofs()
    {
        if (profile.caveKitRoofPrefabs == null || profile.caveKitRoofPrefabs.Length == 0) return;

        float step = Mathf.Max(5f, profile.caveKitRoofSpacing);
        int cellsPerStep = Mathf.Max(1, Mathf.RoundToInt(step / WorldUnitSize));

        // Every cave region, tunnels included - a passage left uncapped is a strip of open sky
        // running through the rock. Two caves near each other can want the same slab; it is
        // laid once.
        var laid = new HashSet<Vector3Int>();
        foreach (var cave in caves)
        {
            var bounds = cave.Bounds;
            var placed = new HashSet<Vector2Int>();

            // Blocks the cave actually occupies, plus a ring of one block around them. The wall
            // panels stand on the cave boundary and lean outward, so a ceiling stopping at the
            // last cave cell leaves a band of open sky between the roof edge and the wall tops.
            var wanted = new List<(Vector2Int block, Vector3Int cell)>();
            var seen = new HashSet<Vector2Int>();

            foreach (var cell in cave.Cells)
            {
                if (grid[cell] != CellType.Cave) continue;

                var occupied = new Vector2Int(
                    Mathf.FloorToInt((cell.x - bounds.xMin) / (float)cellsPerStep),
                    Mathf.FloorToInt((cell.z - bounds.zMin) / (float)cellsPerStep));

                for (int bx = -1; bx <= 1; bx++)
                for (int bz = -1; bz <= 1; bz++)
                {
                    var block = occupied + new Vector2Int(bx, bz);
                    if (seen.Add(block)) wanted.Add((block, cell));
                }
            }

            foreach (var (block, cell) in wanted)
            {
                if (!placed.Add(block)) continue;

                var prefab = PickPrefab(profile.caveKitRoofPrefabs);
                if (prefab == null) continue;

                var anchor = new Vector3Int(
                    bounds.xMin + block.x * cellsPerStep,
                    cell.y,
                    bounds.zMin + block.y * cellsPerStep);

                Vector3 centre = CellCentre(anchor)
                    + new Vector3((cellsPerStep - 1) * HalfWorldUnit, 0f, (cellsPerStep - 1) * HalfWorldUnit);

                // Sampled over everything the slab actually covers, not just its own block. The
                // slab is deliberately oversized, so a low tunnel next to a tall chamber would
                // otherwise hang its ceiling down through the chamber's headroom.
                int margin = Mathf.CeilToInt(step * 0.9f / WorldUnitSize);
                int tallest = 1;
                for (int dx = -margin; dx < cellsPerStep + margin; dx++)
                for (int dz = -margin; dz < cellsPerStep + margin; dz++)
                {
                    var probe = new Vector3Int(anchor.x + dx, cell.y, anchor.z + dz);
                    if (!grid.InBounds(probe) || grid[probe] != CellType.Cave) continue;
                    tallest = Mathf.Max(tallest, KitCavernHeight(probe));
                }

                float underside = cell.y * WorldUnitSize + FloorSurfaceOffset + tallest * WorldUnitSize;
                if (!laid.Add(Vector3Int.RoundToInt(new Vector3(centre.x, underside, centre.z)))) continue;
                // Generously oversized. A slab that only just spans its block leaves a strip of
                // open sky wherever the cavern edge sits between two blocks.
                var slab = FitKitPiece(prefab, centre + Vector3.up * underside,
                    Quaternion.Euler(0f, random.Next(2) * 180f, 0f),
                    new Vector3(step * 1.8f, 0f, step * 1.8f), false);
                if (slab == null) continue;

                AlignSurface(slab, underside, false);
                if (DiscardIfIntruding(slab)) continue;

                slab.name = "Cave roof - " + prefab.name;
                RegisterRoof(slab);
            }
        }
    }

    // ------------------------------------------------------------------ backdrop

    /// <summary>
    /// Drops a few of the pack's huge background masses outside the wall ring of the largest
    /// caverns, at arbitrary rotations, exactly as the demo does. They are never walked on or
    /// walked past - they exist to put rock behind the gaps between wall panels and to give the
    /// silhouette something beyond the immediate boundary.
    /// </summary>
    void BuildKitBackdrops()
    {
        if (profile.caveKitBackdropPrefabs == null || profile.caveKitBackdropPrefabs.Length == 0) return;
        if (profile.caveKitBackdropsPerCavern <= 0) return;

        foreach (var cave in caves)
        {
            if (cave.Shape != CaveShape.Chamber) continue;
            if (cave.CellCount < profile.landmarkMinFloorArea) continue;

            var bounds = cave.Bounds;
            float radius = Mathf.Max(bounds.size.x, bounds.size.z) * HalfWorldUnit + WorldUnitSize * 2f;
            Vector3 centre = CellCentre(cave.Anchor);

            for (int i = 0; i < profile.caveKitBackdropsPerCavern; i++)
            {
                var prefab = PickPrefab(profile.caveKitBackdropPrefabs);
                if (prefab == null) continue;

                float angle = (i + (float)random.NextDouble()) * Mathf.PI * 2f / profile.caveKitBackdropsPerCavern;
                Vector3 at = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                var mass = Spawn(prefab, at, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                if (mass == null) continue;

                float scale = profile.caveKitBackdropScale;
                mass.transform.localScale *= scale;

                // These are 25-47m across before scaling, and their pivots sit well off centre.
                // Dropped at a radius they reach straight back through the cavern, so each one is
                // pushed out along its own radial until nothing of it is inside the wall ring.
                Vector3 outward = (at - centre).normalized;
                PushBehindPlane(mass, centre + outward * radius, -outward);

                AlignSurface(mass, cave.Anchor.y * WorldUnitSize + FloorSurfaceOffset - WorldUnitSize, false);
                if (DiscardIfIntruding(mass)) continue;

                mass.name = "Cave backdrop - " + prefab.name;

                // Backdrops are scenery behind the wall ring. Colliding with them would let a
                // player stand on rock that is not part of the cavern.
                foreach (var collider in mass.GetComponentsInChildren<Collider>()) collider.enabled = false;
            }
        }
    }

    // ------------------------------------------------------------------ shared

    /// <summary>
    /// Spawns a prefab and scales it so its rendered bounds cover <paramref name="target"/>.
    ///
    /// A zero component in the target leaves that axis alone, which is what keeps a wall panel's
    /// natural thickness and a roof slab's natural depth. <paramref name="uniformXZ"/> scales the
    /// horizontal axes together, so a wall's rock detail is not stretched along one axis only.
    /// </summary>
    GameObject FitKitPiece(GameObject prefab, Vector3 centre, Quaternion rotation, Vector3 target, bool uniformXZ)
    {
        var go = Spawn(prefab, centre, rotation);
        if (go == null) return null;

        // Measured in the object's OWN frame, not as a world AABB. A panel rotated to face -Z,
        // or given a few degrees of yaw jitter, has a world AABB whose x is its depth rather
        // than its width - scaling off that stretches the wrong axis and leaves the run full of
        // narrow pillars with gaps between them.
        if (!LocalBounds(go, out Bounds local)) return go;
        if (local.size.x < 0.01f || local.size.y < 0.01f || local.size.z < 0.01f) return go;

        float sx = target.x > 0f ? target.x / local.size.x : 1f;
        float sy = target.y > 0f ? target.y / local.size.y : 1f;
        float sz = target.z > 0f ? target.z / local.size.z : 1f;

        if (uniformXZ)
        {
            // The target's x is the span the piece has to cover along its own width; its depth
            // follows so the rock is not squashed flat in one axis only.
            sz = sx;
        }

        go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(sx, sy, sz));

        // Recentre: scaling happens about the pivot, which on these pieces is rarely the middle
        // of the mesh - Cave_01 pivots 2.6m off one end, the roof slab pivots dead centre.
        if (WorldBounds(go, out Bounds world)) go.transform.position += centre - world.center;

        return go;
    }

    /// <summary>Combined renderer bounds expressed in the object's own local space.</summary>
    static bool LocalBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var renderers = go.GetComponentsInChildren<MeshRenderer>();
        var toLocal = go.transform.worldToLocalMatrix;
        bool first = true;

        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            var mesh = filter.sharedMesh.bounds;
            var toGo = toLocal * renderer.transform.localToWorldMatrix;

            // Every corner, because the child may be rotated within the prefab.
            for (int corner = 0; corner < 8; corner++)
            {
                var point = mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                var inGo = toGo.MultiplyPoint3x4(point);
                if (first) { bounds = new Bounds(inGo, Vector3.zero); first = false; }
                else bounds.Encapsulate(inGo);
            }
        }

        return !first;
    }

    static bool WorldBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var renderers = go.GetComponentsInChildren<MeshRenderer>();
        bool first = true;

        foreach (var renderer in renderers)
        {
            if (first) { bounds = renderer.bounds; first = false; }
            else bounds.Encapsulate(renderer.bounds);
        }

        return !first;
    }

    /// <summary>
    /// Slides an object along <paramref name="inward"/> until nothing of it reaches past
    /// <paramref name="plane"/>. Used to stand a wall piece on the boundary regardless of how
    /// deep the particular rock module happens to be.
    /// </summary>
    static void PushBehindPlane(GameObject go, Vector3 plane, Vector3 inward)
    {
        inward = inward.normalized;

        var renderers = go.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        // The corner of the bounding box that reaches furthest along inward. Written as a
        // support function rather than a sign lookup so a diagonal push works too.
        Vector3 reach = bounds.center + new Vector3(
            bounds.extents.x * (inward.x >= 0f ? 1f : -1f),
            0f,
            bounds.extents.z * (inward.z >= 0f ? 1f : -1f));

        float overshoot = Vector3.Dot(reach - plane, inward);
        if (overshoot > 0f) go.transform.position -= inward * overshoot;
    }

    /// <summary>Cells of solid rock behind a boundary cell, up to <paramref name="limit"/>.</summary>
    int RockDepthBehind(Vector3Int cell, Vector3Int outward, int limit)
    {
        int depth = 0;
        for (int step = 1; step <= limit; step++)
        {
            var probe = cell + outward * step;
            if (!grid.InBounds(probe)) break;
            if (grid[probe] != CellType.None) break;
            depth++;
        }
        return depth;
    }

    /// <summary>
    /// True when any part of a piece's bounds sits inside a cell the built dungeon owns.
    ///
    /// Tested against the grid rather than against colliders: a room's geometry is a floor tile
    /// and a ring of wall panels, so a rock standing in the middle of the room touches none of
    /// it while very obviously being inside the room.
    /// </summary>
    bool IntrudesOnBuiltSpace(GameObject go)
    {
        if (!WorldBounds(go, out Bounds bounds)) return false;

        int xMin = Mathf.FloorToInt(bounds.min.x / WorldUnitSize);
        int xMax = Mathf.FloorToInt(bounds.max.x / WorldUnitSize);
        int zMin = Mathf.FloorToInt(bounds.min.z / WorldUnitSize);
        int zMax = Mathf.FloorToInt(bounds.max.z / WorldUnitSize);

        for (int x = xMin; x <= xMax; x++)
        for (int z = zMin; z <= zMax; z++)
        for (int y = 0; y < size.y; y++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!grid.InBounds(cell) || !IsBuiltSpace(cell)) continue;

            // Vertical overlap too - a cave roof passing above a low room's ceiling is fine.
            float floor = y * WorldUnitSize + FloorSurfaceOffset;
            if (bounds.max.y <= floor || bounds.min.y >= floor + WorldUnitSize) continue;

            return true;
        }

        return false;
    }

    /// <summary>Destroys a piece that reaches into built space. Returns true if it was dropped.</summary>
    bool DiscardIfIntruding(GameObject go)
    {
        if (go == null) return true;
        if (!IntrudesOnBuiltSpace(go)) return false;

        DestroyImmediate(go);
        return true;
    }

    void RegisterGround(GameObject go)
    {
        foreach (var collider in go.GetComponentsInChildren<Collider>())
            if (!collider.isTrigger) caveKitGround.Add(collider);
    }

    void RegisterRoof(GameObject go)
    {
        foreach (var collider in go.GetComponentsInChildren<Collider>())
            if (!collider.isTrigger) caveKitRoof.Add(collider);
    }

    /// <summary>Finds the cave ceiling above a ray, whichever way the cave was built.</summary>
    bool CaveRoofCast(Ray ray, out RaycastHit hit, float distance)
    {
        if (!UsingCaveKit)
        {
            if (caveSurfaceCollider != null) return caveSurfaceCollider.Raycast(ray, out hit, distance);
            hit = default;
            return false;
        }

        return NearestOf(caveKitRoof, ray, out hit, distance);
    }

    /// <summary>
    /// Finds the cave ground under a ray, whichever way the cave was built.
    ///
    /// The mesh cave answers with one collider; the kit cave answers with whichever slab the ray
    /// happens to land on, so the hit has to be filtered back to the pieces this kit laid.
    /// </summary>
    bool CaveGroundCast(Ray ray, out RaycastHit hit, float distance)
    {
        if (!UsingCaveKit)
        {
            if (caveSurfaceCollider != null) return caveSurfaceCollider.Raycast(ray, out hit, distance);
            hit = default;
            return false;
        }

        return NearestOf(caveKitGround, ray, out hit, distance);
    }

    /// <summary>Nearest hit along a ray that belongs to one of the kit's own colliders.</summary>
    static bool NearestOf(HashSet<Collider> set, Ray ray, out RaycastHit hit, float distance)
    {
        hit = default;
        float nearest = float.PositiveInfinity;
        foreach (var candidate in Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!set.Contains(candidate.collider)) continue;
            if (candidate.distance >= nearest) continue;
            nearest = candidate.distance;
            hit = candidate;
        }

        return !float.IsPositiveInfinity(nearest);
    }

    /// <summary>True once there is cave geometry to cast against, either way it was built.</summary>
    bool HasCaveGeometry => UsingCaveKit ? caveKitGround.Count > 0 : caveSurfaceCollider != null;
}
