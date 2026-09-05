using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;
using Graphs;
using Unity.Mathematics;

public class Generator3D : MonoBehaviour
{
    enum CellType
    {
        None,
        Room,
        BottomFloorRoom,
        Hallway,
        Stairs
    }

    class Room
    {
        public BoundsInt bounds;

        public Room(Vector3Int location, Vector3Int size)
        {
            bounds = new BoundsInt(location, size);
        }

        public static bool Intersect(Room a, Room b)
        {
            return !((a.bounds.position.x >= (b.bounds.position.x + b.bounds.size.x)) || ((a.bounds.position.x + a.bounds.size.x) <= b.bounds.position.x)
                || (a.bounds.position.y >= (b.bounds.position.y + b.bounds.size.y)) || ((a.bounds.position.y + a.bounds.size.y) <= b.bounds.position.y)
                || (a.bounds.position.z >= (b.bounds.position.z + b.bounds.size.z)) || ((a.bounds.position.z + a.bounds.size.z) <= b.bounds.position.z));
        }
    }

    [Header("Profile")]
    [Tooltip("Assign a DungeonProfile to drive generation from one asset. Leave empty to use the inline fields below.")]
    [SerializeField]
    Delver.Dungeon.DungeonProfile profile;

    [Header("Inline settings (used when no profile is assigned)")]
    [SerializeField]
    Vector3Int size;
    [SerializeField]
    int roomCount;
    [SerializeField]
    Vector3Int roomMaxSize;
    [SerializeField]
    GameObject cubePrefab;
    [SerializeField]
    GameObject doorPrefab;
    [SerializeField]
    GameObject stairPrefab;

    [Header("Seeding")]
    [Tooltip("Seed used when this generator builds itself on Start. Every client must use the same value or the dungeons will not match.")]
    [SerializeField]
    int seed = 0;
    [Tooltip("Pick a fresh seed on Start instead of using the serialized one. Leave OFF for networked play until the host broadcasts a seed.")]
    [SerializeField]
    bool randomizeSeedOnStart = false;

    [Tooltip("Generate on Start. Turn OFF for networked play - DungeonRunner drives generation from the host's seed instead.")]
    [SerializeField]
    bool generateOnStart = true;

    [Tooltip("Smallest room, in cells.")]
    [SerializeField]
    Vector3Int roomMinSize = new Vector3Int(3, 1, 3);

    [Tooltip("Cells of solid rock kept between rooms.")]
    [Range(0, 4)]
    [SerializeField]
    int roomSeparation = 1;

    [Header("Shape")]
    [Tooltip("Extra attempts allowed while trying to satisfy roomCount. Rooms are placed by rejection sampling, so without a retry budget a dense layout silently ends up with far fewer rooms than asked for.")]
    [SerializeField]
    int roomPlacementAttemptsPerRoom = 12;

    [Tooltip("Fraction of the non-MST Delaunay edges added back as loops. 0 is a pure tree you can always back out of; higher values make the dungeon a place you can get lost in.")]
    [Range(0f, 1f)]
    [SerializeField]
    float loopEdgeChance = 0.125f;

    [Header("Variants")]
    [Tooltip("Floor tile variants. Must be dimensionally equivalent (Synty's SM_Env_Tiles_01..010 are all 5 x 5). One is picked per cell. Falls back to cubePrefab when empty.")]
    [SerializeField]
    GameObject[] floorPrefabs;

    [Tooltip("Door variants, all sized to a 5-unit opening. Falls back to doorPrefab when empty.")]
    [SerializeField]
    GameObject[] doorPrefabs;

    [Tooltip("Door leaves hung in the doorway openings. Leave empty for bare archways.")]
    [SerializeField]
    GameObject[] doorLeafPrefabs;

    [Tooltip("Half the doorway opening width - where the leaf's hinge sits, measured from the centre of the frame.")]
    [SerializeField]
    float doorHingeOffset = 0.83f;

    [Tooltip("Strip colliders from spawned doorways. Only needed for door prefabs whose collider is a solid box spanning the opening; the Synty wall-doorframe pieces use MeshColliders and are already walkable.")]
    [SerializeField]
    bool makeDoorwaysPassable = false;

    [Header("Torches")]
    [Tooltip("Floor-standing light fixtures - braziers, lanterns. Placed against walls.")]
    [SerializeField]
    GameObject[] torchPrefabs;

    [Tooltip("Chance that any given wall gets a torch beside it. Keep low; every torch is a real-time light.")]
    [Range(0f, 1f)]
    [SerializeField]
    float torchChancePerWall = 0.06f;

    [Tooltip("Minimum cells between torches, so they do not cluster into a bonfire.")]
    [SerializeField]
    int torchMinSpacing = 3;

    [Header("Walls")]
    [Tooltip("Wall segments sealing the edge of any walkable cell that borders solid rock. One is picked at random per edge, so give it several variants for texture.")]
    [SerializeField]
    GameObject[] wallPrefabs;

    [Tooltip("Optional ceiling piece placed over every walkable cell. Roughly doubles the object count - leave empty while iterating on layout.")]
    [SerializeField]
    GameObject ceilingPrefab;

    [Tooltip("Nudge walls vertically if they float or sink relative to the floor tiles.")]
    [SerializeField]
    float wallYNudge = 0f;

    [Tooltip("A wall mesh with faces on BOTH sides (e.g. SM_Env_Wall_01_DoubleSided), used only where a wall is genuinely seen from both sides. Leave empty to fall back to an ordinary wall.")]
    [SerializeField]
    GameObject doubleSidedWallPrefab;

    [Header("Debug")]
    [SerializeField]
    bool drawHallwayGizmos = false;

    public int CurrentSeed { get; private set; }

    /// <summary>Everything this generator spawned, so a regenerate can clear it in one destroy.</summary>
    Transform dungeonRoot;

    Room entryRoom;

    /// <summary>World-space point on the entry room's floor. Where the party arrives.</summary>
    public Vector3 PartySpawnPoint { get; private set; }

    /// <summary>Rooms produced by the last generation, in placement order.</summary>
    public int RoomCount => rooms != null ? rooms.Count : 0;

    Random random;
    Grid3D<CellType> grid;
    List<Room> rooms;
    Delaunay3D delaunay;
    HashSet<Prim.Edge> selectedEdges;
    HashSet<Vector3Int> placedHallways = new HashSet<Vector3Int>();
    readonly List<Vector3Int> placedTorches = new List<Vector3Int>();

