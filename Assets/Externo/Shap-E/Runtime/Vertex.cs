using System;
using System.Runtime.CompilerServices;

namespace MeshUtils.Internal
{
    internal struct Vertex : IEquatable<Vertex>
    {
        public int index;
        public Vector3d p;
        public int tstart;
        public int tcount;
        public SymmetricMatrix q;
        public bool borderEdge;
        public bool uvSeamEdge;
        public bool uvFoldoverEdge;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vertex(int index, Vector3d p)
        {
            this.index = index;
            this.p = p;
            this.tstart = 0;
            this.tcount = 0;
            this.q = new SymmetricMatrix();
            this.borderEdge = true;
            this.uvSeamEdge = false;
            this.uvFoldoverEdge = false;
        }

        public override int GetHashCode()
        {
            return index;
        }

        public override bool Equals(object obj)
        {
            if (obj is Vertex)
            {
                var other = (Vertex)obj;
                return index == other.index;
            }

            return false;
        }

        public bool Equals(Vertex other)
        {
            return index == other.index;
        }
    }
}
