using System.Collections.Generic;
using UnityEngine;
using Delver.Dungeon;

/// <summary>
/// Sets small scenes down in the caverns - see <see cref="CaveVignette"/>.
///
/// Spots are spread the same way room lights are: the first at random, each next one in whichever
/// open cell is furthest from everything placed so far, so a big cavern gets a camp at one end
/// and a crystal stand at the other rather than three things in a heap by the entrance.
/// </summary>
public partial class Generator3D
{
    void SetCaveVignettes(CaveRegion cave, bool lush)
    {
        if (profile == null || profile.caveVignettes == null || profile.caveVignettes.Length == 0) return;

        int wanted = Mathf.Min(profile.maxVignettesPerCavern, cave.CellCount / Mathf.Max(4, profile.cellsPerVignette));
        if (wanted <= 0) return;

        // Open cells only: the vignette needs room for its ring, so every neighbour must be cave
        // floor too, and nothing may sit on a mouth, a stair approach or the landmark clearing.
        var open = new List<Vector3Int>();
        foreach (var cell in cave.Cells)
        {
            if (!IsOpenCaveCell(cell)) continue;
            open.Add(cell);
        }
        if (open.Count == 0) return;

        open.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z != b.z ? a.z.CompareTo(b.z) : a.y.CompareTo(b.y));

        // The landmark counts as already placed, so vignettes spread away from it.
        var placed = new List<Vector3> { CellCentre(cave.Anchor) };

