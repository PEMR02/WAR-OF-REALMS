using UnityEngine;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Enlaza el root del muro en construcción con su BuildSite (puerta a mitad de obra, varios aldeanos).
    /// </summary>
    public class CompoundWallUnderConstruction : MonoBehaviour
    {
        public BuildSite Site { get; private set; }

        public void Initialize(BuildSite site) => Site = site;

        /// <summary>Tramos rectos ya colocados (no esquina ni puerta).</summary>
        public int BuiltStraightSegmentCount
        {
            get
            {
                int n = 0;
                var markers = GetComponentsInChildren<CompoundWallSegmentMarker>(true);
                for (int i = 0; i < markers.Length; i++)
                {
                    var m = markers[i];
                    if (m != null && !m.isCornerPiece && !m.isGatePiece)
                        n++;
                }
                return n;
            }
        }
    }
}
