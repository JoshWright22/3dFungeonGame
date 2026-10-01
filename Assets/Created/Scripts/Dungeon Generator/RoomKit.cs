using UnityEngine;

namespace Delver.Dungeon
{
    /// <summary>A prefab with a relative chance of being picked. Only the ratio between weights matters.</summary>
    [System.Serializable]
    public struct WeightedPrefab
    {
        public GameObject prefab;
        [Min(0f)] public float weight;
    }

    /// <summary>
    /// A group of props that belong together and are placed as one - a stack of barrels, a pair of
    /// bookcases, a row of kegs down one wall.
    ///
    /// Synty's demo rooms are dressed in clusters, not by scattering one prop per cell. The cellar
    /// has three barrels and two broken ones in a heap, two bookcases side by side, and three
    /// large kegs evenly down one wall - and that grouping is most of why it reads as a cellar.
    /// </summary>
    [System.Serializable]
    public class PropSet
    {
        [Tooltip("Shown in the spawned object names, so a room can be read back.")]
        public string name = "Props";

        [Tooltip("Drawn from at random for each piece of the set.")]
        public GameObject[] pieces;

        [Min(1)] public int minPieces = 1;
        [Min(1)] public int maxPieces = 1;

        [Tooltip("How many times this set may appear in one room.")]
        [Min(0)] public int maxPerRoom = 1;

        [Tooltip("Chance of each allowed placement actually happening.")]
        [Range(0f, 1f)] public float chance = 1f;

        [Tooltip("Distance from the wall face to the middle of the set.")]
        [Min(0f)] public float inset = 1f;

        [Tooltip("How far pieces scatter around the middle of the set. 0 stands them all on one spot.")]
        [Min(0f)] public float spread = 1.2f;

        [Tooltip("Instead of one heap, put one piece against every cell of the longest wall - a row of kegs, a line of beds.")]
        public bool lineWall;

        [Tooltip("Stand the set in the middle of the room instead of against a wall: the first piece in the centre (a table), the rest in a ring around it at 'spread' (stools). Interior cells are preferred.")]
        public bool inMiddle;

        [Tooltip("Flat things you walk over - rugs, grates, puddles. No collider, and they never block other props.")]
        public bool walkThrough;

        [Tooltip("Above 0: hung on the wall with its top this high above the floor, instead of stood on the floor - banners, shields, trophies.")]
        public float mountHeight;

        [Tooltip("Grade a heap big to small: the first piece a quarter larger, the last little more than half size. For pots, rocks, planks - not for things with a fixed real size like barrels.")]
        public bool gradeSizes;

        [Tooltip("Random yaw added to each piece. A barrel shoved against a wall is not square to it; a bookcase is.")]
        [Range(0f, 180f)] public float yawJitter = 25f;
    }

    /// <summary>
    /// Everything that makes one kind of room look like itself: its walls, floor, ceiling, the
    /// timber framing laid over the stone, and how it is furnished and lit.
    ///
    /// A room's archetype picks its size; its kit picks its look. Before kits, every room in a
    /// dungeon was built from the same pool of six walls and six floor tiles drawn at random per
    /// cell, so a cellar and a great hall looked identical and both looked like a patchwork. The
    /// demo instead gives each room one dominant wall and floor, and layers structure over them.
    /// </summary>
    /// <summary>Where a kit's light fixture goes.</summary>
    public enum LightMount
    {
        /// <summary>Stood on the floor near a wall - a candle stand, a brazier.</summary>
        Floor,
        /// <summary>Hung under the ceiling over the middle of a cell - a chandelier.</summary>
        Ceiling,
        /// <summary>Fixed to a wall at head height - a torch sconce.</summary>
        Wall,
    }

    [CreateAssetMenu(menuName = "Delver/Room Kit", fileName = "RoomKit")]
    public class RoomKit : ScriptableObject
    {
        [Header("Shell")]
        [Tooltip("Wall courses. Give one dominant wall most of the weight and a few accents the rest - the demo's rooms are 55-100% Wall_01.")]
        public WeightedPrefab[] walls;

        [Tooltip("Floor tiles, drawn per cell. Same advice: one dominant tile, a few accents.")]
        public WeightedPrefab[] floors;

        [Tooltip("Turn each floor tile a random multiple of 90 degrees, as the demo does, so the pattern does not repeat in rows.")]
        public bool rotateFloors = true;

        [Tooltip("Ceiling slab over every cell at the top of the room. Empty keeps the profile's ceiling.")]
        public GameObject ceiling;

        [Tooltip("Ceiling variants, drawn per cell by weight. Overrides the single ceiling above when set.")]
        public WeightedPrefab[] ceilings;

