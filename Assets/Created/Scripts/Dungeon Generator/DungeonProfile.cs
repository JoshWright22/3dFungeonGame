using UnityEngine;

namespace Delver.Dungeon
{
    /// <summary>A door wall and the frame that lines its opening.</summary>
    [System.Serializable]
    public struct DoorFrameInsert
    {
        public GameObject doorWall;
        public GameObject frame;
    }

    /// <summary>The structural shell a room is built from. Drives dressing and, later, how it connects.</summary>
    public enum RoomShell
    {
        /// <summary>Wall courses, stone trim, square pillars, flat or curved stone ceiling.</summary>
        MasonryHall,
        /// <summary>Narrow and arcaded - archways and arch ceilings rather than flat courses.</summary>
        Gallery,
        /// <summary>Timbered basement: wall panels, support poles and beams. Always low.</summary>
        Basement,
        /// <summary>Small, plain and functional. Cells, cellars, furnaces.</summary>
        Chamber,
    }

    /// <summary>
    /// One kind of room the generator may place, with its own size band and dressing.
    ///
    /// Rolling every room from one min/max produces a dungeon of identically medium rooms: the
    /// uniform draw concentrates on the middle of the range and the extremes almost never appear.
    /// A weighted set of archetypes is what gives a floor plan a few real halls among a lot of
    /// small rooms, which is how the POLYGON demo is laid out - one Long Hall and one Center Hall
    /// against nine smaller specific rooms.
    /// </summary>
    [System.Serializable]
    public class RoomArchetype
    {
        [Tooltip("Shown in logs and on the spawned room object, so a layout can be read back.")]
        public string name = "Chamber";

        [Tooltip("Which structural kit this room is built from.")]
        public RoomShell shell = RoomShell.MasonryHall;

        [Tooltip("Relative chance of drawing this archetype. Weights are normalised, so only their ratio matters.")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("Place this many before any weighted draws. A great hall is one room in thirty by weight, so on a given seed it may simply never come up - a quota is what guarantees a floor has the big spaces it is built around.")]
        [Min(0)] public int guaranteed = 0;

        [Tooltip("Smallest footprint in cells, on the floor plane.")]
        public Vector2Int minFootprint = new Vector2Int(3, 3);

        [Tooltip("Largest footprint in cells.")]
        public Vector2Int maxFootprint = new Vector2Int(6, 6);

        [Tooltip("Storeys of open height. A cellar is 1; a great hall is 3.")]
        [Min(1)] public int minStoreys = 1;
        [Min(1)] public int maxStoreys = 1;

        [Tooltip("Chance per wall-facing cell of a prop against that wall, for rooms of this kind. Negative uses the profile default.")]
        public float wallPropChance = -1f;

        [Tooltip("Chance per floor cell of loose rubble. Negative uses the profile default.")]
        public float rubbleChance = -1f;

        [Tooltip("Chance per wall-facing cell of a pillar. Negative uses the profile default.")]
        public float pillarChance = -1f;

        [Tooltip("Eligible for a landmark and its accent light, if the room is also large enough.")]
        public bool allowLandmark = true;

        [Tooltip("Walls, floor, framing, props and light for this kind of room. Empty builds it from the profile's shared wall and floor pools.")]
        public RoomKit kit;
    }

    /// <summary>
    /// One size class of natural cavern.
    ///
    /// Same reasoning as <see cref="RoomArchetype"/>: a single min/max makes every cave the same
    /// size. The demo has one large crystal cavern and one goblin cave, but a generated floor
    /// wants a spread - pockets and side chambers as well as the one space worth crossing the
    /// map for.
    /// </summary>
    [System.Serializable]
    public class CaveArchetype
    {
        public string name = "Pocket";

        [Tooltip("Relative chance of drawing this size class.")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("Carve this many before any weighted draws, so a floor reliably gets its large cavern as well as its pockets.")]
        [Min(0)] public int guaranteed = 0;

        [Tooltip("Smallest footprint box the blob is grown inside, in cells.")]
        public Vector2Int minFootprint = new Vector2Int(6, 6);

        [Tooltip("Largest footprint box, in cells.")]
        public Vector2Int maxFootprint = new Vector2Int(9, 9);

