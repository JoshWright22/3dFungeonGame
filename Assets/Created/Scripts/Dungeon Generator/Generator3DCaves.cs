using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Caves: the parts of the dungeon nobody built.
///
/// Three shapes, from three different algorithms, because one algorithm does not give you a cave
/// system - it gives you one kind of hole repeated:
///
///  * CHAMBERS come from cellular automata. A rectangle of noise, smoothed until it settles into
///    a blob with a ragged edge. Big, roughly round, no corridors - which is what you want under
///    a dragon.
///  * TUNNELS come from a Perlin worm. A point walks forward while its heading is steered by
///    smooth noise, so the passage curves instead of jittering the way a drunkard's walk does.
///    They may branch, and they dead-end freely - a passage that goes nowhere is a cave being a
///    cave, not a bug.
///  * RIVERS are a worm too, but cut LAST and across everything: rock, corridors, room floors
///    alike. The channel is sunk rather than sealed, so water reads as depth and can still be
///    waded - a river that severed the dungeon would just get half of it pruned away.
///
/// Everything here works in grid cells, so a cave is blocky at 5m per cell. That reads fine with
/// rough rock tiles and terrible with smooth ones; the fix, if it is ever wanted, is a finer
/// sub-grid for cave regions rather than a different carving algorithm.
/// </summary>
public partial class Generator3D
{
    public enum CaveShape
    {
        Chamber,
        Tunnel,
        River
    }

    /// <summary>Size classes still owed by their quota, drawn before any weighted pick.</summary>
    readonly List<Delver.Dungeon.CaveArchetype> caveQuota = new List<Delver.Dungeon.CaveArchetype>();

    /// <summary>One carved cave, kept so encounters can be hung off it after generation.</summary>
    public class CaveRegion
    {
        public CaveShape Shape;
        public HashSet<Vector3Int> Cells = new HashSet<Vector3Int>();
        public Vector3Int Anchor;
        public BoundsInt Bounds;

        /// <summary>The largest chamber in the dungeon. Where the hoard goes.</summary>
        public bool IsLair;

        /// <summary>
        /// Floor cells below which this region is a sealed pocket rather than a smaller cave,
        /// and is erased. Carried per region because a size class sets its own: a threshold
        /// sized for the one big cavern deletes every pocket on the floor.
        /// </summary>
        public int FloorThreshold = 1;

        public int CellCount => Cells.Count;
    }

    // ------------------------------------------------------------------ chambers

    [Header("Cave chambers")]
    [Tooltip("Blobby natural chambers carved into the rock between rooms. 0 disables them.")]
    [SerializeField]
    int caveChamberCount = 3;

    [Tooltip("Smallest chamber footprint, in cells (x = width, y = depth). The blob inside is smaller than this - the box is only where it is grown.")]
    [SerializeField]
    Vector2Int caveChamberMinSize = new Vector2Int(7, 7);

    [Tooltip("Largest chamber footprint, in cells (x = width, y = depth).")]
    [SerializeField]
    Vector2Int caveChamberMaxSize = new Vector2Int(14, 14);

    [Tooltip("Chamber height in cells. 2 or 3 gives a cavern you can look up in; 1 is a crawlspace.")]
    [Min(1)]
    [SerializeField]
    int caveChamberHeight = 2;

    [Tooltip("Starting proportion of open cells before smoothing. Around 0.45 gives connected blobs; lower breaks into islands, higher fills the whole box.")]
    [Range(0.3f, 0.6f)]
    [SerializeField]
    float caveFillChance = 0.46f;

    [Tooltip("Smoothing passes. Each one rounds the outline off; past about 6 the blob stops changing.")]
    [Range(0, 10)]
    [SerializeField]
    int caveSmoothIterations = 4;

    [Tooltip("Discard a chamber smaller than this many floor cells. Stops the smoothing from leaving a pebble behind.")]
    [Min(1)]
    [SerializeField]
    int caveChamberMinCells = 14;

    [Tooltip("Cells of solid rock kept between a cave and anything already placed.")]
    [Range(0, 4)]
    [SerializeField]
    int caveSeparation = 1;

    [Tooltip("Extra placement attempts per chamber before giving up.")]
    [Min(1)]
    [SerializeField]
    int caveAttemptsPerChamber = 12;

    // ------------------------------------------------------------------ tunnels

    [Header("Cave tunnels (Perlin worms)")]
    [Tooltip("Winding passages wormed out from the edge of a cave or room. 0 disables them.")]
    [SerializeField]
    int caveTunnelCount = 4;

    [Tooltip("How many cells a worm walks before it gives up. It also stops early when it breaks into somewhere that already exists.")]
    [Min(1)]
    [SerializeField]
    int caveTunnelLength = 40;

    [Tooltip("Passage half-width. 0 is a one-cell squeeze, 1 is three cells across.")]
    [Range(0, 2)]
    [SerializeField]
    int caveTunnelRadius = 0;

    [Tooltip("Discard a tunnel shorter than this many cells. Without it a worm that dies on its first step leaves a pointless alcove in a wall.")]
    [Min(1)]
    [SerializeField]
    int caveTunnelMinCells = 6;

    [Tooltip("How often a worm swells to a wider bore as it goes. A constant radius contours into a smooth pipe - a real passage pinches and opens out. 0 is a uniform tube.")]
    [Range(0f, 1f)]
    [SerializeField]
    float caveTunnelBulge = 0.4f;

    [Tooltip("Tunnel height in cells, counting the floor.")]
    [Min(1)]
    [SerializeField]
    int caveTunnelHeight = 1;

    [Tooltip("How fast the steering noise is sampled. Low values give long sweeping bends, high values give a passage that squirms.")]
    [Range(0.02f, 0.6f)]
    [SerializeField]
    float caveTunnelWander = 0.14f;

    [Tooltip("Degrees of turn the noise can ask for per step. This is what separates a cave passage from a corridor.")]
    [Range(0f, 90f)]
    [SerializeField]
    float caveTunnelTurn = 34f;

    [Tooltip("Chance per step that a worm sprouts a side passage. Branches are half length and never branch again.")]
    [Range(0f, 0.2f)]
    [SerializeField]
    float caveTunnelBranchChance = 0.03f;

    // ------------------------------------------------------------------ rivers

    [Header("Rivers")]
    [Tooltip("Watercourses cut across the finished layout - through rock, corridors and room floors alike. 0 disables them.")]
    [SerializeField]
    int riverCount = 1;

    [Tooltip("River half-width in cells. 0 is a stream you step over, 1 is three cells of water.")]
    [Range(0, 2)]
    [SerializeField]
    int riverRadius = 0;

    [Tooltip("How far the bed is sunk below the surrounding floor. Deep enough to read as a channel, shallow enough to wade - a river the party cannot cross just gets the far half of the dungeon pruned away.")]
    [Range(0f, 2f)]
    [SerializeField]
    float riverDepth = 0.6f;

    [Tooltip("Height of the water surface above the sunken bed.")]
    [Range(0f, 2f)]
    [SerializeField]
    float riverSurfaceHeight = 0.35f;

    [Tooltip("Degrees of meander per step.")]
    [Range(0f, 60f)]
    [SerializeField]
    float riverMeander = 22f;

    // ------------------------------------------------------------------ dressing

    [Header("Cave dressing")]
    [Tooltip("Floor tiles for cave cells - rough rock rather than flagstones. Falls back to the ordinary floor tiles.")]
    [SerializeField]
    GameObject[] caveFloorPrefabs;

    [Tooltip("Wall segments for cave cells. Falls back to the ordinary wall prefabs.")]
    [SerializeField]
    GameObject[] caveWallPrefabs;

    [Tooltip("Wall segments with a rough tunnel opening cut through them (SM_Env_Wall_Tunnel_Entrance_01), stood where the built dungeon breaks into a cave. Without one the room's wall line simply stops and flagstone meets rock in mid air.")]
    [SerializeField]
    GameObject[] caveEntrancePrefabs;

    [Tooltip("Ceiling piece over cave cells. Falls back to the ordinary ceiling.")]
    [SerializeField]
    GameObject caveCeilingPrefab;

