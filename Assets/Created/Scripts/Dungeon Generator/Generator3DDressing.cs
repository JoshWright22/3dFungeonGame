using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dresses the built rooms, using the densities and placement habits measured off Synty's own
/// Demo.unity rather than invented ones.
///
/// Parsing all 3,462 prefab instances in that scene gave two rules worth encoding, and one that
/// corrected an assumption:
///
///  * WHAT GOES WHERE. Props sit against walls (median 2.2m from one, 55% within 2.5m) and
///    pillars almost touch them (median 0.3m, 92% within 2.5m). Loose rock and stalagmites do
///    NOT - median 16.9m and 12.2m respectively - because they belong to the caves, not to the
///    built rooms. So the dungeon gets barrels, torches and pillars along its walls, and the
///    rock stays where <see cref="GrowCaveFormations"/> puts it.
///
///  * HOW MUCH. Per 5x5 floor tile the demo carries 1.20 props, 0.53 pillars and 0.44 rubble.
///    Those are showcase numbers - a scene built to photograph, with no one walking through it -
///    so they are the ceiling here rather than the default.
///
///  * The thing that looked like jitter is not. Walls in the demo have a median off-grid offset
///    of 0.00m and a maximum of exactly 2.50m: they are on half-cell boundaries, not nudged at
///    random. Nothing here jitters structure.
/// </summary>
public partial class Generator3D
{
    readonly HashSet<Collider> groundSupports = new HashSet<Collider>();