        [Tooltip("Storeys of headroom.")]
        [Min(1)] public int storeys = 2;

        [Tooltip("Discard a chamber that smooths down below this many floor cells.")]
        [Min(1)] public int minCells = 12;

        [Tooltip("Grow a rounded lobed blob rather than cellular-automata noise. Rounded keeps a usable centre at large sizes; CA gives a rougher, more broken outline that suits small pockets.")]
        public bool lobed = true;
    }

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

        [Tooltip("Floor cells a room needs per extra storey of height, so large crypt halls come out tall and small chambers stay low. 0 rolls height independently of footprint. Ignored when room archetypes are used - those carry their own storey range.")]
        [Min(0)] public int tallRoomArea = 60;

        [Tooltip("Weighted kinds of room to place. Leave empty to fall back on the single roomMinSize/roomMaxSize band above, which makes every room the same middling size.")]
        public RoomArchetype[] roomArchetypes;

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

        [Tooltip("The frame each door wall is lined with, e.g. SM_Env_Wall_DoorFrame_01 -> SM_Env_Door_Frame_01. The door walls are a course of stone with a hole in it; on their own you can see into the hollow of the wall down both sides of the opening. The demo never uses one without its frame.")]
        public DoorFrameInsert[] doorFrameInserts;

        [Tooltip("Heaped against the cave side of a wall where built space meets a cave but no corridor leads in, e.g. SM_Env_Wall_Rock_Blocked_01.")]
        public GameObject caveBlockedMouth;

        [Tooltip("Caps the free end of a wall, e.g. SM_Env_Wall_End_Coner_Outer_01, so you cannot see between its two faces or into the gap between walls standing back to back.")]
        public GameObject wallEndCap;

        [Tooltip("Strip colliders from doorways. Only needed when a door prefab's collider is a solid box spanning the opening.")]
        public bool makeDoorwaysPassable = false;

        [Tooltip("Door leaves hung in the openings. They swing aside as delvers approach. Leave empty for bare archways.")]
        public GameObject[] doorLeafPrefabs;

        [Tooltip("Half the doorway opening width - where a leaf's hinge sits. Must match the frames above (1.66 wide opening -> 0.83).")]
        public float doorHingeOffset = 0.83f;

        [Tooltip("A wall mesh with faces on BOTH sides, for the few walls genuinely seen from both.")]
        public GameObject doubleSidedWallPrefab;

        [Header("Stairs")]
        public GameObject stairPrefab;

        // ------------------------------------------------------------------ light

        [Header("Torches")]
        public GameObject[] torchPrefabs;

        [Tooltip("Wall sconce for corridors, e.g. SM_Prop_Torch_Ornate_02. Floor-standing fixtures in a corridor stand in the walking line; the demo lights its passages from the walls. Empty uses the torch prefabs everywhere.")]
        public GameObject corridorSconce;

        [Tooltip("Chance a given wall gets a torch beside it. Every torch is a real-time light, so this trades atmosphere against frame time.")]
        [Range(0f, 1f)] public float torchChancePerWall = 0.18f;

        [Tooltip("Minimum cells between torches, so they do not pool into a bonfire.")]
        [Min(0)] public int torchMinSpacing = 2;

        [Tooltip("Always light the room the party arrives in, so the run does not open in total darkness.")]
        public bool alwaysLightEntryRoom = true;

        // ------------------------------------------------------------------ caves

        [Header("Cave chambers")]
        [Tooltip("Reserve large caverns before placing masonry, instead of growing caves in leftover gaps.")]
        public bool cavernsFirst = false;
        [Tooltip("Connect same-storey caverns with natural passages before routing masonry corridors.")]
        public bool connectCaverns = false;

        [Header("Cave asset kit")]
        [Tooltip("Build caves from the pack's own rock modules rather than a generated rock mesh. The kit lays overlapping rock ground, rings the boundary with large wall panels and caps it with roof slabs, the way the POLYGON demo is authored.")]
        public bool caveUseAssetKit = false;

        [Tooltip("Rock ground laid under cave cells. Flat, wide pieces - SM_Env_Rock_Flat_01, SM_Env_CrackedRock_01, SM_Env_Rock_Flat_Large_02/03.")]
        public GameObject[] caveKitFloorPrefabs;