    /// <summary>A staircase recorded during pathfinding and built once the layout is final.</summary>
    struct StairRun
    {
        public Vector3Int Prev;
        public Vector3Int Horizontal;
        public Vector3Int Vertical;
        public Vector3 Delta;
        public int XDir;
        public int ZDir;

        /// <summary>Floor cell at the foot of the run.</summary>
        public Vector3Int Lower => Prev;

        /// <summary>
        /// Last ramp cell. The pathfinder steps two cells horizontally and one vertically, and
        /// marks prev+h, prev+2h, prev+v+h and prev+v+2h - so this is the fourth of those, not
        /// a third step along. Getting it wrong walls off the actual top landing.
        /// </summary>
        public Vector3Int Upper => Prev + Vertical + Horizontal * 2;

        /// <summary>Floor cell the run arrives at, one step beyond the last ramp cell.</summary>
        public Vector3Int Landing => Prev + Vertical + Horizontal * 3;
    }

    readonly List<StairRun> stairRuns = new List<StairRun>();

    /// <summary>
    /// The exact (cell, direction) pairs where a staircase opens onto a floor. Only these get
    /// left unwalled; a staircase that merely runs alongside a corridor still needs a wall, or
    /// there is a hole in the corridor edge.
    /// </summary>
    readonly HashSet<(Vector3Int cell, Vector3Int dir)> stairMouths = new HashSet<(Vector3Int, Vector3Int)>();

    /// <summary>
    /// The flanks of every staircase. A ramp climbs past whatever sits beside it, so even when
    /// the neighbouring cell has a floor of its own that floor is half a storey down - a drop the
    /// ordinary same-level wall rule cannot see. These sides are always walled.
    /// </summary>
    readonly HashSet<(Vector3Int cell, Vector3Int dir)> stairFlanks = new HashSet<(Vector3Int, Vector3Int)>();
    readonly List<(Vector3Int cell, Vector3Int dir)> doorways = new List<(Vector3Int, Vector3Int)>();

    // Define the world unit size for one grid cell
    private const float WorldUnitSize = 5f;
    private const float HalfWorldUnit = WorldUnitSize / 2f; // 2.5f - This is the fixed vertical center

    // Two different things that were previously conflated into one number:
    //
    //  * FloorPivotOffset - where a floor tile's PIVOT must go for its geometry to fill the cell.
    //    Synty tiles pivot at their +X/-Z corner, so this is (5, _, 0), not (2.5, _, 2.5).
    //  * CellCentre       - the middle of the cell, which is always half a cell in from the
    //    corner regardless of any prefab's pivot.
    //
    // Walls, doorways, stairs and torches all want the cell centre. Feeding them the tile's
    // pivot offset is what pushed them half a cell out of alignment.
    private Vector3 FloorPivotOffset;

    // Vertical offsets calculated from the prefab's mesh bounds (used for non-cubes or where floor alignment is key)
    private float DoorFloorOffset;

    /// <summary>
    /// Height of the walkable surface above a cell's grid line, derived from the floor tile's own
    /// geometry. Everything that stands on the floor - walls, ceilings, the party - is placed
    /// from this rather than from a guessed constant, so nothing sinks into the ground.
    /// </summary>
    private float FloorSurfaceOffset;

    static readonly Vector3Int[] Directions = new Vector3Int[]
    {
        Vector3Int.right,
        Vector3Int.left,
        Vector3Int.forward,
        Vector3Int.back
    };

    void Start()
    {
        if (!generateOnStart) return;

        Generate(randomizeSeedOnStart ? UnityEngine.Random.Range(int.MinValue, int.MaxValue) : seed);
    }

    /// <summary>
    /// Builds the dungeon from an explicit seed. Two callers with the same seed produce the same
    /// layout, which is what lets the host hand one seed to every client instead of replicating geometry.
    /// </summary>
    public void Generate(int dungeonSeed)
    {
        CurrentSeed = dungeonSeed;

        ApplyProfile();

        // Every spawned tile is parented to this, so regenerating is one destroy rather than a
        // second dungeon standing inside the first.
        ClearDungeon();

        // Calculate the offsets based on the prefab's actual geometry
        CalculateComponentOffsets();

        random = new Random(dungeonSeed);
        grid = new Grid3D<CellType>(size, Vector3Int.zero);
        rooms = new List<Room>();
        placedHallways.Clear();
        placedTorches.Clear();
        stairRuns.Clear();
        stairMouths.Clear();
        stairFlanks.Clear();
        doorways.Clear();
        entryRoom = null;

        PlaceRooms();

        if (rooms.Count < 2)
        {
            Debug.LogError($"Dungeon seed {dungeonSeed} produced {rooms.Count} room(s) - not enough to connect. Check size / roomCount / roomMaxSize.", this);
            return;
        }

        Triangulate();
        CreateHallways();
        PathfindHallways();

        // Layout is only final once everything the party cannot reach has been carved back out.
        SelectEntryRoom();
        int pruned = PruneUnreachable();

        // Geometry is built from the finished grid in one pass, so nothing is ever spawned for a
        // region that later turns out to be unreachable.
        MapStairMouths();

        SpawnFloors();
        SpawnStairs();
        SpawnDoorways();
        PlaceWalls();
        ResolveSpawnPoint();

        Debug.Log($"Dungeon '{(profile != null ? profile.profileName : "inline")}' seed {dungeonSeed}: "
            + $"{rooms.Count} reachable rooms ({pruned} pruned), spawn {PartySpawnPoint}.", this);
    }

