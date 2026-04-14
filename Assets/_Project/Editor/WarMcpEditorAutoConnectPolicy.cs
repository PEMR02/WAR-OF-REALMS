#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;

/// <summary>
/// Reduce intentos de reconexión automática del paquete MCP cuando no hay servidor;
/// KeepConnected=false evita bucles de conexión en arranque del editor.
/// Solo reflexión: compila aunque el paquete Ivan Murzak no esté instalado o no sea
/// referenciado por este ensamblado (sin CS0234).
/// </summary>
[InitializeOnLoad]
static class WarMcpEditorAutoConnectPolicy
{
    const string EditorTypeName = "com.IvanMurzak.Unity.MCP.Editor.UnityMcpPluginEditor";

    static WarMcpEditorAutoConnectPolicy()
    {
        EditorApplication.delayCall += ApplyOnce;
    }

    static void ApplyOnce()
    {
        EditorApplication.delayCall -= ApplyOnce;
        try
        {
            Type editorType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                editorType = assembly.GetType(EditorTypeName, throwOnError: false);
                if (editorType != null)
                    break;
            }

            if (editorType == null)
                return;

            var hasInstance = editorType.GetProperty("HasInstance", BindingFlags.Public | BindingFlags.Static);
            if (hasInstance != null && hasInstance.GetValue(null) is bool hi && !hi)
                return;

            var keepConnected = editorType.GetProperty("KeepConnected", BindingFlags.Public | BindingFlags.Static);
            if (keepConnected != null && keepConnected.CanWrite)
                keepConnected.SetValue(null, false);
        }
        catch
        {
            // Paquete ausente, API distinta o propiedades no estáticas; silencioso
        }
    }
}
#endif
