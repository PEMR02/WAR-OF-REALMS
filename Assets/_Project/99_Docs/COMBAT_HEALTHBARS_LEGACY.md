# Combat / HealthBars — Sistema principal vs legacy

## Sistema principal (usar siempre)

- **HealthBarManager** (Singleton): Registra/desregistra entidades con Health; crea y posiciona barras en Screen Space.
- **HealthBarUI**: Una barra por entidad; Bind(Health), Refresh() en LateUpdate.
- **Prefab:** `PF_HealthBarUI.prefab` (Border, Background, Fill; sin raycast).
- **Flujo:** Al seleccionar una unidad/edificio/recurso, `RTSSelectionController` llama `HealthBarManager.ShowBarForEntity(entity)`. No se instancia barra como hijo de la unidad.

## Legacy (no usar en contenido nuevo)

- **HealthBarWorld**: Script en Canvas World Space por unidad/edificio. Marcado `[Obsolete]`. Se mantiene solo para prefabs que aún lo tienen.
- **PF_WorldHealthBar.prefab**: Prefab de barra world-space; ya no se usa en el flujo de selección (HealthBarManager usa PF_HealthBarUI).

## Prefabs que aún tienen HealthBarWorld

Estos prefabs siguen teniendo el componente HealthBarWorld como hijo (o en un Canvas hijo). El juego funciona porque el flujo de selección usa HealthBarManager; si el prefab tiene además HealthBarWorld, pueden convivir (doble barra) o puedes desactivar/eliminar el objeto con HealthBarWorld en Unity.

| Prefab |
|--------|
| PF_Castillo |
| PF_TownCenter |
| PF_House |
| PF_Barracks |
| PF_Granary |
| PF_LumberCamp |
| PF_MiningCamp |
| PF_WorldHealthBar (prefab de barra legacy) |

## Migración recomendada (en Unity)

1. En cada edificio/unidad que tenga un hijo con **HealthBarWorld** (o Canvas con HealthBarWorld):
   - Desactivar o eliminar ese GameObject hijo (la barra se mostrará con HealthBarManager + PF_HealthBarUI al seleccionar).
2. Asegurar que en la escena hay un GameObject con **HealthBarManager** y **PF_HealthBarUI** asignado en "Health Bar Prefab".
3. Cuando ningún prefab use HealthBarWorld, se puede eliminar el script **HealthBarWorld.cs**, el prefab **PF_WorldHealthBar** y las referencias en **WorldBarPrefabTools** (Editor).

## Puntos de eliminación futura (TODO)

- `HealthBarWorld.cs`: eliminar cuando no quede prefab con el componente.
- `WorldBarSettings.cs`: usado por HealthBarWorld; revisar si IWorldBarSource/Health lo usan para colores; si no, eliminar con HealthBarWorld.
- `Editor/WorldBarPrefabTools.cs`: menús "Health Bars / Auto Fix..."; eliminar o adaptar a HealthBarUI cuando se retire HealthBarWorld.

---

*Documento Bloque 8 — auditoría Combat/HealthBars.*
