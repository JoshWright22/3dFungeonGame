using System.Collections.Generic;
using UnityEngine;
using Delver.Dungeon;

/// <summary>
/// Builds rooms from their <see cref="RoomKit"/>: each room's own walls, floor and ceiling, then
/// the timber framing laid over the stone, then its props and light.
///
/// The framing rules are read straight off Synty's demo cellar, piece by piece:
///
///  * Basement_WallPanel_01 pivots on the ceiling line and hangs 2 m down in front of every wall
///    segment. It is a band across the TOP of the wall, not a wainscot.
///  * Basement_Support_Pole_01 stands floor to ceiling 0.5 m off the wall at every joint between
///    two wall segments, never in a corner.
///  * Basement_Support_Wall_01 is the bracket above each pole, pivoted on the wall line at the
///    ceiling and reaching into the room.
///
/// All three share the wall's pivot convention, so they are placed with the same boundary maths
/// as the walls themselves.
/// </summary>
public partial class Generator3D
{
    /// <summary>Which room each built cell belongs to. Corridors and caves are absent.</summary>
    readonly Dictionary<Vector3Int, Room> roomByCell = new Dictionary<Vector3Int, Room>();

    /// <summary>Every wall the wall pass stood inside a room, as (cell, outward direction).</summary>
    readonly HashSet<(Vector3Int cell, Vector3Int dir)> roomWallFaces = new HashSet<(Vector3Int, Vector3Int)>();

    /// <summary>
    /// Footprints of everything standing on room floors so far - posts, light stands, props.
    /// New pieces are kept clear of these rather than spawning inside them.
    /// </summary>
    readonly List<Bounds> furniture = new List<Bounds>();

    RoomKit defaultKit;
    GameObject corridorSconce;
    float fullyLitDistance = 12f;
    float darkDistance = 40f;

    void ApplyRoomKitProfile(DungeonProfile source)
    {
        if (source == null) return;

        defaultKit = source.defaultKit;
        corridorSconce = source.corridorSconce;
        fullyLitDistance = source.fullyLitDistance;
        darkDistance = source.darkDistance;
    }

    /// <summary>Indexes the finished layout by room, so the build passes can ask any cell for its kit.</summary>
    void MapRoomCells()
    {
        roomByCell.Clear();
        roomWallFaces.Clear();
        furniture.Clear();

        foreach (var room in rooms)
        {
            if (room.IsCave) continue;

            foreach (var cell in room.bounds.allPositionsWithin)
            {
                if (!grid.InBounds(cell)) continue;

                CellType t = grid[cell];
                if (t == CellType.BottomFloorRoom || t == CellType.Room) roomByCell[cell] = room;
            }
        }
    }

    /// <summary>The variant each room leans toward, by index into its kit's wall, floor and ceiling lists.</summary>
    readonly Dictionary<Room, Vector3Int> favourites = new Dictionary<Room, Vector3Int>();

    void PickFavourites()
    {
        favourites.Clear();
        foreach (var room in rooms)
        {
            RoomKit kit = KitOf(room);
            if (room.IsCave || kit == null) continue;

            favourites[room] = new Vector3Int(
                kit.walls != null && kit.walls.Length > 0 ? random.Next(kit.walls.Length) : -1,
                kit.floors != null && kit.floors.Length > 0 ? random.Next(kit.floors.Length) : -1,
                kit.ceilings != null && kit.ceilings.Length > 0 ? random.Next(kit.ceilings.Length) : -1);
        }
    }

    /// <summary>Index of this cell's room's favourite in one of its kit's lists (0 walls, 1 floors, 2 ceilings), or -1.</summary>
    int FavouriteAt(Vector3Int cell, int list)
    {
        if (!roomByCell.TryGetValue(cell, out Room room) || !favourites.TryGetValue(room, out Vector3Int f)) return -1;
        return list == 0 ? f.x : list == 1 ? f.y : f.z;
    }

    RoomKit KitOf(Room room) => room != null && room.archetype != null && room.archetype.kit != null
        ? room.archetype.kit
        : defaultKit;

    /// <summary>The kit that builds this cell, or null to fall back to the profile's shared pools.</summary>
    RoomKit KitAt(Vector3Int cell, CellType type)
    {
        if (roomByCell.TryGetValue(cell, out Room room)) return KitOf(room);

        return type == CellType.Hallway || type == CellType.Stairs ? defaultKit : null;
    }