    [Tooltip("How many grid levels one cave wall mesh covers. The Synty cave pieces (SM_Env_Cave_01/02) are two storeys tall, so 2 - otherwise a chamber with headroom stacks two of them into a cliff twice its own height.")]
    [Min(1)]
    [SerializeField]
    int caveWallSpansLevels = 1;

    [Tooltip("Tiles laid in the sunken riverbed. Falls back to the cave floor tiles.")]
    [SerializeField]
    GameObject[] riverBedPrefabs;

    [Tooltip("A flat water plane sized to one cell, laid over every river cell.")]
    [SerializeField]
    GameObject waterPrefab;

    [Tooltip("Dropped at the centre of the largest chamber - a spawn anchor for a boss, a hoard, whatever the lair holds. Leave empty and the point is still published as LairPoint.")]
    [SerializeField]
    GameObject lairMarkerPrefab;

    [Tooltip("Hang wall torches in caves. Off by default: an unlit cave is the point, and the party brings its own light.")]
    [SerializeField]
    bool torchesInCaves = false;

    // ------------------------------------------------------------------ surface

    [Header("Cave surface (generated mesh)")]
    [Tooltip("Build caves as one smoothed rock mesh instead of tiles and wall panels. Tiles snap every wall to a 5m axis-aligned board, which reads as a room however organic the plan is. Turn OFF to go back to tile-built caves.")]
    [SerializeField]
    bool caveSurfaceMesh = true;

    [Tooltip("Field samples per grid cell. 4 gives 1.25m detail, 5 gives 1m, 6 gives 0.83m. Cost rises with the cube of this.")]
    [Range(2, 8)]
    [SerializeField]
    int caveVoxelsPerCell = 5;

    [Tooltip("Blur passes over the density field. This is what actually rounds the cells off - at 0 you get the boxes back, at 4+ it turns to poured clay. 2 keeps the rock reading as rock.")]
    [Range(0, 8)]
    [SerializeField]
    int caveSurfaceSmoothing = 2;

    [Tooltip("Shade the cave as flat facets rather than a smooth skin. On, because the Synty rock is faceted and a smoothly shaded contour looks like clay.")]
    [SerializeField]
    bool caveFlatShading = true;

    [Tooltip("Draw the rock from both sides. The contour is a thin skin, so seen from behind it is not drawn at all and you look straight through the rock. Doubles the triangle count.")]
    [SerializeField]
    bool caveSurfaceDoubleSided = true;

    [Tooltip("Scatter props on the cave floor - stalagmites, boulders, rubble. The pack's own demo leans on these heavily, and they do more than any amount of mesh tuning to stop a cave reading as a tunnel.")]
    [SerializeField]
    GameObject[] caveScatterPrefabs;

    [Tooltip("Average loose props (boulders, rubble) per cave floor cell. Formations are placed separately.")]
    [Range(0f, 4f)]
    [SerializeField]
    float caveScatterPerCell = 0.5f;

    [Tooltip("Stalagmites - the ones growing up off the floor.")]
    [SerializeField]
    GameObject[] caveStalagmitePrefabs;

    [Tooltip("Stalactites - the ones hanging from the roof.")]
    [SerializeField]
    GameObject[] caveStalactitePrefabs;

    [Tooltip("Roughly one formation cluster per this many cave floor cells. Clusters are one big spire, a couple of middling ones and a scatter of small, the way they actually grow.")]
    [Range(2, 20)]
    [SerializeField]
    int caveFormationSpacing = 5;

    [Tooltip("Push clusters toward the cave walls. Formations grow at the edges of a chamber far more than out in the middle of the floor, and a clear centre is also where you want to be able to fight.")]
    [Range(0f, 1f)]
    [SerializeField]
    float caveFormationEdgeBias = 0.75f;

    [Tooltip("How far noise pushes the rock surface, in world units. Keeps the cave from reading as a blurred box.")]
    [Range(0f, 1.5f)]
    [SerializeField]
    float caveSurfaceNoise = 0.45f;

    [Tooltip("Noise features per world unit. Low is boulders and bulges, high is gravel.")]
    [Range(0.02f, 0.5f)]
    [SerializeField]
    float caveSurfaceNoiseScale = 0.12f;

    [Tooltip("Height above the floor over which noise fades in. The ground stays flat enough to walk on; only walls and roof get rough.")]
    [Range(0.1f, 4f)]
    [SerializeField]
    float caveFloorFlattenHeight = 1.4f;

    [Tooltip("Material for the cave mesh. Leave empty to borrow the material off the first cave wall prefab, which is what keeps the rock matching the rest of the kit.")]
    [SerializeField]
    Material caveSurfaceMaterial;

    [Tooltip("Cells of built space beside a cave held open, so the rock cannot plug the mouth. Keep this at 1: every opened cell grows its own rock boundary, so reaching further scatters geometry through the dungeon rather than clearing it.")]
    [Range(1, 4)]
    [SerializeField]
    int caveOpenMargin = 1;

    [Tooltip("Cells of solid rock kept between any cave and a staircase. Holding the built space open is not enough on its own - the rock skin is drawn on the cave's own boundary, so the cave itself has to be pushed back or the surface lands inside the flight.")]
    [Range(1, 3)]
    [SerializeField]
    int caveStairClearance = 1;

    // ------------------------------------------------------------------ state

    readonly List<CaveRegion> caves = new List<CaveRegion>();

    /// <summary>Caves produced by the last generation, after unreachable ones were pruned.</summary>
    public IReadOnlyList<CaveRegion> Caves => caves;

    /// <summary>Centre of the largest chamber in world space, or zero when there are no caves.</summary>
    public Vector3 LairPoint { get; private set; }

    /// <summary>True when caves are built as a contoured mesh rather than from tiles.</summary>
    // The asset kit replaces the contoured mesh outright: both would occupy the same space.
    bool UsingCaveSurface => caveSurfaceMesh && !UsingCaveKit;

    /// <summary>
    /// The contoured surface built behind the asset kit as a sealed backing. The kit's rock pieces
    /// overlap but never seal - every pass at patching the gaps left some open - while the
    /// contoured surface is watertight by construction. Pushed back into the rock, it is only
    /// seen where the kit leaves a hole, as dark rock instead of the void.
    /// </summary>
    bool UsingCaveBacking => caveSurfaceMesh && UsingCaveKit;

    /// <summary>How far the backing is pushed into the rock, so the kit stands in front of it.</summary>
    const float CaveBackingDepth = 0.45f;

    void ClearCaves()
    {
        foreach (var generated in caveGeneratedMeshes)
            if (generated != null)
            {
                if (Application.isPlaying) Destroy(generated);
                else DestroyImmediate(generated);
            }
        caveGeneratedMeshes.Clear();
        compositionReserved.Clear();
        caves.Clear();
        caveSurfaceCollider = null;
        LairPoint = Vector3.zero;
    }

    MeshCollider caveSurfaceCollider;
    readonly List<Mesh> caveGeneratedMeshes = new List<Mesh>();

