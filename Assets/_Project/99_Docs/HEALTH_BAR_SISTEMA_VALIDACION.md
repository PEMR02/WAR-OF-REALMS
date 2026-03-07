# HealthBar (nuevo sistema) – Archivos y validación

## Archivos modificados

| Archivo | Cambios |
|--------|---------|
| **Assets/_Project/01_Gameplay/Combat/Health.cs** | Añadidos `barAnchor`, `fallbackOffset`, `Normalized`, `GetBarWorldPosition()`, `SetBarAnchor()`. En `OnDestroy` se llama a `HealthBarManager.Instance?.Unregister(this)`. Se mantienen `IHealth` e `IWorldBarSource`. |
| **Assets/_Project/01_Gameplay/Combat/HealthBarManager.cs** | Singleton persistente (DontDestroyOnLoad), destrucción de duplicados con warning, creación de Canvas solo como fallback con warning, validaciones y warnings únicos para refs faltantes (cámara, canvas, prefab). API estática `ShowBarForEntity` / `HideBarForEntity`. Limpieza en OnDestroy (barras + canvas creado en runtime). |
| **Assets/_Project/01_Gameplay/Combat/HealthBarUI.cs** | Cache de `_lastNormalized` para no escribir `fillAmount` cada frame si la vida no cambió. `OnDestroy` llama a `HealthBarManager.Instance?.Unregister(_target)` para evitar entradas huérfanas. |
| **Assets/_Project/01_Gameplay/Units/RTSSelectionController.cs** | Eliminada la lógica que añadía el componente Health a edificios sin Health. Eliminados cachés y prefab fallback del sistema antiguo. Uso de `HealthBarManager.ShowBarForEntity` / `HideBarForEntity` en lugar de resolver Health y Register/Unregister manualmente. |
| **Assets/_Project/01_Gameplay/Building/Construction/BuildSite.cs** | Se asigna `Health.SetBarAnchor(anchor)` cuando el edificio tiene Health. Eliminada la configuración de HealthBarWorld (billboard, escala, etc.). |
| **Assets/_Project/01_Gameplay/Map/RTSMapGenerator.cs** | Igual: se asigna `Health.SetBarAnchor(anchor)` cuando el GO tiene Health. Eliminada la configuración de HealthBarWorld. |
| **Assets/_Project/01_Gameplay/Combat/HealthBarWorld.cs** | Clase marcada con `[Obsolete]` y comentario de reemplazo por HealthBarManager + HealthBarUI. |
| **Assets/_Project/99_Docs/HEALTH_BAR_MANAGER_SETUP.md** | Actualizado: flujo principal con referencias explícitas, fallback de Canvas solo para pruebas, API reutilizable, prefab minimalista. |

## Archivos nuevos creados

| Archivo | Descripción |
|--------|-------------|
| **Assets/_Project/01_Gameplay/Combat/HealthBarUI.cs** | Script de la barra UI: Bind(Health), Refresh() con cache de normalized, RectTransform, OnDestroy para Unregister. |
| **Assets/_Project/01_Gameplay/Combat/HealthBarManager.cs** | Manager singleton: Register/Unregister, ShowBarForEntity/HideBarForEntity, LateUpdate con WorldToScreenPoint, creación de Canvas fallback opcional. |
| **Assets/_Project/08_Prefabs/PF_HealthBarUI.prefab** | Prefab minimalista: raíz con HealthBarUI, hijo Fill (Image Filled). Sin raycast, sin layout. 120×14, pivot centro. |
| **Assets/_Project/99_Docs/HEALTH_BAR_SISTEMA_VALIDACION.md** | Este documento: lista de archivos y resumen de validación. |

## Partes del sistema viejo que siguen presentes (deprecated / compatibilidad)

| Elemento | Estado | Notas |
|----------|--------|-------|
| **HealthBarWorld** (script) | Obsoleto | `[Obsolete]`. No eliminar aún: prefabs de unidades/edificios pueden seguir teniendo el componente; no se usa en la ruta activa (selección usa HealthBarManager). |
| **WorldBarSettings** | Sin uso en flujo nuevo | Sigue en prefabs/buildings; el nuevo sistema usa solo Health.barAnchor y Health.GetBarWorldPosition(). |
| **IWorldBarSource** | Mantenido | Health lo implementa; SelectionHealthBarUI (panel de selección) y posiblemente recursos lo usan. No deprecado. |
| **PF_WorldHealthBar.prefab** | Sin uso en flujo nuevo | Era el prefab fallback que se instanciaba como hijo de la unidad; ya no se referencia desde RTSSelectionController. |
| **Editor/WorldBarPrefabTools.cs** | Herramientas para HealthBarWorld | Menús "Health Bars / Auto Fix..." etc. Siguen operando sobre el sistema viejo; pueden seguir usándose para prefabs legacy o eliminarse más adelante. |

---

## Resumen de validación

### Casos que el sistema ya cumple

- **Barra sigue a la unidad:** Posición en LateUpdate con `WorldToScreenPoint(health.GetBarWorldPosition())` y asignación a `RectTransform.localPosition` en el Canvas.
- **Ocultarse detrás de cámara:** Si `screenPoint.z <= 0`, la barra se desactiva (`SetActive(false)`).
- **Destrucción sin error:** Health.OnDestroy llama a `Unregister(this)`; HealthBarUI.OnDestroy llama a `Unregister(_target)`. Manager en OnDestroy limpia todas las barras y el diccionario. LateUpdate ignora entradas con `health == null` o `bar == null` y las elimina del diccionario. No se añade Health a entidades que no lo tengan.
- **Selección múltiple:** Cada unidad/edificio con Health se registra con su propia barra; varias barras activas a la vez.
- **Una sola instancia del manager:** Singleton con destrucción de duplicados y warning.
- **Referencias faltantes:** Warnings únicos (estáticos) si faltan Canvas, prefab o cámara; Canvas creado en runtime solo como fallback con aviso.
- **API reutilizable:** `ShowBarForEntity` / `HideBarForEntity` para selección, hover, dañados o combate sin acoplar al RTSSelectionController.
- **Fill optimizado:** HealthBarUI solo actualiza `fillAmount` cuando el valor normalizado cambia por encima de una tolerancia.

### Casos pendientes o opcionales

- **Pooling de barras:** El sistema está preparado (Register crea, Unregister destruye); implementar un pool de instancias de HealthBarUI sería un paso posterior.
- **Mostrar barra solo para unidades dañadas / en combate / hover:** Lógica de cuándo llamar `ShowBarForEntity`/`HideBarForEntity` queda en tu código (HUD, combate, hover); la API ya está disponible.
- **Recursos (árboles, piedra, oro):** No tienen Health; no usan este sistema de barras flotantes. El panel de selección (SelectionHealthBarUI) sigue mostrando cantidad de recurso vía IWorldBarSource/ResourceNode.
- **Eliminar por completo HealthBarWorld y prefabs world-space:** Pendiente de cuando decidas migrar o borrar los prefabs que aún lo usan; por ahora quedan como deprecated.
