using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chasm caves, after the demo's Goblin Cave.
///
/// The demo's goblin cave is built in three layers. A ledge of cracked rock at walking height
/// where you come in. A pit floor seven or eight metres down, also cracked rock, with boulder
/// piles and bones on it. And between them, rock platforms - six-metre pillars standing on the pit
/// floor with their flat tops at ledge height - joined to the ledge and each other by rope bridges
/// and mine-track trestles. Nothing floats: every pillar and trestle stands on the pit floor.
///
/// Here the same three layers come out of the grid. Cells by the cave's mouths stay as the ledge;
/// the rest of the floor is dropped one storey to become the pit; islands of graded platforms
/// stand in the pit with their tops level with the ledge; and a spanning tree of bridges joins
/// every island and every ledge into one walkable network.
/// </summary>
public partial class Generator3D
{
    /// <summary>Caves built as chasms this generation.</summary>
    readonly HashSet<CaveRegion> chasms = new HashSet<CaveRegion>();

    /// <summary>Pit floor cells - the storey a chasm's floor was dropped to.</summary>
    readonly HashSet<Vector3Int> chasmCells = new HashSet<Vector3Int>();

    /// <summary>Pit cells an island stands over: the only pit ground a prop or scene may use.</summary>
    readonly HashSet<Vector3Int> chasmGround = new HashSet<Vector3Int>();

    /// <summary>The walking height of each chasm - its ledge and every island top.</summary>
    readonly Dictionary<CaveRegion, float> chasmPlateau = new Dictionary<CaveRegion, float>();

    /// <summary>Each cavern's dressing theme: lush (crystal, fungus) or inhabited (goblins).</summary>
    readonly Dictionary<CaveRegion, bool> caveLush = new Dictionary<CaveRegion, bool>();

    bool IsLushCave(CaveRegion cave) => caveLush.TryGetValue(cave, out bool lush) && lush;

    /// <summary>Something a bridge can land on: a ledge or an island, at plateau height.</summary>
    class ChasmNode
    {
        public Vector3 Centre;
        public List<Vector3> Points = new List<Vector3>();   // sample points on its top, for nearest-pair bridging
    }

    /// <summary>
    /// Picks the caves to build as chasms and drops their floors into a pit. Runs once the layout
    /// is final and before any geometry.
    /// </summary>
    void PlanChasms()
    {
        chasms.Clear();
        chasmCells.Clear();
        chasmGround.Clear();
        chasmPlateau.Clear();
        caveLush.Clear();

        var chambers = caves.FindAll(c => c.Shape == CaveShape.Chamber);
        chambers.Sort((a, b) => b.CellCount.CompareTo(a.CellCount));
        int budget = Mathf.Max(1, chambers.Count / 2);

        if (UsingCaveKit && profile != null && profile.chasmCaves)
            foreach (var cave in chambers)
            {
                if (chasms.Count >= budget || cave.CellCount < profile.chasmMinCells) continue;
                if (DigPit(cave)) chasms.Add(cave);
            }

        // Chasms are the goblins' caves, as in the demo; the floored caves alternate.
        int index = 0;
        foreach (var cave in caves)
        {
            if (cave.Shape != CaveShape.Chamber) continue;
            caveLush[cave] = !chasms.Contains(cave) && index++ % 2 == 0;
        }
    }

    /// <summary>
    /// Drops every floor cell that is not ledge one storey, into rock that was solid. Returns
    /// false, changing nothing, when too little of the cave can be dug to be worth a pit.
    /// </summary>
    bool DigPit(CaveRegion cave)
    {
        var floor = new List<Vector3Int>();
        foreach (var cell in cave.Cells) if (grid[cell] == CellType.Cave) floor.Add(cell);
        if (floor.Count == 0) return false;

        // The ledge: every cell within a cell of a way in, so a corridor always opens onto ground.
        var ledge = new HashSet<Vector3Int>();
        foreach (var cell in floor)
            foreach (var dir in Directions)
            {
                if (dir.y != 0) continue;
                if (!caveEntrances.Contains((cell - dir, dir)) && !IsBuiltEdge(cell, -dir)) continue;

                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    ledge.Add(cell + new Vector3Int(dx, 0, dz));
            }

        var pit = new List<Vector3Int>();
        foreach (var cell in floor)
        {
            if (ledge.Contains(cell) || !CanDigUnder(cell)) continue;
            pit.Add(cell);
        }

        // A pit needs room for islands; a sliver of one is just a hole in the floor.
        if (pit.Count < floor.Count * 0.5f || pit.Count < 12) return false;

        foreach (var cell in pit)
        {
            var below = cell + Vector3Int.down;
            grid[cell] = CellType.CaveOpen;
            grid[below] = CellType.Cave;
            cave.Cells.Add(below);
            chasmCells.Add(below);
        }

        chasmPlateau[cave] = floor[0].y * WorldUnitSize + FloorSurfaceOffset;
        return true;
    }

