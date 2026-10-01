using System.Collections.Generic;
using UnityEngine;

namespace Delver.Dungeon
{
    /// <summary>
    /// Turns carved cave cells into one smooth rock surface.
    ///
    /// A cave built from 5m floor tiles and flat wall panels reads as a room with a ragged plan,
    /// because every wall is an axis-aligned board and the plan itself only has eight or so cells
    /// to curve across. No amount of tuning the carver fixes that - the resolution and the
    /// snapping are the problem, not the shape.
    ///
    /// So the cells stop being geometry and become only a source of SHAPE. They are rasterised
    /// into a density field several times finer than the grid, blurred until the corners round
    /// off, roughened with noise, and contoured into a mesh. Gameplay still reads the cells:
    /// reachability, corridor pathfinding and the cave mouths are all untouched.
    ///
    /// The contouring is naive surface nets rather than marching cubes. It puts one vertex per
    /// cell at the average of that cell's edge crossings and joins them across sign-changing
    /// edges - a page of code with no 256-entry table to get subtly wrong, and it produces a
    /// rounder surface than marching cubes does for the same field.
    /// </summary>
    public static class CaveSurface
    {
        /// <summary>Field sample: positive is rock, negative is open air, the surface is zero.</summary>
        const float Rock = 1f;
        const float Air = -1f;

        public struct Settings
        {
            /// <summary>Field samples per grid cell along each axis. 4 gives 1.25m, 5 gives 1m.</summary>
            public int VoxelsPerCell;

            /// <summary>Blur passes. This is what rounds the cells off - with none you get boxes back.</summary>
            public int SmoothPasses;

            /// <summary>How far the noise pushes the surface, in world units.</summary>
            public float NoiseAmplitude;

            /// <summary>Noise features per world unit. Low is boulders, high is gravel.</summary>
            public float NoiseFrequency;

            /// <summary>Height above the floor over which noise fades in, so the ground stays walkable.</summary>
            public float FloorFlattenHeight;

            /// <summary>World size of one grid cell.</summary>
            public float CellSize;

            /// <summary>Height of the walkable surface above a cell's grid line.</summary>
            public float FloorSurfaceOffset;

            /// <summary>
            /// How many cells of built space around a cave are held open.
            ///
            /// At 1 the rock skin grew straight through anything one step further out - it closed
            /// over staircases and stood inside doorways, because a stair or a door frame occupies
            /// a cell the cave was never told to leave alone.
            /// </summary>
            public int OpenMargin;
        }