    /// <summary>
    /// Contours the carved cave cells into one rock surface and hangs it under the dungeon.
    ///
    /// The cells themselves are untouched - reachability, corridor pathfinding and the cave
    /// mouths all still read them. Only the geometry changes.
    /// </summary>
    void BuildCaveSurface()
    {
        if (!UsingCaveSurface && !UsingCaveBacking) return;

        var settings = new Delver.Dungeon.CaveSurface.Settings
        {
            VoxelsPerCell = caveVoxelsPerCell,
            SmoothPasses = caveSurfaceSmoothing,
            NoiseAmplitude = caveSurfaceNoise,
            NoiseFrequency = caveSurfaceNoiseScale,
            FloorFlattenHeight = caveFloorFlattenHeight,
            CellSize = WorldUnitSize,
            FloorSurfaceOffset = FloorSurfaceOffset,
            OpenMargin = caveOpenMargin,
        };

        int caveCells;
        Mesh mesh = Delver.Dungeon.CaveSurface.Build(
            size,
            cell => grid[cell] == CellType.Cave || grid[cell] == CellType.CaveOpen,
            cell => IsWalkable(grid[cell]),
            IsProtectedFromCaveRock,
            IsBuiltSpace,
            settings,
            out caveCells);

        if (mesh == null) return;

        if (UsingCaveBacking) PushIntoRock(mesh, CaveBackingDepth);

        GameObject rockSource = PickFirst(caveWallPrefabs);
        Delver.Dungeon.CaveSurface.Finish(mesh, caveWallPrefabs, caveFlatShading, caveSurfaceDoubleSided);

        var go = new GameObject("Cave Surface");
        go.transform.SetParent(dungeonRoot, false);

        // The mesh is built in dungeon coordinates, the same ones every tile is instantiated at.
        // Tiles get their world position passed to Instantiate so they ignore this transform
        // entirely; the mesh would otherwise inherit it and slide the whole cave system away
        // from the dungeon it belongs to by however far the generator sits from the origin.
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;

        caveGeneratedMeshes.Add(mesh);

        Material material = caveSurfaceMaterial;
        if (material == null && rockSource != null)
        {
            var sourceRenderer = rockSource.GetComponentInChildren<MeshRenderer>();
            if (sourceRenderer != null) material = sourceRenderer.sharedMaterial;
        }

        // Forward lighting selects lights per renderer. One renderer spanning the entire
        // dungeon can discard the torch beside the player in favour of distant lights.
        // Keep a continuous collider, but give each local patch its own lighting bounds.
        foreach (var chunk in Delver.Dungeon.CaveSurface.SplitForLighting(mesh, WorldUnitSize * 2f))
        {
            caveGeneratedMeshes.Add(chunk);
            var patch = new GameObject("Cave rock patch");
            patch.transform.SetParent(go.transform, false);
            patch.AddComponent<MeshFilter>().sharedMesh = chunk;
            var meshRenderer = patch.AddComponent<MeshRenderer>();
            if (material != null) meshRenderer.sharedMaterial = material;
        }

        caveSurfaceCollider = go.AddComponent<MeshCollider>();
        caveSurfaceCollider.sharedMesh = mesh;

        Debug.Log($"Cave surface: {caveCells} cells contoured into {mesh.vertexCount} verts, "
            + $"{mesh.triangles.Length / 3} tris at {(WorldUnitSize / caveVoxelsPerCell):F2}m.", this);

    }