    GameObject WallPrefabAt(Vector3Int cell, CellType type)
    {
        RoomKit kit = KitAt(cell, type);
        GameObject pick = kit != null ? PickWeighted(kit.walls, FavouriteAt(cell, 0), kit.favouriteBoost) : null;

        return pick != null ? pick : WallPrefabFor(type);
    }

    GameObject CeilingPrefabAt(Vector3Int cell, CellType type)
    {
        RoomKit kit = KitAt(cell, type);
        if (kit == null) return CeilingPrefabFor(type);

        GameObject pick = PickWeighted(kit.ceilings, FavouriteAt(cell, 2), kit.favouriteBoost);
        if (pick != null) return pick;

        return kit.ceiling != null ? kit.ceiling : CeilingPrefabFor(type);
    }

    /// <summary>A random quarter turn for this cell's ceiling slab, or none when its kit keeps them square.</summary>
    Quaternion CeilingTurnAt(Vector3Int cell, CellType type)
    {
        RoomKit kit = KitAt(cell, type);
        return kit != null && kit.rotateCeilings
            ? Quaternion.Euler(0f, random.Next(4) * 90f, 0f)
            : Quaternion.identity;
    }

    /// <summary>True when the cell's room carries its own light, so the profile's torches stay out of it.</summary>
    bool KitLightsCell(Vector3Int cell)
    {
        return roomByCell.TryGetValue(cell, out Room room)
            && room.archetype != null && room.archetype.kit != null
            && room.archetype.kit.lightFixture != null;
    }

    GameObject PickWeighted(WeightedPrefab[] set) => PickWeighted(set, -1, 1f);

    /// <summary>A weighted draw, with one entry - the room's favourite - weighted up by <paramref name="boost"/>.</summary>
    GameObject PickWeighted(WeightedPrefab[] set, int favourite, float boost)
    {
        if (set == null || set.Length == 0) return null;

        float Weight(int i) => set[i].weight * (i == favourite ? boost : 1f);

        float total = 0f;
        for (int i = 0; i < set.Length; i++)
            if (set[i].prefab != null) total += Weight(i);

        if (total <= 0f) return null;

        float roll = (float)random.NextDouble() * total;
        for (int i = 0; i < set.Length; i++)
        {
            if (set[i].prefab == null) continue;

            roll -= Weight(i);
            if (roll <= 0f) return set[i].prefab;
        }

        return set[set.Length - 1].prefab;
    }

    /// <summary>
    /// Lays a floor tile from the cell's kit. Returns false when the cell has no kit floor, so
    /// the caller falls back to the shared pool.
    /// </summary>
    bool SpawnKitFloor(Vector3Int cell, CellType type)
    {
        RoomKit kit = KitAt(cell, type);
        GameObject prefab = kit != null ? PickWeighted(kit.floors, FavouriteAt(cell, 1), kit.favouriteBoost) : null;
        if (prefab == null) return false;

        Quaternion rotation = kit.rotateFloors
            ? Quaternion.Euler(0f, random.Next(4) * 90f, 0f)
            : Quaternion.identity;

        // Tiles pivot on a corner, so turning one swings it out of its cell unless the position
        // is solved for the rotated geometry.
        Vector3 centre = (Vector3)cell * WorldUnitSize + new Vector3(HalfWorldUnit, 0f, HalfWorldUnit);
        float y = cell.y * WorldUnitSize + FloorSurfaceOffset - GetTopOffset(prefab);

        Spawn(prefab, CentredSpawnPosition(prefab, centre, rotation, y), rotation);
        return true;
    }

