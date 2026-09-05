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
    private float StairFloorOffset;

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
        PlaceWalls();
        ChooseEntry();

        Debug.Log($"Dungeon generated from seed {dungeonSeed} ({rooms.Count} rooms, spawn {PartySpawnPoint}).", this);
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

        wallYNudge = profile.wallYNudge;
        makeDoorwaysPassable = profile.makeDoorwaysPassable;
        torchChancePerWall = profile.torchChancePerWall;
        torchMinSpacing = profile.torchMinSpacing;

        Debug.Log($"Dungeon profile '{profile.profileName}' applied.", this);
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
    /// Picks the room the party arrives in: the lowest, largest room, which is the one most
    /// likely to have space for four delvers and a way onward.
    /// </summary>
    void ChooseEntry()
    {
        entryRoom = null;
        int bestScore = int.MinValue;

        foreach (var room in rooms)
        {
            var b = room.bounds;
            int footprint = b.size.x * b.size.z;

            // Prefer low, then large. Depth dominates so the party never starts on a top floor.
            int score = (-b.position.y * 1000) + footprint;

            if (score > bestScore)
            {
                bestScore = score;
                entryRoom = room;
            }
        }

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

        // Then confirm against the geometry that actually got built. Computing a floor height
        // from prefab bounds is a prediction; a raycast is the ground truth, and it is what
        // stops the party spawning a few centimetres inside the floor.
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
        float ceilingOffset = FloorSurfaceOffset;

        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            CellType here = grid[cell];

            if (!IsWalkable(here)) continue;

            Vector3 cellCentre = CellCentre(cell);

            foreach (Vector3Int dir in Directions)
            {
                Vector3Int neighbour = cell + dir;

                bool solid = !grid.InBounds(neighbour) || !IsWalkable(grid[neighbour]);
                if (!solid) continue;

                // Never wall a cell off from a staircase landing.
                if (grid.InBounds(neighbour) && grid[neighbour] == CellType.Stairs) continue;

                PlaceWall(cellCentre, dir, wallFloorOffset);
                TryPlaceTorch(cell, cellCentre, dir, wallFloorOffset);
            }

            if (ceilingPrefab != null)
            {
                Vector3Int above = cell + Vector3Int.up;
                bool openAbove = grid.InBounds(above) && IsWalkable(grid[above]);

                if (!openAbove)
                {
                    Vector3 pos = cellCentre + new Vector3(0f, WorldUnitSize + ceilingOffset, 0f);
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
    void PlaceWall(Vector3 cellCentre, Vector3Int dir, float floorOffset)
    {
        GameObject prefab = wallPrefabs[random.Next(wallPrefabs.Length)];
        if (prefab == null) return;

        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 boundary = cellCentre + outward * HalfWorldUnit + new Vector3(0f, floorOffset, 0f);

        // Face the wall back into the room it encloses.
        Quaternion rotation = Quaternion.LookRotation(-outward, Vector3.up);
        Vector3 right = rotation * Vector3.right;

        Spawn(prefab, boundary + right * HalfWorldUnit, rotation);
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

    /// <summary>Distance from a prefab's pivot to the bottom of its geometry.</summary>
    float GetPrefabFloorOffset(GameObject prefab)
    {
        if (prefab == null) return HalfWorldUnit;

        MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();
        return renderer == null ? HalfWorldUnit : renderer.bounds.extents.y;
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

        // Helper to find the distance from the pivot (0,0,0) to the side (extents.x or extents.z)
        float GetHorizontalCenterOffset(GameObject prefab)
        {
            MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();
            if (renderer == null)
            {
                // Fallback to half the WorldUnitSize (2.5)
                return HalfWorldUnit;
            }
            // Use the X extent, assuming a square-based object
            return renderer.bounds.extents.x;
        }

        // Horizontal offsets come from the tile that actually gets spawned.
        FloorPivotOffset = PivotOffsetFor(RepresentativeFloorPrefab());

        // A tile is instantiated with its pivot at (gridLine + HalfWorldUnit). Its walkable
        // surface is the top of its COLLIDER - the renderer's bounds are a few centimetres
        // taller, which is exactly how far into the floor the party used to spawn.
        FloorSurfaceOffset = HalfWorldUnit + GetTopOffset(RepresentativeFloorPrefab());

        // Door and Stair Prefabs only need the vertical offset to sit on the floor
        DoorFloorOffset = GetFloorOffset(doorPrefab);
        StairFloorOffset = GetFloorOffset(stairPrefab);

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

    /// <summary>World-space centre of a grid cell, at its floor line (y before floor thickness).</summary>
    Vector3 CellCentre(Vector3Int cell)
    {
        return (Vector3)cell * WorldUnitSize + new Vector3(HalfWorldUnit, 0f, HalfWorldUnit);
    }

    /// <summary>
    /// Distance from a prefab's pivot up to the top of the surface a character collides with.
    /// Prefers the collider over the renderer: the mesh sits slightly proud of the collision
    /// hull, and placing anything from the mesh bounds leaves it floating.
    /// </summary>
    float GetTopOffset(GameObject prefab)
    {
        if (prefab == null) return 0f;

        Collider collider = prefab.GetComponentInChildren<Collider>();
        if (collider != null)
            return collider.bounds.center.y + collider.bounds.extents.y;

        MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();
        if (renderer == null) return 0f;

        return renderer.bounds.center.y + renderer.bounds.extents.y;
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
                PlaceRoomTiles(newRoom.bounds.position, newRoom.bounds.size);

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


                            Vector3Int tempdelta = delta * -1;
                            tempdelta.y = 0;
                            Vector3 tempOffests = new Vector3() + prev - (verticalOffset / 2) + (horizontalOffset * 2);
                            PlaceStairs(prev, horizontalOffset, verticalOffset, delta, xDir, zDir);


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
                            PlaceHallway(pos);
                            foreach (Vector3Int direction in Directions)
                            {
                                if (!grid.InBounds(pos + direction)) continue;

                                if (grid[pos + direction] == CellType.BottomFloorRoom)
                                {
                                    PlaceDoor(pos, Quaternion.LookRotation(direction, Vector3.up), direction);
                                }
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
    /// Places a cube. The location is the bottom-left-front of the room (grid units).
    /// Instantiates a separate 1x1x1 cube for every cell of the room.
    /// </summary>
    void PlaceRoomTiles(Vector3Int location, Vector3Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    Vector3Int tileLocation = location + new Vector3Int(x, y, z);
                    InstantiateCellObject(tileLocation);
                }
            }
        }
    }

    void PlaceRoom(Vector3Int location, Vector3Int size)
    {
        PlaceRoomTiles(location, size);
    }

    void PlaceHallway(Vector3Int location)
    {
        InstantiateCellObject(location);
    }

    /// <summary>
    /// Detect which direction the stairs are going and place accordingly.
    /// </summary>
    void PlaceStairs(Vector3Int prev, Vector3Int horizontalOffset, Vector3Int verticalOffset, Vector3 delta, int xDir, int zDir)
    {
        // The half-step height (2.5 units) in the world
        float halfStepHeight = WorldUnitSize / 2f;

        // The stair mesh has its own pivot corner (-X/+Z), distinct from both the floor tile's
        // and the cell centre, so it gets its own offset. Vertically it stands on the floor
        // surface like everything else rather than on a mesh-extent guess.
        Vector3 stairPivot = PivotOffsetFor(stairPrefab);
        Vector3 pivotOffset = new Vector3(stairPivot.x, FloorSurfaceOffset, stairPivot.z);

        Vector3 gridStairOne = prev + horizontalOffset;
        Vector3 stairOneWorldCenter = gridStairOne * WorldUnitSize + pivotOffset;

        Vector3 gridStairFour = prev + horizontalOffset * 2 + verticalOffset;
        Vector3 stairFourWorldCenter = gridStairFour * WorldUnitSize + pivotOffset;

        // The direction for rotation
        Vector3 direction = new Vector3(xDir * -1f, 0, zDir * -1f);

        if (delta.y > 0) // Going up (from prev to current)
        {
            // Stair 1 (Lower step): Base of stair at Y=floor_level (Y=0). The pivot is at Y=StairFloorOffset.
            Spawn(stairPrefab, stairOneWorldCenter, Quaternion.LookRotation(direction * -1f, Vector3.up));

            // Stair 4 (Upper step): Base of stair at Y=WorldUnitSize (Y=5) + halfStepHeight (2.5). 
            // stairFourWorldCenter's Y is StairFloorOffset (Y=5 floor level). Add 2.5 for the ramp height.
            Spawn(stairPrefab, stairFourWorldCenter + new Vector3(0, halfStepHeight, 0), Quaternion.LookRotation(direction * -1f, Vector3.up));
        }
        else if (delta.y < 0) // Going down (from prev to current)
        {
            // Stair 1 (Upper step): Base of stair at Y=WorldUnitSize (Y=5) + halfStepHeight (2.5). 
            // stairOneWorldCenter's Y is StairFloorOffset (Y=5 floor level). Add 2.5 for the ramp height.
            Spawn(stairPrefab, stairOneWorldCenter + new Vector3(0, halfStepHeight, 0), Quaternion.LookRotation(direction, Vector3.up));

            // Stair 4 (Lower step): Base of stair at Y=floor_level (Y=0). The pivot is at Y=StairFloorOffset.
            Spawn(stairPrefab, stairFourWorldCenter, Quaternion.LookRotation(direction, Vector3.up));
        }
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
        Vector3 right = rotation * Vector3.right;

        GameObject door = Spawn(DoorPrefab(), boundary + right * HalfWorldUnit, rotation);
        if (door == null || !makeDoorwaysPassable) return;

        // Only needed for prefabs whose collider spans the opening. The Synty wall-doorframe
        // pieces use MeshColliders, which already leave the doorway walkable.
        foreach (var collider in door.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }
}