        [Tooltip("Turn each ceiling slab a random multiple of 90 degrees. Only for square, flat slabs - an arched or curved ceiling has a direction.")]
        public bool rotateCeilings;

        [Tooltip("Each room picks one wall, one floor and one ceiling variant as its own favourite and weights it this many times over, so two rooms of the same kind still look like different rooms.")]
        [Min(1f)] public float favouriteBoost = 3f;

        [Header("Timber frame")]
        [Tooltip("Hung from the ceiling line in front of every wall segment, e.g. SM_Env_Basement_WallPanel_01 - a 2 m timber band across the top of the stone.")]
        public GameObject upperBand;

        [Tooltip("Floor-to-ceiling post at every joint between two wall segments, e.g. SM_Env_Basement_Support_Pole_01.")]
        public GameObject post;

        [Tooltip("How far the post stands off the wall. The demo cellar uses 0.5.")]
        [Range(0f, 2f)] public float postInset = 0.5f;

        [Tooltip("Bracket from the wall into the room above each post, e.g. SM_Env_Basement_Support_Wall_01.")]
        public GameObject brace;

        [Tooltip("A post at the joints of every storey, not just the floor's - pilasters running up a tall hall.")]
        public bool postEveryStorey;

        [Header("Masonry detail")]
        [Tooltip("Capped onto every inside corner, e.g. SM_Env_Wall_End_Coner_Inner_01. Hides the seam where two wall courses meet.")]
        public GameObject innerCorner;

        [Tooltip("Run along the foot of every wall, e.g. SM_Env_Wall_Trim_Edge_01 - a stone plinth.")]
        public GameObject lowerBand;

        [Tooltip("Run along the top of every wall course, every storey, e.g. SM_Env_Wall_Trim_Top_02. Unlike the walls, its pivot is centred on its length.")]
        public GameObject courseTrim;

        [Tooltip("How far the course trim stands off the wall line. The demo's great hall uses 0.5.")]
        public float courseTrimInset = 0.5f;

        [Tooltip("How far below the top of the course the trim sits.")]
        public float courseTrimDrop = 0.5f;

        [Header("Arcade")]
        [Tooltip("Spans every cell boundary inside the room, e.g. SM_Env_Wall_Archway_01. Turns one big box into a vaulted arcade.")]
        public GameObject archway;

        [Tooltip("Height above the floor the archway springs from. The demo laboratory uses 2.6.")]
        public float archBase = 2.6f;

        public Vector3 archScale = Vector3.one;

        [Tooltip("Free-standing column at every grid vertex inside the room, carrying the archways, e.g. SM_Env_Pillar_Round_02.")]
        public GameObject interiorPost;

        public Vector3 interiorPostScale = Vector3.one;

        [Header("Doors")]
        [Tooltip("Door leaves for doorways into this room, e.g. SM_Env_Door_Bars_01 for prison cells. Empty uses the profile's.")]
        public GameObject[] doorLeaves;

        [Header("Furnishing")]
        public PropSet[] propSets;

        [Tooltip("Hung from the ceiling - chains, cobwebs, cages.")]
        public GameObject[] ceilingHangers;

        [Tooltip("Chance per floor cell of something hanging overhead.")]
        [Range(0f, 1f)] public float hangerChance = 0.15f;

        [Tooltip("Loose debris on the floor.")]
        public GameObject[] rubble;

        [Range(0f, 1f)] public float rubbleChance = 0.3f;

        [Tooltip("The demo stretches its pebble piles flat and wide (1.44, 2.5, 1.44) rather than using them as modelled.")]
        public Vector3 rubbleScale = Vector3.one;

        [Header("Lighting")]
        [Tooltip("Carries the room's light, e.g. SM_Prop_Candle_Stand_01, SM_Prop_Candle_Chandelier_01 or SM_Prop_Torch_Ornate_02. Empty leaves the room to the profile's torches.")]
        public GameObject lightFixture;

        public LightMount lightMount = LightMount.Floor;

        [Tooltip("Wall mounts only: height of the fixture above the floor. The demo's sconces sit at 1.7.")]
        public float mountHeight = 1.7f;

        [Tooltip("The demo's fixtures are a deep orange: (1, 0.69, 0.14).")]
        public Color lightColor = new Color(1f, 0.69f, 0.14f);

        public float lightIntensity = 3f;
        public float lightRange = 10f;

        [Tooltip("Height of the flame above the fixture's lowest point - its base on the floor or the wall, the bottom of a chandelier.")]
        public float lightHeight = 1.7f;

        [Tooltip("Floor cells per fixture in a fully lit room. The demo runs one light per 2-4 cells.")]
        [Min(1)] public int cellsPerLight = 4;
    }
}
