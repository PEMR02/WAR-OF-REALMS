using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Project.Gameplay.Combat;
using Project.Gameplay.Buildings;

public static class WorldBarPrefabTools
{
    enum FixResult
    {
        Updated,
        AlreadyOk,
        SkippedNoHealthBar,
        Error
    }

    [MenuItem("Tools/WAR OF REALMS/Health Bars/Auto Fix Selected Prefabs")]
    static void AutoFixSelectedPrefabs()
    {
        var paths = new List<string>();
        for (int i = 0; i < Selection.objects.Length; i++)
        {
            string path = AssetDatabase.GetAssetPath(Selection.objects[i]);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                paths.Add(path);
        }

        if (paths.Count == 0)
        {
            Debug.LogWarning("WorldBarPrefabTools: no hay prefabs seleccionados.");
            return;
        }

        int changedCount = 0;
        int okCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            var result = FixPrefab(paths[i], out string details);
            switch (result)
            {
                case FixResult.Updated:
                    changedCount++;
                    Debug.Log($"[WorldBar AutoFix] UPDATED: {paths[i]} | {details}");
                    break;
                case FixResult.AlreadyOk:
                    okCount++;
                    Debug.Log($"[WorldBar AutoFix] OK: {paths[i]} | {details}");
                    break;
                case FixResult.SkippedNoHealthBar:
                    skippedCount++;
                    Debug.LogWarning($"[WorldBar AutoFix] SKIPPED (sin HealthBarWorld): {paths[i]} | {details}");
                    break;
                default:
                    errorCount++;
                    Debug.LogError($"[WorldBar AutoFix] ERROR: {paths[i]} | {details}");
                    break;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"WorldBarPrefabTools: updated={changedCount}, ok={okCount}, skipped={skippedCount}, errors={errorCount}, total={paths.Count}.");
    }

    [MenuItem("Tools/WAR OF REALMS/Health Bars/Auto Fix All Gameplay Prefabs")]
    static void AutoFixAllGameplayPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/08_Prefabs" });
        int changedCount = 0;
        int okCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var result = FixPrefab(path, out _);
            switch (result)
            {
                case FixResult.Updated: changedCount++; break;
                case FixResult.AlreadyOk: okCount++; break;
                case FixResult.SkippedNoHealthBar: skippedCount++; break;
                default: errorCount++; break;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"WorldBarPrefabTools: updated={changedCount}, ok={okCount}, skipped={skippedCount}, errors={errorCount}, total={guids.Length}.");
    }

    [MenuItem("Tools/WAR OF REALMS/Health Bars/Diagnose Selected Prefabs")]
    static void DiagnoseSelectedPrefabs()
    {
        var paths = new List<string>();
        for (int i = 0; i < Selection.objects.Length; i++)
        {
            string path = AssetDatabase.GetAssetPath(Selection.objects[i]);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                paths.Add(path);
        }

        if (paths.Count == 0)
        {
            Debug.LogWarning("WorldBarPrefabTools Diagnose: no hay prefabs seleccionados.");
            return;
        }

        for (int i = 0; i < paths.Count; i++)
            DiagnosePrefab(paths[i]);
    }

    static FixResult FixPrefab(string prefabPath, out string details)
    {
        details = string.Empty;
        GameObject root = null;
        bool changed = false;

        try
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
#pragma warning disable CS0618
            var bar = root.GetComponentInChildren<HealthBarWorld>(true);
#pragma warning restore CS0618
            if (bar == null)
            {
                details = "Prefab no tiene HealthBarWorld (se usa fallback en runtime o no aplica).";
                return FixResult.SkippedNoHealthBar;
            }

            var settings = root.GetComponent<WorldBarSettings>();
            if (settings == null)
            {
                settings = root.AddComponent<WorldBarSettings>();
                changed = true;
            }

            // Evitar comportamiento impredecible por componentes duplicados.
            var duplicateSelectables = root.GetComponents<BuildingSelectable>();
            if (duplicateSelectables != null && duplicateSelectables.Length > 1)
            {
                for (int i = 1; i < duplicateSelectables.Length; i++)
                    Object.DestroyImmediate(duplicateSelectables[i], true);
                changed = true;
            }

            if (settings.barAnchor == null)
            {
                var anchor = FindByName(root.transform, "BarAnchor");
                if (anchor == null)
                {
                    var go = new GameObject("BarAnchor");
                    anchor = go.transform;
                    anchor.SetParent(root.transform, false);
                    changed = true;
                }

                PositionAnchorAtRendererTop(root.transform, anchor, settings.rendererTopPadding);
                settings.barAnchor = anchor;
                changed = true;
            }
            else
            {
                // Recolocar anchor existente al top del modelo para homogeneizar en todos los prefabs.
                PositionAnchorAtRendererTop(root.transform, settings.barAnchor, settings.rendererTopPadding);
                changed = true;
            }

            // Defaults robustos para que no dependa de rect transforms bakeados.
            if (!bar.autoUseRendererTopWhenNoAnchor)
            {
                bar.autoUseRendererTopWhenNoAnchor = true;
                changed = true;
            }
            if (bar.rendererTopPadding < 0.01f)
            {
                bar.rendererTopPadding = 0.3f;
                changed = true;
            }
            if (!bar.keepConstantWorldSize)
            {
                bar.keepConstantWorldSize = true;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(bar.defaultAnchorName))
            {
                bar.defaultAnchorName = "BarAnchor";
                changed = true;
            }

            details = $"bar={bar.name}, keepConstantWorldSize={bar.keepConstantWorldSize}, anchor={(settings.barAnchor != null ? settings.barAnchor.name : "null")}";
        }
        catch (System.Exception ex)
        {
            details = ex.Message;
            return FixResult.Error;
        }
        finally
        {
            if (root != null)
            {
                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return changed ? FixResult.Updated : FixResult.AlreadyOk;
    }

    static void DiagnosePrefab(string prefabPath)
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
#pragma warning disable CS0618
            var bar = root.GetComponentInChildren<HealthBarWorld>(true);
#pragma warning restore CS0618
            var health = root.GetComponentInChildren<Health>(true);
            var settings = root.GetComponent<WorldBarSettings>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var anchorByName = FindByName(root.transform, "BarAnchor");
            Debug.Log(
                $"[WorldBar Diagnose] {prefabPath} | " +
                $"HealthBarWorld={(bar != null)}, Health={(health != null)}, Settings={(settings != null)}, " +
                $"Settings.anchor={(settings != null && settings.barAnchor != null ? settings.barAnchor.name : "null")}, " +
                $"AnchorByName={(anchorByName != null ? anchorByName.name : "null")}, Renderers={renderers.Length}"
            );
        }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindByName(Transform root, string name)
    {
        if (root == null) return null;
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == name) return all[i];
        return null;
    }

    static void PositionAnchorAtRendererTop(Transform root, Transform anchor, float padding)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled) continue;
            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        if (!hasBounds)
        {
            anchor.localPosition = new Vector3(0f, 2f, 0f);
            anchor.localRotation = Quaternion.identity;
            return;
        }

        Vector3 worldTop = new Vector3(bounds.center.x, bounds.max.y + Mathf.Max(0f, padding), bounds.center.z);
        anchor.position = worldTop;
        anchor.localRotation = Quaternion.identity;
    }
}
