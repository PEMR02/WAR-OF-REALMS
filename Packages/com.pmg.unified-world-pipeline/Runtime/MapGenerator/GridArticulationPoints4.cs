namespace Project.Gameplay.Map.Generator
{
    // Tarjan articulation points on 4-connected cells where mask is true. Iterative DFS.
    public static class GridArticulationPoints4
    {
        private const int ParentNone = -1;

        private struct Frame
        {
            public int V;
            public int P;
            public byte NextDir;
        }

        public static bool[,] Compute(bool[,] mask, int w, int h, out int nodeCount, out int edgeCountUndirected, out int articulationCount)
        {
            nodeCount = 0;
            edgeCountUndirected = 0;
            articulationCount = 0;

            if (mask == null || w <= 0 || h <= 0)
                return new bool[w, h];

            int n = w * h;
            var tin = new int[n];
            var low = new int[n];
            var parent = new int[n];
            var artic = new bool[n];
            var rootDfsChildren = new int[n];

            for (int i = 0; i < n; i++)
            {
                tin[i] = -1;
                parent[i] = ParentNone;
                rootDfsChildren[i] = 0;
            }

            var stack = new Frame[n];
            int timer = 0;

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!mask[x, z])
                        continue;
                    nodeCount++;
                    if (x + 1 < w && mask[x + 1, z])
                        edgeCountUndirected++;
                    if (z + 1 < h && mask[x, z + 1])
                        edgeCountUndirected++;
                }
            }

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!mask[x, z])
                        continue;
                    int start = z * w + x;
                    if (tin[start] >= 0)
                        continue;

                    int sp = 0;
                    stack[sp++] = new Frame { V = start, P = ParentNone, NextDir = 0 };
                    tin[start] = low[start] = ++timer;

                    while (sp > 0)
                    {
                        ref Frame fr = ref stack[sp - 1];
                        int v = fr.V;
                        int pv = fr.P;

                        if (fr.NextDir < 4)
                        {
                            int to = NeighborFlatIndex(v, fr.NextDir++, w, h, mask);
                            if (to < 0)
                                continue;
                            if (to == pv)
                                continue;
                            if (tin[to] >= 0)
                            {
                                int tv = tin[to];
                                if (tv < low[v])
                                    low[v] = tv;
                                continue;
                            }

                            parent[to] = v;
                            tin[to] = low[to] = ++timer;
                            if (pv == ParentNone)
                                rootDfsChildren[v]++;

                            stack[sp++] = new Frame { V = to, P = v, NextDir = 0 };
                            continue;
                        }

                        sp--;
                        if (sp <= 0)
                        {
                            if (parent[v] == ParentNone && rootDfsChildren[v] > 1)
                                artic[v] = true;
                            continue;
                        }

                        int par = parent[v];
                        if (par >= 0)
                        {
                            if (low[v] < low[par])
                                low[par] = low[v];
                            if (low[v] >= tin[par] && parent[par] != ParentNone)
                                artic[par] = true;
                        }
                    }
                }
            }

            var result = new bool[w, h];
            for (int zz = 0; zz < h; zz++)
            {
                for (int xx = 0; xx < w; xx++)
                {
                    if (!mask[xx, zz])
                        continue;
                    int idx = zz * w + xx;
                    if (artic[idx])
                    {
                        result[xx, zz] = true;
                        articulationCount++;
                    }
                }
            }

            return result;
        }

        private static int NeighborFlatIndex(int v, int dir, int w, int h, bool[,] mask)
        {
            int x = v % w;
            int z = v / w;
            switch (dir)
            {
                case 0:
                    if (x + 1 >= w) return -1;
                    x++;
                    break;
                case 1:
                    if (x - 1 < 0) return -1;
                    x--;
                    break;
                case 2:
                    if (z - 1 < 0) return -1;
                    z--;
                    break;
                case 3:
                    if (z + 1 >= h) return -1;
                    z++;
                    break;
                default:
                    return -1;
            }

            return mask[x, z] ? z * w + x : -1;
        }
    }
}
