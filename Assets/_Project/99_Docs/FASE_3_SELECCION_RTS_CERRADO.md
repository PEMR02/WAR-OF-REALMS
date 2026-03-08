# Fase 3 — Refactor selección RTS cerrado

## Qué quedó reemplazado

- **Box selection:** Usa `UnitSelectableRegistry.All` para iterar todas las unidades registradas y comprobar si están dentro del rect en pantalla. No hay `FindObjectsByType<UnitSelectable>`.
- **Doble clic:** Usa `UnitSelectableRegistry.All` para seleccionar todas las unidades del mismo tipo (mismo nombre base de prefab) visibles en cámara. Tampoco hay búsqueda global por tipo.
- **UnitSelectable** se registra en `OnEnable` y se desregistra en `OnDisable` en `UnitSelectable.cs`, por lo que el registro está siempre al día.

## Qué sigue igual (y es correcto)

- **Click simple:** Un solo raycast con `unitLayerMask` y `GetComponentInParent<UnitSelectable>` en el objeto impactado. No hace falta registro; es un solo hit.
- **Edificios y recursos:** Siguen con raycast + `buildingLayerMask` / `resourceLayerMask` y sus componentes (BuildingSelectable, ResourceSelectable). No están en el registro de unidades (el registro es solo para unidades seleccionables en box/doble clic).
- **Hover sobre recursos:** Raycast por frame cuando hay aldeanos seleccionados; no usa registro.

## Qué sigue pendiente (opcional)

- **AddSelection** sigue haciendo `GetComponent<VillagerGatherer>()` y `GetComponent<Builder>()` para actualizar `_selectedVillagerCount`. Es un coste bajo (solo al añadir a la selección); se podría cachear en un campo de `UnitSelectable` o en un pequeño cache en el controller si se prioriza rendimiento extremo.

## Impacto esperado en rendimiento

- Box select y doble clic: de O(N) con FindObjectsByType (que recorre toda la escena) a O(R), donde R = número de unidades registradas (solo unidades con UnitSelectable activo). En escenas con muchos objetos que no son unidades, el ahorro es notable. En escenas con pocos objetos, la diferencia es pequeña pero no hay alloc ni búsqueda por tipo.