    /// <summary>
    /// Lays the structure a kit puts over bare stone: timber bands and posts, braces, corner caps,
    /// a plinth along the foot of the walls, and an arcade of archways and columns.
    /// </summary>
    void FrameRooms()
    {
        foreach (var room in rooms)
        {
            if (room.IsCave) continue;

            RoomKit kit = KitOf(room);
            if (kit == null) continue;

            int top = room.bounds.yMax - 1;
            int bottom = room.bounds.yMin;
            float ceilingLine = room.bounds.yMax * WorldUnitSize + FloorSurfaceOffset;
            float floorLine = bottom * WorldUnitSize + FloorSurfaceOffset + wallYNudge;

            // Doorways are faces of the room too: the band runs over them and corners form
            // against them. Seen from the room, so the direction points out of it.
            var doorFaces = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (hall, dir) in doorways)
                if (roomByCell.TryGetValue(hall + dir, out Room inside) && inside == room)
                    doorFaces.Add((hall + dir, -dir));

            // The band carries on across doorways: it hangs well above the opening, and a band
            // that broke at every door made the walls with doors read as bare stone.
            if (kit.upperBand != null && top == bottom)
                foreach (var (cell, dir) in doorFaces)
                    SpawnOnFace(kit.upperBand, cell, dir, ceilingLine);

            foreach (var (cell, dir) in roomWallFaces)
            {
                if (!roomByCell.TryGetValue(cell, out Room owner) || owner != room) continue;

                Vector3 outward = new Vector3(dir.x, 0f, dir.z);
                Quaternion facing = Quaternion.LookRotation(-outward, Vector3.up);
                Vector3 centre = CellCentre(cell);
                float storeyFloor = cell.y * WorldUnitSize + FloorSurfaceOffset + wallYNudge;

                if (cell.y == top && kit.upperBand != null)
                    SpawnOnFace(kit.upperBand, cell, dir, ceilingLine);

                if (kit.lowerBand != null)
                    SpawnOnFace(kit.lowerBand, cell, dir, storeyFloor);

                if (kit.courseTrim != null)
                {
                    Vector3 trim = centre + outward * (HalfWorldUnit - kit.courseTrimInset);
                    trim.y = (cell.y + 1) * WorldUnitSize + FloorSurfaceOffset - kit.courseTrimDrop;
                    Spawn(kit.courseTrim, trim, facing);
                }

                if (cell.y != bottom && !kit.postEveryStorey) continue;

                // A joint is where this wall carries straight on into the next cell. Looking one
                // way only means each joint is visited once; corners never qualify, because the
                // next cell along is outside the room.
                var alongCell = new Vector3Int(dir.z, 0, -dir.x);
                if (!roomWallFaces.Contains((cell + alongCell, dir))) continue;

                Vector3 joint = centre + outward * HalfWorldUnit + (Vector3)alongCell * HalfWorldUnit;

                if (kit.post != null)
                {
                    Vector3 foot = joint - outward * kit.postInset;
                    foot.y = storeyFloor;
                    ClaimSpace(Spawn(kit.post, foot, facing));
                }

                if (kit.brace != null && cell.y == bottom)
                {
                    Vector3 head = joint;
                    head.y = ceilingLine;
                    Spawn(kit.brace, head, facing);
                }
            }

            // An inside corner is any face meeting the next one round, a quarter turn on - wall
            // or doorway, since a door frame is a course of wall with a hole in it.
            if (kit.innerCorner != null)
            {
                var solid = new HashSet<(Vector3Int, Vector3Int)>(doorFaces);
                foreach (var (cell, dir) in roomWallFaces)
                    if (InRoom(room, cell)) solid.Add((cell, dir));

                foreach (var (cell, dir) in solid)
                {
                    var turned = new Vector3Int(dir.z, 0, -dir.x);
                    if (!solid.Contains((cell, turned))) continue;

                    float storeyFloor = cell.y * WorldUnitSize + FloorSurfaceOffset + wallYNudge;
                    SpawnInnerCorner(kit.innerCorner, CellCentre(cell), dir, turned, storeyFloor);
                }
            }

            if (kit.archway != null || kit.interiorPost != null)
                BuildArcade(room, kit, floorLine);
        }
    }

    /// <summary>Stands a wall-sized piece on one face of a cell, at the given height, facing in.</summary>
    void SpawnOnFace(GameObject prefab, Vector3Int cell, Vector3Int dir, float height)
    {
        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 boundary = CellCentre(cell) + outward * HalfWorldUnit;
        boundary.y = height;
        SpawnWallPiece(prefab, boundary, Quaternion.LookRotation(-outward, Vector3.up));
    }

    /// <summary>
    /// Caps the inside corner where walls facing <paramref name="a"/> and <paramref name="b"/>
    /// meet. The demo stands the cap half a unit in from the corner on both axes, turned to
    /// whichever quarter of the room it looks into.
    /// </summary>
    void SpawnInnerCorner(GameObject prefab, Vector3 cellCentre, Vector3Int a, Vector3Int b, float height)
    {
        Vector3 toCorner = new Vector3(a.x + b.x, 0f, a.z + b.z);
        Vector3 position = cellCentre + toCorner * HalfWorldUnit - toCorner * 0.5f;
        position.y = height;

        // Read off the demo's cell block: which quarter the room lies in decides the turn.
        float intoX = -toCorner.x, intoZ = -toCorner.z;
        float yaw = intoX > 0f ? (intoZ < 0f ? 0f : 270f) : (intoZ < 0f ? 90f : 180f);

        Spawn(prefab, position, Quaternion.Euler(0f, yaw, 0f));
    }

    /// <summary>
    /// An archway across every cell boundary inside the room, springing from columns at every
    /// grid vertex - the demo laboratory's vaulted arcade.
    /// </summary>
    void BuildArcade(Room room, RoomKit kit, float floorLine)
    {
        foreach (var cell in room.FloorCells())
        {
            Vector3 centre = CellCentre(cell);

            if (kit.archway != null)
                foreach (var dir in new[] { Vector3Int.right, Vector3Int.forward })
                {
                    if (!InRoom(room, cell + dir)) continue;

                    Vector3 across = new Vector3(dir.x, 0f, dir.z);
                    Vector3 boundary = centre + across * HalfWorldUnit;
                    boundary.y = floorLine + kit.archBase;

                    GameObject arch = SpawnWallPiece(kit.archway, boundary, Quaternion.LookRotation(-across, Vector3.up));
                    if (arch != null) arch.transform.localScale = Vector3.Scale(arch.transform.localScale, kit.archScale);
                }

            // A column wherever four of the room's cells meet.
            if (kit.interiorPost != null
                && InRoom(room, cell + Vector3Int.right) && InRoom(room, cell + Vector3Int.forward)
                && InRoom(room, cell + Vector3Int.right + Vector3Int.forward))
            {
                Vector3 vertex = centre + new Vector3(HalfWorldUnit, 0f, HalfWorldUnit);
                vertex.y = floorLine;

                GameObject column = Spawn(kit.interiorPost, vertex, Quaternion.identity);
                if (column != null)
                {
                    column.transform.localScale = Vector3.Scale(column.transform.localScale, kit.interiorPostScale);
                    ClaimSpace(column);
                }
            }
        }
    }

    bool InRoom(Room room, Vector3Int cell) => roomByCell.TryGetValue(cell, out Room r) && r == room;

    /// <summary>
    /// How much fixture light a place gets: 1 near the entry, falling to 0 at
    /// <see cref="darkDistance"/> cells away - lit near the entry, darker deeper.
    /// </summary>
    float LitFactor(Vector3Int cell)
    {
        if (entryRoom == null) return 1f;

        float distance = Vector3.Distance(entryRoom.anchor, cell);
        return 1f - Mathf.InverseLerp(fullyLitDistance, Mathf.Max(fullyLitDistance + 0.01f, darkDistance), distance);
    }

    /// <summary>Props, hangers, rubble and lights for every room that has a kit of its own.</summary>
    void FurnishRooms()
    {
        Vector3 entry = entryRoom != null ? (Vector3)entryRoom.anchor : Vector3.zero;

        var doorCells = new HashSet<Vector3Int>();
        foreach (var (cell, dir) in doorways)
        {
            doorCells.Add(cell);
            doorCells.Add(cell + dir);
        }

        foreach (var room in rooms)
        {
            if (room.IsCave || room.archetype == null || room.archetype.kit == null) continue;

            RoomKit kit = room.archetype.kit;

            // Wall faces at floor level, keeping clear of doorways so nothing blocks the way in.
            var faces = new List<(Vector3Int cell, Vector3Int dir)>();
            foreach (var face in roomWallFaces)
                if (face.cell.y == room.bounds.yMin && !doorCells.Contains(face.cell)
                    && roomByCell.TryGetValue(face.cell, out Room owner) && owner == room)
                    faces.Add(face);

            // HashSet order is not stable across runs; every client has to dress the same room
            // the same way, so sort before drawing from the shared random sequence.
            faces.Sort((a, b) =>
            {
                int c = a.cell.x.CompareTo(b.cell.x);
                if (c == 0) c = a.cell.z.CompareTo(b.cell.z);
                if (c == 0) c = a.dir.x.CompareTo(b.dir.x);
                return c != 0 ? c : a.dir.z.CompareTo(b.dir.z);
            });

            var usedCells = new HashSet<Vector3Int>();

            float distance = Vector3.Distance(entry, room.anchor);
            float lit = room == entryRoom ? 1f
                : 1f - Mathf.InverseLerp(fullyLitDistance, Mathf.Max(fullyLitDistance + 0.01f, darkDistance), distance);

            PlaceRoomLights(room, kit, faces, usedCells, lit);

            if (kit.propSets != null)
                foreach (var set in kit.propSets)
                    PlacePropSet(room, set, faces, usedCells);

            HangAndLitter(room, kit);
        }
    }

    void PlaceRoomLights(Room room, RoomKit kit, List<(Vector3Int cell, Vector3Int dir)> faces,
        HashSet<Vector3Int> usedCells, float lit)
    {
        if (kit.lightFixture == null || lit <= 0f) return;

        bool hung = kit.lightMount == LightMount.Ceiling;
        if (!hung && faces.Count == 0) return;

        // Fractional fixtures round up or down at random, so a half-lit wing is a mix of lit and
        // dark rooms rather than every room getting exactly the same dim treatment.
        float wanted = Mathf.Max(1f, room.FloorArea / (float)kit.cellsPerLight) * lit;
        int count = Mathf.FloorToInt(wanted);
        if (random.NextDouble() < wanted - count) count++;

        // Chandeliers hang over cells; everything else goes against a wall.
        var spots = new List<(Vector3Int cell, Vector3Int dir)>();
        if (hung) foreach (var cell in room.FloorCells()) spots.Add((cell, Vector3Int.zero));
        else spots.AddRange(faces);

        // Spread the fixtures out: the first goes at random, every next one wherever is furthest
        // from the lights already placed. Random picks bunched them up and left the middle of a
        // wide room outside every light's range.
        var placed = new List<Vector3>();
        float underside = room.bounds.yMax * WorldUnitSize + FloorSurfaceOffset - 0.2f;

        for (int i = 0; i < count; i++)
        {
            int pick = -1;
            float bestGap = -1f;
            int first = random.Next(spots.Count);

            for (int k = 0; k < spots.Count; k++)
            {
                int index = (first + k) % spots.Count;
                if (!hung && usedCells.Contains(spots[index].cell)) continue;

                if (placed.Count == 0) { pick = index; break; }

                float gap = float.MaxValue;
                foreach (var p in placed) gap = Mathf.Min(gap, Vector3.Distance(p, CellCentre(spots[index].cell)));
                if (gap > bestGap) { bestGap = gap; pick = index; }
            }

            if (pick < 0) break;

            var (cell, dir) = spots[pick];
            placed.Add(CellCentre(cell));

            GameObject fixture = SpawnFixture(kit, cell, dir, underside);
            if (fixture == null) continue;

            var flame = fixture.AddComponent<Delver.Game.TorchLight>();
            flame.FlameColor = kit.lightColor;
            flame.BaseIntensity = kit.lightIntensity;
            flame.Range = kit.lightRange;
            flame.LocalOffset = FlameOffset(fixture, kit);

            if (!hung) usedCells.Add(cell);
        }
    }

    GameObject SpawnFixture(RoomKit kit, Vector3Int cell, Vector3Int dir, float underside)
    {
        Vector3 centre = CellCentre(cell);
        Vector3 outward = new Vector3(dir.x, 0f, dir.z);

        switch (kit.lightMount)
        {
            case LightMount.Ceiling:
            {
                Vector3 spot = centre;
                spot.y = underside;
                GameObject chandelier = Spawn(kit.lightFixture, spot, Quaternion.identity);
                if (chandelier != null) AlignSurface(chandelier, underside, true);
                return chandelier;
            }

            case LightMount.Wall:
            {
                // The demo's sconces sit 0.2 off the wall line, the bracket reaching into the room.
                Vector3 spot = centre + outward * (HalfWorldUnit - 0.2f);
                spot.y = cell.y * WorldUnitSize + FloorSurfaceOffset + kit.mountHeight;
                return Spawn(kit.lightFixture, spot, Quaternion.LookRotation(-outward, Vector3.up));
            }

            default:
            {
                Vector3 along = Vector3.Cross(outward, Vector3.up);
                Vector3 spot = centre + outward * (HalfWorldUnit - 1.6f)
                    + along * ((float)random.NextDouble() - 0.5f) * 2f
                    + new Vector3(0f, FloorSurfaceOffset, 0f);

                GameObject stand = SpawnGrounded(kit.lightFixture, spot,
                    Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                ClaimSpace(stand);
                return stand;
            }
        }
    }

    /// <summary>Where the flame goes, in the fixture's own space.</summary>
    Vector3 FlameOffset(GameObject fixture, RoomKit kit)
    {
        if (kit.lightMount == LightMount.Floor) return new Vector3(0f, kit.lightHeight, 0f);

        var renderer = fixture.GetComponentInChildren<Renderer>();
        if (renderer == null) return new Vector3(0f, kit.lightHeight, 0f);

        // Measured from the fixture's lowest point: the candles of a chandelier, the cup of a
        // sconce. A sconce's flame also sits out over the far end of its bracket, which the demo
        // puts 0.42 off the wall.
        Bounds b = renderer.bounds;
        Vector3 flame = new Vector3(b.center.x, b.min.y + kit.lightHeight, b.center.z);
        if (kit.lightMount == LightMount.Wall) flame = fixture.transform.position
            + fixture.transform.forward * 0.42f + Vector3.up * kit.lightHeight;

        return fixture.transform.InverseTransformPoint(flame);
    }

    void PlacePropSet(Room room, PropSet set, List<(Vector3Int cell, Vector3Int dir)> faces, HashSet<Vector3Int> usedCells)
    {
        if (set == null || set.pieces == null || set.pieces.Length == 0) return;

        if (set.inMiddle)
        {
            PlaceMiddleSet(room, set, faces, usedCells);
            return;
        }

        if (faces.Count == 0) return;

        if (set.lineWall)
        {
            if (random.NextDouble() >= set.chance) return;

            // The longest straight wall: faces grouped by which way they look and which line they sit on.
            var runs = new Dictionary<(Vector3Int dir, int line), List<Vector3Int>>();
            foreach (var (cell, dir) in faces)
            {
                int line = dir.x != 0 ? cell.x : cell.z;
                var key = (dir, line);
                if (!runs.TryGetValue(key, out var list)) runs[key] = list = new List<Vector3Int>();
                list.Add(cell);
            }

            (Vector3Int dir, int line) best = default;
            int bestCount = 1;
            foreach (var run in runs)
            {
                int free = 0;
                foreach (var c in run.Value) if (!usedCells.Contains(c)) free++;
                if (free > bestCount) { best = run.Key; bestCount = free; }
            }

            if (bestCount < 2) return;

            Vector3 outward = new Vector3(best.dir.x, 0f, best.dir.z);
            foreach (var cell in runs[best])
            {
                if (usedCells.Contains(cell)) continue;

                Vector3 spot = CellCentre(cell) + outward * (HalfWorldUnit - set.inset)
                    + new Vector3(0f, FloorSurfaceOffset, 0f);

                GameObject piece = SpawnPiece(set, spot, outward);
                if (piece != null && !KeepPiece(set, piece)) Discard(piece);
                usedCells.Add(cell);
            }

            return;
        }

        for (int n = 0; n < set.maxPerRoom; n++)
        {
            if (random.NextDouble() >= set.chance) continue;

            // A few tries to find a stretch of wall nothing else is using yet.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var (cell, dir) = faces[random.Next(faces.Count)];
                if (usedCells.Contains(cell)) continue;

                if (set.mountHeight > 0f)
                {
                    HangOnWall(set, cell, dir);
                    usedCells.Add(cell);
                    break;
                }

                Vector3 outward = new Vector3(dir.x, 0f, dir.z);
                Vector3 along = Vector3.Cross(outward, Vector3.up);
                Vector3 middle = CellCentre(cell) + outward * (HalfWorldUnit - set.inset)
                    + along * ((float)random.NextDouble() - 0.5f) * 1.5f
                    + new Vector3(0f, FloorSurfaceOffset, 0f);

                int pieces = random.Next(set.minPieces, Mathf.Max(set.minPieces, set.maxPieces) + 1);
                for (int p = 0; p < pieces; p++)
                {
                    // Graded sets run big, medium, small: the first piece largest, the last smallest.
                    float grade = set.gradeSizes && pieces > 1
                        ? Mathf.Lerp(1.25f, 0.6f, p / (float)(pieces - 1))
                        : 1f;

                    // Pieces jostle for space rather than spawning inside one another: each gets
                    // a few spots to try, and is dropped if none of them is clear.
                    for (int tryAt = 0; tryAt < 5; tryAt++)
                    {
                        // Spread along the wall more than away from it, so a heap hugs the wall.
                        Vector3 spot = middle
                            + along * ((float)random.NextDouble() - 0.5f) * 2f * set.spread
                            - outward * (float)random.NextDouble() * set.spread * 0.6f;

                        GameObject piece = SpawnPiece(set, spot, outward);
                        if (piece == null) continue;
                        if (grade != 1f) ScaleOnGround(piece, grade);
                        if (KeepPiece(set, piece)) break;

                        Discard(piece);
                    }
                }

                usedCells.Add(cell);
                break;
            }
        }
    }

    /// <summary>
    /// A centrepiece in the middle of the room with the rest of the set ringed around it - a
    /// table and its stools, a grate and the bones beside it.
    /// </summary>
    void PlaceMiddleSet(Room room, PropSet set, List<(Vector3Int cell, Vector3Int dir)> faces, HashSet<Vector3Int> usedCells)
    {
        var walled = new HashSet<Vector3Int>();
        foreach (var (cell, _) in faces) walled.Add(cell);

        // Interior cells first, so the table is not pushed up against a wall; small rooms have
        // none, and fall back to any free cell.
        var candidates = new List<Vector3Int>();
        foreach (var cell in room.FloorCells())
            if (!usedCells.Contains(cell) && !walled.Contains(cell)) candidates.Add(cell);
        if (candidates.Count == 0)
            foreach (var cell in room.FloorCells())
                if (!usedCells.Contains(cell)) candidates.Add(cell);

        for (int n = 0; n < set.maxPerRoom && candidates.Count > 0; n++)
        {
            if (random.NextDouble() >= set.chance) continue;

            int index = random.Next(candidates.Count);
            Vector3Int cell = candidates[index];
            candidates.RemoveAt(index);

            Vector3 middle = CellCentre(cell) + new Vector3(0f, FloorSurfaceOffset, 0f);
            float turn = (float)random.NextDouble() * 360f;

            GameObject centrepiece = SpawnGrounded(set.pieces[0], middle, Quaternion.Euler(0f, turn, 0f));
            if (centrepiece == null) continue;
            if (!KeepPiece(set, centrepiece)) { Discard(centrepiece); continue; }
            centrepiece.name = set.name + " - " + set.pieces[0].name;

            if (set.pieces.Length > 1)
            {
                // Stools tuck under their table in the demo, so the ring may overlap its own
                // centrepiece - just not anything else, or each other.
                bool claimed = !set.walkThrough && furniture.Count > 0;
                Bounds centreFootprint = claimed ? furniture[furniture.Count - 1] : default;
                if (claimed) furniture.RemoveAt(furniture.Count - 1);

                int ring = random.Next(set.minPieces, Mathf.Max(set.minPieces, set.maxPieces) + 1);
                for (int p = 0; p < ring; p++)
                {
                    float angle = turn + p * 360f / ring + ((float)random.NextDouble() - 0.5f) * 30f;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * set.spread;

                    // Faces the centrepiece, give or take - stools pulled up to a table.
                    float yaw = angle + 180f + ((float)random.NextDouble() - 0.5f) * 2f * set.yawJitter;
                    GameObject prefab = set.pieces[1 + random.Next(set.pieces.Length - 1)];

                    GameObject piece = SpawnGrounded(prefab, middle + offset, Quaternion.Euler(0f, yaw, 0f));
                    if (piece == null) continue;

                    piece.name = set.name + " - " + prefab.name;
                    if (!KeepPiece(set, piece)) Discard(piece);
                }

                if (claimed) furniture.Add(centreFootprint);
            }

            usedCells.Add(cell);
        }
    }

    /// <summary>Hangs a set's pieces on a wall, their tops at the set's mount height - banners, shields.</summary>
    void HangOnWall(PropSet set, Vector3Int cell, Vector3Int dir)
    {
        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 along = Vector3.Cross(outward, Vector3.up);
        float top = cell.y * WorldUnitSize + FloorSurfaceOffset + set.mountHeight;

        int pieces = random.Next(set.minPieces, Mathf.Max(set.minPieces, set.maxPieces) + 1);
        for (int p = 0; p < pieces; p++)
        {
            // Spread evenly along the wall when there is more than one, as a pair of banners is.
            float slide = pieces > 1 ? (p / (float)(pieces - 1) - 0.5f) * 2f * set.spread : 0f;
            Vector3 spot = CellCentre(cell) + outward * (HalfWorldUnit - set.inset) + along * slide;
            spot.y = top;

            GameObject prefab = set.pieces[random.Next(set.pieces.Length)];
            GameObject piece = Spawn(prefab, spot, Quaternion.LookRotation(-outward, Vector3.up));
            if (piece == null) continue;

            AlignSurface(piece, top, true);
            piece.name = set.name + " - " + prefab.name;
        }
    }

    GameObject SpawnPiece(PropSet set, Vector3 spot, Vector3 outward)
    {
        GameObject prefab = set.pieces[random.Next(set.pieces.Length)];
        float yaw = Quaternion.LookRotation(-outward, Vector3.up).eulerAngles.y
            + ((float)random.NextDouble() - 0.5f) * 2f * set.yawJitter;

        GameObject piece = SpawnGrounded(prefab, spot, Quaternion.Euler(0f, yaw, 0f));
        if (piece != null) piece.name = set.name + " - " + prefab.name;
        return piece;
    }

    /// <summary>
    /// Whether a placed piece may stay. Walk-through pieces always may, and lose their colliders;
    /// everything else has to find clear floor.
    /// </summary>
    bool KeepPiece(PropSet set, GameObject piece)
    {
        if (!set.walkThrough) return ClaimSpace(piece);

        MakeWalkThrough(piece);
        return true;
    }

    /// <summary>
    /// Records a piece's footprint and returns true, or returns false when it would stand inside
    /// furniture already placed. Footprints are trimmed a few centimetres so pieces may touch.
    ///
    /// Deliberately not a physics query: that also caught the timber band and chains overhead,
    /// which a tall keg grazes in the demo too, and threw every keg away.
    /// </summary>
    bool ClaimSpace(GameObject piece)
    {
        if (piece == null) return false;

        var renderers = piece.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return true;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        bounds.Expand(new Vector3(-0.12f, -0.2f, -0.12f));

        foreach (var other in furniture)
            if (other.Intersects(bounds)) return false;

        furniture.Add(bounds);
        return true;
    }

    /// <summary>Removes a rejected piece at once, so it cannot block the next attempt.</summary>
    void Discard(GameObject piece)
    {
        // Inactive colliders leave the physics scene immediately; Destroy itself waits a frame.
        piece.SetActive(false);
        if (Application.isPlaying) Destroy(piece);
        else DestroyImmediate(piece);
    }

    /// <summary>
    /// Floor debris is scenery. Pebble piles with colliders snagged the character controller
    /// and made walking across a room feel like wading through boulders.
    /// </summary>
    static void MakeWalkThrough(GameObject piece)
    {
        if (piece == null) return;

        foreach (var collider in piece.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }
    }

    /// <summary>Chains from the ceiling and debris on the floor, scattered per cell.</summary>
    void HangAndLitter(Room room, RoomKit kit)
    {
        bool hangers = kit.ceilingHangers != null && kit.ceilingHangers.Length > 0;
        bool litter = kit.rubble != null && kit.rubble.Length > 0;
        if (!hangers && !litter) return;

        // Underside of the ceiling slab, which hangs a fifth of a unit below the ceiling line.
        float underside = room.bounds.yMax * WorldUnitSize + FloorSurfaceOffset - 0.2f;

        foreach (var cell in room.FloorCells())
        {
            Vector3 centre = CellCentre(cell);

            if (hangers && random.NextDouble() < kit.hangerChance)
            {
                Vector3 spot = centre + new Vector3(
                    ((float)random.NextDouble() - 0.5f) * 3f, 0f,
                    ((float)random.NextDouble() - 0.5f) * 3f);
                spot.y = underside;

                GameObject chain = Spawn(PickPrefab(kit.ceilingHangers), spot,
                    Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                if (chain != null) AlignSurface(chain, underside, true);
            }

            if (litter && random.NextDouble() < kit.rubbleChance)
            {
                Vector3 heap = centre + new Vector3(
                    ((float)random.NextDouble() - 0.5f) * 3f,
                    FloorSurfaceOffset,
                    ((float)random.NextDouble() - 0.5f) * 3f);

                // A graded heap - one big pile, a couple of medium, a few small - rather than
                // single same-sized pieces dotted about.
                foreach (var tier in ScatterTiers(random.Next(1, 3), random.Next(2, 4)))
                {
                    Vector2 offset = RandomInDisc() * tier.reach * 0.7f;
                    GameObject debris = SpawnGrounded(PickPrefab(kit.rubble), heap + new Vector3(offset.x, 0f, offset.y),
                        Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                    if (debris == null) continue;

                    MakeWalkThrough(debris);

                    // SpawnGrounded has just sat it on the floor; keep it there through the rescale.
                    var renderer = debris.GetComponentInChildren<MeshRenderer>();
                    float ground = renderer != null ? renderer.bounds.min.y : debris.transform.position.y;

                    debris.transform.localScale = Vector3.Scale(debris.transform.localScale, kit.rubbleScale * tier.scale);
                    AlignSurface(debris, ground, false);
                }
            }
        }
    }
}