    /// <summary>True when the cell under a cave floor cell is solid rock with no built space round it.</summary>
    bool CanDigUnder(Vector3Int cell)
    {
        var below = cell + Vector3Int.down;
        if (!grid.InBounds(below) || grid[below] != CellType.None) return false;

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 0; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var n = below + new Vector3Int(dx, dy, dz);
            if (grid.InBounds(n) && IsBuiltSpace(grid[n])) return false;
        }

        return true;
    }

    /// <summary>Stands the islands and lays the bridges of every chasm. Runs after the ground is laid.</summary>
    void BuildChasms()
    {
        foreach (var cave in chasms)
        {
            float plateau = chasmPlateau[cave];
            var nodes = new List<ChasmNode>();

            nodes.AddRange(LedgeNodes(cave, plateau));
            var islands = PlaceIslands(cave, plateau, nodes);
            islandCount = islands.Count;
            brokenSpans = 0;
            nodes.AddRange(islands);

            Physics.SyncTransforms();
            int bridges = BridgeNodes(nodes, plateau);
            HangChasmFog(cave, plateau);

            Debug.Log($"Chasm at {cave.Anchor}: {nodes.Count - islandCount} ledges, {islandCount} islands, {bridges} bridges, "
                + $"{brokenSpans} bridge points with nothing underfoot.", this);
        }
    }

    int islandCount, brokenSpans;

    // ------------------------------------------------------------------ ledges

    /// <summary>One node per connected stretch of ledge.</summary>
    List<ChasmNode> LedgeNodes(CaveRegion cave, float plateau)
    {
        var ledge = new HashSet<Vector3Int>();
        int y = Mathf.RoundToInt((plateau - FloorSurfaceOffset) / WorldUnitSize);
        foreach (var cell in cave.Cells)
            if (cell.y == y && grid[cell] == CellType.Cave) ledge.Add(cell);

        var nodes = new List<ChasmNode>();
        var seen = new HashSet<Vector3Int>();
        foreach (var start in Sorted(ledge))
        {
            if (!seen.Add(start)) continue;

            var node = new ChasmNode();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                AddCellPoints(node, cell, plateau);
                foreach (var dir in Directions)
                {
                    var n = cell + dir;
                    if (dir.y == 0 && ledge.Contains(n) && seen.Add(n)) queue.Enqueue(n);
                }
            }

            node.Centre = node.Points[node.Points.Count / 2];
            nodes.Add(node);
        }

        return nodes;
    }

    void AddCellPoints(ChasmNode node, Vector3Int cell, float plateau)
    {
        Vector3 c = CellCentre(cell);
        for (float dx = -1.5f; dx <= 1.5f; dx += 1.5f)
        for (float dz = -1.5f; dz <= 1.5f; dz += 1.5f)
            node.Points.Add(new Vector3(c.x + dx, plateau, c.z + dz));
    }

    // ------------------------------------------------------------------ islands

    List<ChasmNode> PlaceIslands(CaveRegion cave, float plateau, List<ChasmNode> ledges)
    {
        var islands = new List<ChasmNode>();
        var pit = new List<Vector3Int>();
        foreach (var cell in cave.Cells) if (chasmCells.Contains(cell)) pit.Add(cell);
        pit = Sorted(pit);
        if (pit.Count == 0) return islands;

        // Farthest-point spread, measured from the ledges as well, so islands step out across the
        // pit rather than crowding the way in.
        var taken = new List<Vector3>();
        foreach (var node in ledges) taken.AddRange(node.Points);

        int wanted = Mathf.Max(1, pit.Count / Mathf.Max(4, profile.chasmCellsPerIsland));
        for (int i = 0; i < wanted * 2 && islands.Count < wanted; i++)
        {
            Vector3Int best = pit[0];
            float bestGap = -1f;
            int start = random.Next(pit.Count);
            for (int k = 0; k < pit.Count; k++)
            {
                var cell = pit[(start + k) % pit.Count];
                // Islands sit inside the pit, never against its wall.
                if (PitEdgeDistance(cell) < 1) continue;

                Vector3 c = CellCentre(cell);
                float gap = float.MaxValue;
                foreach (var p in taken) gap = Mathf.Min(gap, FlatDistance(p, c));
                if (gap > bestGap) { bestGap = gap; best = cell; }
            }

            // Closer than about two cells to everything else and the pit is full.
            if (bestGap < WorldUnitSize * 2.2f) break;

            var island = BuildIsland(best, plateau);
            if (island == null) continue;
            islands.Add(island);
            taken.AddRange(island.Points);
        }

        return islands;
    }

    /// <summary>Cells from a pit cell to the nearest cell that is not pit, in a square ring.</summary>
    int PitEdgeDistance(Vector3Int cell)
    {
        for (int r = 1; r <= 3; r++)
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                if (!chasmCells.Contains(cell + new Vector3Int(dx, 0, dz))) return r - 1;
            }
        return 3;
    }

    /// <summary>
    /// One island: a big platform, medium ones lapping it at the same height, and a couple of
    /// small stepping stones a step lower - graded as the demo stacks them. Every piece stands on
    /// the pit floor: the platforms are six-metre pillars and the pit is one five-metre storey.
    /// </summary>
    ChasmNode BuildIsland(Vector3Int cell, float plateau)
    {
        Vector3 centre = CellCentre(cell);
        centre.y = plateau;

        var big = PlacePlatform(PickPrefab(profile.chasmPlatformsLarge), centre, 1.2f + (float)random.NextDouble() * 0.3f, plateau);
        if (big == null) return null;

        var node = new ChasmNode { Centre = centre };
        node.Points.Add(centre);
        AddTopPoints(node, big);

        int medium = random.Next(1, 4);
        for (int i = 0; i < medium; i++)
        {
            float angle = (i + (float)random.NextDouble() * 0.6f) * Mathf.PI * 2f / medium;
            Vector3 at = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (3.5f + (float)random.NextDouble() * 1.5f);
            // A few centimetres apart in height so the overlapping tops do not z-fight.
            var piece = PlacePlatform(PickPrefab(profile.chasmPlatformsMedium), at, 1.1f + (float)random.NextDouble() * 0.3f,
                plateau - 0.03f * (i + 1));
            if (piece != null) AddTopPoints(node, piece);
        }

        int small = random.Next(1, 3);
        for (int i = 0; i < small; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 at = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (5.5f + (float)random.NextDouble() * 1.5f);
            PlacePlatform(PickPrefab(profile.chasmPlatformsSmall), at, 1f + (float)random.NextDouble() * 0.2f,
                plateau - 0.4f - (float)random.NextDouble() * 0.5f);
        }

        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var c = cell + new Vector3Int(dx, 0, dz);
            if (chasmCells.Contains(c) && FlatDistance(CellCentre(c), centre) < 5f) chasmGround.Add(c);
        }

        // Goblin torch sticks on most islands - the demo's cave is lit by fourteen of them, and
        // it is what makes the islands read across the dark of the pit.
        if (profile.chasmTorch != null && random.NextDouble() < profile.chasmTorchChance)
        {
            Physics.SyncTransforms();
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 at = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.5f;
            if (NearestOf(caveKitGround, new Ray(new Vector3(at.x, plateau + 1f, at.z), Vector3.down), out RaycastHit hit, 2f))
            {
                var torch = Spawn(profile.chasmTorch, hit.point, Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f));
                if (torch != null)
                {
                    AlignSurface(torch, hit.point.y - 0.1f, false);
                    var flame = torch.AddComponent<Delver.Game.TorchLight>();
                    flame.FlameColor = new Color(1f, 0.55f, 0.15f);
                    flame.BaseIntensity = 3f * Mathf.Max(0.5f, LitFactor(cell + Vector3Int.up));
                    flame.Range = 13f;
                    flame.LocalOffset = new Vector3(0f, 1.4f, 0f);
                }
            }
        }

        return node;
    }

    /// <summary>Points on a platform's top, inset from its edge, for finding where bridges land.</summary>
    void AddTopPoints(ChasmNode node, GameObject platform)
    {
        if (!WorldBounds(platform, out Bounds b)) return;
        float rx = b.extents.x * 0.55f, rz = b.extents.z * 0.55f;
        foreach (var (sx, sz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f), (0.7f, 0.7f), (-0.7f, 0.7f), (0.7f, -0.7f), (-0.7f, -0.7f) })
            node.Points.Add(new Vector3(b.center.x + sx * rx, b.max.y, b.center.z + sz * rz));
    }

    GameObject PlacePlatform(GameObject prefab, Vector3 at, float scale, float top)
    {
        if (prefab == null) return null;

        // Platforms may only stand over pit cells - not across the ledge, not in the wall.
        var owner = CellOf(at, top - WorldUnitSize);
        if (!chasmCells.Contains(owner)) return null;

        GameObject piece = Spawn(prefab, at, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
        if (piece == null) return null;

        piece.transform.localScale = Vector3.Scale(piece.transform.localScale, new Vector3(scale, 1f, scale));

        // Standing on the pit floor with its foot sunk a little into it - no deeper, or it would
        // reach whatever lies under the pit.
        ScaleToHeight(piece, top - Floor(owner) + 0.4f);
        AlignSurface(piece, top, true);

        if (ReachesIntoBuilt(piece, 0f) || !StandsInPit(piece, owner.y))
        {
            DestroyImmediate(piece);
            return null;
        }

        piece.name = "Chasm platform - " + prefab.name;
        RegisterGround(piece);
        return piece;
    }

    /// <summary>True when every corner of a piece's footprint is over pit floor, so none of it hangs over a ledge or into rock.</summary>
    bool StandsInPit(GameObject piece, int pitY)
    {
        if (!WorldBounds(piece, out Bounds b)) return false;
        foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
        {
            var cell = new Vector3Int(Mathf.FloorToInt((b.center.x + sx * b.extents.x * 0.7f) / WorldUnitSize), pitY,
                Mathf.FloorToInt((b.center.z + sz * b.extents.z * 0.7f) / WorldUnitSize));
            if (!chasmCells.Contains(cell)) return false;
        }
        return true;
    }

    Vector3Int CellOf(Vector3 point, float height) => new Vector3Int(
        Mathf.FloorToInt(point.x / WorldUnitSize),
        Mathf.FloorToInt((height - FloorSurfaceOffset + 0.01f) / WorldUnitSize),
        Mathf.FloorToInt(point.z / WorldUnitSize));

    static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    static List<Vector3Int> Sorted(IEnumerable<Vector3Int> cells)
    {
        var list = new List<Vector3Int>(cells);
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z != b.z ? a.z.CompareTo(b.z) : a.y.CompareTo(b.y));
        return list;
    }

    // ------------------------------------------------------------------ bridges

    /// <summary>
    /// Joins every ledge and island into one network - a minimum spanning tree over the closest
    /// pair of points between each two nodes - and lays a bridge across each gap.
    /// </summary>
    int BridgeNodes(List<ChasmNode> nodes, float plateau)
    {
        int laid = 0;
        if (nodes.Count < 2) return laid;

        var joined = new HashSet<int> { 0 };
        while (joined.Count < nodes.Count)
        {
            int to = -1;
            Vector3 bestA = default, bestB = default;
            float best = float.MaxValue;
            foreach (int a in joined)
                for (int b = 0; b < nodes.Count; b++)
                {
                    if (joined.Contains(b)) continue;
                    foreach (var pa in nodes[a].Points)
                    foreach (var pb in nodes[b].Points)
                    {
                        float d = FlatDistance(pa, pb);
                        if (d < best) { best = d; to = b; bestA = pa; bestB = pb; }
                    }
                }

            if (to < 0) break;
            joined.Add(to);
            if (SpanGap(bestA, bestB, plateau)) laid++;
        }

        return laid;
    }

    /// <summary>
    /// Finds where the ground actually ends on each side of the gap and lays a bridge between,
    /// lapping half a metre onto the ground at both ends.
    /// </summary>
    bool SpanGap(Vector3 a, Vector3 b, float plateau)
    {
        Vector3 dir = b - a;
        dir.y = 0f;
        float distance = dir.magnitude;
        if (distance < 0.5f) return false;
        dir /= distance;

        // March out from each side while there is ground at plateau height underfoot.
        float startT = 0f;
        while (startT < distance && HasGroundAt(a + dir * (startT + 0.25f), plateau)) startT += 0.25f;
        float endT = distance;
        while (endT > startT && HasGroundAt(a + dir * (endT - 0.25f), plateau)) endT -= 0.25f;

        // Solid all the way across: the platforms already touch.
        if (endT - startT < 0.6f) return false;

        Vector3 start = a + dir * Mathf.Max(0f, startT - 0.6f);
        Vector3 end = a + dir * Mathf.Min(distance, endT + 0.6f);
        start.y = end.y = plateau;

        float gap = endT - startT;
        bool track = profile.chasmTrestle != null && (gap > 12f || random.NextDouble() < 0.4);
        if (track || profile.ropeBridgeSpan == null) LayTrack(start, end);
        else LayRopeBridge(start, end);

        // Walk it: every half metre there should be deck or ground underfoot.
        Physics.SyncTransforms();
        for (float t = 0f; t <= 1f; t += 0.5f / Mathf.Max(0.5f, (end - start).magnitude))
        {
            var p = Vector3.Lerp(start, end, t);
            if (!Physics.Raycast(new Vector3(p.x, plateau + 0.8f, p.z), Vector3.down, 1.6f, ~0, QueryTriggerInteraction.Ignore)) brokenSpans++;
        }
        return true;
    }

    bool HasGroundAt(Vector3 point, float plateau)
    {
        var ray = new Ray(new Vector3(point.x, plateau + 0.6f, point.z), Vector3.down);
        return NearestOf(caveKitGround, ray, out RaycastHit hit, 1.2f) && hit.normal.y > 0.6f;
    }

    void LayRopeBridge(Vector3 start, Vector3 end)
    {
        Vector3 run = end - start;
        float length = run.magnitude;
        Vector3 flat = run / length;
        Quaternion along = Quaternion.FromToRotation(Vector3.right, flat);

        const float endLength = 8.04f, endDeck = 0.83f;
        const float spanLength = 7.2f, spanDeck = 0.43f;

        // Anchored ends only on a long crossing; a short one is spans alone, stretched to fit.
        bool anchored = profile.ropeBridgeEnd != null && length >= endLength * 2f + 2f;
        float middleStart = anchored ? endLength - 0.3f : 0f;
        float middle = anchored ? length - endLength * 2f + 0.6f : length;

        if (anchored)
        {
            Name(Spawn(profile.ropeBridgeEnd, start + flat * endLength * 0.5f - Vector3.up * endDeck, along), "Chasm bridge");
            Name(Spawn(profile.ropeBridgeEnd, end - flat * endLength * 0.5f - Vector3.up * endDeck, along * Quaternion.Euler(0f, 180f, 0f)), "Chasm bridge");
        }

        int spans = Mathf.Max(1, Mathf.CeilToInt(middle / spanLength));
        float each = middle / spans;
        for (int i = 0; i < spans; i++)
        {
            Vector3 at = start + flat * (middleStart + each * (i + 0.5f));
            var span = Spawn(profile.ropeBridgeSpan, at - Vector3.up * spanDeck, along);
            if (span == null) continue;
            span.transform.localScale = Vector3.Scale(span.transform.localScale, new Vector3(each / spanLength + 0.02f, 1f, 1f));
            Name(span, "Chasm bridge");
        }
    }

    /// <summary>A mine track on trestles, five metres a segment, legs standing on the pit floor.</summary>
    void LayTrack(Vector3 start, Vector3 end)
    {
        Vector3 run = end - start;
        float length = run.magnitude;
        int segments = Mathf.Max(1, Mathf.CeilToInt(length / 5f));
        Quaternion along = Quaternion.LookRotation(run.normalized, Vector3.up);

        // Both pieces pivot 2.5 m off their centre line and run five metres along local Z.
        Vector3 pivotShift = along * new Vector3(2.5f, 0f, 0f);
        float scale = length / (segments * 5f);
        const float railDepth = 0.24f;

        for (int i = 0; i < segments; i++)
        {
            Vector3 at = start + run * (i / (float)segments) + pivotShift - Vector3.up * railDepth;

            var trestle = Spawn(profile.chasmTrestle, at, along);
            if (trestle != null)
            {
                trestle.transform.localScale = Vector3.Scale(trestle.transform.localScale, new Vector3(1f, 1f, scale));
                Name(trestle, "Chasm bridge");
            }

            if (profile.chasmRail != null)
            {
                var rail = Spawn(profile.chasmRail, at, along);
                if (rail != null)
                {
                    rail.transform.localScale = Vector3.Scale(rail.transform.localScale, new Vector3(1f, 1f, scale));
                    Name(rail, "Chasm bridge");
                }
            }
        }
    }

    static void Name(GameObject go, string label)
    {
        if (go != null) go.name = label + " - " + go.name.Replace("(Clone)", "");
    }

    // ------------------------------------------------------------------ pit

    /// <summary>
    /// Dresses the pit floor the way the demo does: boulder piles round the feet of the islands,
    /// graded big, medium, small. Walk-through is irrelevant down here - nobody walks it.
    /// </summary>
    void DressPitFloor(CaveRegion cave)
    {
        if (caveScatterPrefabs == null || caveScatterPrefabs.Length == 0) return;

        foreach (var cell in Sorted(cave.Cells))
        {
            if (!chasmCells.Contains(cell) || random.NextDouble() > 0.35) continue;

            Vector3 middle = CellCentre(cell) + new Vector3(((float)random.NextDouble() - 0.5f) * 3f, 0f, ((float)random.NextDouble() - 0.5f) * 3f);
            float probe = cell.y * WorldUnitSize + FloorSurfaceOffset + 2f;

            foreach (var tier in ScatterTiers(random.Next(1, 3), random.Next(2, 4)))
            {
                Vector2 offset = RandomInDisc() * tier.reach;
                var ray = new Ray(new Vector3(middle.x + offset.x, probe, middle.z + offset.y), Vector3.down);
                if (!NearestOf(caveKitGround, ray, out RaycastHit hit, 3f) || hit.normal.y < 0.7f) continue;

                // Only the pit floor itself, not the side of a pillar.
                if (hit.point.y > cell.y * WorldUnitSize + FloorSurfaceOffset + 0.5f) continue;

                GameObject prop = Spawn(PickPrefab(caveScatterPrefabs), hit.point, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                if (prop == null) continue;
                prop.transform.localScale *= tier.scale * 1.6f;
                AlignSurface(prop, hit.point.y - 0.05f, false);
            }
        }
    }

    /// <summary>Two sheets of dark fog in the pit, as the demo hangs them, so it reads as depth.</summary>
    void HangChasmFog(CaveRegion cave, float plateau)
    {
        DressPitFloor(cave);
        if (profile.chasmFogMaterial == null) return;

        Bounds area = new Bounds();
        bool first = true;
        foreach (var cell in cave.Cells)
        {
            if (!chasmCells.Contains(cell)) continue;
            var c = CellCentre(cell);
            if (first) { area = new Bounds(c, Vector3.zero); first = false; }
            else area.Encapsulate(c);
        }
        if (first) return;

        area.Expand(new Vector3(WorldUnitSize, 0f, WorldUnitSize));

        foreach (float depth in new[] { 2.5f, 4f })
        {
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sheet.name = "Chasm fog";
            DestroyImmediate(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(dungeonRoot, true);

            // Unity's plane is 10 x 10 units.
            sheet.transform.position = new Vector3(area.center.x, plateau - depth, area.center.z);
            sheet.transform.localScale = new Vector3(area.size.x / 10f, 1f, area.size.z / 10f);
            sheet.GetComponent<MeshRenderer>().sharedMaterial = profile.chasmFogMaterial;
        }
    }
}