    /// <summary>
    /// Moves every vertex into the rock, away from the open cave. The contoured mesh is wound so
    /// its normals point into the rock, not toward the cave - pushing against them, as the
    /// first version did, swelled the backing out into the cave and buried the floor.
    /// </summary>
    static void PushIntoRock(Mesh mesh, float depth)
    {
        mesh.RecalculateNormals();
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        for (int i = 0; i < vertices.Length; i++) vertices[i] += normals[i] * depth;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Drops rock props onto the finished cave floor.
    ///
    /// Placed by raycast rather than by grid position: the floor is a contoured surface with real
    /// undulation, so the only way to sit a boulder ON it is to ask the collider where it is.
    /// Steep hits are skipped, or props end up embedded in the walls.
    /// </summary>
    void ScatterCaveProps()
    {
        if (caveScatterPrefabs == null || caveScatterPrefabs.Length == 0) return;
        if (caveScatterPerCell <= 0f) return;

        // The surface was instantiated moments ago; it is not in the physics scene until told.
        Physics.SyncTransforms();

        foreach (var cave in caves)
        foreach (var cell in cave.Cells)
        {
            if (grid[cell] != CellType.Cave) continue;

            // Cave mouths, landmark clearings and their approach lanes stay bare.
            if (compositionReserved.Contains(cell)) continue;

            // Rocks come in clusters, graded big, medium, small - one boulder, a couple of
            // stones against it, a spray of pebbles round the edge - which is how rock actually
            // falls and settles. Evenly sized pieces sprinkled one per spot read as scatter.
            float clusters = caveScatterPerCell / 2f;
            int count = Mathf.FloorToInt(clusters);
            if (random.NextDouble() < clusters - count) count++;

            for (int i = 0; i < count; i++)
            {
                Vector3 middle = new Vector3(
                    (cell.x + 0.15f + (float)random.NextDouble() * 0.7f) * WorldUnitSize, 0f,
                    (cell.z + 0.15f + (float)random.NextDouble() * 0.7f) * WorldUnitSize);
                float top = cell.y * WorldUnitSize + FloorSurfaceOffset + WorldUnitSize * 0.5f;

                foreach (var tier in ScatterTiers(random.Next(2, 4), random.Next(3, 6)))
                {
                    GameObject prefab = PickPrefab(caveScatterPrefabs);
                    if (prefab == null) continue;

                    Vector2 offset = RandomInDisc() * tier.reach;
                    var from = new Vector3(middle.x + offset.x, top, middle.z + offset.y);
                    if (!CaveGroundCast(new Ray(from, Vector3.down), out RaycastHit hit, WorldUnitSize)) continue;

                    // Only lay them on ground, not on the walls the same cast can find.
                    if (hit.normal.y < 0.7f) continue;

                    GameObject prop = Spawn(prefab, hit.point, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                    if (prop == null) continue;

                    prop.transform.localScale *= tier.scale;
                    AlignSurface(prop, hit.point.y, false);

                    // Knee-high and under is walked through, not around. Boulders stay solid.
                    var size = prop.GetComponentInChildren<Renderer>();
                    if ((size != null && size.bounds.size.y < 0.75f) || IsGroundClutter(prefab.name)) MakeWalkThrough(prop);
                }
            }
        }

    }

    /// <summary>Growth and litter a foot passes through, however big it is drawn: moss, vines, grass, pebbles.</summary>
    static bool IsGroundClutter(string name) =>
        name.Contains("Moss") || name.Contains("Vine") || name.Contains("Grass") || name.Contains("Pebbles")
        || name.Contains("Flowers") || name.Contains("Mushroom_Small") || name.Contains("Roots");

    /// <summary>
    /// Grows stalagmites and stalactites in clusters.
    ///
    /// Uniform scatter reads as debris sprinkled on a floor. Real formations come in families -
    /// one dominant spire with a couple of middling ones and a spray of small around the base,
    /// because they grow from the same drip - and they crowd the edges of a chamber rather than
    /// the middle. Each cluster mirrors onto the roof, since a stalagmite is the floor half of a
    /// drip that left a stalactite directly above it.
    /// </summary>
    void GrowCaveFormations()
    {
        bool anyFloor = caveStalagmitePrefabs != null && caveStalagmitePrefabs.Length > 0;
        bool anyRoof = caveStalactitePrefabs != null && caveStalactitePrefabs.Length > 0;
        if (!anyFloor && !anyRoof) return;

        Physics.SyncTransforms();

        foreach (var cave in caves)
        {
            int clusters = Mathf.Max(1, cave.CellCount / Mathf.Max(2, caveFormationSpacing));

            for (int i = 0; i < clusters; i++)
            {
                if (!PickFormationCell(cave, out Vector3Int cell)) continue;

                Vector3 origin = new Vector3(
                    (cell.x + (float)random.NextDouble()) * WorldUnitSize,
                    0f,
                    (cell.z + (float)random.NextDouble()) * WorldUnitSize);

                float probeTop = cell.y * WorldUnitSize + FloorSurfaceOffset + WorldUnitSize * 0.5f;

                // One big, two or three middling, four to six small - roughly.
                PlaceFormation(origin, probeTop, 0f, 1.5f + (float)random.NextDouble() * 0.6f, anyFloor, anyRoof);

                int mediums = 2 + random.Next(2);
                for (int m = 0; m < mediums; m++)
                    PlaceFormation(origin, probeTop, 1.6f, 0.75f + (float)random.NextDouble() * 0.4f, anyFloor, anyRoof);

                int smalls = 4 + random.Next(3);
                for (int m = 0; m < smalls; m++)
                    PlaceFormation(origin, probeTop, 2.8f, 0.3f + (float)random.NextDouble() * 0.25f, anyFloor, anyRoof);
            }
        }
    }

    /// <summary>
    /// Picks where a cluster starts, biased toward the cave wall.
    ///
    /// Sampling repeatedly and keeping the most enclosed candidate is cheaper than measuring a
    /// distance field, and the bias only has to be a tendency rather than a rule.
    /// </summary>
    bool PickFormationCell(CaveRegion cave, out Vector3Int chosen)
    {
        chosen = default;

        int samples = 0;
        int bestEdges = -1;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector3Int pick = default;
            int seen = 0;

            foreach (var cell in cave.Cells)
            {
                if (grid[cell] != CellType.Cave) continue;
                seen++;
                if (random.Next(seen) == 0) pick = cell;
            }

            if (seen == 0) return false;

            samples++;

            int edges = 0;
            foreach (var dir in Directions)
            {
                Vector3Int n = pick + dir;
                if (!grid.InBounds(n) || grid[n] != CellType.Cave) edges++;
            }

            if (edges > bestEdges)
            {
                bestEdges = edges;
                chosen = pick;
            }

            // Out at the wall already, or not fussy this time.
            if (bestEdges > 0 && random.NextDouble() > caveFormationEdgeBias) break;
            if (bestEdges >= 2) break;
        }

        return samples > 0;
    }

    /// <summary>Stands one spire on the floor and hangs its twin from the roof above it.</summary>
    void PlaceFormation(Vector3 origin, float probeTop, float spread, float scale,
        bool anyFloor, bool anyRoof)
    {
        float ox = origin.x + ((float)random.NextDouble() - 0.5f) * spread * 2f;
        float oz = origin.z + ((float)random.NextDouble() - 0.5f) * spread * 2f;

        var down = new Vector3(ox, probeTop, oz);
        var owner = new Vector3Int(Mathf.FloorToInt(ox / WorldUnitSize),
            Mathf.FloorToInt((probeTop - FloorSurfaceOffset) / WorldUnitSize),
            Mathf.FloorToInt(oz / WorldUnitSize));
        // Cluster spread must not carry formations into an adjoining room or river, nor into a
        // mouth or focal clearing - a spire across a cave entrance reads as a sealed wall.
        if (!grid.InBounds(owner) || grid[owner] != CellType.Cave || !HasCaveGeometry) return;
        if (compositionReserved.Contains(owner)) return;

        if (anyFloor
            && CaveGroundCast(new Ray(down, Vector3.down), out RaycastHit floorHit, WorldUnitSize)
            && floorHit.normal.y >= 0.65f)
        {
            GameObject prefab = PickPrefab(caveStalagmitePrefabs);
            if (prefab != null)
            {
                var rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                GameObject spire = Spawn(prefab, floorHit.point, rotation);
                if (spire != null)
                {
                    spire.transform.localScale *= scale;
                    AlignSurface(spire, floorHit.point.y, false);
                }
            }
        }

        // The roof twin, cast from just above the floor so it finds this cave's own ceiling.
        if (anyRoof
            && CaveRoofCast(new Ray(new Vector3(ox, probeTop - WorldUnitSize * 0.45f, oz), Vector3.up),
                out RaycastHit roofHit, WorldUnitSize * 2.5f)
            && roofHit.normal.y <= -0.5f)
        {
            GameObject prefab = PickPrefab(caveStalactitePrefabs);
            if (prefab != null)
            {
                var rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                GameObject spike = Spawn(prefab, roofHit.point, rotation);
                if (spike != null)
                {
                    spike.transform.localScale *= scale * 0.85f;
                    AlignSurface(spike, roofHit.point.y, true);
                }
            }
        }
    }

    /// <summary>
    /// Space the dungeon was built in, as opposed to cave or solid rock.
    ///
    /// The cave shell is clipped out of these entirely, so rock can never appear inside a room,
    /// a corridor or a flight of stairs however the field around it happens to fall.
    /// </summary>
    bool IsBuiltSpace(Vector3Int cell)
    {
        if (!grid.InBounds(cell)) return false;

        CellType t = grid[cell];

        return t == CellType.BottomFloorRoom || t == CellType.Room
            || t == CellType.Hallway || t == CellType.Stairs || t == CellType.Water;
    }

    /// <summary>
    /// Cells already occupied by built geometry the cave rock must not share space with.
    ///
    /// A staircase ramp fills its cell, and a doorway frame stands on its edge. Neither leaves
    /// room for a rock face, so these are held solid in the density field rather than opened.
    /// </summary>
    bool IsProtectedFromCaveRock(Vector3Int cell)
    {
        if (grid[cell] == CellType.Stairs) return true;
        if (stairUnderFloors.Contains(cell)) return true;

        // The cell under, and the cell beyond, every doorway.
        foreach (var (doorCell, dir) in doorways)
            if (cell == doorCell || cell == doorCell + dir) return true;

        return false;
    }

    /// <summary>First non-null entry, without consuming a random draw.</summary>
    GameObject PickFirst(GameObject[] set)
    {
        if (set == null) return null;

        foreach (var p in set)
            if (p != null) return p;

        return null;
    }

    // ================================================================== chambers

    /// <summary>
    /// Grows blobby chambers in the rock left between rooms.
    ///
    /// Each one is registered as a region, so the triangulation and spanning tree that connect
    /// rooms connect caves too - a chamber is always reachable by an ordinary corridor even when
    /// every worm tunnel toward it happens to dead-end.
    /// </summary>
    void CarveCaveChambers()
    {
        if (caveChamberCount <= 0) return;

        int placed = 0;
        int attempts = 0;
        int budget = caveChamberCount * Mathf.Max(1, caveAttemptsPerChamber);

        caveQuota.Clear();
        if (profile != null && profile.caveArchetypes != null)
            foreach (var entry in profile.caveArchetypes)
                for (int i = 0; entry != null && i < entry.guaranteed; i++) caveQuota.Add(entry);

        while (placed < caveChamberCount && attempts < budget)
        {
            attempts++;

            // A size class per chamber, so a floor gets pockets and side chambers as well as the
            // one cavern worth crossing the map for. Without this every cave comes out the same
            // size, because a single uniform draw concentrates on the middle of its range.
            var kind = PickCaveArchetype();

            int height = Mathf.Clamp(kind != null ? kind.storeys : caveChamberHeight, 1, size.y);
            int floor = kind != null ? kind.minCells : caveChamberMinCells;

            int w = kind != null
                ? random.Next(Mathf.Max(3, kind.minFootprint.x), Mathf.Max(3, kind.maxFootprint.x) + 1)
                : random.Next(Mathf.Max(3, caveChamberMinSize.x), Mathf.Max(3, caveChamberMaxSize.x) + 1);
            int d = kind != null
                ? random.Next(Mathf.Max(3, kind.minFootprint.y), Mathf.Max(3, kind.maxFootprint.y) + 1)
                : random.Next(Mathf.Max(3, caveChamberMinSize.y), Mathf.Max(3, caveChamberMaxSize.y) + 1);

            if (w + 2 >= size.x || d + 2 >= size.z) continue;

            int x = random.Next(1, size.x - w);
            int z = random.Next(1, size.z - d);
            int y = random.Next(0, size.y - height + 1);

            var footprint = new BoundsInt(new Vector3Int(x, y, z), new Vector3Int(w, height, d));

            // The blob is grown first and fitted to the rock afterwards. Demanding that the whole
            // rectangle be untouched put chambers almost nowhere once a couple of dozen rooms
            // were down - the gaps left between rooms are the wrong shape for a box, which is
            // exactly why a cave is being grown here and not another room.
            var cells = TrimToRock(GrowChamber(footprint, kind), y, height, caveSeparation);

            // Trimming can cut a blob in half, and half a chamber either side of a room is two
            // caves, not one.
            cells = LargestComponentOf(cells);

            if (cells.Count < floor) continue;

            CommitCave(cells, y, height, CaveShape.Chamber, register: true, floorThreshold: floor);
            placed++;
        }

        MarkLair();
    }

    /// <summary>
    /// Cellular automata on the chamber's footprint: random noise, then repeated smoothing where
    /// a cell takes the majority of its eight neighbours. Anything outside the footprint counts
    /// as rock, so the blob pulls in from the edges and the separation buffer stays honest.
    /// </summary>
    HashSet<Vector3Int> GrowChamber(BoundsInt footprint, Delver.Dungeon.CaveArchetype kind = null)
    {
        // A rounded lobed blob keeps a usable centre at large sizes; the CA noise below gives a
        // rougher, more broken outline that suits a small pocket. The size class chooses.
        if (kind != null) return kind.lobed ? GrowLobedChamber(footprint) : GrowChamberNoise(footprint);
        if (profile != null && profile.cavernsFirst) return GrowLobedChamber(footprint);
        return GrowChamberNoise(footprint);
    }

    HashSet<Vector3Int> GrowChamberNoise(BoundsInt footprint)
    {
        int w = footprint.size.x;
        int d = footprint.size.z;
        int y = footprint.position.y;

        bool[,] open = new bool[w, d];

        for (int i = 0; i < w; i++)
        for (int j = 0; j < d; j++)
            open[i, j] = random.NextDouble() < caveFillChance;

        for (int pass = 0; pass < caveSmoothIterations; pass++)
        {
            bool[,] next = new bool[w, d];

            for (int i = 0; i < w; i++)
            for (int j = 0; j < d; j++)
            {
                int rock = 0;

                for (int di = -1; di <= 1; di++)
                for (int dj = -1; dj <= 1; dj++)
                {
                    if (di == 0 && dj == 0) continue;

                    int ni = i + di;
                    int nj = j + dj;

                    if (ni < 0 || nj < 0 || ni >= w || nj >= d || !open[ni, nj]) rock++;
                }

                // Majority rules, and a cell surrounded four-and-four keeps what it was - which
                // is what stops the outline from oscillating between passes.
                next[i, j] = rock < 4 || (rock == 4 && open[i, j]);
            }

            open = next;
        }

        return LargestComponent(open, footprint.position.x, y, footprint.position.z);
    }

    /// <summary>
    /// Keeps only the biggest four-connected island.
    ///
    /// Four-connected, not eight: the party walks on the four grid directions, so two blobs
    /// touching at a corner are two caves however connected they look from above.
    /// </summary>
    HashSet<Vector3Int> LargestComponent(bool[,] open, int originX, int y, int originZ)
    {
        int w = open.GetLength(0);
        int d = open.GetLength(1);

        var seen = new bool[w, d];
        var best = new HashSet<Vector3Int>();
        var current = new List<Vector2Int>();
        var stack = new Stack<Vector2Int>();

        for (int i = 0; i < w; i++)
        for (int j = 0; j < d; j++)
        {
            if (!open[i, j] || seen[i, j]) continue;

            current.Clear();
            stack.Clear();
            stack.Push(new Vector2Int(i, j));
            seen[i, j] = true;

            while (stack.Count > 0)
            {
                var c = stack.Pop();
                current.Add(c);

                TryPush(c.x + 1, c.y);
                TryPush(c.x - 1, c.y);
                TryPush(c.x, c.y + 1);
                TryPush(c.x, c.y - 1);
            }

            if (current.Count > best.Count)
            {
                best.Clear();
                foreach (var c in current)
                    best.Add(new Vector3Int(originX + c.x, y, originZ + c.y));
            }
        }

        return best;

        void TryPush(int i, int j)
        {
            if (i < 0 || j < 0 || i >= w || j >= d) return;
            if (!open[i, j] || seen[i, j]) return;

            seen[i, j] = true;
            stack.Push(new Vector2Int(i, j));
        }
    }

    // ================================================================== tunnels

    /// <summary>
    /// Worms winding passages out of the edges of existing regions.
    ///
    /// Unlike a chamber these are not registered as regions: a tunnel is attached to whatever it
    /// wormed out of, so it is reachable by construction, and registering it would have the
    /// corridor pathfinder drive a straight hallway at it - exactly the thing a cave passage is
    /// not supposed to look like.
    /// </summary>
    void CarveCaveTunnels()
    {
        if (caveTunnelCount <= 0 || rooms.Count == 0) return;

        for (int i = 0; i < caveTunnelCount; i++)
        {
            // Nothing is written to the grid until a worm has earned its place, so a run that
            // dies immediately costs a retry rather than leaving a stub behind.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!FindTunnelMouth(out Vector3Int mouth, out Vector3Int outward)) break;

                var cells = new HashSet<Vector3Int>();

                // Headed away from whatever it emerged from, give or take. A worm started on a
                // random bearing spends its first step walking back into the room behind it.
                float heading = Mathf.Atan2(outward.z, outward.x) * Mathf.Rad2Deg
                    + ((float)random.NextDouble() - 0.5f) * 60f;

                Worm(mouth, heading, caveTunnelLength, caveTunnelRadius, caveTunnelWander,
                    caveTunnelTurn, cells, allowBranch: true);

                if (cells.Count < caveTunnelMinCells) continue;

                CommitCave(cells, mouth.y, Mathf.Clamp(caveTunnelHeight, 1, size.y),
                    CaveShape.Tunnel, register: false);
                break;
            }
        }
    }