        [Tooltip("Large rock panels stood around the cave boundary. SM_Env_Cave_Large_01 and SM_Env_Cave_Curved_01 are the pack's wall-shaped pieces.")]
        public GameObject[] caveKitWallPrefabs;

        [Tooltip("Roof slabs laid over caverns. SM_Env_Cave_Roof_01 is 23m by 20m and tiles flat.")]
        public GameObject[] caveKitRoofPrefabs;

        [Tooltip("Huge background masses dropped outside the wall ring for depth. Collision is stripped from these - they are scenery, not surfaces.")]
        public GameObject[] caveKitBackdropPrefabs;

        [Tooltip("How far each rock floor plate laps past its cell, in world units. These plates are irregular blobs in a square bounding box, so they need real overlap or the corners leave holes you can see through.")]
        [Range(0f, 3f)] public float caveKitFloorOverlap = 1.6f;

        [Tooltip("Metres between wall panels along a boundary run. Keep this well under a panel's own width so consecutive panels overlap instead of meeting at a seam.")]
        [Range(2f, 20f)] public float caveKitWallSpacing = 7f;

        [Tooltip("How far a wall panel is sunk back into the rock behind the boundary, in world units.")]
        [Range(0f, 3f)] public float caveKitWallInset = 0.6f;

        [Tooltip("Degrees of random yaw on each wall panel, so a straight run does not read as a fence.")]
        [Range(0f, 30f)] public float caveKitWallJitter = 12f;

        [Tooltip("Metres between roof slabs. Under the slab's own 20m footprint so they overlap.")]
        [Range(5f, 25f)] public float caveKitRoofSpacing = 15f;

        [Tooltip("Background masses ringed around each large cavern. 0 disables them.")]
        [Min(0)] public int caveKitBackdropsPerCavern = 5;

        [Tooltip("Scale applied to background masses. The demo runs these between 0.43 and 0.93.")]
        [Range(0.2f, 2f)] public float caveKitBackdropScale = 0.85f;

        [Header("Cavern composition")]
        public GameObject[] caveCladdingPrefabs;
        public GameObject[] caveRoofPrefabs;
        public GameObject[] caveLandmarkPrefabs;
        public GameObject[] roomLandmarkPrefabs;
        public GameObject[] lushCavePrefabs;
        public GameObject[] inhabitedCavePrefabs;
        [Min(1)] public int landmarkMinFloorArea = 60;
        [Min(1f)] public float landmarkClearRadius = 6f;
        [Range(0f, 1f)] public float caveThemePropChance = 0.12f;

        [Header("Chasm caves")]
        [Tooltip("Build inhabited caverns as the demo's Goblin Cave: a pit with platforms standing out of it, joined by bridges, instead of a floored room.")]
        public bool chasmCaves = true;

        [Tooltip("Caverns smaller than this keep a floor.")]
        [Min(4)] public int chasmMinCells = 24;

        [Tooltip("Cave cells per island. The demo's goblin cave has an island every ten or so.")]
        [Min(4)] public int chasmCellsPerIsland = 10;

        [Tooltip("The big platform at the middle of an island, e.g. SM_Env_Rock_Flat_Platform_Large_01-03.")]
        public GameObject[] chasmPlatformsLarge;

        [Tooltip("Platforms round it, e.g. SM_Env_Rock_Flat_Platform_01-04.")]
        public GameObject[] chasmPlatformsMedium;

        [Tooltip("Stepping stones at the edge, a step down, e.g. SM_Env_Rock_Flat_Platform_05-06.")]
        public GameObject[] chasmPlatformsSmall;

        [Tooltip("Rope bridge end piece, e.g. SM_Env_Wood_Bridge_Start_01.")]
        public GameObject ropeBridgeEnd;

        [Tooltip("Rope bridge span, e.g. SM_Env_Wood_Bridge_01.")]
        public GameObject ropeBridgeSpan;

        [Tooltip("Mine-track trestle, e.g. SM_Env_Minetrack_Bridge_01.")]
        public GameObject chasmTrestle;