        /// <summary>
        /// Builds the cave surface.
        ///
        /// <paramref name="isCaveAir"/> reports cells that are open cave, <paramref name="isOpenAir"/>
        /// reports every other cell a delver can occupy - rooms and corridors. The second is needed
        /// so the field does not seal a wall across a cave mouth: where a cave meets a room, both
        /// sides have to read as air or the mesh grows a rock plug across the opening.
        ///
        /// <paramref name="isProtected"/> reports cells whose contents are built geometry that the
        /// rock must never share space with - staircases and doorways. These are held SOLID, not
        /// open. Opening them was an attempt to stop rock growing through a flight of stairs, but
        /// it backfired: a pocket of air inside the stair cell has its own boundary against the
        /// surrounding rock, and contouring that boundary put the mesh inside the flight. Keeping
        /// them solid produces no boundary at all, and the caves are held clear of them upstream.
        /// </summary>
        public static Mesh Build(Vector3Int gridSize, System.Func<Vector3Int, bool> isCaveAir,
            System.Func<Vector3Int, bool> isOpenAir, System.Func<Vector3Int, bool> isProtected,
            System.Func<Vector3Int, bool> isBuiltSpace, Settings settings, out int caveCellCount)
        {
            int vpc = Mathf.Clamp(settings.VoxelsPerCell, 1, 8);
            float voxel = settings.CellSize / vpc;

            // Rock samples kept above the highest cave and below the lowest. A single layer does
            // not survive the blur - it gets averaged against the air under it and dissolves, so
            // the cave opens onto the skybox. Three layers hold.
            const int Shell = 3;

            int nx = gridSize.x * vpc + 1;
            int ny = gridSize.y * vpc + 1 + Shell + 1;
            int nz = gridSize.z * vpc + 1;

            var field = new float[nx * ny * nz];
            for (int i = 0; i < field.Length; i++) field[i] = Rock;

            // Floor height per column, so noise can fade in above the ground rather than turning
            // the walkable surface into rubble. NaN marks a column with no cave in it.
            var columnFloor = new float[nx * nz];
            for (int i = 0; i < columnFloor.Length; i++) columnFloor[i] = float.NaN;

            caveCellCount = 0;

            for (int x = 0; x < gridSize.x; x++)
            for (int y = 0; y < gridSize.y; y++)
            for (int z = 0; z < gridSize.z; z++)
            {
                var cell = new Vector3Int(x, y, z);
                bool cave = isCaveAir(cell);

                if (cave) caveCellCount++;

                if (!cave)
                {
                    // Built space beside a cave is held open so the rock cannot plug the mouth.
                    // Only immediately beside it: every opened cell has its own boundary against
                    // the rock around it, and each of those boundaries becomes mesh, so reaching
                    // further just scatters rock through the dungeon.
                    if (!isOpenAir(cell)) continue;
                    if (isProtected(cell)) continue;
                    if (!CaveWithin(cell, 0, gridSize, isCaveAir, settings.OpenMargin)) continue;
                }

                float floorY = y * settings.CellSize + settings.FloorSurfaceOffset;
                float ceilY = floorY + settings.CellSize;

                int x0 = x * vpc, x1 = (x + 1) * vpc;
                int z0 = z * vpc, z1 = (z + 1) * vpc;

                // Inclusive on both ends: neighbouring cells must share their boundary samples or
                // the field grows a wafer of rock along every cell seam.
                int j0 = Mathf.Max(Shell, Mathf.CeilToInt(floorY / voxel));
                int j1 = Mathf.Min(ny - 1 - Shell, Mathf.FloorToInt(ceilY / voxel));

                for (int i = Mathf.Max(1, x0); i <= x1 && i < nx - 1; i++)
                for (int k = Mathf.Max(1, z0); k <= z1 && k < nz - 1; k++)
                {
                    for (int j = j0; j <= j1; j++)
                        field[(j * nz + k) * nx + i] = Air;

                    if (cave)
                    {
                        int ci = k * nx + i;
                        if (float.IsNaN(columnFloor[ci]) || floorY < columnFloor[ci])
                            columnFloor[ci] = floorY;
                    }
                }
            }

            if (caveCellCount == 0) return null;

            // Keep what was carved before anything is allowed to soften it.
            var carved = new bool[field.Length];
            for (int i = 0; i < field.Length; i++) carved[i] = field[i] < 0f;

            Smooth(field, nx, ny, nz, settings.SmoothPasses);

            // Blurring is what rounds the rock, but it also closes passages: a one-cell opening is
            // only a handful of samples across, and a 3x3x3 kernel run twice reaches most of the
            // way through it from both sides. Openings silently sealed, which is how you end up
            // able to squeeze into a cave and not back out.
            //
            // Clamping to the carve fixes it without giving up the rounding. Where rock was, the
            // blurred value stands and the corners soften; where air was carved, the sample can
            // only go MORE open. The surface is free to erode into rock and can never grow into
            // the space the carver opened.
            for (int i = 0; i < field.Length; i++)
                if (carved[i]) field[i] = Mathf.Min(field[i], Air);

            if (settings.NoiseAmplitude > 0f)
            {
                ApplyNoise(field, columnFloor, nx, ny, nz, voxel, settings);

                // Noise may roughen the one-sample skin of a passage, but not its core, or it
                // reopens the same problem the clamp just closed.
                ProtectCores(field, carved, nx, ny, nz);
            }

            return Contour(field, nx, ny, nz, voxel, settings, isBuiltSpace,
                cell => cell.x >= 0 && cell.y >= 0 && cell.z >= 0
                    && cell.x < gridSize.x && cell.y < gridSize.y && cell.z < gridSize.z
                    && isCaveAir(cell));
        }

