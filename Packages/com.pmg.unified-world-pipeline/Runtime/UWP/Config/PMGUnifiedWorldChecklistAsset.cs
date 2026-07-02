using System;
using System.Collections.Generic;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    public enum PMGUnifiedWorldChecklistStatus
    {
        NotStarted = 0,
        InProgress = 1,
        Done = 2,
        Blocked = 3,
        Skipped = 4
    }

    [Serializable]
    public class PMGUnifiedWorldChecklistSegment
    {
        public string id = "SETUP";
        public string title = "Segmento";
        [TextArea(2, 4)] public string description;
        public int sortOrder;
    }

    [Serializable]
    public class PMGUnifiedWorldChecklistItem
    {
        public string id;
        public string segmentId = "SETUP";
        public string title;
        [TextArea(2, 5)] public string description;
        public PMGUnifiedWorldChecklistStatus status = PMGUnifiedWorldChecklistStatus.NotStarted;
        [TextArea(1, 3)] public string notes;
        public PMGUnifiedWorldQualityAspect linkedAspect = PMGUnifiedWorldQualityAspect.Overall;
        public float lastScore0To10 = -1f;
        public string lastGradeLetter;
    }

    /// <summary>
    /// Checklist persistente del pipeline unificado. Permite retomar el trabajo por segmentos.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PMGUnifiedWorldChecklist",
        menuName = "PMG/Unified World Pipeline/Checklist",
        order = 1)]
    public class PMGUnifiedWorldChecklistAsset : ScriptableObject
    {
        public string pipelineVersion = "1.0.0";
        [TextArea(2, 4)] public string missionSummary =
            "Pipeline único basado en MapGenerator (juego) + evaluación por aspectos estilo index.html.";

        public List<PMGUnifiedWorldChecklistSegment> segments = new List<PMGUnifiedWorldChecklistSegment>();
        public List<PMGUnifiedWorldChecklistItem> items = new List<PMGUnifiedWorldChecklistItem>();

        public int CurrentSegmentIndex { get; set; }

        public void EnsureDefaultStructure()
        {
            if (segments != null && segments.Count > 0 && items != null && items.Count > 0)
                return;

            segments = new List<PMGUnifiedWorldChecklistSegment>
            {
                new PMGUnifiedWorldChecklistSegment { id = "SETUP", title = "1. Setup", description = "PMGUnifiedWorldPipelineConfig + capas/materiales propios.", sortOrder = 0 },
                new PMGUnifiedWorldChecklistSegment { id = "LOGIC", title = "2. Generación lógica", description = "GridSystem definitivo sin export visual (rápido).", sortOrder = 1 },
                new PMGUnifiedWorldChecklistSegment { id = "HYDRO", title = "3. Hidrología", description = "Ríos orgánicos, lagos BFS, cobertura agua.", sortOrder = 2 },
                new PMGUnifiedWorldChecklistSegment { id = "SURFACE", title = "4. Superficie", description = "TerrainExporter + WaterMeshBuilder con materiales del juego.", sortOrder = 3 },
                new PMGUnifiedWorldChecklistSegment { id = "NAV", title = "5. NavMesh", description = "Walkable, exclusiones agua, franjas finas.", sortOrder = 4 },
                new PMGUnifiedWorldChecklistSegment { id = "GAMEPLAY", title = "6. Gameplay", description = "Ciudades, caminos, recursos, fairness.", sortOrder = 5 },
                new PMGUnifiedWorldChecklistSegment { id = "POLISH", title = "7. Pulido", description = "Batch seeds, notas manuales, congelar perfil.", sortOrder = 6 }
            };

            items = new List<PMGUnifiedWorldChecklistItem>
            {
                Item("setup-match", "SETUP", "Config UWP", "PMGUnifiedWorldPipelineConfig con modo independiente activo.", PMGUnifiedWorldQualityAspect.Overall),
                Item("setup-rts", "SETUP", "Materiales / capas", "Grass, rock, agua asignados en el config UWP (no requiere RTS).", PMGUnifiedWorldQualityAspect.VisualWater),
                Item("setup-materials", "SETUP", "Layers / materiales explícitos", "Asignar TerrainLayers y materiales agua en Pipeline Config.", PMGUnifiedWorldQualityAspect.VisualWater),
                Item("logic-generate", "LOGIC", "Preview lógico", "Generar GridSystem con MapGenerator (skip surface).", PMGUnifiedWorldQualityAspect.Overall),
                Item("logic-batch", "LOGIC", "Batch de seeds", "Evaluar N seeds y guardar top candidatos.", PMGUnifiedWorldQualityAspect.Overall),
                Item("hydro-rivers", "HYDRO", "Nota ríos", "Span, meandro, afluentes, straightness.", PMGUnifiedWorldQualityAspect.Rivers),
                Item("hydro-endpoints", "HYDRO", "Nota extremos río", "Sin ensanche en borde del mapa.", PMGUnifiedWorldQualityAspect.RiverEndpoints),
                Item("hydro-lakes", "HYDRO", "Nota lagos", "Cantidad, tamaño mínimo, dispersión orgánica.", PMGUnifiedWorldQualityAspect.Lakes),
                Item("hydro-coast", "HYDRO", "Nota costa", "Banda costera y océano vs tierra jugable.", PMGUnifiedWorldQualityAspect.Coastline),
                Item("surf-terrain", "SURFACE", "Export terreno", "Terrain + splat con layers del RTS.", PMGUnifiedWorldQualityAspect.TerrainRelief),
                Item("surf-water", "SURFACE", "Meshes agua", "Pipeline visual del MapGenConfig (WebFusion/Mouth).", PMGUnifiedWorldQualityAspect.VisualWater),
                Item("nav-bake", "NAV", "NavMesh bake", "Bake y medir walkable vs agua.", PMGUnifiedWorldQualityAspect.NavMeshWalkable),
                Item("nav-thin", "NAV", "Franjas finas", "Detectar tiras walkable < 2 celdas junto a río.", PMGUnifiedWorldQualityAspect.NavMeshWalkable),
                Item("gp-cities", "GAMEPLAY", "Ciudades/spawns", "Separación mínima y conectividad caminos.", PMGUnifiedWorldQualityAspect.CityFairness),
                Item("gp-resources", "GAMEPLAY", "Recursos", "Densidad y distancia a TC.", PMGUnifiedWorldQualityAspect.ResourcePlacement),
                Item("polish-manual", "POLISH", "Revisión visual manual", "Nota subjetiva agua/terreno en escena.", PMGUnifiedWorldQualityAspect.VisualWater),
                Item("polish-freeze", "POLISH", "Congelar perfil", "Guardar seed + scores en SessionRoot.", PMGUnifiedWorldQualityAspect.Overall)
            };
        }

        static PMGUnifiedWorldChecklistItem Item(
            string id,
            string segment,
            string title,
            string desc,
            PMGUnifiedWorldQualityAspect aspect)
        {
            return new PMGUnifiedWorldChecklistItem
            {
                id = id,
                segmentId = segment,
                title = title,
                description = desc,
                linkedAspect = aspect,
                status = PMGUnifiedWorldChecklistStatus.NotStarted
            };
        }

        public void ApplyReportToChecklist(PMGUnifiedWorldQualityReport report)
        {
            if (items == null || report.aspects == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                PMGUnifiedWorldChecklistItem item = items[i];
                if (item.linkedAspect == PMGUnifiedWorldQualityAspect.Overall) continue;

                for (int a = 0; a < report.aspects.Length; a++)
                {
                    if (report.aspects[a].aspect != item.linkedAspect) continue;
                    item.lastScore0To10 = report.aspects[a].score0To10;
                    item.lastGradeLetter = report.aspects[a].gradeLetter;
                    if (item.status == PMGUnifiedWorldChecklistStatus.NotStarted)
                        item.status = PMGUnifiedWorldChecklistStatus.InProgress;
                    if (report.aspects[a].score0To10 >= 6f)
                        item.status = PMGUnifiedWorldChecklistStatus.Done;
                    break;
                }
            }
        }

        public float Progress01()
        {
            if (items == null || items.Count == 0) return 0f;
            int done = 0;
            for (int i = 0; i < items.Count; i++)
            {
                PMGUnifiedWorldChecklistStatus s = items[i].status;
                if (s == PMGUnifiedWorldChecklistStatus.Done || s == PMGUnifiedWorldChecklistStatus.Skipped)
                    done++;
            }

            return done / (float)items.Count;
        }
    }
}