        [Tooltip("Rails laid on the trestle, e.g. SM_Env_Minetrack_Straight_01.")]
        public GameObject chasmRail;

        [Tooltip("Fog lying in the pit, e.g. the demo's FX_Fog_Dark.")]
        public Material chasmFogMaterial;

        [Tooltip("Torch stuck in an island's edge, e.g. SM_Prop_TorchStick_01.")]
        public GameObject chasmTorch;

        [Range(0f, 1f)] public float chasmTorchChance = 0.6f;

        [Tooltip("Low ground mounds laid over a floored cave, e.g. SM_Env_GroundMound_01-02 - the demo crystal cave's ground detail.")]
        public GameObject[] grottoMounds;

        [Tooltip("The big plates cave ground is laid with, e.g. SM_Env_CrackedRock_01 and _03 - the demo floors every cave with them.")]
        public GameObject[] caveGroundPlates;

        [Tooltip("Mounds banked against the foot of a cave wall, e.g. SM_Env_GroundMound_Rough_01-02 and _Edge_02.")]
        public GameObject[] caveWallMounds;

        [Header("Cave scenes")]
        [Tooltip("Scenes set down in caverns: camps, crystal stands, bone heaps, shrines. See CaveVignette.")]
        public CaveVignette[] caveVignettes;

        [Tooltip("What lines the foot of cave walls, per theme. See CaveEdgeSet.")]
        public CaveEdgeSet[] caveEdges;

        [Tooltip("Rock masses stood in the open middle of big caves, so they become passages round columns as in the demo. Two set back to back make one column - e.g. SM_Env_Cave_Curved_Corner_01_DoubleSided.")]
        public GameObject[] caveColumnMasses;

        [Tooltip("Spires round a column's foot, e.g. SM_Env_Rock_Spike_01-03 - the demo scales these up to several metres.")]
        public GameObject[] caveColumnSpires;

        [Tooltip("Columns keep at least this many cells apart, and this far from the cave wall less one.")]
        [Min(2)] public int caveColumnSpacing = 3;

        [Tooltip("Cavern floor cells per vignette. The demo's caves have something worth walking to every 15-20 cells.")]
        [Min(4)] public int cellsPerVignette = 18;

        [Tooltip("Upper bound per cavern, however large.")]
        [Min(0)] public int maxVignettesPerCavern = 7;

        [Tooltip("Weighted size classes of cavern. Leave empty to fall back on the single caveChamberMinSize/MaxSize band below, which makes every cavern the same size.")]
        public CaveArchetype[] caveArchetypes;

        [Tooltip("Blobby natural chambers grown in the rock. 0 disables them.")]
        [Min(0)] public int caveChamberCount = 3;

        [Tooltip("Smallest chamber footprint in cells (x = width, y = depth). The blob grown inside is smaller than the box.")]
        public Vector2Int caveChamberMinSize = new Vector2Int(7, 7);

        [Tooltip("Largest chamber footprint in cells (x = width, y = depth).")]
        public Vector2Int caveChamberMaxSize = new Vector2Int(14, 14);

        [Tooltip("Chamber height in cells. 2 or 3 gives a cavern with headroom; 1 is a crawlspace.")]
        [Min(1)] public int caveChamberHeight = 2;

        [Tooltip("Proportion of open cells before smoothing. Around 0.45 gives connected blobs.")]
        [Range(0.3f, 0.6f)] public float caveFillChance = 0.46f;

        [Tooltip("Smoothing passes. Each rounds the outline off; past about 6 nothing changes.")]
        [Range(0, 10)] public int caveSmoothIterations = 4;

        [Tooltip("Discard a chamber smaller than this many floor cells.")]
        [Min(1)] public int caveChamberMinCells = 14;

        [Tooltip("Cells of rock kept between a cave and anything already placed.")]
        [Range(0, 4)] public int caveSeparation = 1;

        [Header("Cave tunnels")]
        [Tooltip("Winding Perlin-worm passages wormed out of the edges of caves and rooms. 0 disables them.")]
        [Min(0)] public int caveTunnelCount = 4;

        [Tooltip("Cells a worm walks before giving up. It stops early when it breaks into somewhere that exists.")]
        [Min(1)] public int caveTunnelLength = 40;

