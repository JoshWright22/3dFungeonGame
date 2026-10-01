using UnityEngine;

namespace Delver.Dungeon
{
    /// <summary>Which caverns a vignette may appear in.</summary>
    public enum CaveTheme
    {
        Any,
        /// <summary>Crystal and fungus caverns: the demo's Crystal_Caves.</summary>
        Lush,
        /// <summary>Lived-in caverns: the demo's Goblin_Cave.</summary>
        Inhabited,
    }

    /// <summary>
    /// A small scene set down in a cavern: a centrepiece, a ring of pieces around it, a scatter of
    /// small things, and usually its own light.
    ///
    /// A large cavern used to get one landmark scaled down to 3.5 m and a sprinkle of grass, so the
    /// biggest spaces in the dungeon were the emptiest. The demo fills its caves with clusters -
    /// a goblin camp round a fire, a stand of crystals, a heap of bones - and each one is somewhere
    /// worth walking to. Colours follow the rulebook's painted plates: fire orange, magic green and
    /// magenta, crystal cyan.
    /// </summary>
    [System.Serializable]
    public class CaveVignette
    {
        public string name = "Vignette";
        public CaveTheme theme = CaveTheme.Any;

        [Tooltip("Relative chance among the vignettes this cavern's theme allows.")]
        [Min(0f)] public float weight = 1f;

        [Header("Centrepiece")]
        [Tooltip("One of these stands in the middle.")]
        public GameObject[] centrepieces;

        [Tooltip("Random uniform scale range for the centrepiece.")]
        public Vector2 centreScale = Vector2.one;

        [Header("Ring")]
        [Tooltip("Stood in a loose ring around the centrepiece - tents round a fire, obelisks round a rune stone.")]
        public GameObject[] ring;

        [Min(0)] public int minRing = 2;
        [Min(0)] public int maxRing = 4;

        [Tooltip("Distance of the ring from the centre, in metres.")]
        public Vector2 ringRadius = new Vector2(2f, 3.5f);

        [Tooltip("Spaced evenly round the circle instead of at random angles - a standing-stone circle.")]
        public bool evenRing;

        [Tooltip("Ring pieces face the centrepiece. Off for things with no front, like rocks.")]
        public bool ringFacesCentre = true;

        public Vector2 ringScale = Vector2.one;

        [Header("Scatter")]
        [Tooltip("Small things strewn about - bones, coins, mushrooms. Walked through, never block.")]
        public GameObject[] scatter;

        [Min(0)] public int minScatter = 0;
        [Min(0)] public int maxScatter = 4;

        [Tooltip("How far the scatter strays from the centre.")]
        public float scatterRadius = 4f;

        [Header("Light")]
        [Tooltip("Black for none - some places should be dark.")]
        public Color lightColor = Color.black;

        public float lightIntensity = 2.5f;
        public float lightRange = 12f;
        public float lightHeight = 1.5f;

        [Tooltip("Firelight flickers; crystal glow barely does.")]
        [Range(0f, 0.6f)] public float flicker = 0.2f;
    }

    /// <summary>
    /// What lines the foot of a cave wall, graded big, medium, small. The demo never leaves a cave
    /// wall bare where it meets the ground: the crystal caves bank stalagmites, crystals, giant
    /// mushrooms and grass against it, the goblin cave fences, spikes and bone heaps.
    /// </summary>
    [System.Serializable]
    public class CaveEdgeSet
    {
        public string name = "Edge";
        public CaveTheme theme = CaveTheme.Any;

        [Tooltip("One per cluster, against the wall: a giant mushroom, a fence, a rock pile.")]
        public GameObject[] big;
        [Tooltip("A couple per cluster, round the big one.")]
        public GameObject[] medium;
        [Tooltip("A spray per cluster, spilling out from the wall. Walked through.")]
        public GameObject[] small;

        [Tooltip("Chance a wall face gets a cluster.")]
        [Range(0f, 1f)] public float chance = 0.55f;

        [Tooltip("Every this many clusters, one gets a light. 0 for none.")]
        [Min(0)] public int lightEvery = 4;
        public Color lightColor = new Color(1f, 0.58f, 0.25f);
        public float lightIntensity = 2.5f;
        public float lightRange = 9f;
        [Tooltip("What stands at a lit cluster, e.g. a torch stick or a glowing orb. Optional.")]
        public GameObject lightProp;
    }
}
