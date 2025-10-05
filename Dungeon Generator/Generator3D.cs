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
    [SerializeField]
    Material redMaterial;
    [SerializeField]
    Material blueMaterial;
    [SerializeField]
    Material greenMaterial;


    Random random;
    Grid3D<CellType> grid;
    List<Room> rooms;
    Delaunay3D delaunay;
    HashSet<Prim.Edge> selectedEdges;
    HashSet<Vector3Int> placedHallways = new HashSet<Vector3Int>();

    static readonly Vector3Int[] Directions = new Vector3Int[]
    {
        Vector3Int.right,
        Vector3Int.left,
        Vector3Int.forward,
        Vector3Int.back
    };

    void Start()
    {
        random = new Random(0);
        grid = new Grid3D<CellType>(size, Vector3Int.zero);
        rooms = new List<Room>();

        PlaceRooms();
        Triangulate();
        CreateHallways();
        PathfindHallways();
    }

    void PlaceRooms()
    {
        for (int i = 0; i < roomCount; i++)
        {
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
                PlaceRoom(newRoom.bounds.position, newRoom.bounds.size);

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
            if (random.NextDouble() < 0.125)
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
                //     return pathCost;
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

    void PlaceCube(Vector3Int location, Vector3Int size, Material material)
    {

        GameObject go = Instantiate(cubePrefab, new Vector3(location.x * 5, location.y * 5, location.z * 5), Quaternion.identity);
        go.GetComponent<Transform>().localScale = new Vector3(size.x, size.y, size.z);
        go.GetComponentInChildren<MeshRenderer>().material = material;
    }

    void PlaceRoom(Vector3Int location, Vector3Int size)
    {
        PlaceCube(location, size, redMaterial);
    }

    void PlaceHallway(Vector3Int location)
    {
        PlaceCube(location, new Vector3Int(1, 1, 1), blueMaterial);
    }

    //Detect which direction the stairs are going and place acordingly
    void PlaceStairs(Vector3Int prev, Vector3Int horizontalOffset, Vector3Int verticalOffset, Vector3 delta, int xDir, int zDir)
    {
        float stairPrefabOffset = 2;
        float universalScaler = 5;

        Vector3 stairOne = new Vector3(
            (prev.x + horizontalOffset.x) * universalScaler + stairPrefabOffset,
            (prev.y + horizontalOffset.y) * universalScaler,
            (prev.z + horizontalOffset.z) * universalScaler + stairPrefabOffset);

        Vector3 stairTwo = new Vector3(
            (prev.x + horizontalOffset.x * 2) * universalScaler + stairPrefabOffset,
            (prev.y + horizontalOffset.y * 2) * universalScaler,
            (prev.z + horizontalOffset.z * 2) * universalScaler + stairPrefabOffset);

        Vector3 stairThree = new Vector3(
            (prev.x + horizontalOffset.x + verticalOffset.x) * universalScaler + stairPrefabOffset,
            (prev.y + horizontalOffset.y + verticalOffset.y) * universalScaler,
            (prev.z + horizontalOffset.z + verticalOffset.z) * universalScaler + stairPrefabOffset);

        Vector3 stairFour = new Vector3(
            (prev.x + verticalOffset.x + horizontalOffset.x * 2) * universalScaler + stairPrefabOffset,
            (prev.y + horizontalOffset.y + verticalOffset.y) * universalScaler,
            (prev.z + verticalOffset.z + horizontalOffset.z * 2) * universalScaler + stairPrefabOffset);

        Vector3 direction = new Vector3(xDir * -1f, 0, zDir * -1f);

        // Vector3 prefabOffsetOne = new Vector3();
        // Vector3 prefabOffsetTwo = new Vector3();
        // Vector3 prefabOffsetThree = new Vector3();
        // Vector3 prefabOffsetFour = new Vector3();

        if (delta.y > 0) //Going up
        {
            Instantiate(stairPrefab, stairOne, Quaternion.LookRotation(direction * -1f, Vector3.up));
            Instantiate(stairPrefab, stairFour - ((verticalOffset * 5) / 2), Quaternion.LookRotation(direction * -1f, Vector3.up));
        }
        else if (delta.y < 0) //Going down
        {
            //Top Stair
            Instantiate(stairPrefab, stairOne + ((verticalOffset * 5) / 2), Quaternion.LookRotation(direction, Vector3.up));
            Instantiate(stairPrefab, stairFour, Quaternion.LookRotation(direction, Vector3.up));
        }

    }

    void PlaceDoor(Vector3Int location, Quaternion rotation, Vector3Int offset)
    {
        Instantiate(doorPrefab, new Vector3((location.x * 5) + 2.5f + offset.x * 2.5f, location.y * 5, (location.z * 5) + 2.5f + offset.z * 2.5f), rotation);
    }
}