    /// <summary>
    /// Finds a patch of rock touching an existing region, for a worm to start from, along with
    /// the direction that leads away from that region.
    /// </summary>
    bool FindTunnelMouth(out Vector3Int mouth, out Vector3Int outward)
    {
        mouth = default;
        outward = default;

        // Bounded retries rather than an exhaustive search: a dense dungeon may simply have no
        // rock left to worm into, and that is a reason to skip a tunnel, not to stall.
        for (int attempt = 0; attempt < 48; attempt++)
        {
            Room region = rooms[random.Next(rooms.Count)];

            // Reservoir-sample one floor cell, so this does not depend on the region carrying an
            // indexable list of them.
            Vector3Int pick = default;
            int seen = 0;

            foreach (var cell in region.FloorCells())
            {
                seen++;
                if (random.Next(seen) == 0) pick = cell;
            }

            if (seen == 0) continue;

            foreach (var dir in Shuffled(Directions))
            {
                Vector3Int candidate = pick + dir;

                if (!grid.InBounds(candidate)) continue;
                if (grid[candidate] != CellType.None) continue;

                mouth = candidate;
                outward = dir;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A Perlin worm. The heading is nudged by smooth noise sampled along the worm's own length,
    /// which is what makes the passage curve rather than jitter - a random turn per step gives
    /// you a drunkard's walk, and a drunkard's walk looks like a mistake.
    /// </summary>
    void Worm(Vector3Int start, float heading, int length, int radius, float wander, float turn,
        HashSet<Vector3Int> cells, bool allowBranch)
    {
        // Unity's PerlinNoise is a fixed function, so each worm needs its own place to read it
        // from or every tunnel in the dungeon curves identically.
        float noiseX = (float)random.NextDouble() * 512f;
        float noiseY = (float)random.NextDouble() * 512f;

        float fx = start.x + 0.5f;
        float fz = start.z + 0.5f;
        int y = start.y;
        Vector3Int previous = start;

        for (int step = 0; step < length; step++)
        {
            float n = Mathf.PerlinNoise(noiseX + step * wander, noiseY);
            heading += (n - 0.5f) * 2f * turn;

            // Swell and pinch along the run. Carving a constant radius gives a field that
            // contours into a perfectly circular pipe, which reads as a corridor with the corners
            // knocked off rather than as a cave.
            float bore = Mathf.PerlinNoise(noiseX * 0.61f + step * wander * 1.9f, noiseY + 37.1f);
            int stepRadius = radius + (bore > 1f - caveTunnelBulge ? 1 : 0);

            float rad = heading * Mathf.Deg2Rad;
            fx += Mathf.Cos(rad);
            fz += Mathf.Sin(rad);

            var centre = new Vector3Int(Mathf.FloorToInt(fx), y, Mathf.FloorToInt(fz));

            // Leave a one-cell margin: a passage carved hard against the grid edge has nothing
            // on the far side for the wall pass to seal against.
            if (centre.x < 1 || centre.z < 1 || centre.x >= size.x - 1 || centre.z >= size.z - 1) return;

            if (centre == previous) continue;

            // The worm walks in a continuous heading, so one step can shift x and z at once.
            // Carving only the cells it lands on leaves a chain joined at the corners - which
            // the party cannot walk down and the reachability pass then deletes. Squaring off
            // the diagonal is what makes the passage a passage.
            if (centre.x != previous.x && centre.z != previous.z)
            {
                if (!CarveDisc(new Vector3Int(previous.x, y, centre.z), stepRadius, cells)) return;
            }

            if (!CarveDisc(centre, stepRadius, cells)) return;

            previous = centre;

            if (allowBranch && random.NextDouble() < caveTunnelBranchChance)
            {
                float side = heading + (random.Next(2) == 0 ? 70f : -70f);
                Worm(centre, side, Mathf.Max(4, length / 2), radius, wander, turn, cells,
                    allowBranch: false);
            }
        }
    }

    /// <summary>
    /// Cuts one cross-section of a worm. Returns false once the worm breaks into somewhere that
    /// already exists - it has arrived, and driving on would bore straight through a room.
    /// </summary>
    bool CarveDisc(Vector3Int centre, int radius, HashSet<Vector3Int> cells)
    {
        bool broke = false;

        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            if (dx * dx + dz * dz > radius * radius + radius) continue;

            var cell = new Vector3Int(centre.x + dx, centre.y, centre.z + dz);
            if (!grid.InBounds(cell)) continue;

            CellType here = grid[cell];

            if (here == CellType.Cave) continue;

            if (here != CellType.None)
            {
                // Room headroom, a corridor, a staircase - all of them mean the worm is through.
                broke = true;
                continue;
            }

            // Never undercut a staircase: its ramp geometry assumes solid rock beside and below.
            if (TouchesStairs(cell)) return false;

            cells.Add(cell);
        }

        return !broke;
    }

    bool TouchesStairs(Vector3Int cell)
    {
        if (stairUnderFloors.Contains(cell)) return true;

        for (int dy = -1; dy <= 1; dy++)
        {
            var c = cell + new Vector3Int(0, dy, 0);
            if (grid.InBounds(c) && grid[c] == CellType.Stairs) return true;
        }

        return false;
    }

    // ================================================================== rivers

    /// <summary>
    /// Cuts a watercourse across the finished layout.
    ///
    /// This runs after the corridors precisely so it can cross them. The bed is sunk rather than
    /// walled off, so the water is an obstacle to wade and light up rather than a barrier - a
    /// river the party could not cross would split the dungeon, and the reachability pass would
    /// then delete whichever half the entrance was not in.
    /// </summary>
    void CarveRivers()
    {
        if (riverCount <= 0) return;

        // Doorframes stand on the floor line, so a doorway over a sunken bed would hang in mid
        // air. The river goes around them.
        var doorCells = new HashSet<Vector3Int>();
        foreach (var (cell, dir) in doorways)
        {
            doorCells.Add(cell);
            doorCells.Add(cell + dir);
        }

        for (int i = 0; i < riverCount; i++)
        {
            if (rooms.Count == 0) return;

            // A river runs at one level, and it has to be the level the dungeon actually lives
            // on. Taking it from a random region put channels on upper storeys where there was
            // nothing but rock to cut - so the water joined nothing, and the reachability pass
            // deleted the whole river again.
            int y = BusiestLevel();

            // Enter from a random edge and head roughly across.
            bool alongX = random.Next(2) == 0;
            Vector3Int start;
            float heading;

            if (alongX)
            {
                bool fromMinus = random.Next(2) == 0;
                start = new Vector3Int(fromMinus ? 1 : size.x - 2, y, random.Next(1, size.z - 1));
                heading = fromMinus ? 0f : 180f;
            }
            else
            {
                bool fromMinus = random.Next(2) == 0;
                start = new Vector3Int(random.Next(1, size.x - 1), y, fromMinus ? 1 : size.z - 2);
                heading = fromMinus ? 90f : 270f;
            }

            FlowRiver(start, heading, doorCells);
        }
    }

    /// <summary>The storey carrying the most floor. Where a river will meet the most dungeon.</summary>
    int BusiestLevel()
    {
        int best = 0;
        int bestCount = -1;

        for (int y = 0; y < size.y; y++)
        {
            int count = 0;

            for (int x = 0; x < size.x; x++)
            for (int z = 0; z < size.z; z++)
                if (HasFloor(grid[new Vector3Int(x, y, z)])) count++;

            if (count > bestCount)
            {
                bestCount = count;
                best = y;
            }
        }

        return best;
    }

    void FlowRiver(Vector3Int start, float heading, HashSet<Vector3Int> doorCells)
    {
        float noiseX = (float)random.NextDouble() * 512f;
        float fx = start.x + 0.5f;
        float fz = start.z + 0.5f;
        int y = start.y;
        Vector3Int previous = start;

        int maxSteps = size.x + size.z;

        for (int step = 0; step < maxSteps; step++)
        {
            float n = Mathf.PerlinNoise(noiseX + step * 0.09f, 0.5f);
            heading += (n - 0.5f) * 2f * riverMeander;

            float rad = heading * Mathf.Deg2Rad;
            fx += Mathf.Cos(rad);
            fz += Mathf.Sin(rad);

            var centre = new Vector3Int(Mathf.FloorToInt(fx), y, Mathf.FloorToInt(fz));

            // Out the far side is a finished river, not a failed one.
            if (centre.x < 1 || centre.z < 1 || centre.x >= size.x - 1 || centre.z >= size.z - 1) return;

            if (centre == previous) continue;

            // Square off diagonal steps, or the channel is a chain of cells touching at the
            // corners - not water you can wade along, and not water the flood fill can follow.
            if (centre.x != previous.x && centre.z != previous.z)
                FloodCell(new Vector3Int(previous.x, y, centre.z), doorCells);

            FloodCell(centre, doorCells);

            previous = centre;
        }
    }

    /// <summary>Turns one cross-section of the channel to water.</summary>
    void FloodCell(Vector3Int centre, HashSet<Vector3Int> doorCells)
    {
        for (int dx = -riverRadius; dx <= riverRadius; dx++)
        for (int dz = -riverRadius; dz <= riverRadius; dz++)
        {
            if (dx * dx + dz * dz > riverRadius * riverRadius + riverRadius) continue;

            var cell = new Vector3Int(centre.x + dx, centre.y, centre.z + dz);
            if (!grid.InBounds(cell)) continue;

            CellType here = grid[cell];

            // Headroom is above the water line, and a staircase's ramp assumes solid ground.
            if (here == CellType.Stairs || here == CellType.Room || here == CellType.CaveOpen) continue;
            if (doorCells.Contains(cell)) continue;
            if (TouchesStairs(cell)) continue;

            // Rock included: the river carves its own gorge where nothing was built, which is
            // what stops it from being a puddle that only exists indoors.
            grid[cell] = CellType.Water;
        }
    }

    // ================================================================== commit / prune

    /// <summary>
    /// Keeps only the carved cells that sit in open rock, margin included, over the cave's full
    /// height. This is what lets a chamber nestle into an irregular gap between rooms instead of
    /// needing a clear rectangle.
    /// </summary>
    HashSet<Vector3Int> TrimToRock(HashSet<Vector3Int> cells, int y, int height, int margin)
    {
        var kept = new HashSet<Vector3Int>();

        foreach (var cell in cells)
        {
            bool clear = true;

            for (int dx = -margin; dx <= margin && clear; dx++)
            for (int dz = -margin; dz <= margin && clear; dz++)
            for (int level = 0; level < height && clear; level++)
            {
                var c = new Vector3Int(cell.x + dx, y + level, cell.z + dz);

                // The grid edge counts as occupied: a chamber carved hard against it has nothing
                // on the far side for the wall pass to seal against.
                if (!grid.InBounds(c) || grid[c] != CellType.None) clear = false;
            }

            if (clear) kept.Add(cell);
        }

        return kept;
    }

    /// <summary>Largest four-connected island of an arbitrary set of same-level cells.</summary>
    HashSet<Vector3Int> LargestComponentOf(HashSet<Vector3Int> cells)
    {
        if (cells.Count == 0) return cells;

        int minX = int.MaxValue, maxX = int.MinValue;
        int minZ = int.MaxValue, maxZ = int.MinValue;
        int y = 0;

        foreach (var cell in cells)
        {
            minX = Mathf.Min(minX, cell.x);
            maxX = Mathf.Max(maxX, cell.x);
            minZ = Mathf.Min(minZ, cell.z);
            maxZ = Mathf.Max(maxZ, cell.z);
            y = cell.y;
        }

        var open = new bool[maxX - minX + 1, maxZ - minZ + 1];
        foreach (var cell in cells) open[cell.x - minX, cell.z - minZ] = true;

        return LargestComponent(open, minX, y, minZ);
    }

    /// <summary>Writes a set of carved cells into the grid and records the region.</summary>
    void CommitCave(HashSet<Vector3Int> cells, int y, int height, CaveShape shape, bool register,
        int floorThreshold = 0)
    {
        if (cells.Count == 0) return;

        foreach (var cell in cells)
        {
            grid[cell] = CellType.Cave;

            for (int level = 1; level < height; level++)
            {
                var above = cell + Vector3Int.up * level;
                if (grid.InBounds(above) && grid[above] == CellType.None)
                    grid[above] = CellType.CaveOpen;
            }
        }

        var min = new Vector3Int(int.MaxValue, y, int.MaxValue);
        var max = new Vector3Int(int.MinValue, y + height, int.MinValue);

        foreach (var cell in cells)
        {
            min.x = Mathf.Min(min.x, cell.x);
            min.z = Mathf.Min(min.z, cell.z);
            max.x = Mathf.Max(max.x, cell.x + 1);
            max.z = Mathf.Max(max.z, cell.z + 1);
        }

        var bounds = new BoundsInt(min, max - min);

        var region = new CaveRegion
        {
            Shape = shape,
            Cells = cells,
            Bounds = bounds,
            Anchor = CentralCell(cells, bounds),
            FloorThreshold = floorThreshold > 0
                ? floorThreshold
                : (shape == CaveShape.Chamber ? caveChamberMinCells : caveTunnelMinCells),
        };

        caves.Add(region);

        if (register) rooms.Add(new Room(cells, bounds, region.Anchor));
    }

    /// <summary>
    /// The carved cell nearest the region's centre. Scans in grid order rather than iterating the
    /// set, so the answer does not depend on hash ordering - two clients on the same seed must
    /// put the corridor mouth in the same place.
    /// </summary>
    Vector3Int CentralCell(HashSet<Vector3Int> cells, BoundsInt bounds)
    {
        float cx = bounds.xMin + bounds.size.x * 0.5f;
        float cz = bounds.zMin + bounds.size.z * 0.5f;

        Vector3Int best = default;
        float bestDistance = float.MaxValue;

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        for (int z = bounds.zMin; z < bounds.zMax; z++)
        {
            var cell = new Vector3Int(x, bounds.yMin, z);
            if (!cells.Contains(cell)) continue;

            float dx = x + 0.5f - cx;
            float dz = z + 0.5f - cz;
            float distance = dx * dx + dz * dz;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = cell;
            }
        }

        return best;
    }

    /// <summary>Flags the biggest chamber as the lair.</summary>
    void MarkLair()
    {
        CaveRegion best = null;

        foreach (var cave in caves)
        {
            if (cave.Shape != CaveShape.Chamber) continue;
            if (best == null || cave.CellCount > best.CellCount) best = cave;
        }

        foreach (var cave in caves) cave.IsLair = false;
        if (best != null) best.IsLair = true;
    }

    /// <summary>
    /// Pushes caves back from every staircase.
    ///
    /// Chambers are grown before the corridors are routed, so a flight can be pathfound to run
    /// right alongside one. Holding the stair cell open in the density field is not enough on its
    /// own: the rock skin is drawn on the CAVE's boundary, so a cave one cell away still puts
    /// geometry inside the flight. The cave itself has to give ground.
    /// </summary>
    void ClearCavesNearStairs()
    {
        var stairs = new HashSet<Vector3Int>();

        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (grid[cell] == CellType.Stairs) stairs.Add(cell);
        }

        if (stairs.Count == 0) return;

        int clearance = Mathf.Max(1, caveStairClearance);
        int removed = 0;

        var doomed = new List<Vector3Int>();

        for (int i = caves.Count - 1; i >= 0; i--)
        {
            var cave = caves[i];
            doomed.Clear();

            foreach (var cell in cave.Cells)
            {
                bool near = false;

                for (int dx = -clearance; dx <= clearance && !near; dx++)
                // Include the entire cave column: its ceiling can intersect an upper flight
                // even when the cave floor is several levels below it.
                for (int dy = -1; dy <= cave.Bounds.size.y && !near; dy++)
                for (int dz = -clearance; dz <= clearance && !near; dz++)
                    if (stairs.Contains(cell + new Vector3Int(dx, dy, dz))) near = true;

                if (near) doomed.Add(cell);
            }

            // Mutated in place, never swapped for a new set: a registered chamber's Room holds
            // this very set, and handing the region a fresh one would leave the graph reading
            // cells that are solid rock again.
            foreach (var cell in doomed)
            {
                cave.Cells.Remove(cell);
                // Corridor routing may have replaced this former cave floor with built space.
                if (grid[cell] != CellType.Cave) continue;
                grid[cell] = CellType.None;
                removed++;

                for (int level = 1; level < size.y; level++)
                {
                    var above = cell + Vector3Int.up * level;
                    if (!grid.InBounds(above) || grid[above] != CellType.CaveOpen) break;
                    grid[above] = CellType.None;
                }
            }

            // A cave trimmed to a remnant is not a smaller cave, it is a sealed pocket - too
            // small to have kept a way in, and the reachability pass cannot tell because the grid
            // still reads it as connected to what surrounds it. Erase whatever is left.
            int floor = cave.FloorThreshold;

            if (cave.Cells.Count < floor)
            {
                foreach (var cell in cave.Cells)
                {
                    if (grid[cell] != CellType.Cave) continue;
                    grid[cell] = CellType.None;

                    for (int level = 1; level < size.y; level++)
                    {
                        var above = cell + Vector3Int.up * level;
                        if (!grid.InBounds(above) || grid[above] != CellType.CaveOpen) break;
                        grid[above] = CellType.None;
                    }
                }

                cave.Cells.Clear();
                caves.RemoveAt(i);
                continue;
            }

            cave.Anchor = CentralCell(cave.Cells, cave.Bounds);
        }

        // The registered regions share these sets, so anything emptied has to leave the graph too.
        rooms.RemoveAll(r => r.IsCave && r.caveCells.Count == 0);

        foreach (var room in rooms)
            if (room.IsCave && !room.caveCells.Contains(room.anchor))
                foreach (var cell in room.caveCells) { room.anchor = cell; break; }

        if (removed > 0)
            Debug.Log($"Cleared {removed} cave cell(s) within {clearance} of a staircase.", this);
    }