        /// <summary>Is there a cave cell within <paramref name="margin"/> cells, on the storey
        /// <paramref name="levelOffset"/> away from this one?</summary>
        /// <summary>
        /// Forces every carved sample whose six neighbours were also carved back to open.
        ///
        /// This leaves the boundary layer free for the noise to roughen - which is where all the
        /// visible detail is anyway - while guaranteeing the middle of a passage stays clear.
        /// </summary>
        static void ProtectCores(float[] field, bool[] carved, int nx, int ny, int nz)
        {
            for (int j = 1; j < ny - 1; j++)
            for (int k = 1; k < nz - 1; k++)
            for (int i = 1; i < nx - 1; i++)
            {
                int index = (j * nz + k) * nx + i;
                if (!carved[index]) continue;

                if (!carved[index - 1] || !carved[index + 1]) continue;
                if (!carved[index - nx] || !carved[index + nx]) continue;
                if (!carved[index - nx * nz] || !carved[index + nx * nz]) continue;

                field[index] = Air;
            }
        }

        static bool CaveWithin(Vector3Int cell, int levelOffset, Vector3Int size,
            System.Func<Vector3Int, bool> isCaveAir, int margin)
        {
            int y = cell.y + levelOffset;
            if (y < 0 || y >= size.y) return false;

            margin = Mathf.Max(1, margin);

            for (int dx = -margin; dx <= margin; dx++)
            for (int dz = -margin; dz <= margin; dz++)
            {
                int x = cell.x + dx;
                int z = cell.z + dz;

                if (x < 0 || z < 0 || x >= size.x || z >= size.z) continue;
                if (isCaveAir(new Vector3Int(x, y, z))) return true;
            }

            return false;
        }

        /// <summary>
        /// Box blur. This is the step that does the actual rounding: a cube of air blurred a few
        /// times has its corners pulled in, and the zero crossing through it comes out curved.
        /// </summary>
        static void Smooth(float[] field, int nx, int ny, int nz, int passes)
        {
            if (passes <= 0) return;

            var next = new float[field.Length];

            for (int pass = 0; pass < passes; pass++)
            {
                for (int j = 0; j < ny; j++)
                for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                {
                    float sum = 0f;
                    int count = 0;

                    for (int dj = -1; dj <= 1; dj++)
                    for (int dk = -1; dk <= 1; dk++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int a = i + di, b = j + dj, c = k + dk;
                        if (a < 0 || b < 0 || c < 0 || a >= nx || b >= ny || c >= nz) continue;

                        sum += field[(b * nz + c) * nx + a];
                        count++;
                    }

                    next[(j * nz + k) * nx + i] = sum / count;
                }

                System.Array.Copy(next, field, field.Length);
            }
        }

        /// <summary>
        /// Roughens the surface so it does not read as a blurred box.
        ///
        /// Fades to nothing near the floor of each column - a bumpy wall is a cave, a bumpy floor
        /// is somewhere a character controller gets stuck.
        /// </summary>
        static void ApplyNoise(float[] field, float[] columnFloor, int nx, int ny, int nz,
            float voxel, Settings settings)
        {
            float freq = Mathf.Max(0.001f, settings.NoiseFrequency);
            float flatten = Mathf.Max(0.01f, settings.FloorFlattenHeight);

            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
            {
                float floorY = columnFloor[k * nx + i];
                float worldY = j * voxel;

                float fade = 1f;
                if (!float.IsNaN(floorY))
                    fade = Mathf.Clamp01((worldY - floorY) / flatten);

                if (fade <= 0f) continue;

                float n = Noise3(i * voxel * freq, worldY * freq, k * voxel * freq);
                field[(j * nz + k) * nx + i] += n * settings.NoiseAmplitude * fade;
            }
        }

        /// <summary>Cheap 3D value noise from three orthogonal slices of Unity's 2D Perlin.</summary>
        static float Noise3(float x, float y, float z)
        {
            float xy = Mathf.PerlinNoise(x, y);
            float yz = Mathf.PerlinNoise(y + 31.7f, z);
            float zx = Mathf.PerlinNoise(z + 57.3f, x);

            return (xy + yz + zx) / 3f - 0.5f;
        }

