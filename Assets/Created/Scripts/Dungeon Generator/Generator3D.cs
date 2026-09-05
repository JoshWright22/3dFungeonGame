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

    [Header("Shape")]
    [Tooltip("Extra attempts allowed while trying to satisfy roomCount. Rooms are placed by rejection sampling, so without a retry budget a dense layout silently ends up with far fewer rooms than asked for.")]
    [SerializeField]
    int roomPlacementAttemptsPerRoom = 12;

    [Tooltip("Fraction of the non-MST Delaunay edges added back as loops. 0 is a pure tree you can always back out of; higher values make the dungeon a place you can get lost in.")]
    [Range(0f, 1f)]
    [SerializeField]
    float loopEdgeChance = 0.125f;

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

    // Define the world unit size for one grid cell
    private const float WorldUnitSize = 5f;
    private const float HalfWorldUnit = WorldUnitSize / 2f; // 2.5f - This is the fixed vertical center

    // Offset for the X and Z axes, calculated from the cubePrefab's horizontal size
    private float CubeCenterXZOffset;

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

        // Every spawned tile is parented to this, so regenerating is one destroy rather than a
        // second dungeon standing inside the first.
        ClearDungeon();

        // Calculate the offsets based on the prefab's actual geometry
        CalculateComponentOffsets();

        random = new Random(dungeonSeed);
        grid = new Grid3D<CellType>(size, Vector3Int.zero);
        rooms = new List<Room>();
        placedHallways.Clear();
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

        // Sit the party on the floor of that cell, not at its volumetric centre.
        PartySpawnPoint = centreCell * WorldUnitSize
            + new Vector3(CubeCenterXZOffset, FloorSurfaceOffset + 0.25f, CubeCenterXZOffset);
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

            // Stairs cut through the grid diagonally; walling them off would seal the route.
            if (here == CellType.Stairs) continue;

            Vector3 cellCentre = (Vector3)cell * WorldUnitSize
                + new Vector3(CubeCenterXZOffset, 0f, CubeCenterXZOffset);

            foreach (Vector3Int dir in Directions)
            {
                Vector3Int neighbour = cell + dir;

                bool solid = !grid.InBounds(neighbour) || !IsWalkable(grid[neighbour]);
                if (!solid) continue;

                // Never wall a cell off from a staircase landing.
                if (grid.InBounds(neighbour) && grid[neighbour] == CellType.Stairs) continue;

                PlaceWall(cellCentre, dir, wallFloorOffset);
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

        // Calculate horizontal offset for the cube (used for X/Z centering)
        CubeCenterXZOffset = GetHorizontalCenterOffset(cubePrefab);

        // A tile is instantiated with its pivot at (gridLine + HalfWorldUnit). Its walkable
        // surface is wherever the top of its geometry sits relative to that pivot.
        FloorSurfaceOffset = HalfWorldUnit + GetTopOffset(cubePrefab);

        // Door and Stair Prefabs only need the vertical offset to sit on the floor
        DoorFloorOffset = GetFloorOffset(doorPrefab);
        StairFloorOffset = GetFloorOffset(stairPrefab);

        if (drawHallwayGizmos)
            Debug.Log($"Calculated Offsets: Cube XZ Center={CubeCenterXZOffset}, Door Y={DoorFloorOffset}, Stair Y={StairFloorOffset}");
    }

    /// <summary>Distance from a prefab's pivot up to the top of its geometry.</summary>
    float GetTopOffset(GameObject prefab)
    {
        if (prefab == null) return 0f;

        MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();
        if (renderer == null) return 0f;

        return renderer.bounds.center.y + renderer.bounds.extents.y;
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
                random.Next(1, roomMaxSize.x + 1),
                random.Next(1, roomMaxSize.y + 1),
                random.Next(1, roomMaxSize.z + 1)
            );

            bool add = true;
            Room newRoom = new Room(location, roomSize);
            // This buffer is critical for ensuring space between rooms in grid units
            Room buffer = new Room(location + new Vector3Int(-1, 0, -1), roomSize + new Vector3Int(2, 0, 2));

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
        // ⭐ X and Z use the dynamic offset based on the prefab's width/depth (CubeCenterXZOffset).
        // ⭐ Y is reverted to the fixed center of the 5-unit cell (HalfWorldUnit = 2.5).
        Vector3 worldCenter = (Vector3)location * WorldUnitSize + new Vector3(CubeCenterXZOffset, HalfWorldUnit, CubeCenterXZOffset);

        Spawn(cubePrefab, worldCenter, Quaternion.identity);
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

        // 1. Calculate World Center of the Grid Cells
        // X and Z center is determined by the cube prefab's offset (CubeCenterXZOffset).
        // Y center is determined by the stair prefab's offset (StairFloorOffset).
        Vector3 gridStairOne = prev + horizontalOffset;
        Vector3 stairOneWorldCenter = gridStairOne * WorldUnitSize + new Vector3(CubeCenterXZOffset, StairFloorOffset, CubeCenterXZOffset);

        Vector3 gridStairFour = prev + horizontalOffset * 2 + verticalOffset;
        Vector3 stairFourWorldCenter = gridStairFour * WorldUnitSize + new Vector3(CubeCenterXZOffset, StairFloorOffset, CubeCenterXZOffset);

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
    /// Places a door. The location is the hallway grid unit, and the door should be placed
    /// halfway between the hallway and the room (at the 5-unit grid boundary).
    /// </summary>
    void PlaceDoor(Vector3Int location, Quaternion rotation, Vector3Int offset)
    {
        // World center of the hallway block (X/Z determined by CubeCenterXZOffset, Y by DoorFloorOffset)
        Vector3 worldCenter = (Vector3)location * WorldUnitSize + new Vector3(CubeCenterXZOffset, DoorFloorOffset, CubeCenterXZOffset);

        // Move the door HalfWorldUnit (2.5) in the direction of the offset to place it at the boundary.
        // We use HalfWorldUnit (2.5) because the boundary is fixed at half the 5-unit cell size.
        Vector3 doorPosition = worldCenter + (Vector3)offset * HalfWorldUnit;

        // The Y position is already correctly set to DoorFloorOffset (pivot is at floor level).
        Spawn(doorPrefab, doorPosition, rotation);
    }
}