        [Tooltip("Passage half-width. 0 is a one-cell squeeze, 1 is three cells across.")]
        [Range(0, 2)] public int caveTunnelRadius = 0;

        [Tooltip("Discard a tunnel shorter than this many cells, so a worm that dies immediately does not leave an alcove.")]
        [Min(1)] public int caveTunnelMinCells = 6;

        [Tooltip("Tunnel height in cells, counting the floor.")]
        [Min(1)] public int caveTunnelHeight = 1;

        [Tooltip("How fast the steering noise is sampled. Low gives sweeping bends, high gives a squirm.")]
        [Range(0.02f, 0.6f)] public float caveTunnelWander = 0.14f;

        [Tooltip("Degrees of turn per step. This is what separates a cave passage from a corridor.")]
        [Range(0f, 90f)] public float caveTunnelTurn = 34f;

        [Tooltip("Chance per step of sprouting a side passage. Branches are half length and never branch again.")]
        [Range(0f, 0.2f)] public float caveTunnelBranchChance = 0.03f;

        [Header("Rivers")]
        [Tooltip("Watercourses cut across the finished layout - rock, corridors and room floors alike. 0 disables them.")]
        [Min(0)] public int riverCount = 1;

        [Tooltip("River half-width in cells. 0 is a stream you step over, 1 is three cells of water.")]
        [Range(0, 2)] public int riverRadius = 0;

        [Tooltip("How far the bed is sunk below the surrounding floor. Shallow on purpose: a river the party cannot cross splits the dungeon, and the unreachable half gets pruned away.")]
        [Range(0f, 2f)] public float riverDepth = 0.6f;

        [Tooltip("Height of the water surface above the sunken bed.")]
        [Range(0f, 2f)] public float riverSurfaceHeight = 0.35f;

        [Tooltip("Degrees of meander per step.")]
        [Range(0f, 60f)] public float riverMeander = 22f;

        [Header("Cave dressing")]
        [Tooltip("Rough rock floor tiles for cave cells. Falls back to the ordinary floor tiles.")]
        public GameObject[] caveFloorPrefabs;

        [Tooltip("Rock wall segments for cave cells. Falls back to the ordinary walls.")]
        public GameObject[] caveWallPrefabs;

        [Tooltip("Wall segments with a tunnel opening cut through them, stood where the dungeon breaks into a cave. SM_Env_Wall_Tunnel_Entrance_01 is the kit's piece for this.")]
        public GameObject[] caveEntrancePrefabs;

        [Tooltip("Ceiling piece over cave cells. Falls back to the ordinary ceiling.")]
        public GameObject caveCeilingPrefab;

        [Tooltip("Grid levels one cave wall mesh covers. The Synty cave pieces are two storeys tall, so 2.")]
        [Min(1)] public int caveWallSpansLevels = 1;

        [Tooltip("Tiles laid in the sunken riverbed. Falls back to the cave floor tiles.")]
        public GameObject[] riverBedPrefabs;

        [Tooltip("A flat water plane sized to one cell, laid over every river cell.")]
        public GameObject waterPrefab;

        [Tooltip("Dropped at the centre of the largest chamber - a spawn anchor for a boss or a hoard.")]
        public GameObject lairMarkerPrefab;

        [Tooltip("Hang wall torches in caves. Off by default: an unlit cave is the point.")]
        public bool torchesInCaves = false;

        [Header("Cave surface (generated mesh)")]
        [Tooltip("Build caves as one smoothed rock mesh instead of tiles and wall panels. Tiles snap every wall to a 5m axis-aligned board, so caves read as rooms however organic the plan is.")]
        public bool caveSurfaceMesh = true;

        [Tooltip("Field samples per grid cell. 4 gives 1.25m detail, 5 gives 1m, 6 gives 0.83m. Cost rises with the cube of this.")]
        [Range(2, 8)] public int caveVoxelsPerCell = 5;

        [Tooltip("Blur passes over the density field. 0 gives boxes back, 4+ turns to poured clay. 2 keeps rock reading as rock.")]
        [Range(0, 8)] public int caveSurfaceSmoothing = 2;