    /// <summary>
    /// Copies the assigned profile over the inline fields. Everything downstream keeps reading
    /// the same fields, so a profile is a source of settings rather than a second code path.
    /// </summary>
    void ApplyProfile()
    {
        if (profile == null) return;

        if (!profile.IsUsable(out string problem))
        {
            Debug.LogError($"DungeonProfile '{profile.name}' is not usable: {problem} Falling back to inline settings.", this);
            return;
        }

        size = profile.gridSize;
        roomCount = profile.roomCount;
        roomPlacementAttemptsPerRoom = profile.placementAttemptsPerRoom;
        roomMinSize = profile.roomMinSize;
        roomMaxSize = profile.roomMaxSize;
        roomSeparation = profile.roomSeparation;
        loopEdgeChance = profile.loopEdgeChance;

        floorPrefabs = profile.floorPrefabs;
        wallPrefabs = profile.wallPrefabs;
        doorPrefabs = profile.doorPrefabs;
        torchPrefabs = profile.torchPrefabs;
        ceilingPrefab = profile.ceilingPrefab;

        if (profile.stairPrefab != null) stairPrefab = profile.stairPrefab;

        doorLeafPrefabs = profile.doorLeafPrefabs;
        doorHingeOffset = profile.doorHingeOffset;
        if (profile.doubleSidedWallPrefab != null) doubleSidedWallPrefab = profile.doubleSidedWallPrefab;

        wallYNudge = profile.wallYNudge;
        makeDoorwaysPassable = profile.makeDoorwaysPassable;
        torchChancePerWall = profile.torchChancePerWall;
        torchMinSpacing = profile.torchMinSpacing;

        Debug.Log($"Dungeon profile '{profile.profileName}' applied.", this);
    }

    /// <summary>
    /// Picks the room the party arrives in: the lowest, largest one, which is most likely to
    /// have space for four delvers and a way onward.
    /// </summary>
    void SelectEntryRoom()
    {
        entryRoom = null;
        int bestScore = int.MinValue;

        foreach (var room in rooms)
        {
            var b = room.bounds;
            int score = (-b.position.y * 1000) + b.size.x * b.size.z;

            if (score > bestScore)
            {
                bestScore = score;
                entryRoom = room;
            }
        }
    }

    /// <summary>
    /// Flood-fills from the entry room and carves out everything it cannot reach.
    ///
    /// The generator connects rooms through a spanning tree, but the A* that carves each corridor
    /// can fail - it gives up when a route would need a staircase it has no room for. Any room on
    /// the far side of a failed corridor is sealed off. Rather than leave the party walking into a
    /// dungeon with rooms they can never enter, those cells go back to solid rock.
    /// </summary>
    int PruneUnreachable()
    {
        if (entryRoom == null) return 0;

        var reachable = new HashSet<Vector3Int>();
        var queue = new Queue<Vector3Int>();

        // Seed from every cell of the entry room's floor.
        foreach (var pos in entryRoom.bounds.allPositionsWithin)
        {
            if (!grid.InBounds(pos)) continue;
            if (grid[pos] != CellType.BottomFloorRoom) continue;
            if (reachable.Add(pos)) queue.Enqueue(pos);
        }

        // Staircases are the only vertical links, so they are added as explicit edges rather
        // than relying on grid adjacency.
        var stairLinks = new Dictionary<Vector3Int, List<Vector3Int>>();
        foreach (var run in stairRuns)
        {
            AddLink(stairLinks, run.Lower, run.Upper);
            AddLink(stairLinks, run.Upper, run.Lower);
            AddLink(stairLinks, run.Upper, run.Landing);
            AddLink(stairLinks, run.Landing, run.Upper);

            // The ramp cells themselves sit between the two ends.
            Vector3Int a = run.Prev + run.Horizontal;
            Vector3Int b = run.Prev + run.Vertical + run.Horizontal * 2;
            AddLink(stairLinks, run.Lower, a);
            AddLink(stairLinks, a, run.Lower);
            AddLink(stairLinks, a, b);
            AddLink(stairLinks, b, a);
            AddLink(stairLinks, b, run.Upper);
            AddLink(stairLinks, run.Upper, b);
        }

        while (queue.Count > 0)
        {
            Vector3Int cell = queue.Dequeue();

            foreach (Vector3Int dir in Directions)
            {
                Vector3Int next = cell + dir;
                if (!grid.InBounds(next)) continue;
                if (!HasFloor(grid[next])) continue;
                if (reachable.Add(next)) queue.Enqueue(next);
            }

            if (stairLinks.TryGetValue(cell, out var links))
            {
                foreach (var next in links)
                {
                    if (!grid.InBounds(next)) continue;
                    if (!HasFloor(grid[next])) continue;
                    if (reachable.Add(next)) queue.Enqueue(next);
                }
            }
        }

        // Carve back anything the flood never touched.
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!HasFloor(grid[cell])) continue;
            if (reachable.Contains(cell)) continue;

