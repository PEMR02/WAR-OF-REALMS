using UnityEngine;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Filtro único para calcular la base (Y mínimo) al apoyar edificios en terreno.
    /// Debe coincidir con la intención de <see cref="BuildingController"/>.TryGetVisualBoundsSize: ignora utilidades
    /// cuyos bounds “roban” el mínimo (Footprint grid, Outline, HealthBar, decals, plataformas, VFX).
    /// </summary>
    public static class BuildingTerrainAlignment
    {
        /// <summary>Renderers que no deben recibir tint de selección ni servir como fuente de outline 3D duplicado.</summary>
        public static bool ShouldExcludeRendererForBaseAlignment(Renderer r)
        {
            if (r == null) return true;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                return true;
            if (IsUnderNamedAncestor(r.transform, "HealthBar"))
                return true;
            if (IsUnderNamedAncestor(r.transform, "UnitSelectionRing"))
                return true;

            GameObject go = r.gameObject;
            string n = go.name;
            if (n.Equals("DropAnchor", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("SpawnPoint", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("BarAnchor", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("GroundDecal", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("BasePlatform", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Footprint", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Outline", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("HealthBar", System.StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("Decal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Platform", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("VFX", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("FX", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Smoke", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Selection", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Ring", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("ResourcePick", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (go.GetComponent<Canvas>() != null) return true;
            // Solo el GO que lleva BuildingGroundDecal (suele ser root con malla placeholder). No usar GetComponentInParent: excluiría todo el edificio.
            // BuildingBasePlatform: mismo criterio que arriba (solo el GO con el componente).
            if (go.GetComponent<BuildingGroundDecal>() != null) return true;
            if (go.GetComponent<BuildingBasePlatform>() != null) return true;
            return false;
        }

        public static bool ShouldExcludeColliderForBaseAlignment(Collider c)
        {
            if (c == null) return true;
            return ShouldExcludeGameObjectForBaseAlignment(c.gameObject);
        }

        /// <summary>Fuentes de mesh que no deben generar siluetas outline (decal, footprint, pick, etc.).</summary>
        public static bool ShouldExcludeMeshFilterForOutline(MeshFilter mf)
        {
            if (mf == null || mf.sharedMesh == null) return true;
            if (mf.gameObject.name.Equals("Outline", System.StringComparison.OrdinalIgnoreCase)) return true;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null) return ShouldExcludeRendererForBaseAlignment(mr);
            return ShouldExcludeGameObjectForBaseAlignment(mf.gameObject);
        }

        /// <summary>Recoge renderers del root para tint de selección/hover (mismo criterio que outline).</summary>
        public static Renderer[] CollectRenderersForSelectionHighlight(Transform root)
        {
            if (root == null) return System.Array.Empty<Renderer>();
            var all = root.GetComponentsInChildren<Renderer>(true);
            var list = new System.Collections.Generic.List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r != null && !ShouldExcludeRendererForBaseAlignment(r))
                    list.Add(r);
            }
            return list.Count > 0 ? list.ToArray() : System.Array.Empty<Renderer>();
        }

        static bool ShouldExcludeGameObjectForBaseAlignment(GameObject go)
        {
            if (go == null) return true;
            if (IsUnderNamedAncestor(go.transform, "HealthBar")) return true;
            if (IsUnderNamedAncestor(go.transform, "UnitSelectionRing")) return true;

            string n = go.name;
            if (n.Equals("DropAnchor", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("SpawnPoint", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("BarAnchor", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("GroundDecal", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("BasePlatform", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Footprint", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Outline", System.StringComparison.OrdinalIgnoreCase) ||
                n.Equals("HealthBar", System.StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("ResourcePick", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Decal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Platform", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("VFX", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("FX", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Smoke", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (go.GetComponent<Canvas>() != null) return true;
            if (go.GetComponent<BuildingGroundDecal>() != null) return true;
            if (go.GetComponent<BuildingBasePlatform>() != null) return true;
            return false;
        }

        static bool IsUnderNamedAncestor(Transform t, string ancestorName)
        {
            Transform p = t != null ? t.parent : null;
            while (p != null)
            {
                if (p.name.Equals(ancestorName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
                p = p.parent;
            }
            return false;
        }
    }
}
