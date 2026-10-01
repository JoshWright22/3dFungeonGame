using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;
using Graphs;
using Delver.Dungeon;
using Unity.Mathematics;

public partial class Generator3D : MonoBehaviour
{
    enum CellType
    {
        None,
        Room,
        BottomFloorRoom,
        Hallway,
        Stairs,

        /// <summary>Floor of a cave - an organic chamber or a worm-carved passage.</summary>
        Cave,

        /// <summary>Headroom above a cave floor. Open space, nothing to stand on.</summary>
        CaveOpen,

        /// <summary>A sunken channel of water. Shallow, so it is still floor you can wade across.</summary>
        Water
    }

    class Room
    {
        public BoundsInt bounds;

        /// <summary>
        /// The exact floor cells, when this region is a cave. Null for an ordinary rectangular
        /// room, whose floor is simply the bottom level of its bounds.
        ///
        /// A cave's bounds are only its bounding box, most of which is still solid rock - so
        /// anything that walks a region's floor has to go through <see cref="FloorCells"/>
        /// rather than through the bounds.
        /// </summary>
        public HashSet<Vector3Int> caveCells;

        /// <summary>
        /// A floor cell near the middle of the region, used as its vertex in the connectivity
        /// graph and as the corridor pathfinder's target. A cave's bounding-box centre is
        /// frequently solid rock, so it cannot stand in for this.
        /// </summary>
        public Vector3Int anchor;

        public bool IsCave => caveCells != null;

        /// <summary>Which kind of room this is. Null for caves and for the untyped fallback band.</summary>
        public Delver.Dungeon.RoomArchetype archetype;

        public Room(Vector3Int location, Vector3Int size)
        {
            bounds = new BoundsInt(location, size);
            anchor = location + new Vector3Int(size.x / 2, 0, size.z / 2);
        }

        public Room(HashSet<Vector3Int> cells, BoundsInt caveBounds, Vector3Int caveAnchor)
        {
            bounds = caveBounds;
            caveCells = cells;
            anchor = caveAnchor;
        }

        /// <summary>Every cell of this region a delver can stand on.</summary>
        public IEnumerable<Vector3Int> FloorCells()
        {
            if (caveCells != null)
            {
                foreach (var c in caveCells) yield return c;
                yield break;
            }

            foreach (var p in bounds.allPositionsWithin)
                if (p.y == bounds.yMin) yield return p;
        }

        /// <summary>Cells on the floor plane. How much room a region actually has.</summary>
        public int FloorArea => caveCells != null ? caveCells.Count : bounds.size.x * bounds.size.z;

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

    [Tooltip("Floor cells a room needs per extra storey of height, so the big crypt halls come out tall and the small chambers stay low. 0 rolls height independently of footprint, as before.")]
    [Min(0)]
    [SerializeField]
    int tallRoomArea = 60;

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

    [Tooltip("A material with Render Face set to Both. Applied to doorways and to walls seen from both sides, so the far face is textured instead of being culled away.")]
    [SerializeField]
    Material twoSidedMaterial;

    [Header("Debug")]
    [SerializeField]
    bool drawHallwayGizmos = false;

    [Tooltip("Editor only: start the party in the room of this archetype nearest the entry, instead of the entry room itself - for walking straight into whatever is being worked on. Empty for normal play. Ignored in builds.")]
    [SerializeField]
    string debugStartInRoom = "";

    public int CurrentSeed { get; private set; }

    [Tooltip("Editor only: after each generation, check the dungeon is sealed, that a player cannot walk out of it, and what is stuck inside what. Tools > Dungeon > Audit Current Dungeon runs the same checks on demand.")]
    [SerializeField]
    bool auditOnGenerate = true;

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

    /// <summary>
    /// Stair cells that still need a floor laid under them.
    ///
    /// A run marks four cells but only two carry ramp geometry. The other lower-level cell is the
    /// space underneath the upper ramp - and because Stairs cells are skipped when laying floors,
    /// it was an open hole you could walk into from the level below.
    /// </summary>
    readonly HashSet<Vector3Int> stairUnderFloors = new HashSet<Vector3Int>();

    /// <summary>
    /// Direction of travel for each staircase cell.
    ///
    /// A stairwell is a tube: walls down its two sides, open at both ends. Nothing along the axis
    /// of travel is ever walled - not the far end of the wedge, not an upper cell whose neighbour
    /// happens to be rock. Walls there end up hanging over the ramp or standing across it,
    /// because a stair cell's floor line is half a storey away from the ramp surface running
    /// through it.
    /// </summary>
    readonly Dictionary<Vector3Int, Vector3Int> stairAxis = new Dictionary<Vector3Int, Vector3Int>();

    /// <summary>
    /// The only faces along a staircase's axis that the climb actually passes through.
    ///
    /// Everything else on that axis gets walled - the far end of the wedge under the run, and the
    /// back of the upper landing, where the ramp arrives from below and never continues. Leaving
    /// the whole axis open exposed those two spots.
    /// </summary>
    readonly HashSet<(Vector3Int cell, Vector3Int dir)> stairOpenFaces = new HashSet<(Vector3Int, Vector3Int)>();

    /// <summary>
    /// Every cell the party can actually stand in, from the reachability pass. Kept so the wall
    /// pass can seal anything outside it: if you cannot get somewhere, you should not be able to
    /// see or step into it either.
    /// </summary>
    readonly HashSet<Vector3Int> reachableCells = new HashSet<Vector3Int>();
    readonly List<(Vector3Int cell, Vector3Int dir)> doorways = new List<(Vector3Int, Vector3Int)>();

    /// <summary>
    /// Where the built dungeon really opens into a cave, as (built cell, direction to the cave).
    /// Every other face between the two is a caved-in passage - see <see cref="ResolveCaveEntrances"/>.
    /// </summary>
    readonly HashSet<(Vector3Int cell, Vector3Int dir)> caveEntrances = new HashSet<(Vector3Int, Vector3Int)>();

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
        doorOpeningSizes.Clear();

        // Calculate the offsets based on the prefab's actual geometry
        CalculateComponentOffsets();

        random = new Random(dungeonSeed);
        grid = new Grid3D<CellType>(size, Vector3Int.zero);
        rooms = new List<Room>();
        placedTorches.Clear();
        stairRuns.Clear();
        stairMouths.Clear();
        stairFlanks.Clear();
        stairUnderFloors.Clear();
        stairAxis.Clear();
        stairOpenFaces.Clear();
        reachableCells.Clear();
        doorways.Clear();
        caveEntrances.Clear();
        wallSides.Clear();
        wallSegments.Clear();
        ClearCaves();
        drawnCache.Clear();
        entryRoom = null;

        bool cavernsFirst = profile != null && profile.cavernsFirst;
        if (cavernsFirst) CarveCaveChambers();
        PlaceRooms();
        if (!cavernsFirst) CarveCaveChambers();
        if (profile != null && profile.connectCaverns) ConnectNaturalCaverns();
        CarveCaveTunnels();

        if (rooms.Count < 2)
        {
            Debug.LogError($"Dungeon seed {dungeonSeed} produced {rooms.Count} room(s) - not enough to connect. Check size / roomCount / roomMaxSize.", this);
            return;
        }

        Triangulate();
        CreateHallways();
        PathfindHallways();

