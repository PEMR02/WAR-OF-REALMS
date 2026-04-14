#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Aviso único por sesión si el ensamblado ya no expone tipos antiguos usados por grafos VS.
/// </summary>
[InitializeOnLoad]
static class VisualScriptingObsoleteTypesWarn
{
    const string SessionKey = "WOR_VisualScripting_ObsoleteMsgOnce";

    static VisualScriptingObsoleteTypesWarn()
    {
        EditorApplication.delayCall += RunOnce;
    }

    static void RunOnce()
    {
        EditorApplication.delayCall -= RunOnce;
        if (SessionState.GetBool(SessionKey, false))
            return;

        Type bridge = Type.GetType("Project.Gameplay.Map.Generator.MapGeneratorBridge, Assembly-CSharp");
        Type grid = Type.GetType("GridConfig, Assembly-CSharp") ?? Type.GetType("Project.Gameplay.Map.GridConfig, Assembly-CSharp");

        if (bridge != null || grid != null)
            return;

        SessionState.SetBool(SessionKey, true);
        Debug.LogWarning(
            "[VisualScripting] Tipos obsoletos detectados (GridConfig / MapGeneratorBridge no existen en Assembly-CSharp). " +
            "Si en consola aparece \"Unable to find type\" o \"node options failed\", elimina o rehaz los grafos que los referencian y regenera nodos (Tools > Visual Scripting).");
    }
}
#endif
