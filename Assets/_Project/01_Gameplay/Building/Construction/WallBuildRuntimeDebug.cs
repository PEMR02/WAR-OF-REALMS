using UnityEngine;
using Project.Gameplay.Units;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Soporte mínimo para diagnóstico runtime de muros (activar en <see cref="Builder.debugWallBuildRuntime"/>
    /// o con el aldeano seleccionado en <see cref="RTSSelectionController"/>).
    /// </summary>
    public static class WallBuildRuntimeDebug
    {
        public static bool IsBuilderInCurrentSelection(Builder builder)
        {
            if (builder == null) return false;
            var sel = Object.FindFirstObjectByType<RTSSelectionController>();
            if (sel == null) return false;
            var u = builder.GetComponent<UnitSelectable>();
            if (u == null) return false;
            var list = sel.GetSelected();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == u)
                    return true;
            }
            return false;
        }
    }
}
