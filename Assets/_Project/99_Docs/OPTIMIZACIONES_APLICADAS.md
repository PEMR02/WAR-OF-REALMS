# Optimizaciones aplicadas

Resumen de cambios de rendimiento sin alterar el comportamiento visible.

---

## 1. HealthBarManager — Cull por distancia

- **Campo:** `maxBarDistance` (default 120 u).
- **Lógica:** Antes de `WorldToScreenPoint`, se calcula la posición mundial de la barra. Si la distancia al cuadrado entre esa posición y la cámara supera `maxBarDistance²`, la barra se oculta y se salta `WorldToScreenPoint` y `Refresh()`.
- **Efecto:** Con muchas unidades/edificios lejos de cámara se evitan llamadas costosas por barra; las barras lejanas no se actualizan (quedan ocultas). Ajustable en el Inspector.

---

## 2. RTSSelectionController — Throttle del hover sobre recursos

- **Lógica:** El raycast de hover sobre recursos (árboles, piedra, etc.) se ejecuta solo cada **2 frames** cuando hay aldeanos seleccionados (`_resourceHoverFrameSkip` y comprobación `(_resourceHoverFrameSkip & 1) != 0`).
- **Efecto:** Aproximadamente la mitad de raycasts en esa ruta; el hover sigue siendo fluido.

---

## 3. BuildingController — Cache de BuildingInstance

- **Campos:** `_buildingInstanceCached`, `_buildingInstanceResolved`.
- **Lógica:** En `TryApplyFootprintWhenGridReady()` se llama a `GetComponent<BuildingInstance>()` solo la primera vez; el resultado se guarda y se reutiliza.
- **Efecto:** Se evitan llamadas repetidas a GetComponent en cada frame hasta que el footprint se aplica (normalmente una vez por edificio).

---

## 4. OrderFeedbackMarker — Cache de Renderer

- **Campo:** `_cachedRenderer`.
- **Lógica:** Se asigna en `Awake()` con `GetComponent<Renderer>()`. En `Update()` se usa `_cachedRenderer` en lugar de GetComponent.
- **Efecto:** Sin GetComponent por frame en cada marcador de orden (mover, rally).

---

## 5. UnitAnimatorDriver — Cache de AnimalPastureBehaviour

- **Campo:** `_skipDriver` (bool).
- **Lógica:** En `Awake()` se hace una vez `GetComponentInParent<AnimalPastureBehaviour>() != null` y se guarda en `_skipDriver`. En `Update()` se hace `if (_skipDriver) return;` en lugar de GetComponentInParent cada frame.
- **Efecto:** En unidades que son animales (vaca, etc.) no se hace GetComponentInParent en cada frame.

---

## Resumen de archivos modificados

| Archivo | Cambio |
|---------|--------|
| HealthBarManager.cs | `maxBarDistance`, cull por distancia antes de WorldToScreenPoint. |
| RTSSelectionController.cs | Throttle raycast hover recursos (cada 2 frames). |
| BuildingController.cs | Cache de BuildingInstance en TryApplyFootprintWhenGridReady. |
| OrderFeedback.cs (OrderFeedbackMarker) | Cache de Renderer en Awake. |
| UnitAnimatorDriver.cs | Cache de “tiene AnimalPastureBehaviour” en Awake. |

---

## Optimizaciones ya presentes (sesiones anteriores)

- RTSOrderController: cache de componentes por unidad (CachedUnitComponents).
- HealthBarManager: lista reutilizable `_toRemove` (zero alloc en LateUpdate/Unregister).
- BuildSite: lista reutilizable `_buildersSnapshot`.
- UnitSelectable: propiedad cacheada `IsVillager`.
- HealthBarWorld desactivado en prefabs de edificios (evita doble LateUpdate).