        // Now that the staircases exist, push the caves back off them. This has to happen before
        // the prune, so anything a trimmed cave leaves stranded is carved out with everything else.
        ClearCavesNearStairs();

        // Staircase sides are decided first: the prune has to know which faces are walled before
        // it can tell which rooms are genuinely reachable.
        MapStairMouths();

        // Water is cut last, so it can run through finished rooms and corridors rather than
        // having the layout route around it. It stays shallow enough to wade, which is what
        // keeps a river from severing the dungeon in two.
        CarveRivers();

        // Layout is only final once everything the party cannot reach has been carved back out.
        SelectEntryRoom();
        int pruned = PruneUnreachable();
        MapRoomCells();
        PickFavourites();
        ResolveCaveEntrances();
        PlanChasms();

        // Geometry is built from the finished grid in one pass, so nothing is ever spawned for a
        // region that later turns out to be unreachable.
        SpawnFloors();
        SpawnStairs();
        BuildCaveSurface();
        BuildCaveKit();
        groundSupports.Clear();
        foreach (var support in dungeonRoot.GetComponentsInChildren<Collider>())
            if (!support.isTrigger) groundSupports.Add(support);
        SpawnDoorways();
        PlaceWalls();
        CapWallEnds();
        FrameRooms();
        ReserveCaveMouths();
        BuildCavePortals();
        ComposeCaverns();
        if (HasCaveGeometry) RaiseCaveColumns();
        DressRooms();
        FurnishRooms();
        if (HasCaveGeometry)
        {
            DressCaveEdges();
            ScatterCaveProps();
            GrowCaveFormations();
            SettleCaveProps();
        }
        SpawnWater();
        // Last of the geometry: close anything a player in a cave could still see out through.
        RepairSightLeaks();
        SpawnLairMarkers();
        ResolveSpawnPoint();
#if UNITY_EDITOR
        if (auditOnGenerate)
        {
            string audit = AuditAll(false, out int problems);
            if (problems > 0) Debug.LogWarning(audit, this);
            else Debug.Log(audit, this);
        }
#endif

        Debug.Log($"Dungeon '{(profile != null ? profile.profileName : "inline")}' seed {dungeonSeed}: "
            + $"{rooms.Count} reachable regions ({pruned} pruned), {caves.Count} caves, spawn {PartySpawnPoint}.", this);
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

        tallRoomArea = profile.tallRoomArea;

        if (profile.stairPrefab != null) stairPrefab = profile.stairPrefab;

        doorLeafPrefabs = profile.doorLeafPrefabs;
        doorHingeOffset = profile.doorHingeOffset;
        if (profile.doubleSidedWallPrefab != null) doubleSidedWallPrefab = profile.doubleSidedWallPrefab;

        wallYNudge = profile.wallYNudge;
        makeDoorwaysPassable = profile.makeDoorwaysPassable;
        torchChancePerWall = profile.torchChancePerWall;
        torchMinSpacing = profile.torchMinSpacing;

        ApplyCaveProfile(profile);
        ApplyDressingProfile(profile);
        ApplyRoomKitProfile(profile);

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

            // Caves score last: the party should arrive somewhere built, and a lair is the worst
            // imaginable place to open a run in.
            int score = (-b.position.y * 1000) + room.FloorArea - (room.IsCave ? 1000000 : 0);

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
        foreach (var pos in entryRoom.FloorCells())
        {
            if (!grid.InBounds(pos)) continue;
            if (!HasFloor(grid[pos])) continue;
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

                // A walled staircase side is not a route, however open the grid looks.
                if (stairFlanks.Contains((cell, dir))) continue;
                if (stairFlanks.Contains((next, -dir))) continue;

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

        // The cells under a staircase are structure, not route: nothing walks through them, so
        // the flood never reaches them. Keep them anyway - carving them out takes their floor
        // with it and reopens the hole beneath the ramp.
        foreach (var c in stairUnderFloors)
            if (grid.InBounds(c)) reachable.Add(c);

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

        // Headroom whose floor has just been carved away is left hanging in the void, and the
        // wall pass would happily seal it into a box floating in solid rock. Sweeping upward
        // means a column of headroom dies with the floor it belonged to.
        for (int y = 0; y < size.y; y++)
        for (int x = 0; x < size.x; x++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (!IsHeadroom(grid[cell])) continue;

            Vector3Int below = cell + Vector3Int.down;
            bool supported = grid.InBounds(below)
                && (IsHeadroom(grid[below]) || (HasFloor(grid[below]) && reachable.Contains(below)));

            if (!supported) grid[cell] = CellType.None;
        }

        PruneCaves(reachable);

        reachableCells.Clear();
        foreach (var c in reachable) reachableCells.Add(c);

        // Drop rooms with no reachable floor left, and the staircases and doorways that served them.
        int before = rooms.Count;
        rooms.RemoveAll(room => !RoomHasReachableFloor(room, reachable));

        stairRuns.RemoveAll(run => !reachable.Contains(run.Lower) && !reachable.Contains(run.Upper));
        doorways.RemoveAll(d => !reachable.Contains(d.cell) || !reachable.Contains(d.cell + d.dir));

        return before - rooms.Count;
    }

    /// <summary>Open space above a floor, as opposed to the floor itself.</summary>
    bool IsHeadroom(CellType t) => t == CellType.Room || t == CellType.CaveOpen;

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
        foreach (var pos in room.FloorCells())
            if (reachable.Contains(pos)) return true;

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

            // Room is headroom above a floor, so it never gets one. Stairs normally carry their
            // own ramp instead of a tile - except the cells under a run, which need ground.
            bool wantsFloor = type == CellType.BottomFloorRoom
                || type == CellType.Hallway
                || type == CellType.Cave
                || type == CellType.Water
                || (type == CellType.Stairs && stairUnderFloors.Contains(cell));

            if (!wantsFloor) continue;