        for (int i = 0; i < wanted; i++)
        {
            int pick = -1;
            float bestGap = -1f;
            foreach (int index in ShuffledIndices(open.Count))
            {
                if (compositionReserved.Contains(open[index])) continue;

                float gap = float.MaxValue;
                foreach (var p in placed) gap = Mathf.Min(gap, Vector3.Distance(p, CellCentre(open[index])));
                if (gap > bestGap) { bestGap = gap; pick = index; }
            }

            // Closer than two cells to the last thing placed means the cavern is already full.
            if (pick < 0 || bestGap < WorldUnitSize * 2f) break;

            Vector3Int spot = open[pick];
            CaveVignette vignette = PickVignette(lush);
            if (vignette == null) break;

            SetVignette(vignette, spot);
            placed.Add(CellCentre(spot));

            // Hold the vignette's ground clear of the scatter and formation passes that follow.
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                compositionReserved.Add(spot + new Vector3Int(dx, 0, dz));
        }
    }

    bool IsOpenCaveCell(Vector3Int cell)
    {
        if (compositionReserved.Contains(cell) || TouchesStairs(cell)) return false;

        // In a chasm only an island's own middle is solid enough to stage a scene on.
        if (chasmCells.Contains(cell) && !chasmGround.Contains(cell)) return false;

        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var n = cell + new Vector3Int(dx, 0, dz);
            if (!grid.InBounds(n) || grid[n] != CellType.Cave) return false;
        }

        return true;
    }

    IEnumerable<int> ShuffledIndices(int count)
    {
        int start = random.Next(count);
        for (int k = 0; k < count; k++) yield return (start + k) % count;
    }

    CaveVignette PickVignette(bool lush)
    {
        CaveTheme wanted = lush ? CaveTheme.Lush : CaveTheme.Inhabited;

        float total = 0f;
        foreach (var v in profile.caveVignettes)
            if (v != null && (v.theme == CaveTheme.Any || v.theme == wanted)) total += v.weight;
        if (total <= 0f) return null;

        float roll = (float)random.NextDouble() * total;
        foreach (var v in profile.caveVignettes)
        {
            if (v == null || (v.theme != CaveTheme.Any && v.theme != wanted)) continue;
            roll -= v.weight;
            if (roll <= 0f) return v;
        }

        return null;
    }

    void SetVignette(CaveVignette v, Vector3Int cell)
    {
        Vector3 middle = CellCentre(cell) + new Vector3(0f, FloorSurfaceOffset, 0f);
        float turn = (float)random.NextDouble() * 360f;

        GameObject centre = null;
        if (v.centrepieces != null && v.centrepieces.Length > 0)
        {
            centre = SpawnGrounded(PickPrefab(v.centrepieces), middle, Quaternion.Euler(0f, turn, 0f));
            if (centre != null)
            {
                ScaleOnGround(centre, RollBetween(v.centreScale));
                centre.name = v.name + " - " + centre.name.Replace("(Clone)", "");
                ClaimSpace(centre);
            }
        }

        if (v.ring != null && v.ring.Length > 0)
        {
            int count = random.Next(v.minRing, Mathf.Max(v.minRing, v.maxRing) + 1);
            for (int i = 0; i < count; i++)
            {
                float angle = v.evenRing
                    ? turn + i * 360f / count
                    : turn + i * 360f / count + ((float)random.NextDouble() - 0.5f) * (300f / Mathf.Max(1, count));
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * RollBetween(v.ringRadius);

                float yaw = v.ringFacesCentre ? angle + 180f : (float)random.NextDouble() * 360f;
                GameObject piece = SpawnGrounded(PickPrefab(v.ring), middle + offset, Quaternion.Euler(0f, yaw, 0f));
                if (piece == null) continue;

                ScaleOnGround(piece, RollBetween(v.ringScale));
                piece.name = v.name + " - " + piece.name.Replace("(Clone)", "");
                if (!ClaimSpace(piece)) Discard(piece);
            }
        }

        if (v.scatter != null && v.scatter.Length > 0)
        {
            int count = random.Next(v.minScatter, Mathf.Max(v.minScatter, v.maxScatter) + 1);
            for (int i = 0; i < count; i++)
            {
                Vector2 disc = RandomInDisc() * v.scatterRadius;
                GameObject bit = SpawnGrounded(PickPrefab(v.scatter), middle + new Vector3(disc.x, 0f, disc.y),
                    Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));

                // The scatter is the small tier: centrepiece big, ring medium, this small.
                if (bit != null) ScaleOnGround(bit, 0.5f + (float)random.NextDouble() * 0.35f);
                MakeWalkThrough(bit);
            }
        }

        if (v.lightColor.maxColorComponent > 0.01f)
        {
            // Hung off the centrepiece when there is one, so batching and culling treat them as one.
            var holder = new GameObject(v.name + " light");
            holder.transform.SetParent(centre != null ? centre.transform : dungeonRoot, true);
            holder.transform.position = middle;

            var glow = holder.AddComponent<Delver.Game.TorchLight>();
            glow.FlameColor = v.lightColor;
            glow.BaseIntensity = v.lightIntensity;
            glow.Range = v.lightRange;
            glow.FlickerDepth = v.flicker;
            glow.LocalOffset = new Vector3(0f, v.lightHeight, 0f);
        }
    }

    /// <summary>
    /// Scale and spread for a graded cluster: one big piece in the middle, <paramref name="medium"/>
    /// around it, <paramref name="small"/> furthest out.
    /// </summary>
    IEnumerable<(float scale, float reach)> ScatterTiers(int medium, int small)
    {
        yield return (1.35f + (float)random.NextDouble() * 0.45f, 0.3f);
        for (int i = 0; i < medium; i++) yield return (0.8f + (float)random.NextDouble() * 0.3f, 1.4f);
        for (int i = 0; i < small; i++) yield return (0.35f + (float)random.NextDouble() * 0.3f, 2.4f);
    }

    float RollBetween(Vector2 range) => range.x + (float)random.NextDouble() * (range.y - range.x);

    Vector2 RandomInDisc()
    {
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        float radius = Mathf.Sqrt((float)random.NextDouble());
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    /// <summary>Rescales a grounded piece and puts its base back on the ground.</summary>
    static void ScaleOnGround(GameObject piece, float scale)
    {
        if (Mathf.Approximately(scale, 1f)) return;

        var renderer = piece.GetComponentInChildren<Renderer>();
        float ground = renderer != null ? renderer.bounds.min.y : piece.transform.position.y;

        piece.transform.localScale *= scale;
        AlignSurface(piece, ground, false);
    }
}