        [Tooltip("Shade the cave as flat facets rather than a smooth skin, matching the pack's faceted rock.")]
        public bool caveFlatShading = true;

        [Tooltip("Draw the rock from both sides. A thin skin is invisible from behind, so you see through the rock. Doubles the triangle count.")]
        public bool caveSurfaceDoubleSided = true;

        [Tooltip("Props scattered on the cave floor - stalagmites, boulders, rubble. The pack's demo leans on these heavily.")]
        public GameObject[] caveScatterPrefabs;

        [Tooltip("Average loose props (boulders, rubble) per cave floor cell. Formations are separate.")]
        [Range(0f, 4f)] public float caveScatterPerCell = 0.5f;

        [Tooltip("Stalagmites - growing up off the floor.")]
        public GameObject[] caveStalagmitePrefabs;

        [Tooltip("Stalactites - hanging from the roof.")]
        public GameObject[] caveStalactitePrefabs;

        [Tooltip("Roughly one formation cluster per this many cave floor cells.")]
        [Range(2, 20)] public int caveFormationSpacing = 5;

        [Tooltip("Push formation clusters toward the cave walls rather than the open middle.")]
        [Range(0f, 1f)] public float caveFormationEdgeBias = 0.75f;

        [Tooltip("Cells of built space beside a cave held open so rock cannot plug the mouth. Keep at 1 - every opened cell grows its own rock boundary.")]
        [Range(1, 4)] public int caveOpenMargin = 1;

        [Tooltip("Cells of solid rock kept between any cave and a staircase, so the rock skin cannot land inside a flight of stairs.")]
        [Range(1, 3)] public int caveStairClearance = 1;

        [Tooltip("How often a worm swells to a wider bore. 0 contours into a smooth pipe.")]
        [Range(0f, 1f)] public float caveTunnelBulge = 0.4f;

        [Tooltip("How far noise pushes the rock surface, in world units.")]
        [Range(0f, 1.5f)] public float caveSurfaceNoise = 0.45f;

        [Tooltip("Noise features per world unit. Low is boulders, high is gravel.")]
        [Range(0.02f, 0.5f)] public float caveSurfaceNoiseScale = 0.12f;

        [Tooltip("Height above the floor over which noise fades in, so the ground stays walkable.")]
        [Range(0.1f, 4f)] public float caveFloorFlattenHeight = 1.4f;

        [Tooltip("Material for the cave mesh. Empty borrows the material off the first cave wall prefab.")]
        public Material caveSurfaceMaterial;

        // ------------------------------------------------------------------ dressing

        [Header("Room dressing")]
        [Tooltip("Props stood against room walls - barrels, torch sticks, chains, crates.")]
        public GameObject[] wallPropPrefabs;

        [Tooltip("Pillars set against the walls of larger rooms.")]
        public GameObject[] pillarPrefabs;

        [Tooltip("Loose debris scattered across room floors.")]
        public GameObject[] rubblePrefabs;

        [Tooltip("Chance per wall-facing floor cell of a prop against that wall. Synty's demo runs about 1.2 props per tile, but it is a showcase scene.")]
        [Range(0f, 1f)] public float wallPropChance = 0.3f;

        [Tooltip("Chance per wall-facing cell of a pillar, in rooms large enough to deserve them.")]
        [Range(0f, 1f)] public float pillarChance = 0.14f;

        [Tooltip("Floor cells a room needs before it gets pillars.")]
        [Min(1)] public int pillarMinRoomArea = 40;

        [Tooltip("Chance per floor cell of loose rubble anywhere on the floor.")]
        [Range(0f, 1f)] public float rubbleChance = 0.18f;

        // ------------------------------------------------------------------ room kits

        [Header("Room kits")]
        [Tooltip("Walls and floor for corridors and for rooms whose archetype has no kit. Empty uses the shared wall and floor pools above.")]
        public RoomKit defaultKit;

        [Tooltip("Rooms within this many cells of the entry get every fixture their kit asks for.")]
        [Min(0f)] public float fullyLitDistance = 12f;

        [Tooltip("Rooms this far from the entry or more get no fixtures at all - torchlight only. Between the two, fixtures thin out linearly.")]
        [Min(0f)] public float darkDistance = 40f;

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
