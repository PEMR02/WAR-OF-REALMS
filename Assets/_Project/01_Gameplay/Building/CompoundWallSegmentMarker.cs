using UnityEngine;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Marca un tramo del muro ya colocado (construcción o instantáneo) para vida individual y colocación de puerta.
    /// </summary>
    public class CompoundWallSegmentMarker : MonoBehaviour
    {
        public int slotIndex;
        public bool isCornerPiece;
        public bool isGatePiece;
    }
}
