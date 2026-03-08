using UnityEngine;
using UnityEditor;

namespace WarOfRealms.Editor
{
    /// <summary>
    /// Crea un cubo rojo en (0,0,0) llamado TestMCP. Menú: Tools > Create TestMCP Cube
    /// </summary>
    public static class CreateTestMPCCube
    {
        [MenuItem("Tools/Create TestMCP Cube")]
        public static void Create()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "TestMCP";
            cube.transform.position = new Vector3(0f, 0f, 0f);

            // Material rojo (URP Lit o fallback Unlit/Color)
            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    var mat = new Material(shader) { color = Color.red };
                    renderer.sharedMaterial = mat;
                }
            }

            Undo.RegisterCreatedObjectUndo(cube, "Create TestMCP Cube");
            Selection.activeGameObject = cube;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}
