using UnityEngine;

namespace Project.Gameplay.Buildings
{
    public static class PlacementValidator
    {
        // Valida sobre Ground y sin colisiones con Unit/Building/Obstacle
        public static bool IsValidPlacement(
            Vector3 pos,
            Vector2 size,
            LayerMask blockingMask,
            float yOffset = 0.5f)
        {
            Vector3 halfExtents = new Vector3(size.x * 0.5f, yOffset, size.y * 0.5f);
            Collider[] hits = Physics.OverlapBox(pos, halfExtents, Quaternion.identity, blockingMask);

            // Si toca algo bloqueante => inválido
            return hits == null || hits.Length == 0;
        }
    }
}