        // Cell-corner offsets, indexed so bit 0 is +x, bit 1 is +y, bit 2 is +z.
        static readonly Vector3Int[] Corners =
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1),
            new Vector3Int(0, 1, 1), new Vector3Int(1, 1, 1),
        };

        static readonly int[,] Edges =
        {
            {0,1},{2,3},{4,5},{6,7},
            {0,2},{1,3},{4,6},{5,7},
            {0,4},{1,5},{2,6},{3,7},
        };

        /// <summary>Naive surface nets: one vertex per straddling cell, quads across sign changes.</summary>
        static Mesh Contour(float[] field, int nx, int ny, int nz, float voxel,
            Settings settings, System.Func<Vector3Int, bool> isBuiltSpace,
            System.Func<Vector3Int, bool> isCaveAir)
        {
            int cx = nx - 1, cy = ny - 1, cz = nz - 1;

            var cellVertex = new int[cx * cy * cz];
            for (int i = 0; i < cellVertex.Length; i++) cellVertex[i] = -1;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            var corner = new float[8];

            for (int j = 0; j < cy; j++)
            for (int k = 0; k < cz; k++)
            for (int i = 0; i < cx; i++)
            {
                int mask = 0;

                for (int c = 0; c < 8; c++)
                {
                    var o = Corners[c];
                    corner[c] = field[((j + o.y) * nz + (k + o.z)) * nx + (i + o.x)];
                    if (corner[c] < 0f) mask |= 1 << c;
                }

                // Entirely rock or entirely air - no surface passes through.
                if (mask == 0 || mask == 255) continue;

                Vector3 sum = Vector3.zero;
                int crossings = 0;

                for (int e = 0; e < 12; e++)
                {
                    int a = Edges[e, 0], b = Edges[e, 1];
                    bool aAir = corner[a] < 0f;
                    bool bAir = corner[b] < 0f;
                    if (aAir == bAir) continue;

                    float t = corner[a] / (corner[a] - corner[b]);
                    Vector3 pa = (Vector3)Corners[a];
                    Vector3 pb = (Vector3)Corners[b];
                    sum += Vector3.Lerp(pa, pb, t);
                    crossings++;
                }

                if (crossings == 0) continue;

                Vector3 local = sum / crossings;
                var world = new Vector3((i + local.x) * voxel, (j + local.y) * voxel, (k + local.z) * voxel);

                // Rock is never drawn inside built space.
                //
                // Holding a room cell open so the cave mouth is not plugged makes that cell a
                // pocket of air, and its other faces still border rock - so the contour dutifully
                // drew a rock surface inside the room. Clipping here is what actually stops it:
                // the shell terminates at the boundary and the room's own walls take over. It
                // covers staircases for free, since a flight is built space too.
                var owner = new Vector3Int(
                    Mathf.FloorToInt(world.x / settings.CellSize),
                    Mathf.FloorToInt((world.y - settings.FloorSurfaceOffset) / settings.CellSize),
                    Mathf.FloorToInt(world.z / settings.CellSize));

                if (isBuiltSpace(owner))
                {
                    float upperBoundary = (owner.y + 1) * settings.CellSize + settings.FloorSurfaceOffset;
                    float lowerBoundary = owner.y * settings.CellSize + settings.FloorSurfaceOffset;
                    // Smoothing moves the floor into the cell below. Deleting that vertex
                    // deletes the cave floor above a room too. Keep the separating surface
                    // on the cave side of the boundary, clear of the ceiling slab.
                    if (isCaveAir(owner + Vector3Int.up) && upperBoundary - world.y <= voxel)
                        world.y = upperBoundary + 0.05f;
                    else if (isCaveAir(owner + Vector3Int.down) && world.y - lowerBoundary <= voxel)
                        world.y = lowerBoundary - 0.05f;
                    else continue;
                }

                cellVertex[(j * cz + k) * cx + i] = verts.Count;
                verts.Add(world);
            }

            // One quad per sign-changing grid edge, joining the four cells around that edge.
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
            {
                float here = field[(j * nz + k) * nx + i];
                bool hereAir = here < 0f;

                // X edge - the four cells around it vary in y and z.
                if (i < nx - 1 && j > 0 && k > 0)
                {
                    bool nextAir = field[(j * nz + k) * nx + (i + 1)] < 0f;
                    if (hereAir != nextAir)
                        Quad(cellVertex, cx, cy, cz, tris, hereAir,
                            i, j - 1, k - 1, i, j - 1, k, i, j, k, i, j, k - 1);
                }

                // Y edge - cells vary in x and z.
                if (j < ny - 1 && i > 0 && k > 0)
                {
                    bool nextAir = field[((j + 1) * nz + k) * nx + i] < 0f;
                    if (hereAir != nextAir)
                        Quad(cellVertex, cx, cy, cz, tris, hereAir,
                            i - 1, j, k - 1, i, j, k - 1, i, j, k, i - 1, j, k);
                }

                // Z edge - cells vary in x and y.
                if (k < nz - 1 && i > 0 && j > 0)
                {
                    bool nextAir = field[(j * nz + (k + 1)) * nx + i] < 0f;
                    if (hereAir != nextAir)
                        Quad(cellVertex, cx, cy, cz, tris, hereAir,
                            i - 1, j - 1, k, i - 1, j, k, i, j, k, i, j - 1, k);
                }
            }

            if (verts.Count == 0) return null;

            var mesh = new Mesh { name = "CaveSurface" };

            // Cave systems comfortably exceed the 16-bit vertex limit.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        static void Quad(int[] cellVertex, int cx, int cy, int cz, List<int> tris, bool flip,
            int ax, int ay, int az, int bx, int by, int bz,
            int ccx, int ccy, int ccz, int dx, int dy, int dz)
        {
            int a = CellIndex(cellVertex, cx, cy, cz, ax, ay, az);
            int b = CellIndex(cellVertex, cx, cy, cz, bx, by, bz);
            int c = CellIndex(cellVertex, cx, cy, cz, ccx, ccy, ccz);
            int d = CellIndex(cellVertex, cx, cy, cz, dx, dy, dz);

            // A missing corner means the edge runs along the outside of the field.
            if (a < 0 || b < 0 || c < 0 || d < 0) return;

            if (flip)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
            else
            {
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }
        }

        static int CellIndex(int[] cellVertex, int cx, int cy, int cz, int i, int j, int k)
        {
            if (i < 0 || j < 0 || k < 0 || i >= cx || j >= cy || k >= cz) return -1;

            return cellVertex[(j * cz + k) * cx + i];
        }

        /// <summary>
        /// Gives the surface the kit's look: flat-shaded facets, each one coloured by a UV
        /// borrowed from the pack's own rock meshes.
        ///
        /// Two things are going on. Splitting every triangle onto its own vertices and using the
        /// face normal kills the smooth shading - a contoured field with shared normals looks
        /// like poured clay, and Synty rock is faceted. And because the pack's materials are one
        /// texture atlas, a mesh with no UVs samples whatever sits at (0,0); sampling a spread of
        /// coordinates off SM_Env_Cave_01 instead means every facet lands on a real rock colour,
        /// with the variation the atlas already contains and no new art.
        /// </summary>
        public static void Finish(Mesh mesh, GameObject[] rockSources, bool flatShaded,
            bool doubleSided)
        {
            if (mesh == null) return;

            Vector2[] palette = SampleRockPalette(rockSources);

            var srcVerts = mesh.vertices;
            var srcTris = mesh.triangles;

            if (!flatShaded)
            {
                var flatUv = new Vector2[srcVerts.Length];
                for (int i = 0; i < flatUv.Length; i++) flatUv[i] = palette[0];
                mesh.uv = flatUv;
                return;
            }

            // The contour is an infinitely thin skin, so from behind it is simply not drawn and
            // you look straight through the rock into the cave. Emitting each facet a second time
            // wound the other way makes it read as solid from either side - which matters most
            // exactly where the shell is clipped against a room.
            int copies = doubleSided ? 2 : 1;
            int count = srcTris.Length * copies;

            var verts = new Vector3[count];
            var norms = new Vector3[count];
            var uvs = new Vector2[count];
            var tris = new int[count];

            for (int t = 0; t < srcTris.Length; t += 3)
            {
                Vector3 a = srcVerts[srcTris[t]];
                Vector3 b = srcVerts[srcTris[t + 1]];
                Vector3 c = srcVerts[srcTris[t + 2]];

                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;

                // Picked from the facet's own position, so the pattern is stable across
                // regenerates and neighbouring facets differ.
                Vector3 mid = (a + b + c) / 3f;
                int pick = Mathf.Abs(Hash(mid)) % palette.Length;

                verts[t] = a; verts[t + 1] = b; verts[t + 2] = c;
                norms[t] = normal; norms[t + 1] = normal; norms[t + 2] = normal;
                uvs[t] = palette[pick]; uvs[t + 1] = palette[pick]; uvs[t + 2] = palette[pick];
                tris[t] = t; tris[t + 1] = t + 1; tris[t + 2] = t + 2;

                if (!doubleSided) continue;

                int b0 = srcTris.Length + t;
                verts[b0] = a; verts[b0 + 1] = c; verts[b0 + 2] = b;
                norms[b0] = -normal; norms[b0 + 1] = -normal; norms[b0 + 2] = -normal;
                uvs[b0] = palette[pick]; uvs[b0 + 1] = palette[pick]; uvs[b0 + 2] = palette[pick];
                tris[b0] = b0; tris[b0 + 1] = b0 + 1; tris[b0 + 2] = b0 + 2;
            }

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        /// <summary>A spread of UV coordinates taken off the kit's rock meshes.</summary>
        static Vector2[] SampleRockPalette(GameObject[] sources)
        {
            var palette = new List<Vector2>();

            if (sources != null)
            {
                foreach (var source in sources)
                {
                    if (source == null) continue;

                    foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
                    {
                        var m = filter.sharedMesh;
                        if (m == null) continue;

                        // Imported meshes are not readable unless the model asset says so, and
                        // asking anyway throws - in a player build as well as in the editor. A
                        // rock we cannot sample is simply one we skip.
                        if (!m.isReadable) continue;

                        var uv = m.uv;
                        var tri = m.triangles;
                        if (uv == null || uv.Length == 0 || tri == null || tri.Length == 0) continue;

                        // Triangle centroids rather than raw vertices: a vertex can sit on the
                        // seam between two atlas tiles, a centroid is always inside one.
                        int step = Mathf.Max(3, (tri.Length / 3 / 12) * 3);
                        for (int t = 0; t + 2 < tri.Length; t += step)
                        {
                            Vector2 centroid = (uv[tri[t]] + uv[tri[t + 1]] + uv[tri[t + 2]]) / 3f;
                            palette.Add(centroid);
                        }
                    }
                }
            }

            if (palette.Count == 0) palette.Add(new Vector2(0.5f, 0.5f));

            return palette.ToArray();
        }

        public static IEnumerable<Mesh> SplitForLighting(Mesh source, float chunkSize)
        {
            var vertices = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            var triangles = source.triangles;
            var groups = new Dictionary<Vector3Int, List<int>>();
            chunkSize = Mathf.Max(1f, chunkSize);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 centre = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
                var key = Vector3Int.FloorToInt(centre / chunkSize);
                if (!groups.TryGetValue(key, out var group)) groups[key] = group = new List<int>();
                group.Add(triangles[i]); group.Add(triangles[i + 1]); group.Add(triangles[i + 2]);
            }
            foreach (var group in groups.Values)
            {
                var v = new Vector3[group.Count];
                var n = new Vector3[group.Count];
                var u = new Vector2[group.Count];
                var t = new int[group.Count];
                for (int i = 0; i < group.Count; i++)
                {
                    int index = group[i];
                    v[i] = vertices[index]; n[i] = normals[index]; u[i] = uv[index]; t[i] = i;
                }
                var mesh = new Mesh { name = "Cave lighting patch", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.vertices = v; mesh.normals = n; mesh.uv = u; mesh.triangles = t;
                mesh.RecalculateBounds();
                yield return mesh;
            }
        }

        static int Hash(Vector3 p)
        {
            unchecked
            {
                int h = Mathf.RoundToInt(p.x * 4f) * 73856093
                      ^ Mathf.RoundToInt(p.y * 4f) * 19349663
                      ^ Mathf.RoundToInt(p.z * 4f) * 83492791;
                return h;
            }
        }
    }
}
