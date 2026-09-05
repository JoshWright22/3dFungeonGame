using UnityEngine;

namespace Delver.Dungeon
{
    /// <summary>
    /// Every knob that shapes a dungeon, in one asset.
    ///
    /// Create several (Crypt, Sewers, Mines) and swap which one the generator uses, instead of
    /// re-tuning a dozen inspector fields by hand each time. Right-click in the Project window:
    /// Create > Dungeon > Profile.
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonProfile", menuName = "Dungeon/Profile")]
    public class DungeonProfile : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Shown in logs so you can tell which profile built a run.")]
        public string profileName = "Untitled";

        [TextArea(2, 4)]
        public string notes;

        // ------------------------------------------------------------------ extent

        [Header("Extent")]
        [Tooltip("Grid size in cells. One cell is 5 world units, so 48 x 3 x 48 is a 240m square, three storeys deep.")]
        public Vector3Int gridSize = new Vector3Int(48, 3, 48);

        [Tooltip("How many rooms to fit. Rooms are placed by rejection sampling, so this is a target rather than a promise.")]
        [Min(2)] public int roomCount = 26;

        [Tooltip("Extra attempts per room before giving up. Raise it if the log warns that rooms were dropped.")]
        [Min(1)] public int placementAttemptsPerRoom = 12;

        // ------------------------------------------------------------------ rooms

        [Header("Room shape")]
        [Tooltip("Smallest room, in cells. Keep at least 2x2 on the floor plane or rooms read as corridor bulges.")]
        public Vector3Int roomMinSize = new Vector3Int(3, 1, 3);

        [Tooltip("Largest room, in cells.")]
        public Vector3Int roomMaxSize = new Vector3Int(8, 2, 8);

        [Tooltip("Cells of solid rock to keep between rooms. 0 lets rooms share a wall.")]
        [Range(0, 4)] public int roomSeparation = 1;

        // ------------------------------------------------------------------ connectivity

        [Header("Connectivity")]
        [Tooltip("Fraction of the discarded Delaunay edges added back as loops. 0 is a pure spanning tree - every dead end is a real dead end, and you can always retrace. Higher makes the dungeon a place you get lost in.")]
        [Range(0f, 1f)] public float loopEdgeChance = 0.16f;

        [Tooltip("A* cost charged for a staircase. High values keep a floor sprawling before it descends.")]
        [Min(0f)] public float stairCost = 100f;

        [Tooltip("A* cost for carving fresh corridor through rock. Low values let corridors wander; high values make them share.")]
        [Min(0f)] public float freshHallwayCost = 1f;

        // ------------------------------------------------------------------ dressing

        [Header("Floors")]
        [Tooltip("Tile variants. These must be dimensionally equivalent or the floor will not line up.")]
        public GameObject[] floorPrefabs;

        [Header("Walls")]
        public GameObject[] wallPrefabs;

        [Tooltip("Nudge walls vertically if they float above or sink into the floor.")]
        public float wallYNudge = 0f;

        [Tooltip("Optional ceiling over every walkable cell. Roughly doubles the object count.")]
        public GameObject ceilingPrefab;

        [Header("Doorways")]
        [Tooltip("Wall segments with an opening cut through them - not free-standing frames, or they read as an outline in a gap.")]
        public GameObject[] doorPrefabs;

        [Tooltip("Strip colliders from doorways. Only needed when a door prefab's collider is a solid box spanning the opening.")]
        public bool makeDoorwaysPassable = false;

        [Header("Stairs")]
        public GameObject stairPrefab;

        // ------------------------------------------------------------------ light

        [Header("Torches")]
        public GameObject[] torchPrefabs;

        [Tooltip("Chance a given wall gets a torch beside it. Every torch is a real-time light, so this trades atmosphere against frame time.")]
        [Range(0f, 1f)] public float torchChancePerWall = 0.18f;

        [Tooltip("Minimum cells between torches, so they do not pool into a bonfire.")]
        [Min(0)] public int torchMinSpacing = 2;

        [Tooltip("Always light the room the party arrives in, so the run does not open in total darkness.")]
        public bool alwaysLightEntryRoom = true;

        /// <summary>True when the profile has enough assigned to build something visible.</summary>
        public bool IsUsable(out string problem)
        {
            if (gridSize.x < 4 || gridSize.y < 1 || gridSize.z < 4)
            {
                problem = "gridSize is too small to hold a room.";
                return false;
            }

            if (roomMaxSize.x < roomMinSize.x || roomMaxSize.y < roomMinSize.y || roomMaxSize.z < roomMinSize.z)
            {
                problem = "roomMaxSize is smaller than roomMinSize on at least one axis.";
                return false;
            }

            if (floorPrefabs == null || floorPrefabs.Length == 0)
            {
                problem = "no floor prefabs assigned.";
                return false;
            }

            problem = null;
            return true;
        }
    }
}