    /// <summary>Draws a cavern size class by weight, or null to use the single min/max band.</summary>
    Delver.Dungeon.CaveArchetype PickCaveArchetype()
    {
        var set = profile != null ? profile.caveArchetypes : null;
        if (set == null || set.Length == 0) return null;

        if (caveQuota.Count > 0)
        {
            var owed = caveQuota[0];
            caveQuota.RemoveAt(0);
            return owed;
        }

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

    /// <summary>Drops cave cells the reachability pass carved back out, and empty regions with them.</summary>
    void PruneCaves(HashSet<Vector3Int> reachable)
    {
        for (int i = caves.Count - 1; i >= 0; i--)
        {
            var cave = caves[i];

            // In place for the same reason as ClearCavesNearStairs: the Room registered for a
            // chamber shares this set.
            cave.Cells.RemoveWhere(cell => !reachable.Contains(cell));

            if (cave.Cells.Count == 0)
            {
                caves.RemoveAt(i);
                continue;
            }

            cave.Anchor = CentralCell(cave.Cells, cave.Bounds);
        }

        MarkLair();
    }

    // ================================================================== dressing

    /// <summary>Lays the water surface over every river cell that survived.</summary>
    void SpawnWater()
    {
        if (waterPrefab == null) return;

        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        for (int z = 0; z < size.z; z++)
        {
            var cell = new Vector3Int(x, y, z);
            if (grid[cell] != CellType.Water) continue;

            // Water assets have their own mesh dimensions and pivot. Fit the actual rendered
            // surface to the channel cell instead of treating it as a floor tile.
            var water = Spawn(waterPrefab, Vector3.zero, Quaternion.identity);
            var renderers = water.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
            {
                water.transform.position = CellCentre(cell)
                    + Vector3.up * (FloorSurfaceOffset - riverDepth + riverSurfaceHeight);
                continue;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.x > 0.001f && bounds.size.z > 0.001f)
            {
                Vector3 scale = water.transform.localScale;
                scale.x *= WorldUnitSize / bounds.size.x;
                scale.z *= WorldUnitSize / bounds.size.z;
                water.transform.localScale = scale;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Vector3 target = CellCentre(cell)
                + Vector3.up * (FloorSurfaceOffset - riverDepth + riverSurfaceHeight);
            water.transform.position += target - new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);

            foreach (var renderer in renderers)
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>Drops the lair anchor and publishes its point, so encounters can find the hoard.</summary>
    void SpawnLairMarkers()
    {
        LairPoint = Vector3.zero;

        foreach (var cave in caves)
        {
            if (!cave.IsLair) continue;

            Vector3 point = CellCentre(cave.Anchor) + new Vector3(0f, FloorSurfaceOffset, 0f);
            LairPoint = point;

            if (lairMarkerPrefab != null)
                SpawnGrounded(lairMarkerPrefab, point, Quaternion.identity);

            return;
        }
    }

    bool IsCaveCell(CellType t)
    {
        return t == CellType.Cave || t == CellType.CaveOpen || t == CellType.Water;
    }

    /// <summary>
    /// Whether a new course of cave wall starts at this cell.
    ///
    /// A cave wall mesh can be several storeys tall - the Synty pieces are two - so walling every
    /// level the way built rooms are walled stacks them into a cliff far higher than the cave.
    /// Only the cell at the bottom of each span gets a piece; the rest are already covered by it.
    /// </summary>
    bool CaveWallCourseStartsHere(Vector3Int cell)
    {
        int span = Mathf.Max(1, caveWallSpansLevels);
        if (span == 1) return true;

        int levels = 0;
        Vector3Int c = cell;

        while (grid.InBounds(c) && grid[c] == CellType.CaveOpen)
        {
            c += Vector3Int.down;
            levels++;
        }

        return levels % span == 0;
    }

    GameObject CaveFloorPrefab()
    {
        GameObject pick = PickPrefab(caveFloorPrefabs);
        return pick != null ? pick : FloorPrefab();
    }

    GameObject RiverBedPrefab()
    {
        GameObject pick = PickPrefab(riverBedPrefabs);
        return pick != null ? pick : CaveFloorPrefab();
    }

    /// <summary>The piece stood in the wall where the dungeon opens into a cave.</summary>
    GameObject CaveEntrancePrefab()
    {
        return PickPrefab(caveEntrancePrefabs);
    }

    GameObject WallPrefabFor(CellType t)
    {
        if (IsCaveCell(t))
        {
            GameObject pick = PickPrefab(caveWallPrefabs);
            if (pick != null) return pick;
        }

        return PickPrefab(wallPrefabs);
    }

    GameObject CeilingPrefabFor(CellType t)
    {
        if (IsCaveCell(t) && caveCeilingPrefab != null) return caveCeilingPrefab;

        return ceilingPrefab;
    }

    GameObject PickPrefab(GameObject[] set)
    {
        if (set == null || set.Length == 0) return null;

        return set[random.Next(set.Length)];
    }

    /// <summary>A copy of the direction list in a random order, so worms do not all favour +X.</summary>
    Vector3Int[] Shuffled(Vector3Int[] source)
    {
        var copy = (Vector3Int[])source.Clone();

        for (int i = copy.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }

    /// <summary>Copies the cave section of a profile over the inline fields.</summary>
    void ApplyCaveProfile(Delver.Dungeon.DungeonProfile source)
    {
        if (source == null) return;

        caveChamberCount = source.caveChamberCount;
        caveChamberMinSize = source.caveChamberMinSize;
        caveChamberMaxSize = source.caveChamberMaxSize;
        caveChamberHeight = source.caveChamberHeight;
        caveFillChance = source.caveFillChance;
        caveSmoothIterations = source.caveSmoothIterations;
        caveChamberMinCells = source.caveChamberMinCells;
        caveSeparation = source.caveSeparation;

        caveTunnelCount = source.caveTunnelCount;
        caveTunnelLength = source.caveTunnelLength;
        caveTunnelRadius = source.caveTunnelRadius;
        caveWallSpansLevels = source.caveWallSpansLevels;
        caveTunnelMinCells = source.caveTunnelMinCells;
        caveTunnelHeight = source.caveTunnelHeight;
        caveTunnelWander = source.caveTunnelWander;
        caveTunnelTurn = source.caveTunnelTurn;
        caveTunnelBranchChance = source.caveTunnelBranchChance;

        riverCount = source.riverCount;
        riverRadius = source.riverRadius;
        riverDepth = source.riverDepth;
        riverSurfaceHeight = source.riverSurfaceHeight;
        riverMeander = source.riverMeander;

        torchesInCaves = source.torchesInCaves;

        caveSurfaceMesh = source.caveSurfaceMesh;
        caveVoxelsPerCell = source.caveVoxelsPerCell;
        caveSurfaceSmoothing = source.caveSurfaceSmoothing;
        caveSurfaceNoise = source.caveSurfaceNoise;
        caveSurfaceNoiseScale = source.caveSurfaceNoiseScale;
        caveFloorFlattenHeight = source.caveFloorFlattenHeight;
        caveFlatShading = source.caveFlatShading;
        caveSurfaceDoubleSided = source.caveSurfaceDoubleSided;
        caveScatterPerCell = source.caveScatterPerCell;
        caveOpenMargin = source.caveOpenMargin;
        caveStairClearance = source.caveStairClearance;
        caveTunnelBulge = source.caveTunnelBulge;
        caveFormationSpacing = source.caveFormationSpacing;
        caveFormationEdgeBias = source.caveFormationEdgeBias;

        if (source.caveScatterPrefabs != null && source.caveScatterPrefabs.Length > 0)
            caveScatterPrefabs = source.caveScatterPrefabs;

        if (source.caveStalagmitePrefabs != null && source.caveStalagmitePrefabs.Length > 0)
            caveStalagmitePrefabs = source.caveStalagmitePrefabs;

        if (source.caveStalactitePrefabs != null && source.caveStalactitePrefabs.Length > 0)
            caveStalactitePrefabs = source.caveStalactitePrefabs;
        if (source.caveSurfaceMaterial != null) caveSurfaceMaterial = source.caveSurfaceMaterial;

        if (source.caveFloorPrefabs != null && source.caveFloorPrefabs.Length > 0)
            caveFloorPrefabs = source.caveFloorPrefabs;

        if (source.caveWallPrefabs != null && source.caveWallPrefabs.Length > 0)
            caveWallPrefabs = source.caveWallPrefabs;

        if (source.caveEntrancePrefabs != null && source.caveEntrancePrefabs.Length > 0)
            caveEntrancePrefabs = source.caveEntrancePrefabs;

        if (source.caveCeilingPrefab != null) caveCeilingPrefab = source.caveCeilingPrefab;

        if (source.riverBedPrefabs != null && source.riverBedPrefabs.Length > 0)
            riverBedPrefabs = source.riverBedPrefabs;

        if (source.waterPrefab != null) waterPrefab = source.waterPrefab;
        if (source.lairMarkerPrefab != null) lairMarkerPrefab = source.lairMarkerPrefab;
    }
}