            grid[cell] = CellType.None;
        }

        // Drop rooms with no reachable floor left, and the staircases and doorways that served them.
        int before = rooms.Count;
        rooms.RemoveAll(room => !RoomHasReachableFloor(room, reachable));

        stairRuns.RemoveAll(run => !reachable.Contains(run.Lower) && !reachable.Contains(run.Upper));
        doorways.RemoveAll(d => !reachable.Contains(d.cell) || !reachable.Contains(d.cell + d.dir));

        return before - rooms.Count;
    }

    static void AddLink(Dictionary<Vector3Int, List<Vector3Int>> map, Vector3Int from, Vector3Int to)
    {
        if (!map.TryGetValue(from, out var list))
        {
            list = new List<Vector3Int>();
            map[from] = list;
        }

        list.Add(to);
    }

    bool RoomHasReachableFloor(Room room, HashSet<Vector3Int> reachable)
    {
        foreach (var pos in room.bounds.allPositionsWithin)
            if (pos.y == room.bounds.yMin && reachable.Contains(pos)) return true;

        return false;
    }

    /// <summary>
    /// Lays a floor on every cell a delver can stand on.
    ///
    /// Only the bottom level of a room gets a floor. The cells above it are headroom, which is
    /// what makes a tall room tall - previously every level of a room was floored, producing a
    /// second storey with no way up to it.
    /// </summary>
    void SpawnFloors()
    {
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            CellType type = grid[cell];

            // Stairs carry their own ramp geometry; Room is headroom above a floor.
            if (type != CellType.BottomFloorRoom && type != CellType.Hallway) continue;

            InstantiateCellObject(cell);
        }
    }

    /// <summary>Records where each staircase meets a floor, in both directions.</summary>
    void MapStairMouths()
    {
        stairMouths.Clear();
        stairFlanks.Clear();

        foreach (var run in stairRuns)
        {
            Vector3Int h = run.Horizontal;

            // Perpendicular to the direction of travel: the open sides of the stairwell.
            Vector3Int perpA = new Vector3Int(h.z, 0, h.x);
            Vector3Int perpB = -perpA;

            foreach (var cell in new[] { run.Prev + h, run.Prev + h * 2,
                                         run.Prev + run.Vertical + h, run.Prev + run.Vertical + h * 2 })
            {
                stairFlanks.Add((cell, perpA));
                stairFlanks.Add((cell, perpB));
            }

            // Bottom of the run: the floor cell steps onto the first ramp cell.
            stairMouths.Add((run.Lower, h));
            stairMouths.Add((run.Lower + h, -h));

            // Between the two ramp cells on each level.
            stairMouths.Add((run.Lower + h, h));
            stairMouths.Add((run.Lower + h * 2, -h));
            stairMouths.Add((run.Upper - h, h));
            stairMouths.Add((run.Upper, -h));

            // Top of the run: the last ramp cell steps onto the landing.
            stairMouths.Add((run.Upper, h));
            stairMouths.Add((run.Landing, -h));
        }
    }

    void SpawnStairs()
    {
        foreach (var run in stairRuns)
            PlaceStairs(run.Prev, run.Horizontal, run.Vertical, run.Delta, run.XDir, run.ZDir);
    }

    void SpawnDoorways()
    {
        foreach (var (cell, dir) in doorways)
            PlaceDoor(cell, Quaternion.identity, dir);
    }

    /// <summary>Destroys everything the previous generation spawned and starts a fresh container.</summary>
    void ClearDungeon()
    {
        if (dungeonRoot != null)
        {
            if (Application.isPlaying) Destroy(dungeonRoot.gameObject);
            else DestroyImmediate(dungeonRoot.gameObject);
        }

        var go = new GameObject("Dungeon");
        go.transform.SetParent(transform, false);
        dungeonRoot = go.transform;
    }

    /// <summary>Instantiates under the dungeon container so <see cref="ClearDungeon"/> can reclaim it.</summary>
    GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;
        return Instantiate(prefab, position, rotation, dungeonRoot);
    }

    /// <summary>
    /// Places the party on the entry room's floor, confirmed against the geometry that actually
    /// got built rather than predicted from prefab bounds.
    /// </summary>
    void ResolveSpawnPoint()
    {
        if (entryRoom == null)
        {
            PartySpawnPoint = Vector3.zero;
            return;
        }

        var bounds = entryRoom.bounds;
        Vector3 centreCell = new Vector3(
            bounds.position.x + (bounds.size.x - 1) * 0.5f,
            bounds.position.y,
            bounds.position.z + (bounds.size.z - 1) * 0.5f);

        Vector3 predicted = centreCell * WorldUnitSize
            + new Vector3(HalfWorldUnit, FloorSurfaceOffset, HalfWorldUnit);

        PartySpawnPoint = predicted + new Vector3(0f, 0.15f, 0f);

        // The tiles were instantiated moments ago; push their transforms into the physics scene
        // before asking it anything.
        Physics.SyncTransforms();

        Vector3 probeFrom = predicted + new Vector3(0f, WorldUnitSize, 0f);
        if (Physics.Raycast(probeFrom, Vector3.down, out RaycastHit hit, WorldUnitSize * 2f,
                ~0, QueryTriggerInteraction.Ignore))
        {
            PartySpawnPoint = hit.point + new Vector3(0f, 0.15f, 0f);
        }
    }

    /// <summary>Cells a delver can actually stand in.</summary>
    bool IsWalkable(CellType t)
    {
        return t == CellType.Room || t == CellType.BottomFloorRoom
            || t == CellType.Hallway || t == CellType.Stairs;
    }

    /// <summary>
    /// Cells with something underfoot. Distinct from <see cref="IsWalkable"/>, which also counts
    /// Room - the headroom ABOVE a room's floor, which is open space with nothing to stand on.
    ///
    /// Walls and connectivity must use this one. Treating headroom as walkable left no wall
    /// between an upper corridor and a room's open volume, so you could stroll off the edge.
    /// </summary>
    bool HasFloor(CellType t)
    {
        return t == CellType.BottomFloorRoom || t == CellType.Hallway || t == CellType.Stairs;
    }

    /// <summary>
    /// Seals the dungeon. Any walkable cell that borders solid rock gets a wall on that edge,
    /// which is what turns a field of floor tiles into rooms and corridors you cannot see across.
    /// </summary>
    void PlaceWalls()
    {
        if (wallPrefabs == null || wallPrefabs.Length == 0)
        {
            Debug.LogWarning("Generator3D: no wall prefabs assigned - the dungeon will generate as open floor.", this);
            return;
        }

        // Walls pivot at their own base, so they simply sit on the floor surface.
        float wallFloorOffset = FloorSurfaceOffset + wallYNudge;

        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            CellType here = grid[cell];

            // Every open cell gets walled, headroom included - otherwise a tall room is open to
            // the void above head height.
            if (!IsWalkable(here)) continue;

            Vector3 cellCentre = CellCentre(cell);
            bool standable = HasFloor(here);

            foreach (Vector3Int dir in Directions)
            {
                Vector3Int neighbour = cell + dir;
                bool inBounds = grid.InBounds(neighbour);
                CellType beyond = inBounds ? grid[neighbour] : CellType.None;

                // Leave the staircase mouths open, but only those - a ramp running alongside a
                // corridor is not a way through, and skipping its wall leaves a hole to fall down.
                if (stairMouths.Contains((cell, dir))) continue;

                bool enclose = !inBounds || !IsWalkable(beyond);

                // A guard wall stops you strolling off a floor into open space that has none -
                // an upper corridor meeting a room's headroom, for instance.
                bool guard = standable && !enclose && !HasFloor(beyond);

                // A stairwell flank is walled even when the cell beside it has a floor, because
                // that floor is half a storey below the ramp climbing past it.
                bool flank = stairFlanks.Contains((cell, dir));

                if (!enclose && !guard && !flank) continue;

                // A guard wall is seen from both sides, so it needs a mesh with faces on both.
                // Guards and flanks are seen from both sides. A stairwell flank especially: the
                // ramp climbs past it, so it is looked at from above, below and alongside.
                bool seenBothSides = guard || flank;

                GameObject prefab = seenBothSides && doubleSidedWallPrefab != null
                    ? doubleSidedWallPrefab
                    : wallPrefabs[random.Next(wallPrefabs.Length)];

                PlaceWall(cellCentre, dir, wallFloorOffset, prefab);

                // A staircase cell's ramp descends half a storey below that cell's floor line, so
                // a wall based at the floor line leaves the lower half of the ramp open to the
                // drop beside it. A second course underneath closes it.
                if (here == CellType.Stairs)
                {
                    PlaceWall(cellCentre, dir, wallFloorOffset - HalfWorldUnit, prefab);
                    PlaceWall(cellCentre, dir, wallFloorOffset + HalfWorldUnit, prefab);
                }

                if (standable) TryPlaceTorch(cell, cellCentre, dir, wallFloorOffset);
            }

            if (ceilingPrefab != null && standable)
            {
                Vector3Int above = cell + Vector3Int.up;
                bool openAbove = grid.InBounds(above) && IsWalkable(grid[above]);

                if (!openAbove)
                {
                    // Ceiling slabs pivot at a corner like the floor tiles do, and hang below
                    // that pivot - so they take the tile's pivot offset, not the cell centre,
                    // and sit at the top of the cell.
                    Vector3 pivot = PivotOffsetFor(ceilingPrefab);
                    Vector3 pos = (Vector3)cell * WorldUnitSize
                        + new Vector3(pivot.x, WorldUnitSize + FloorSurfaceOffset, pivot.z);

                    Spawn(ceilingPrefab, pos, Quaternion.identity);
                }
            }
        }
    }

    /// <summary>
    /// Stands one wall segment on the boundary between a cell and the rock beside it.
    ///
    /// The Synty wall meshes pivot at floor level on one end and run 5 units along their local
    /// -X, so the piece is pushed half a cell along its own right vector to sit centred on the
    /// edge rather than hanging off it.
    /// </summary>
    void PlaceWall(Vector3 cellCentre, Vector3Int dir, float floorOffset, GameObject prefab)
    {
        if (prefab == null) return;

        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 boundary = cellCentre + outward * HalfWorldUnit + new Vector3(0f, floorOffset, 0f);

        // Face the wall back into the room it encloses.
        Quaternion rotation = Quaternion.LookRotation(-outward, Vector3.up);
        SpawnWallPiece(prefab, boundary, rotation);
    }

    /// <summary>
    /// Stands a wall-sized piece centred on a cell boundary, textured face toward the cell.
    ///
    /// The piece spans 5 units along its own -X from a pivot at one end, so centring means
    /// offsetting half a cell along its right vector.
    ///
    /// It deliberately does NOT spawn a mirrored copy. The mesh is 0.43 thick centred slightly
    /// off the pivot plane, so a 180-degree copy interpenetrates the original and its blank back
    /// plane renders in front of the textured face. Walls are instead placed per open cell, which
    /// already puts a textured face on whichever side you can stand.
    /// </summary>
    void SpawnWallPiece(GameObject prefab, Vector3 boundary, Quaternion rotation)
    {
        Vector3 right = rotation * Vector3.right;
        Spawn(prefab, boundary + right * HalfWorldUnit, rotation);
    }

    /// <summary>
    /// Stands two copies of a piece back to back so it is textured from both directions.
    ///
    /// This is NOT the mirror that failed before. That spawned the copy at the same depth, where
    /// the 0.43-thick slabs interpenetrated and the blank back plane punched through the textured
    /// face. Here each copy is pushed back along its own forward by the distance from its pivot
    /// plane to its front face, so the two front faces meet on the boundary and each body sits
    /// entirely on its own side.
    /// </summary>
    void SpawnDoubleSidedPiece(GameObject prefab, Vector3 boundary, Quaternion rotation)
    {
        if (prefab == null) return;

        float front = FrontFaceOffset(prefab);

        Vector3 rightA = rotation * Vector3.right;
        Vector3 fwdA = rotation * Vector3.forward;
        Spawn(prefab, boundary + rightA * HalfWorldUnit - fwdA * front, rotation);

        Quaternion flipped = rotation * Quaternion.Euler(0f, 180f, 0f);
        Vector3 rightB = flipped * Vector3.right;
        Vector3 fwdB = flipped * Vector3.forward;
        Spawn(prefab, boundary + rightB * HalfWorldUnit - fwdB * front, flipped);
    }

    /// <summary>Distance from a prefab's pivot plane to the front of its geometry, along +Z.</summary>
    float FrontFaceOffset(GameObject prefab)
    {
        MeshRenderer renderer = prefab != null ? prefab.GetComponentInChildren<MeshRenderer>() : null;
        return renderer == null ? 0f : renderer.bounds.max.z;
    }

    /// <summary>
    /// Occasionally stands a brazier or lantern against a wall. Spaced out deliberately: each one
    /// is a real-time light, and a dungeon lit end to end defeats the point of carrying a torch.
    /// </summary>
    void TryPlaceTorch(Vector3Int cell, Vector3 cellCentre, Vector3Int dir, float floorOffset)
    {
        if (torchPrefabs == null || torchPrefabs.Length == 0) return;
        if (random.NextDouble() >= torchChancePerWall) return;

        for (int i = 0; i < placedTorches.Count; i++)
        {
            Vector3Int d = placedTorches[i] - cell;
            if (Mathf.Abs(d.x) + Mathf.Abs(d.y) * 2 + Mathf.Abs(d.z) < torchMinSpacing) return;
        }

        GameObject prefab = torchPrefabs[random.Next(torchPrefabs.Length)];
        if (prefab == null) return;

        // Stand it just inside the wall, facing the room.
        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 position = cellCentre + outward * (HalfWorldUnit - 0.9f) + new Vector3(0f, floorOffset, 0f);

        GameObject torch = Spawn(prefab, position, Quaternion.LookRotation(-outward, Vector3.up));
        if (torch == null) return;

        torch.AddComponent<Delver.Game.TorchLight>();
        placedTorches.Add(cell);
    }


    // Combined offset calculation method
    void CalculateComponentOffsets()
    {
        // Helper to find the distance from the pivot (0,0,0) to the bottom (extents.y)
        float GetFloorOffset(GameObject prefab)
        {
            MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();
            if (renderer == null)
            {
                // Fallback to half the WorldUnitSize (2.5)
                return HalfWorldUnit;
            }
            return renderer.bounds.extents.y;
        }

        // Horizontal offsets come from the tile that actually gets spawned.
        FloorPivotOffset = PivotOffsetFor(RepresentativeFloorPrefab());

        // A tile is instantiated with its pivot at (gridLine + HalfWorldUnit). Its walkable
        // surface is the top of its COLLIDER - the renderer's bounds are a few centimetres
        // taller, which is exactly how far into the floor the party used to spawn.
        FloorSurfaceOffset = HalfWorldUnit + GetTopOffset(RepresentativeFloorPrefab());

        // The doorway keeps its own vertical offset; stairs now stand on FloorSurfaceOffset.
        DoorFloorOffset = GetFloorOffset(doorPrefab);

        if (drawHallwayGizmos)
            Debug.Log($"Offsets: floor pivot={FloorPivotOffset}, floor surface={FloorSurfaceOffset}, door Y={DoorFloorOffset}");
    }

    /// <summary>
    /// Where a prefab's pivot must sit, relative to a cell's grid corner, for its geometry to
    /// land centred in that cell. Solves pivot + localCentre = HalfWorldUnit on each horizontal
    /// axis, so it is correct whatever corner the artist put the pivot on.
    /// </summary>
    Vector3 PivotOffsetFor(GameObject prefab)
    {
        if (prefab == null) return new Vector3(HalfWorldUnit, 0f, HalfWorldUnit);

        MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();
        if (renderer == null) return new Vector3(HalfWorldUnit, 0f, HalfWorldUnit);

        Bounds b = renderer.bounds;
        return new Vector3(HalfWorldUnit - b.center.x, 0f, HalfWorldUnit - b.center.z);
    }

    /// <summary>
    /// Position at which to spawn a rotated prefab so its geometry lands centred on
    /// <paramref name="cellCentreWorld"/>.
    ///
    /// Rotating a prefab swings its pivot around its geometry, so a pivot offset measured on the
    /// unrotated mesh is only valid unrotated. Solving position + rotation * localCentre = target
    /// keeps it correct at any angle - which is what the stairs needed.
    /// </summary>
    Vector3 CentredSpawnPosition(GameObject prefab, Vector3 cellCentreWorld, Quaternion rotation, float worldY)
    {
        Vector3 localCentre = Vector3.zero;

        if (prefab != null)
        {
            MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
            {
                Bounds b = renderer.bounds;
                localCentre = new Vector3(b.center.x, 0f, b.center.z);
            }
        }

        Vector3 offset = rotation * localCentre;
        return new Vector3(cellCentreWorld.x - offset.x, worldY, cellCentreWorld.z - offset.z);
    }

    /// <summary>World-space centre of a grid cell, at its floor line (y before floor thickness).</summary>
    Vector3 CellCentre(Vector3Int cell)
    {
        return (Vector3)cell * WorldUnitSize + new Vector3(HalfWorldUnit, 0f, HalfWorldUnit);
    }

    /// <summary>
    /// Distance from a prefab's pivot up to the top of the surface a character collides with.
    ///
    /// Deliberately avoids Collider.bounds: on a prefab ASSET that returns an empty box, because
    /// bounds are only computed for a collider living in a scene. Reading the collider's own
    /// geometry works on assets, which is what this is called with.
    /// </summary>
    float GetTopOffset(GameObject prefab)
    {
        if (prefab == null) return 0f;

        float best = float.NegativeInfinity;

        foreach (var box in prefab.GetComponentsInChildren<BoxCollider>())
        {
            float top = box.transform.localPosition.y
                + (box.center.y + box.size.y * 0.5f) * box.transform.localScale.y;
            best = Mathf.Max(best, top);
        }

        foreach (var mesh in prefab.GetComponentsInChildren<MeshCollider>())
        {
            if (mesh.sharedMesh == null) continue;
            float top = mesh.transform.localPosition.y
                + mesh.sharedMesh.bounds.max.y * mesh.transform.localScale.y;
            best = Mathf.Max(best, top);
        }

        if (!float.IsNegativeInfinity(best)) return best;

        // No collider at all - fall back to what is drawn.
        MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();
        return renderer == null ? 0f : renderer.bounds.center.y + renderer.bounds.extents.y;
    }

    /// <summary>
    /// A stable stand-in for measuring tile geometry. Every variant is the same size, so any of
    /// them will do - but it must not consume a random draw, because offsets are calculated
    /// before the run's RNG is seeded.
    /// </summary>
    GameObject RepresentativeFloorPrefab()
    {
        if (floorPrefabs != null)
            foreach (var p in floorPrefabs)
                if (p != null) return p;

        return cubePrefab;
    }

    /// <summary>A floor tile variant, or the single legacy prefab when no variants are set.</summary>
    GameObject FloorPrefab()
    {
        if (floorPrefabs != null && floorPrefabs.Length > 0)
        {
            var pick = floorPrefabs[random.Next(floorPrefabs.Length)];
            if (pick != null) return pick;
        }

        return cubePrefab;
    }

    GameObject DoorPrefab()
    {
        if (doorPrefabs != null && doorPrefabs.Length > 0)
        {
            var pick = doorPrefabs[random.Next(doorPrefabs.Length)];
            if (pick != null) return pick;
        }

        return doorPrefab;
    }

    void PlaceRooms()
    {
        int attempts = 0;
        int maxAttempts = roomCount * Mathf.Max(1, roomPlacementAttemptsPerRoom);

        // Rejection sampling: keep trying until roomCount rooms fit or the budget runs out.
        // The original loop ran exactly roomCount times, so every rejected candidate was a room
        // the dungeon simply never got.
        while (rooms.Count < roomCount && attempts < maxAttempts)
        {
            attempts++;

            Vector3Int location = new Vector3Int(
                random.Next(0, size.x),
                random.Next(0, size.y),
                random.Next(0, size.z)
            );

            Vector3Int roomSize = new Vector3Int(
                random.Next(Mathf.Max(1, roomMinSize.x), roomMaxSize.x + 1),
                random.Next(Mathf.Max(1, roomMinSize.y), roomMaxSize.y + 1),
                random.Next(Mathf.Max(1, roomMinSize.z), roomMaxSize.z + 1)
            );

            bool add = true;
            Room newRoom = new Room(location, roomSize);
            // Rock kept between rooms. Without it rooms fuse into one cave and the dungeon
            // stops reading as a set of chambers.
            int sep = Mathf.Max(0, roomSeparation);
            Room buffer = new Room(location + new Vector3Int(-sep, 0, -sep), roomSize + new Vector3Int(sep * 2, 0, sep * 2));

            foreach (var room in rooms)
            {
                if (Room.Intersect(room, buffer))
                {
                    add = false;
                    break;
                }
            }

            if (newRoom.bounds.xMin < 0 || newRoom.bounds.xMax >= size.x
                || newRoom.bounds.yMin < 0 || newRoom.bounds.yMax >= size.y
                || newRoom.bounds.zMin < 0 || newRoom.bounds.zMax >= size.z)
            {
                add = false;
            }

            if (add)
            {
                rooms.Add(newRoom);

                foreach (var pos in newRoom.bounds.allPositionsWithin)
                {
                    if (newRoom.bounds.yMin == pos.y)
                    {
                        grid[pos] = CellType.BottomFloorRoom;
                    }
                    else
                    {
                        grid[pos] = CellType.Room;
                    }

                }
            }
        }

        if (rooms.Count < roomCount)
        {
            Debug.LogWarning($"Placed {rooms.Count}/{roomCount} rooms in {attempts} attempts. The grid is too small or the rooms too large for the requested count.", this);
        }
    }

    void Triangulate()
    {
        List<Vertex> vertices = new List<Vertex>();

        foreach (var room in rooms)
        {
            vertices.Add(new Vertex<Room>((Vector3)room.bounds.position + ((Vector3)room.bounds.size) / 2, room));
        }

        delaunay = Delaunay3D.Triangulate(vertices);
    }

    void CreateHallways()
    {
        List<Prim.Edge> edges = new List<Prim.Edge>();

        foreach (var edge in delaunay.Edges)
        {
            edges.Add(new Prim.Edge(edge.U, edge.V));
        }

        List<Prim.Edge> minimumSpanningTree = Prim.MinimumSpanningTree(edges, edges[0].U);

        selectedEdges = new HashSet<Prim.Edge>(minimumSpanningTree);
        var remainingEdges = new HashSet<Prim.Edge>(edges);
        remainingEdges.ExceptWith(selectedEdges);

        foreach (var edge in remainingEdges)
        {
            if (random.NextDouble() < loopEdgeChance)
            {
                selectedEdges.Add(edge);
            }
        }
    }

    void PathfindHallways()
    {
        DungeonPathfinder3D aStar = new DungeonPathfinder3D(size);

        foreach (var edge in selectedEdges)
        {
            var startRoom = (edge.U as Vertex<Room>).Item;
            var endRoom = (edge.V as Vertex<Room>).Item;

            var startPosf = startRoom.bounds.center;
            var endPosf = endRoom.bounds.center;
            var startPos = new Vector3Int((int)startPosf.x, (int)startPosf.y, (int)startPosf.z);
            var endPos = new Vector3Int((int)endPosf.x, (int)endPosf.y, (int)endPosf.z);

            var path = aStar.FindPath(startPos, endPos, (DungeonPathfinder3D.Node a, DungeonPathfinder3D.Node b) =>
            {
                var pathCost = new DungeonPathfinder3D.PathCost();

                var delta = b.Position - a.Position;

                // if (grid[endPos] == CellType.Room || grid[startPos] == CellType.Room)
                // {
                //     return pathCost;
                // }
                if (delta.y == 0)
                {
                    //flat hallway
                    pathCost.cost = Vector3Int.Distance(b.Position, endPos);    //heuristic

                    if (grid[b.Position] == CellType.Stairs)
                    {
                        return pathCost;
                    }
                    else if (grid[b.Position] == CellType.Room)
                    {
                        pathCost.cost += 50;
                    }
                    else if (grid[b.Position] == CellType.BottomFloorRoom)
                    {
                        pathCost.cost += 5;
                    }
                    else if (grid[b.Position] == CellType.None)
                    {
                        pathCost.cost += 1;
                    }

                    pathCost.traversable = true;
                }
                else
                {
                    //staircase

                    if ((grid[a.Position] != CellType.None && grid[a.Position] != CellType.Hallway)
                    || (grid[b.Position] != CellType.None && grid[b.Position] != CellType.Hallway))
                        return pathCost;

                    pathCost.cost = 100 + Vector3Int.Distance(b.Position, endPos);    //base cost + heuristic
                    int xDir = Mathf.Clamp(delta.x, -1, 1);
                    int zDir = Mathf.Clamp(delta.z, -1, 1);
                    Vector3Int verticalOffset = new Vector3Int(0, delta.y, 0);
                    Vector3Int horizontalOffset = new Vector3Int(xDir, 0, zDir);

                    if (!grid.InBounds(a.Position + verticalOffset)
                        || !grid.InBounds(a.Position + horizontalOffset)
                        || !grid.InBounds(a.Position + verticalOffset + horizontalOffset))
                    {
                        return pathCost;
                    }

                    if (grid[a.Position + horizontalOffset] != CellType.None
                        || grid[a.Position + horizontalOffset * 2] != CellType.None
                        || grid[a.Position + verticalOffset + horizontalOffset] != CellType.None
                        || grid[a.Position + verticalOffset + horizontalOffset * 2] != CellType.None)
                    {
                        return pathCost;
                    }

                    pathCost.traversable = true;
                    pathCost.isStairs = true;
                }

                return pathCost;
            });

            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    var current = path[i];

                    if (grid[current] == CellType.None)
                    {
                        grid[current] = CellType.Hallway;
                    }

                    if (i > 0)
                    {
                        var prev = path[i - 1];

                        var delta = current - prev;

                        if (delta.y != 0)
                        {
                            int xDir = Mathf.Clamp(delta.x, -1, 1);
                            int zDir = Mathf.Clamp(delta.z, -1, 1);
                            Vector3Int verticalOffset = new Vector3Int(0, delta.y, 0);
                            Vector3Int horizontalOffset = new Vector3Int(xDir, 0, zDir);

                            grid[prev + horizontalOffset] = CellType.Stairs;
                            grid[prev + horizontalOffset * 2] = CellType.Stairs;
                            grid[prev + verticalOffset + horizontalOffset] = CellType.Stairs;
                            grid[prev + verticalOffset + horizontalOffset * 2] = CellType.Stairs;


                            // Recorded, not built: the layout is not final until unreachable
                            // regions have been pruned, and building here would strand geometry.
                            stairRuns.Add(new StairRun
                            {
                                Prev = prev,
                                Horizontal = horizontalOffset,
                                Vertical = verticalOffset,
                                Delta = delta,
                                XDir = xDir,
                                ZDir = zDir,
                            });
                        }

                        if (drawHallwayGizmos)
                            Debug.DrawLine(prev + new Vector3(0.5f, 0.5f, 0.5f), current + new Vector3(0.5f, 0.5f, 0.5f), Color.blue, 100, false);
                    }
                }

                foreach (var pos in path)
                {
                    if (grid[pos] == CellType.Hallway)
                    {
                        if (placedHallways.Contains(pos))
                        {
                            continue;
                        }
                        else
                        {
                            placedHallways.Add(pos);

                            foreach (Vector3Int direction in Directions)
                            {
                                if (!grid.InBounds(pos + direction)) continue;

                                if (grid[pos + direction] == CellType.BottomFloorRoom)
                                    doorways.Add((pos, direction));
                            }
                        }

                    }
                }
            }
        }
    }

    // Helper method to instantiate a single 1x1x1 cube (for a 5x5x5 cell)
    void InstantiateCellObject(Vector3Int location)
    {
        GameObject prefab = FloorPrefab();

        // Pivot offset is per-variant: a tile whose pivot sits on a different corner still has
        // to land filling the same cell.
        Vector3 pivot = PivotOffsetFor(prefab);
        Vector3 worldPosition = (Vector3)location * WorldUnitSize
            + new Vector3(pivot.x, HalfWorldUnit, pivot.z);

        Spawn(prefab, worldPosition, Quaternion.identity);
    }




    /// <summary>
    /// Detect which direction the stairs are going and place accordingly.
    /// </summary>
    void PlaceStairs(Vector3Int prev, Vector3Int horizontalOffset, Vector3Int verticalOffset, Vector3 delta, int xDir, int zDir)
    {
        // The half-step height (2.5 units) in the world
        float halfStepHeight = WorldUnitSize / 2f;

        Vector3Int gridStairOne = prev + horizontalOffset;
        Vector3Int gridStairFour = prev + horizontalOffset * 2 + verticalOffset;

        bool goingUp = delta.y > 0;

        // Measured from the mesh: the ramp rises along its own -Z (climb vector -0.08, 2.49,
        // -4.72). So to make it ascend in a given world direction, -Z must point that way, which
        // means forward points the opposite way. Aiming forward along the ascent - the obvious
        // reading - builds every staircase backwards.
        Vector3 travel = new Vector3(xDir, 0f, zDir);
        Vector3 ascent = goingUp ? travel : -travel;

        Quaternion rotation = Quaternion.LookRotation(-ascent, Vector3.up);

        // Both ramps are measured from the LOWER floor, because together they climb one storey:
        // the first spans floor -> floor+2.5, the second floor+2.5 -> floor+5. Measuring the
        // second from the upper floor instead left a 7.5 unit gap between two 2.5 unit ramps,
        // which is why the staircases could not be walked up.
        int lowerLevel = Mathf.Min(gridStairOne.y, gridStairFour.y);
        float lowerY = lowerLevel * WorldUnitSize + FloorSurfaceOffset;

        Vector3 oneCentre = CellCentre(gridStairOne);
        Vector3 fourCentre = CellCentre(gridStairFour);

        float oneY = goingUp ? lowerY : lowerY + halfStepHeight;
        float fourY = goingUp ? lowerY + halfStepHeight : lowerY;

        Spawn(stairPrefab, CentredSpawnPosition(stairPrefab, oneCentre, rotation, oneY), rotation);
        Spawn(stairPrefab, CentredSpawnPosition(stairPrefab, fourCentre, rotation, fourY), rotation);
    }

    /// <summary>
    /// Places a doorway between a hallway and a room.
    ///
    /// These are wall-sized pieces with an opening cut through them, not free-standing frames,
    /// so they are positioned exactly like a wall: pivot at floor level on one end, pushed half
    /// a cell along the piece's own right vector to sit centred on the boundary. That is what
    /// makes a door read as a hole in a wall rather than an outline floating in a gap.
    /// </summary>
    void PlaceDoor(Vector3Int location, Quaternion unusedRotation, Vector3Int offset)
    {
        Vector3 cellCentre = CellCentre(location);

        Vector3 outward = new Vector3(offset.x, 0f, offset.z);
        Vector3 boundary = cellCentre + outward * HalfWorldUnit
            + new Vector3(0f, FloorSurfaceOffset + wallYNudge, 0f);

        Quaternion rotation = Quaternion.LookRotation(-outward, Vector3.up);

        // A doorway is walked through, so it is seen from both sides by definition.
        int before = dungeonRoot.childCount;
        SpawnDoubleSidedPiece(DoorPrefab(), boundary, rotation);

        if (makeDoorwaysPassable)
        {
            // Only needed for frames whose collider spans the opening. The Synty wall-doorframe
            // pieces use MeshColliders, which already leave the doorway walkable.
            for (int i = before; i < dungeonRoot.childCount; i++)
                foreach (var collider in dungeonRoot.GetChild(i).GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
        }

        // Hung after the stripping pass: the leaf's collider is what makes a shut door solid, so
        // it must survive even when the frame's is being removed.
        PlaceDoorLeaf(boundary, rotation);
    }

    /// <summary>
    /// Hangs a door leaf in a doorway. The Synty leaves pivot on their hinge edge, so the hinge
    /// goes at one jamb and the leaf fills across to the other.
    /// </summary>
    void PlaceDoorLeaf(Vector3 boundary, Quaternion rotation)
    {
        if (doorLeafPrefabs == null || doorLeafPrefabs.Length == 0) return;

        GameObject prefab = doorLeafPrefabs[random.Next(doorLeafPrefabs.Length)];
        if (prefab == null) return;

        Vector3 right = rotation * Vector3.right;
        bool hingeRight = random.Next(2) == 0;

        Vector3 hinge = boundary + right * (hingeRight ? doorHingeOffset : -doorHingeOffset);
        Quaternion leafRotation = hingeRight ? rotation : rotation * Quaternion.Euler(0f, 180f, 0f);

        GameObject leaf = Spawn(prefab, hinge, leafRotation);
        if (leaf == null) return;

        // Synty's leaves are cut for a narrower frame than these wall pieces, so widen them to
        // span the opening. Width only - scaling height too would push the leaf through the lintel.
        MeshRenderer leafRenderer = prefab.GetComponentInChildren<MeshRenderer>();
        if (leafRenderer != null)
        {
            float leafWidth = leafRenderer.bounds.size.x;
            if (leafWidth > 0.01f)
            {
                float wanted = doorHingeOffset * 2f;
                Vector3 scale = leaf.transform.localScale;
                scale.x *= wanted / leafWidth;
                leaf.transform.localScale = scale;
            }
        }

        var door = leaf.AddComponent<Delver.Dungeon.DungeonDoor>();
        door.SwingSign = hingeRight ? 1f : -1f;
    }
}