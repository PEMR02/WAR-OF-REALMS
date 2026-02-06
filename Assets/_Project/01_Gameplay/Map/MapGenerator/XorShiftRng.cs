using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>RNG determinista XorShift. Reproducible por seed.</summary>
    public class XorShiftRng : IRng
    {
        private uint _state;
        public int Seed { get; }

        public XorShiftRng(int seed)
        {
            Seed = seed;
            _state = (uint)Mathf.Max(1, seed);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint u = NextUInt();
            int range = maxExclusive - minInclusive;
            return minInclusive + (int)(u % (uint)range);
        }

        public float NextFloat()
        {
            return NextUInt() / (float)uint.MaxValue;
        }

        private uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }
    }
}