    // Use structural colliders only: decorations must never become support for later props.
    GameObject SpawnGrounded(GameObject prefab, Vector3 expected, Quaternion rotation)
    {
        if (prefab == null) return null;
        Physics.SyncTransforms();
        var ray = new Ray(expected + Vector3.up * 2f, Vector3.down);
        RaycastHit best = default;
        float distance = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(ray, 4f, ~0, QueryTriggerInteraction.Ignore))
            if (groundSupports.Contains(hit.collider)
                && hit.normal.y >= 0.7f && hit.distance < distance)
            {
                best = hit;
                distance = hit.distance;
            }
        if (float.IsPositiveInfinity(distance)) return null;
        var instance = Spawn(prefab, best.point, rotation);
        AlignSurface(instance, best.point.y, false);
        return instance;
    }

    // Measure after rotation and scaling, including every mesh in a compound prefab.
    static void AlignSurface(GameObject instance, float height, bool ceiling)
    {
        var renderers = instance.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        instance.transform.position += Vector3.up * (height - (ceiling ? bounds.max.y : bounds.min.y));
    }

    [Header("Room dressing")]
    [Tooltip("Props stood against room walls - barrels, torch sticks, chains, crates. The demo leans on SM_Prop_Barrel_01 and SM_Prop_TorchStick_01 above all others.")]
    [SerializeField]
    GameObject[] wallPropPrefabs;

    [Tooltip("Pillars set against the walls of larger rooms. In the demo these hug the wall almost exactly - a median of 0.3m off it.")]
    [SerializeField]
    GameObject[] pillarPrefabs;

    [Tooltip("Loose debris scattered across room floors - pebbles, broken brick, planks.")]
    [SerializeField]
    GameObject[] rubblePrefabs;

    [Tooltip("Chance per wall-facing floor cell of standing a prop against that wall. The demo runs about 1.2 props per floor tile, but it is a showcase scene - half that reads as lived-in without being an obstacle course.")]
    [Range(0f, 1f)]
    [SerializeField]
    float wallPropChance = 0.3f;

    [Tooltip("Chance per wall-facing cell of a pillar. Only rooms at least as large as the threshold below get them.")]
    [Range(0f, 1f)]
    [SerializeField]
    float pillarChance = 0.14f;

    [Tooltip("Floor cells a room needs before it is grand enough for pillars. A broom cupboard with a colonnade reads as a mistake.")]
    [Min(1)]
    [SerializeField]
    int pillarMinRoomArea = 40;

    [Tooltip("Chance per floor cell of loose rubble anywhere on the floor, wall or no wall.")]
    [Range(0f, 1f)]
    [SerializeField]
    float rubbleChance = 0.18f;

    [Tooltip("How far a prop is inset from the wall face it stands against.")]
    [Range(0f, 2f)]
    [SerializeField]
    float wallPropInset = 0.55f;

    /// <summary>
    /// Stands props, pillars and rubble in the built rooms.
    ///
    /// Runs after the walls exist, because a prop's whole placement rule is which faces of its
    /// cell are walled. Caves are excluded - they get rock and formations instead.
    /// </summary>
    void DressRooms()
    {
        bool anyProps = wallPropPrefabs != null && wallPropPrefabs.Length > 0;
        bool anyPillars = pillarPrefabs != null && pillarPrefabs.Length > 0;
        bool anyRubble = rubblePrefabs != null && rubblePrefabs.Length > 0;
        if (!anyProps && !anyPillars && !anyRubble) return;

        // Doorways and cave mouths are the ways through. Blocking one with a barrel turns a
        // corridor into a dead end that looks passable.
        var blocked = new HashSet<(Vector3Int, Vector3Int)>();
        foreach (var (cell, dir) in doorways)
        {
            blocked.Add((cell, dir));
            blocked.Add((cell + dir, -dir));
        }

        foreach (var room in rooms)
        {
            if (room.IsCave) continue;

            // A room with a kit of its own is furnished by FurnishRooms instead.
            if (room.archetype != null && room.archetype.kit != null) continue;

            bool grand = room.FloorArea >= pillarMinRoomArea;

            // Each kind of room dresses itself. A cellar is dense with clutter and has no
            // pillars; a great hall is colonnaded and comparatively bare between them. A
            // negative override on the archetype means "use the profile default".
            float propChance = wallPropChance;
            float debrisChance = rubbleChance;
            float columnChance = pillarChance;

            if (room.archetype != null)
            {
                if (room.archetype.wallPropChance >= 0f) propChance = room.archetype.wallPropChance;
                if (room.archetype.rubbleChance >= 0f) debrisChance = room.archetype.rubbleChance;
                if (room.archetype.pillarChance >= 0f) columnChance = room.archetype.pillarChance;
            }

            foreach (var cell in room.FloorCells())
            {
                if (!grid.InBounds(cell)) continue;
                if (grid[cell] != CellType.BottomFloorRoom) continue;

                Vector3 centre = CellCentre(cell);
                float floor = FloorSurfaceOffset;

                // Which faces of this cell are walled, and therefore have something to lean on.
                var walls = new List<Vector3Int>();
                foreach (var dir in Directions)
                {
                    var n = cell + dir;
                    if (grid.InBounds(n) && IsWalkable(grid[n])) continue;
                    if (blocked.Contains((cell, dir))) continue;

                    walls.Add(dir);
                }

                if (walls.Count > 0)
                {
                    Vector3Int face = walls[random.Next(walls.Count)];

                    if (anyPillars && grand && random.NextDouble() < columnChance)
                        StandAgainstWall(PickPrefab(pillarPrefabs), centre, face, floor, 0.25f, 0.6f);
                    else if (anyProps && random.NextDouble() < propChance)
                        StandAgainstWall(PickPrefab(wallPropPrefabs), centre, face, floor, wallPropInset, 1.4f);
                }

                if (anyRubble && random.NextDouble() < debrisChance)
                {
                    var spot = centre + new Vector3(
                        ((float)random.NextDouble() - 0.5f) * (WorldUnitSize - 1.2f),
                        floor,
                        ((float)random.NextDouble() - 0.5f) * (WorldUnitSize - 1.2f));

                    MakeWalkThrough(SpawnGrounded(PickPrefab(rubblePrefabs), spot,
                        Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f)));
                }
            }
        }
    }

    /// <summary>
    /// Sets a piece against one face of a cell, facing back into the room.
    ///
    /// <paramref name="slide"/> is how far it may drift along the wall, so a row of barrels does
    /// not come out looking like it was laid on the same grid the walls were.
    /// </summary>
    void StandAgainstWall(GameObject prefab, Vector3 cellCentre, Vector3Int dir, float floorOffset,
        float inset, float slide)
    {
        if (prefab == null) return;

        Vector3 outward = new Vector3(dir.x, 0f, dir.z);
        Vector3 along = Vector3.Cross(outward, Vector3.up);

        Vector3 position = cellCentre
            + outward * (HalfWorldUnit - inset)
            + along * ((float)random.NextDouble() - 0.5f) * slide
            + new Vector3(0f, floorOffset, 0f);

        // Facing back into the room, give or take - a barrel shoved against a wall is not square
        // to it, and a torch bracket only reads right if it is.
        float yaw = Quaternion.LookRotation(-outward, Vector3.up).eulerAngles.y
            + ((float)random.NextDouble() - 0.5f) * 14f;

        SpawnGrounded(prefab, position, Quaternion.Euler(0f, yaw, 0f));
    }

    /// <summary>Copies the dressing section of a profile over the inline fields.</summary>
    void ApplyDressingProfile(Delver.Dungeon.DungeonProfile source)
    {
        if (source == null) return;

        wallPropChance = source.wallPropChance;
        pillarChance = source.pillarChance;
        pillarMinRoomArea = source.pillarMinRoomArea;
        rubbleChance = source.rubbleChance;

        if (source.wallPropPrefabs != null && source.wallPropPrefabs.Length > 0)
            wallPropPrefabs = source.wallPropPrefabs;

        if (source.pillarPrefabs != null && source.pillarPrefabs.Length > 0)
            pillarPrefabs = source.pillarPrefabs;

        if (source.rubblePrefabs != null && source.rubblePrefabs.Length > 0)
            rubblePrefabs = source.rubblePrefabs;
    }
}