            // A riverbed sits below the surrounding floor line, which is what makes the water in
            // it read as depth rather than as a decal lying on the ground.
            if (type == CellType.Water)
                InstantiateCellObject(cell, RiverBedPrefab(), -riverDepth);
            else if (type == CellType.Cave)
            {
                // The contoured surface carries its own floor, and so does the asset kit's rock
                // ground, so a tile under either of them would sit inside it.
                if (!UsingCaveSurface && !UsingCaveKit) InstantiateCellObject(cell, CaveFloorPrefab());
            }
            else if (!SpawnKitFloor(cell, type))
                InstantiateCellObject(cell);
        }
    }

    /// <summary>The room <see cref="debugStartInRoom"/> asks for, nearest the entry, or null.</summary>
    Room DebugStartRoom()
    {
        if (!Application.isEditor || string.IsNullOrEmpty(debugStartInRoom) || entryRoom == null) return null;

        Room best = null;
        float bestDistance = float.MaxValue;

        foreach (var room in rooms)
        {
            if (room.archetype == null || room.archetype.name != debugStartInRoom) continue;

            float distance = Vector3.Distance(room.anchor, entryRoom.anchor);
            if (distance < bestDistance)
            {
                best = room;
                bestDistance = distance;
            }
        }

        if (best == null)
            Debug.LogWarning($"Generator3D: no '{debugStartInRoom}' room on seed {CurrentSeed}; starting in the entry room.", this);
        else
            Debug.Log($"Generator3D: debug start in '{debugStartInRoom}' at cells {best.bounds.position}.", this);

        return best;
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

            // Every ramp cell, including the last. A staircase opens at its two ends and nowhere
            // else - if that cuts a room off, the reachability pass carves the room out rather
            // than leaving a staircase spilling sideways into it.
            foreach (var cell in new[] { run.Prev + h, run.Prev + h * 2,
                                         run.Prev + run.Vertical + h, run.Prev + run.Vertical + h * 2 })
            {
                stairFlanks.Add((cell, perpA));
                stairFlanks.Add((cell, perpB));
            }

            // Floor the two cells on the run's lower level, so the space beneath the upper ramp
            // has ground under it. The upper-level cells are left alone - a tile there would sit
            // across the top of the ramp and block it.
            Vector3Int[] quad =
            {
                run.Prev + h, run.Prev + h * 2,
                run.Prev + run.Vertical + h, run.Prev + run.Vertical + h * 2,
            };

            int lowestY = int.MaxValue;
            foreach (var c in quad) lowestY = Mathf.Min(lowestY, c.y);
            foreach (var c in quad) if (c.y == lowestY) stairUnderFloors.Add(c);

            // The lower-level cell WITHOUT a ramp in it is the wedge under the upper ramp. It is
            // floored so nobody falls through, but it is not a place worth walking into - a dead
            // end with the ramp's underside as a sloping ceiling. Seal its outward face.
            //
            // Only that one face: the face toward the lower ramp has to stay open, because the
            // climb passes over this cell at half a storey up.
            Vector3Int lowerRamp = run.Prev + h;
            Vector3Int deadEnd = (run.Prev + h * 2).y == lowestY
                ? run.Prev + h * 2
                : run.Prev + run.Vertical + h;

            // Every cell of the run remembers which way the stairs travel.
            foreach (var c in quad) stairAxis[c] = h;

            // The four faces the climb passes through, and only those. Read bottom to top:
            // floor onto the first ramp; first ramp over the wedge; wedge airspace into the
            // second ramp; second ramp out onto the landing.
            Vector3Int lower1 = run.Prev + h;          // lower ramp
            Vector3Int wedge  = run.Prev + h * 2;      // under the upper ramp
            Vector3Int upper1 = run.Prev + run.Vertical + h;
            Vector3Int upper2 = run.Upper;             // upper ramp

            void Open(Vector3Int a, Vector3Int dir)
            {
                stairOpenFaces.Add((a, dir));
                stairOpenFaces.Add((a + dir, -dir));
            }

            Open(run.Prev, h);   // bottom mouth
            Open(lower1, h);     // lower ramp -> wedge
            Open(upper1, h);     // wedge airspace -> upper ramp
            Open(upper2, h);     // upper ramp -> landing

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
        // Sweep by name rather than trusting the cached reference. dungeonRoot is an ordinary
        // field, so a domain reload - any script edit, entering play mode - wipes it while the
        // GameObject it pointed at survives in the scene. The old code then saw null, skipped the
        // destroy and parented a SECOND dungeon on top of the first, so every regenerate after a
        // recompile silently doubled the scene.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name != "Dungeon") continue;

            // Destroy is deferred to the end of the frame, and the new dungeon is generated in
            // this one - so the old one's colliders would still answer every raycast the build
            // makes, and props would settle onto floors that vanish a moment later. Detaching and
            // deactivating takes it out of physics now.
            if (Application.isPlaying)
            {
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
            else DestroyImmediate(child.gameObject);
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
        Room startRoom = DebugStartRoom() ?? entryRoom;

        if (startRoom == null)
        {
            PartySpawnPoint = Vector3.zero;
            return;
        }

        var bounds = startRoom.bounds;
        Vector3 centreCell = startRoom.IsCave
            ? (Vector3)startRoom.anchor
            : new Vector3(
                bounds.position.x + (bounds.size.x - 1) * 0.5f,
                bounds.position.y,
                bounds.position.z + (bounds.size.z - 1) * 0.5f);

        Vector3 predicted = centreCell * WorldUnitSize
            + new Vector3(HalfWorldUnit, FloorSurfaceOffset, HalfWorldUnit);

        PartySpawnPoint = predicted + new Vector3(0f, 0.15f, 0f);

        // The tiles were instantiated moments ago; push their transforms into the physics scene
        // before asking it anything.
        Physics.SyncTransforms();

        // Probe from HALF a cell up, not a whole one. A ceiling slab sits at exactly
        // WorldUnitSize + FloorSurfaceOffset above the grid line - precisely where the old probe
        // started - so the ray began level with the ceiling and landed on top of it, and the
        // party spawned on the roof of their own entry room. Intermittently: whether it hit
        // depended on the slab's thickness above its pivot and on rounding.
        Vector3 probeFrom = predicted + new Vector3(0f, HalfWorldUnit, 0f);

        if (Physics.Raycast(probeFrom, Vector3.down, out RaycastHit hit, WorldUnitSize,
                ~0, QueryTriggerInteraction.Ignore))
        {
            // Only trust ground near where the floor was predicted. Anything well above it is
            // something the party should be standing under, not on.
            if (hit.point.y - predicted.y < HalfWorldUnit * 0.5f)
                PartySpawnPoint = hit.point + new Vector3(0f, 0.15f, 0f);
        }

        // The middle of a room is where its centrepiece stands - an arcade pillar, a table. Step
        // out to the nearest spot where a delver actually fits.
        if (!FitsDelver(PartySpawnPoint))
            for (float ring = 0.75f; ring <= 6f; ring += 0.75f)
            for (int k = 0; k < 12; k++)
            {
                var offset = Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * ring;
                var from = PartySpawnPoint + offset + Vector3.up * 1f;
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit ground, 2f, ~0, QueryTriggerInteraction.Ignore)) continue;

                var candidate = ground.point + new Vector3(0f, 0.15f, 0f);
                if (Mathf.Abs(candidate.y - PartySpawnPoint.y) > 0.6f || !FitsDelver(candidate)) continue;

                PartySpawnPoint = candidate;
                return;
            }
    }

    /// <summary>True when a player-sized capsule stood here touches nothing but the floor under it.</summary>
    static bool FitsDelver(Vector3 feet) =>
        !Physics.CheckCapsule(feet + Vector3.up * 0.6f, feet + Vector3.up * 1.5f, 0.4f, ~0, QueryTriggerInteraction.Ignore);

    /// <summary>Cells a delver can actually stand in.</summary>
    bool IsWalkable(CellType t)
    {
        return t == CellType.Room || t == CellType.BottomFloorRoom
            || t == CellType.Hallway || t == CellType.Stairs
            || t == CellType.Cave || t == CellType.CaveOpen || t == CellType.Water;
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
        return t == CellType.BottomFloorRoom || t == CellType.Hallway || t == CellType.Stairs
            || t == CellType.Cave || t == CellType.Water;
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

        // Looked up from the room side, where a corridor meets a room it does not open into.
        var doorwayFaces = new HashSet<(Vector3Int, Vector3Int)>();
        foreach (var (hall, dir) in doorways) doorwayFaces.Add((hall + dir, -dir));

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

            // A riverbed is sunk below the surrounding floor line. Dropping the whole wall to
            // match left an equally large gap at the TOP of it instead, which is the hole you can
            // see the void through - so the wall stays where it belongs and the channel gets a
            // second course underneath to line it.
            float cellWallOffset = wallFloorOffset;

            // A cave wall mesh may span several levels, so the courses it already covers are
            // skipped rather than stacked on top of it.
            bool skipWallCourse = IsCaveCell(here) && !CaveWallCourseStartsHere(cell);

            // The contoured surface IS the cave's walls and roof. Water keeps its panels: a river
            // runs through built rooms too, and its banks are not part of the cave shell.
            if ((UsingCaveSurface || UsingCaveKit) && (here == CellType.Cave || here == CellType.CaveOpen))
                skipWallCourse = true;

            foreach (Vector3Int dir in Directions)
            {
                if (skipWallCourse) continue;

                Vector3Int neighbour = cell + dir;
                bool inBounds = grid.InBounds(neighbour);
                CellType beyond = inBounds ? grid[neighbour] : CellType.None;

                // Water and dry floor are both walkable, so the ordinary wall pass skips
                // their shared edge. Seal only the submerged part, leaving the crossing open.
                if (here == CellType.Water && HasFloor(beyond) && beyond != CellType.Water)
                    PlaceRiverBank(cellCentre, dir);

                // Along a staircase's axis, only the faces the climb passes through stay open.
                // The rest - the far end of the wedge, the back of the landing - are walled, or
                // they expose space you cannot reach from the stairs.
                bool onStairAxis = here == CellType.Stairs
                    && stairAxis.TryGetValue(cell, out Vector3Int axis)
                    && (dir == axis || dir == -axis);

                if (onStairAxis)
                {
                    if (stairOpenFaces.Contains((cell, dir))) continue;

                    // A stairwell climbing up through a cave: its back wall would stand bare
                    // masonry in the cave.
                    if (beyond == CellType.Cave || beyond == CellType.CaveOpen)
                    {
                        CloseCaveFace(cellCentre, dir, cellWallOffset, cell, here);
                        continue;
                    }

                    // Force it: the neighbour may look like open grid, but there is no way to it
                    // from here.
                    GameObject axisPrefab = WallPrefabAt(cell, here);
                    PlaceWall(cellCentre, dir, cellWallOffset, axisPrefab, true);
                    continue;
                }

                // Elsewhere, leave the staircase mouths open - but only those. A ramp running
                // alongside a corridor is not a way through, and skipping its wall leaves a hole.
                if (stairMouths.Contains((cell, dir))) continue;

                // Where the built dungeon breaks into a cave, both cells have a floor, so none of
                // the rules below fire and nothing at all was placed - the room's wall line just
                // stopped, and flagstone met rock in mid air. The kit has a piece for exactly
                // this: a wall with a rough tunnel bored through it.
                //
                // Water is excluded deliberately. A river crossing a room is not a cave mouth,
                // and framing every one of its cells would ring the stream with archways.
                bool caveMouth = standable
                    && here != CellType.Cave && here != CellType.Water
                    && beyond == CellType.Cave;

                if (caveMouth)
                {
                    // Only where a corridor actually leads in. Every cell that merely touched a
                    // cave used to become a mouth - 97 on one seed - so a corridor along a cave's
                    // edge was a row of holes. The rest are walled, rockfall on the cave side.
                    if (!caveEntrances.Contains((cell, dir)))
                    {
                        CloseCaveFace(cellCentre, dir, cellWallOffset, cell, here);
                        if (roomByCell.ContainsKey(cell)) roomWallFaces.Add((cell, dir));
                        continue;
                    }

                    GameObject entrance = CaveEntrancePrefab();

                    // Seen from the room and from inside the cave, so it goes up double-sided
                    // the same way a doorway does.
                    if (entrance != null) PlaceWall(cellCentre, dir, cellWallOffset, entrance, true);
                    continue;
                }

                // Built space beside a cave's open upper reaches, or a tall room's headroom beside
                // a cave floor. A plain double-sided guard stood bare masonry in the middle of
                // the cave, and a single-sided one left a hole, since the cave kit cannot clad a
                // face that borders built space.
                if (IsBuiltSpace(here) && (beyond == CellType.Cave || beyond == CellType.CaveOpen))
                {
                    CloseCaveFace(cellCentre, dir, cellWallOffset, cell, here);
                    if (roomByCell.ContainsKey(cell)) roomWallFaces.Add((cell, dir));
                    continue;
                }

                // Anything outside the reachable set is sealed, whatever the grid says it is.
                // A space you cannot get to should not be visible from one you can.
                bool unreachableBeyond = inBounds && HasFloor(beyond) && !reachableCells.Contains(neighbour);

                bool enclose = !inBounds || !IsWalkable(beyond) || unreachableBeyond;

                // A guard wall stops you strolling off a floor into open space that has none -
                // an upper corridor meeting a room's headroom, for instance.
                bool guard = standable && !enclose && !HasFloor(beyond);

                // Forced on the interior ramp cells only (see MapStairMouths): a ramp climbs past
                // whatever is beside it, so a neighbour with a floor of its own is still half a
                // storey down - a drop the same-level rule cannot see.
                bool flank = stairFlanks.Contains((cell, dir));

                // A corridor running alongside a room is a separate space, not part of it. Both
                // cells have a floor, so none of the rules above fire; walled from the room side
                // only, double-sided, so the two cells do not each stand a copy on the same line.
                bool partition = here == CellType.BottomFloorRoom && beyond == CellType.Hallway
                    && !doorwayFaces.Contains((cell, dir));

                if (!enclose && !guard && !flank && !partition) continue;

                // A guard wall is seen from both sides, so it needs a mesh with faces on both.
                // Guards and flanks are seen from both sides. A stairwell flank especially: the
                // ramp climbs past it, so it is looked at from above, below and alongside.
                bool seenBothSides = guard || flank || partition;

                GameObject prefab = WallPrefabAt(cell, here);

                PlaceWall(cellCentre, dir, cellWallOffset, prefab, seenBothSides);

                // The framing and furnishing passes hang off a room's own enclosing walls.
                if (roomByCell.ContainsKey(cell)) roomWallFaces.Add((cell, dir));

                // A staircase cell's ramp descends half a storey below that cell's floor line, so
                // a wall based at the floor line leaves the lower half of the ramp open to the
                // drop beside it. A second course underneath closes it.
                if (here == CellType.Stairs && flank)
                {
                    Vector3Int below = cell + Vector3Int.down;
                    bool solidBelow = !grid.InBounds(below) || !IsWalkable(grid[below]);

                    // Skipped when there is a room underneath, or the course hangs through its ceiling.
                    if (solidBelow)
                        PlaceWall(cellCentre, dir, cellWallOffset - HalfWorldUnit, prefab, seenBothSides);
                }

                // Line the sunken channel, so the bank is rock rather than a slot into the void.
                if (here == CellType.Water && riverDepth > 0.05f)
                    PlaceRiverBank(cellCentre, dir);

                bool inCave = here == CellType.Cave || here == CellType.Water;

                if (standable && here != CellType.Stairs && (torchesInCaves || !inCave) && !KitLightsCell(cell))
                    TryPlaceTorch(cell, cellCentre, dir, wallFloorOffset);

            }

            // Ceilings cap the TOP of an open volume, not the floor of it. Requiring somewhere to
            // stand left every tall room - and every cave with headroom - open to the void
            // above head height.
            GameObject ceiling = CeilingPrefabAt(cell, here);

            if ((UsingCaveSurface || UsingCaveKit) && (here == CellType.Cave || here == CellType.CaveOpen))
                ceiling = null;

            if (ceiling != null)
            {
                Vector3Int above = cell + Vector3Int.up;

                // A cave overhead is not this room's headroom. The cave carries its own floor
                // half a cell higher, so treating it as open left the room with no ceiling and
                // you could stand in the cave and look straight down into the room below.
                bool caveAbove = grid.InBounds(above)
                    && (grid[above] == CellType.Cave || grid[above] == CellType.CaveOpen);

                // A separate floor overhead is a storey boundary, not room headroom.
                bool openAbove = grid.InBounds(above) && !caveAbove
                    && (IsHeadroom(grid[above]) || grid[above] == CellType.Stairs);

                if (!openAbove)
                {
                    // Ceiling slabs pivot at a corner like the floor tiles and hang below it, so
                    // the position is solved for the (possibly turned) geometry rather than taken
                    // from the pivot - a turned slab would otherwise swing out of its cell.
                    Quaternion turn = CeilingTurnAt(cell, here);
                    Vector3 centre = (Vector3)cell * WorldUnitSize + new Vector3(HalfWorldUnit, 0f, HalfWorldUnit);
                    Vector3 pos = CentredSpawnPosition(ceiling, centre, turn, WorldUnitSize + FloorSurfaceOffset + cell.y * WorldUnitSize);

                    var slab = Spawn(ceiling, pos, turn);
                    AlignSurface(slab, (cell.y + 1) * WorldUnitSize + FloorSurfaceOffset, true);

                    // Some ceilings - the vaulted arch among them - ship without a collider, so
                    // nothing physical closed the top of the room. A thin lid at the ceiling line.
                    if (slab.GetComponentInChildren<Collider>() == null)
                    {
                        var lid = new GameObject("Ceiling lid");
                        lid.transform.SetParent(slab.transform, true);
                        lid.transform.SetPositionAndRotation(
                            centre + Vector3.up * (WorldUnitSize + FloorSurfaceOffset + 0.1f), Quaternion.identity);
                        lid.AddComponent<BoxCollider>().size = new Vector3(WorldUnitSize, 0.2f, WorldUnitSize);
                        lid.transform.localScale = new Vector3(1f / slab.transform.lossyScale.x, 1f / slab.transform.lossyScale.y, 1f / slab.transform.lossyScale.z);
                    }
                }
            }
        }
    }

    void PlaceRiverBank(Vector3 centre, Vector3Int dir)
    {
        if (riverDepth <= 0.01f) return;
        var prefab = WallPrefabFor(CellType.Hallway);
        if (prefab == null) return;
        var outward = new Vector3(dir.x, 0f, dir.z);
        var boundary = centre + outward * HalfWorldUnit + Vector3.up * FloorSurfaceOffset;
        var bank = SpawnWallPiece(prefab, boundary, Quaternion.LookRotation(-outward, Vector3.up));
        var renderers = bank.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.y <= 0.001f) return;
        var scale = bank.transform.localScale;
        scale.y *= riverDepth / bounds.size.y;
        bank.transform.localScale = scale;
        AlignSurface(bank, boundary.y, true);
        bank.name = "River bank - " + prefab.name;
    }

    /// <summary>
    /// Stands one wall segment on the boundary between a cell and the rock beside it.
    ///
    /// The Synty wall meshes pivot at floor level on one end and run 5 units along their local
    /// -X, so the piece is pushed half a cell along its own right vector to sit centred on the
    /// edge rather than hanging off it.
    /// </summary>
    void PlaceWall(Vector3 cellCentre, Vector3Int dir, float floorOffset, GameObject prefab, bool bothSides = false)
    {
        if (prefab == null) return;

        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 boundary = cellCentre + outward * HalfWorldUnit + new Vector3(0f, floorOffset, 0f);

        // Face the wall back into the room it encloses.
        Quaternion rotation = Quaternion.LookRotation(-outward, Vector3.up);

        // One wall per boundary. The same boundary can be asked for from both sides - a stair
        // flank and the guard of the cell beside it - and two walls in one place flicker as
        // they fight over which is in front.
        Vector3Int key = Vector3Int.RoundToInt(boundary * 4f);
        int facing = outward.x + outward.z > 0f ? 1 : 2;
        wallSides.TryGetValue(key, out int placed);
        int wanted = bothSides ? 3 : facing;
        int missing = wanted & ~placed;
        if (missing == 0) return;

        wallSides[key] = placed | wanted;
        if (placed == 0) wallSegments[key] = (boundary, new Vector3(outward.z, 0f, -outward.x));

        if (missing == 3) SpawnDoubleSidedPiece(prefab, boundary, rotation);
        else if (missing == facing) SpawnWallPiece(prefab, boundary, rotation);
        else SpawnWallPiece(prefab, boundary, rotation * Quaternion.Euler(0f, 180f, 0f));
    }

    /// <summary>Which faces of each wall boundary are already walled: 1 one way, 2 the other.</summary>
    readonly Dictionary<Vector3Int, int> wallSides = new Dictionary<Vector3Int, int>();

    /// <summary>Every walled boundary, by its centre and the direction its wall runs.</summary>
    readonly Dictionary<Vector3Int, (Vector3 centre, Vector3 along)> wallSegments = new Dictionary<Vector3Int, (Vector3, Vector3)>();

    /// <summary>
    /// Caps the free ends of walls - where a wall line stops and nothing meets it - with the
    /// kit's end piece. Without one you look straight into the gap between the two faces of a
    /// wall, or between two walls standing back to back. The demo caps 48 of these.
    /// </summary>
    void CapWallEnds()
    {
        GameObject cap = profile != null ? profile.wallEndCap : null;
        if (cap == null) return;

        var capped = new HashSet<Vector3Int>();
        foreach (var segment in wallSegments.Values)
        {
            Vector3 across = Vector3.Cross(segment.along, Vector3.up);

            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 end = segment.centre + segment.along * side * HalfWorldUnit;
                Vector3Int endKey = Vector3Int.RoundToInt(end * 4f);
                if (capped.Contains(endKey)) continue;

                // Carries straight on, turns a corner, or meets a wall end-on: not free.
                bool joined =
                    wallSides.ContainsKey(Vector3Int.RoundToInt((segment.centre + segment.along * side * WorldUnitSize) * 4f))
                    || wallSides.ContainsKey(Vector3Int.RoundToInt((end + across * HalfWorldUnit) * 4f))
                    || wallSides.ContainsKey(Vector3Int.RoundToInt((end - across * HalfWorldUnit) * 4f));
                if (joined) continue;

                capped.Add(endKey);
                Spawn(cap, end, Quaternion.LookRotation(across, Vector3.up));
            }
        }
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
    GameObject SpawnWallPiece(GameObject prefab, Vector3 boundary, Quaternion rotation)
    {
        Vector3 right = rotation * Vector3.right;
        return Spawn(prefab, boundary + right * HalfWorldUnit, rotation);
    }

    /// <summary>
    /// Stands two copies of a piece facing opposite ways, separated so neither intrudes on the
    /// other's side of the wall.
    ///
    /// The Synty wall meshes carry their brick relief on the front and a flat plane on the back,
    /// so simply rendering the back face - which a two-sided material does - still shows a blank
    /// wall from one side. Two outward-facing copies is the only way to get relief both ways.
    ///
    /// Each copy is pushed off the boundary by exactly the depth of its flat back, so the two
    /// backs meet on the boundary line. Placed at the same depth the slabs interleave and the
    /// second copy's back shows between the first copy's bricks. Pushed apart by half the total
    /// thickness instead, they left a 15 cm hollow slot - the mesh is not centred on its pivot -
    /// which showed as black wedges inside every doorway arch.
    /// </summary>
    void SpawnDoubleSidedPiece(GameObject prefab, Vector3 boundary, Quaternion rotation)
    {
        if (prefab == null) return;

        // A centimetre of overlap, so the backs never leave a hairline between them.
        float separation = Mathf.Max(0f, BackDepth(prefab) - 0.01f);

        Vector3 fwd = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;
        Spawn(prefab, boundary + right * HalfWorldUnit + fwd * separation, rotation);

        Quaternion flipped = rotation * Quaternion.Euler(0f, 180f, 0f);
        Vector3 rightB = flipped * Vector3.right;
        Spawn(prefab, boundary + rightB * HalfWorldUnit - fwd * separation, flipped);
    }

    /// <summary>How far a prefab's geometry reaches behind its pivot, along its own -Z.</summary>
    float BackDepth(GameObject prefab)
    {
        MeshRenderer renderer = prefab != null ? prefab.GetComponentInChildren<MeshRenderer>() : null;
        return renderer == null ? 0.15f : -renderer.bounds.min.z;
    }

    /// <summary>Swaps a spawned piece onto the two-sided material, if one is assigned.</summary>
    void ApplyTwoSidedMaterial(GameObject instance)
    {
        if (instance == null || twoSidedMaterial == null) return;

        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = twoSidedMaterial;
            renderer.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// Occasionally stands a brazier or lantern against a wall. Spaced out deliberately: each one
    /// is a real-time light, and a dungeon lit end to end defeats the point of carrying a torch.
    /// </summary>
    void TryPlaceTorch(Vector3Int cell, Vector3 cellCentre, Vector3Int dir, float floorOffset)
    {
        if (torchPrefabs == null || torchPrefabs.Length == 0) return;

        // Fewer the further from the entry: lit near the entry, darker deeper (design doc S9).
        if (random.NextDouble() >= torchChancePerWall * LitFactor(cell)) return;

        for (int i = 0; i < placedTorches.Count; i++)
        {
            Vector3Int d = placedTorches[i] - cell;
            if (Mathf.Abs(d.x) + Mathf.Abs(d.y) * 2 + Mathf.Abs(d.z) < torchMinSpacing) return;
        }

        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Quaternion facing = Quaternion.LookRotation(-outward, Vector3.up);

        // Corridors get a sconce on the wall. A brazier stood 0.9 m off the wall of a 5 m
        // corridor is in the walking line - a quarter of all corridor cells had one in the way.
        if (grid[cell] == CellType.Hallway && corridorSconce != null)
        {
            Vector3 mount = cellCentre + outward * (HalfWorldUnit - 0.2f) + new Vector3(0f, floorOffset + 1.7f, 0f);
            GameObject sconce = Spawn(corridorSconce, mount, facing);
            if (sconce == null) return;

            // The demo's sconce flame sits half a metre up and 0.42 out from the wall.
            var flame = sconce.AddComponent<Delver.Game.TorchLight>();
            flame.FlameColor = new Color(1f, 0.69f, 0.14f);
            flame.BaseIntensity = 3f;
            flame.Range = 10f;
            flame.LocalOffset = new Vector3(0f, 0.5f, 0.42f);
            placedTorches.Add(cell);
            return;
        }

        GameObject prefab = torchPrefabs[random.Next(torchPrefabs.Length)];
        if (prefab == null) return;

        // Stand it just inside the wall, facing the room.
        Vector3 position = cellCentre + outward * (HalfWorldUnit - 0.9f) + new Vector3(0f, floorOffset, 0f);

        GameObject torch = SpawnGrounded(prefab, position, facing);
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
        roomQuota.Clear();
        if (profile != null && profile.roomArchetypes != null)
            foreach (var entry in profile.roomArchetypes)
                for (int i = 0; entry != null && i < entry.guaranteed; i++) roomQuota.Add(entry);

        int builtCount = 0;
        while (builtCount < roomCount && attempts < maxAttempts)
        {
            attempts++;

            Vector3Int location = new Vector3Int(
                random.Next(0, size.x),
                random.Next(0, size.y),
                random.Next(0, size.z)
            );

            var archetype = PickRoomArchetype();

            int width, depth, height;

            if (archetype != null)
            {
                // The archetype carries its own band, so a cellar stays a cellar however large
                // the profile's overall range is.
                width = random.Next(Mathf.Max(1, archetype.minFootprint.x), Mathf.Max(1, archetype.maxFootprint.x) + 1);
                depth = random.Next(Mathf.Max(1, archetype.minFootprint.y), Mathf.Max(1, archetype.maxFootprint.y) + 1);
                height = random.Next(Mathf.Max(1, archetype.minStoreys), Mathf.Max(1, archetype.maxStoreys) + 1);
            }
            else
            {
                width = random.Next(Mathf.Max(1, roomMinSize.x), roomMaxSize.x + 1);
                depth = random.Next(Mathf.Max(1, roomMinSize.z), roomMaxSize.z + 1);

                // A big crypt hall with a five-metre ceiling reads as a corridor that got fat.
                // Height is scaled off the footprint instead of rolled independently, so the
                // large rooms are the tall ones and a broom cupboard never comes out as a shaft.
                height = random.Next(Mathf.Max(1, roomMinSize.y), roomMaxSize.y + 1);

                if (tallRoomArea > 0)
                {
                    int storeys = 1 + (width * depth) / tallRoomArea;
                    height = Mathf.Min(Mathf.Max(height, storeys), Mathf.Max(1, roomMaxSize.y));
                }
            }

            Vector3Int roomSize = new Vector3Int(width, height, depth);

            bool add = true;
            Room newRoom = new Room(location, roomSize) { archetype = archetype };
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

            // BoundsInt.max is one PAST the last cell, so a room filling the grid exactly has
            // max == size and is still in bounds. Testing >= rejected every room that reached the
            // far edge on any axis - invisible while rooms were small, but with only three
            // storeys it threw out every full-height room and placement collapsed.
            if (newRoom.bounds.xMin < 0 || newRoom.bounds.xMax > size.x
                || newRoom.bounds.yMin < 0 || newRoom.bounds.yMax > size.y
                || newRoom.bounds.zMin < 0 || newRoom.bounds.zMax > size.z)
            {
                add = false;
            }

            if (add)
            {
                if (roomQuota.Count > 0 && ReferenceEquals(archetype, roomQuota[0])) roomQuota.RemoveAt(0);

                rooms.Add(newRoom);
                builtCount++;

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

        if (profile != null && profile.roomArchetypes != null && profile.roomArchetypes.Length > 0)
        {
            var tally = new Dictionary<string, int>();
            foreach (var room in rooms)
            {
                if (room.archetype == null) continue;
                tally.TryGetValue(room.archetype.name, out int had);
                tally[room.archetype.name] = had + 1;
            }
            Debug.Log("Room mix: " + string.Join(", ", tally.Select(kv => $"{kv.Value} x {kv.Key}")), this);
        }

        if (builtCount < roomCount)
        {
            Debug.LogWarning($"Placed {builtCount}/{roomCount} rooms in {attempts} attempts. The grid is too small or the rooms too large for the requested count.", this);
        }
    }

    /// <summary>
    /// Draws a room kind by weight, or null when the profile has no archetypes and the single
    /// min/max band should be used instead.
    /// </summary>
    Delver.Dungeon.RoomArchetype PickRoomArchetype()
    {
        var set = profile != null ? profile.roomArchetypes : null;
        if (set == null || set.Length == 0) return null;

        // Quotas first. These are the rooms the floor is built around, and leaving them to a
        // weighted draw means a seed can simply not have one. Peeked rather than taken: a big
        // hall fails the intersection test far more often than a cellar, and consuming the quota
        // on a failed attempt is exactly how the guarantee gets lost again.
        if (roomQuota.Count > 0) return roomQuota[0];

        float total = 0f;
        foreach (var entry in set)
            if (entry != null && entry.weight > 0f) total += entry.weight;

        if (total <= 0f) return null;

        float roll = (float)random.NextDouble() * total;
        foreach (var entry in set)
        {
            if (entry == null || entry.weight <= 0f) continue;
            roll -= entry.weight;
            if (roll <= 0f) return entry;
        }

        return null;
    }

    /// <summary>Archetypes still owed by their quota, drawn before any weighted pick.</summary>
    readonly List<Delver.Dungeon.RoomArchetype> roomQuota = new List<Delver.Dungeon.RoomArchetype>();

    /// <summary>Where a region sits in the connectivity graph.</summary>
    Vector3 GraphPoint(Room room)
    {
        if (room.IsCave) return (Vector3)room.anchor + new Vector3(0.5f, 0f, 0.5f);

        return (Vector3)room.bounds.position + ((Vector3)room.bounds.size) / 2;
    }

    /// <summary>The cell a corridor should aim for when connecting to a region.</summary>
    Vector3Int PathAnchor(Room room)
    {
        if (room.IsCave) return room.anchor;

        // The room's FLOOR, not the middle of its volume. For a two-storey room the centre sits
        // on the headroom level - open air with nothing underfoot - so corridors were pathfinding
        // to a cell no one can stand in, and every tall room ended up pruned as unreachable.
        Vector3 c = room.bounds.center;
        return new Vector3Int((int)c.x, room.bounds.yMin, (int)c.z);
    }

    void Triangulate()
    {
        List<Vertex> vertices = new List<Vertex>();

        foreach (var room in rooms)
        {
            vertices.Add(new Vertex<Room>(GraphPoint(room), room));
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

            // A cave's bounding-box centre is usually solid rock, so both ends of the corridor
            // are taken from the region's anchor cell instead.
            var startPos = PathAnchor(startRoom);
            var endPos = PathAnchor(endRoom);

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
                        // Headroom is open space with nothing underfoot. A corridor routed
                        // through it is never carved as Hallway, so it never gets a floor - the
                        // pathfinder sees a connection and the party sees a hole. It looked
                        // harmless only because single-storey rooms have no headroom at all.
                        return pathCost;
                    }
                    else if (grid[b.Position] == CellType.BottomFloorRoom)
                    {
                        pathCost.cost += 5;
                    }
                    else if (grid[b.Position] == CellType.Cave || grid[b.Position] == CellType.Water)
                    {
                        // Cheap to route through: a corridor meeting a cave should open into it
                        // rather than tunnel politely around the outside.
                        pathCost.cost += 5;
                    }
                    else if (grid[b.Position] == CellType.CaveOpen)
                    {
                        // Same as Room above: open air over a cave floor, nothing to walk on.
                        return pathCost;
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

                // A doorway goes only where the corridor actually steps into a room. Every room
                // face a corridor merely ran alongside used to become one as well, so a hallway
                // hugging a cellar turned its whole side into a row of door frames. Those faces
                // are walled instead (see PlaceWalls).
                for (int i = 1; i < path.Count; i++)
                {
                    Vector3Int from = path[i - 1];
                    Vector3Int to = path[i];
                    if (from.y != to.y) continue;

                    if (grid[from] == CellType.Hallway && grid[to] == CellType.BottomFloorRoom)
                        AddDoorway(from, to - from);
                    else if (grid[to] == CellType.Hallway && grid[from] == CellType.BottomFloorRoom)
                        AddDoorway(to, from - to);

                    // The same for caves: the path's own step into one is a real mouth.
                    if (IsBuiltFloor(grid[from]) && grid[to] == CellType.Cave)
                        caveEntrances.Add((from, to - from));
                    else if (IsBuiltFloor(grid[to]) && grid[from] == CellType.Cave)
                        caveEntrances.Add((to, from - to));
                }
            }
        }
    }

    static bool IsBuiltFloor(CellType t) => t == CellType.Hallway || t == CellType.BottomFloorRoom;

    static bool IsBuiltSpace(CellType t) =>
        t == CellType.Hallway || t == CellType.BottomFloorRoom || t == CellType.Room || t == CellType.Stairs;

    /// <summary>
    /// Keeps every cave reachable. A cave whose only ways in are faces no corridor path crossed -
    /// a worm tunnel that broke into a corridor, say - keeps all of those openings, as before.
    /// </summary>
    void ResolveCaveEntrances()
    {
        foreach (var cave in caves)
        {
            bool entered = false;
            var natural = new List<(Vector3Int, Vector3Int)>();

            foreach (var cell in cave.Cells)
            foreach (var dir in Directions)
            {
                var built = cell - dir;
                if (!grid.InBounds(built) || !IsBuiltFloor(grid[built])) continue;

                if (caveEntrances.Contains((built, dir))) entered = true;
                else natural.Add((built, dir));
            }

            if (!entered)
                foreach (var face in natural) caveEntrances.Add(face);
        }
    }

    /// <summary>
    /// Closes a face between built space and a cave: an ordinary wall, masonry on both faces,
    /// with a rockfall heaped against the cave side. The tunnel-entrance piece used before is
    /// over a metre thick, and doubled up it bulged into corridors and stairwells.
    /// </summary>
    void CloseCaveFace(Vector3 cellCentre, Vector3Int dir, float floorOffset, Vector3Int cell, CellType here)
    {
        PlaceWall(cellCentre, dir, floorOffset, WallPrefabAt(cell, here), true);

        GameObject rubble = profile != null ? profile.caveBlockedMouth : null;
        if (rubble == null) return;

        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 boundary = cellCentre + outward * HalfWorldUnit + new Vector3(0f, floorOffset, 0f);

        // Flat back against the wall's cave face, the heap spilling into the cave, stretched to
        // cover the whole 5 m face rather than the 2.6 m opening it was modelled for.
        var heap = Spawn(rubble, boundary + outward * 0.45f, Quaternion.LookRotation(-outward, Vector3.up));
        if (heap != null) heap.transform.localScale = Vector3.Scale(heap.transform.localScale, new Vector3(2.1f, 1.45f, 1f));
    }

    GameObject DoorFrameFor(GameObject doorWall)
    {
        if (profile == null || profile.doorFrameInserts == null || doorWall == null) return null;

        foreach (var pair in profile.doorFrameInserts)
            if (pair.doorWall == doorWall) return pair.frame;

        return null;
    }

    void AddDoorway(Vector3Int hallway, Vector3Int intoRoom)
    {
        if (!doorways.Contains((hallway, intoRoom))) doorways.Add((hallway, intoRoom));
    }

    // Helper method to instantiate a single 1x1x1 cube (for a 5x5x5 cell)
    void InstantiateCellObject(Vector3Int location, GameObject prefab = null, float yOffset = 0f)
    {
        if (prefab == null) prefab = FloorPrefab();

        // Pivot offset is per-variant: a tile whose pivot sits on a different corner still has
        // to land filling the same cell.
        Vector3 pivot = PivotOffsetFor(prefab);
        Vector3 worldPosition = (Vector3)location * WorldUnitSize
            + new Vector3(pivot.x, FloorSurfaceOffset - GetTopOffset(prefab) + yOffset, pivot.z);

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

        // A doorway is a course of wall too: walls running into it are joined, not free ends.
        Vector3Int doorKey = Vector3Int.RoundToInt(boundary * 4f);
        wallSides[doorKey] = 3;
        wallSegments[doorKey] = (boundary, new Vector3(outward.z, 0f, -outward.x));

        // A doorway is walked through, so it is seen from both sides by definition.
        int before = dungeonRoot.childCount;
        GameObject framePrefab = DoorPrefab();
        SpawnDoubleSidedPiece(framePrefab, boundary, rotation);

        // Line the opening before measuring it, so the leaf is fitted to the frame rather than
        // to the bare hole. The demo stands the frame centred on the opening, on the wall line,
        // turned the same way as the wall.
        GameObject lining = DoorFrameFor(framePrefab);
        if (lining != null) Spawn(lining, boundary, rotation);

        Vector2 opening = MeasureDoorOpening(framePrefab, before, boundary, rotation);

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
        // A room can ask for its own doors - barred ones for a prison cell.
        roomByCell.TryGetValue(location + offset, out Room into);
        RoomKit intoKit = into != null && into.archetype != null ? into.archetype.kit : null;
        GameObject[] leaves = intoKit != null && intoKit.doorLeaves != null && intoKit.doorLeaves.Length > 0
            ? intoKit.doorLeaves
            : doorLeafPrefabs;

        PlaceDoorLeaf(boundary, rotation, opening, leaves);
    }

    /// <summary>
    /// Hangs a door leaf in a doorway. The Synty leaves pivot on their hinge edge, so the hinge
    /// goes at one jamb and the leaf fills across to the other.
    /// </summary>
    readonly Dictionary<GameObject, Vector2> doorOpeningSizes = new Dictionary<GameObject, Vector2>();

    Vector2 MeasureDoorOpening(GameObject frame, int firstChild, Vector3 boundary, Quaternion rotation)
    {
        if (frame != null && doorOpeningSizes.TryGetValue(frame, out var cached)) return cached;
        var colliders = new List<Collider>();
        for (int i = firstChild; i < dungeonRoot.childCount; i++)
            colliders.AddRange(dungeonRoot.GetChild(i).GetComponentsInChildren<Collider>());
        Physics.SyncTransforms();
        bool Blocked(float x, float y)
        {
            var ray = new Ray(boundary + rotation * new Vector3(x, y, -WorldUnitSize), rotation * Vector3.forward);
            foreach (var c in colliders)
                if (c.enabled && !c.isTrigger && c.Raycast(ray, out _, WorldUnitSize * 2f)) return true;
            return false;
        }
        float halfWidth = 0f;
        const float step = 0.025f;
        for (float x = step; x < HalfWorldUnit; x += step)
        {
            if (Blocked(x, 0.3f) || Blocked(-x, 0.3f)) break;
            halfWidth = x;
        }
        // Jamb relief varies with height. Find the largest clear rectangle, rather than
        // using the widest point at ankle height and mistaking a projecting brick for a lintel.
        float height = 0f, fittedHalfWidth = 0f, bestArea = 0f;
        float clearHalfWidth = halfWidth;
        for (float y = 0.1f; y < WorldUnitSize; y += step)
        {
            if (Blocked(0f, y)) break;
            float rowWidth = 0f;
            for (float x = step; x <= clearHalfWidth; x += step)
            {
                if (Blocked(x, y) || Blocked(-x, y)) break;
                rowWidth = x;
            }
            clearHalfWidth = Mathf.Min(clearHalfWidth, rowWidth);
            float area = clearHalfWidth * y;
            if (area > bestArea)
            {
                bestArea = area; height = y; fittedHalfWidth = clearHalfWidth;
            }
        }
        var result = new Vector2(Mathf.Max(0f, fittedHalfWidth * 2f - 0.04f), Mathf.Max(0f, height - 0.04f));
        if (colliders.Count == 0 || halfWidth >= HalfWorldUnit - step * 2f || height <= 0.1f)
        {
            Debug.LogWarning("Cannot measure doorway opening; leaving this frame without a door leaf.", this);
            return Vector2.zero;
        }
        if (frame != null) doorOpeningSizes[frame] = result;
        return result;
    }

    void PlaceDoorLeaf(Vector3 boundary, Quaternion rotation, Vector2 opening, GameObject[] leaves)
    {
        if (leaves == null || leaves.Length == 0) return;
        if (opening.x <= 0f || opening.y <= 0f) return;

        GameObject prefab = leaves[random.Next(leaves.Length)];
        if (prefab == null) return;

        Vector3 right = rotation * Vector3.right;
        bool hingeRight = random.Next(2) == 0;

        Vector3 hinge = boundary + right * (hingeRight ? opening.x * 0.5f : -opening.x * 0.5f);
        Quaternion leafRotation = hingeRight ? rotation : rotation * Quaternion.Euler(0f, 180f, 0f);

        GameObject leaf = Spawn(prefab, hinge, leafRotation);
        if (leaf == null) return;

        // Each selected frame has its own measured clear opening. Fit both dimensions,
        // with a small clearance at the jambs and lintel.
        MeshRenderer leafRenderer = prefab.GetComponentInChildren<MeshRenderer>();
        if (leafRenderer != null)
        {
            float leafWidth = leafRenderer.bounds.size.x;
            if (leafWidth > 0.01f)
            {
                float wanted = opening.x;
                Vector3 scale = leaf.transform.localScale;
                scale.x *= wanted / leafWidth;
                if (leafRenderer.bounds.size.y > 0.01f)
                    scale.y *= opening.y / leafRenderer.bounds.size.y;
                leaf.transform.localScale = scale;
            }
        }

        // Put the visible leaf between the jambs, regardless of its imported hinge pivot.
        var liveRenderer = leaf.GetComponentInChildren<MeshRenderer>();
        if (liveRenderer != null)
        {
            Vector3 centreOffset = boundary - liveRenderer.bounds.center;
            leaf.transform.position += new Vector3(centreOffset.x, 0f, centreOffset.z);
        }
        AlignSurface(leaf, boundary.y + 0.02f, false);

        var door = leaf.AddComponent<Delver.Dungeon.DungeonDoor>();
        door.SwingSign = hingeRight ? 1f : -1f;
    }
}
