using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    public static class UwpSceneBindingsUtility
    {
        public static IUwpSceneVisualBindings Resolve(PMGUnifiedWorldPipelineConfig config)
        {
            if (config == null || config.sceneVisualBindingsHost == null)
                return null;
            if (config.sceneVisualBindingsHost is IUwpSceneVisualBindings direct)
                return direct;
            return config.sceneVisualBindingsHost.GetComponent<IUwpSceneVisualBindings>();
        }

        public static IUwpSceneVisualBindings FindInScene()
        {
            var behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IUwpSceneVisualBindings bindings)
                    return bindings;
            }

            return null;
        }
    }
}
