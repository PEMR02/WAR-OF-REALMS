# Plan: Vida, daño y recursos seleccionables

Objetivos:
1. **Sistema de vida** para unidades y edificios (base para ataque y daños).
2. **Recursos seleccionables** con información: cantidad actual y máxima.
3. **Preparación a futuro**: un recurso puede otorgar más de un tipo (ej. árbol → madera + leña).

---

## 1. Sistema de vida (unidades y edificios)

### 1.1 Interfaz y componente reutilizable

- **`IHealth`** (opcional): para que cualquier cosa que tenga vida exponga lo mismo (unidades, edificios, tal vez destructibles).
  - `int CurrentHP { get; }`
  - `int MaxHP { get; }`
  - `bool IsAlive { get; }`
  - `void TakeDamage(int amount, object source = null)`
  - Evento `System.Action OnDeath` o `UnityEvent` para reaccionar (destruir, soltar celdas, etc.).

- **`Health`** (MonoBehaviour): componente que lleva la vida.
  - Campos: `maxHP`, `currentHP` (runtime).
  - Inicialización: `currentHP = maxHP` en `Start()` o desde SO.
  - `TakeDamage(int)` reduce `currentHP`; si llega a ≤ 0, dispara `OnDeath` y destruye/desactiva el GameObject (o solo notifica, según diseño).
  - Opcional: `Heal(int)` para reparación/curación.

### 1.2 Unidades

- **UnitSO** ya tiene `maxHP` (y `attack` para más adelante).
- En el **prefab de unidad**: añadir componente **Health**.
  - En runtime, inicializar `Health.maxHP` desde `UnitSO.maxHP` (necesitamos referencia al UnitSO en la instancia, p. ej. un componente `UnitInstance` que tenga el SO y en Start asigne `Health.maxHP = unitSO.maxHP`).
  - O que el prefab tenga `Health` con valor por defecto y un script que lo sincronice con el SO al spawnear.

### 1.3 Edificios

- **BuildingSO**: añadir campo `maxHP` (ej. Casa 300, Town Center 500, Cuartel 400).
- **BuildingInstance** (o el prefab): añadir **Health**.
  - Inicializar `maxHP` desde `buildingSO.maxHP` en Start; `currentHP = maxHP` cuando el edificio está completo.
  - BuildSite: mientras se construye se puede dejar sin Health o con Health en 0 y al completar asignar maxHP y currentHP.

- Al **morir** (OnDeath): liberar celdas (BuildingInstance ya tiene FreeCells en OnDestroy), destruir GameObject.

### 1.4 Barra en mundo unificada (IWorldBarSource)

- **Objetivo**: una sola barra visual para vida (unidades/edificios) y “cantidad restante” (recursos).
- **`IWorldBarSource`**: interfaz con `GetBarRatio01()`, `GetBarFullColor()`, `GetBarEmptyColor()`, `IsBarVisible()`.
- **`Health`** implementa `IWorldBarSource` (ratio = CurrentHP/MaxHP, verde/rojo).
- **`ResourceNode`** implementa `IWorldBarSource` (ratio = amount/MaxAmount; colores por tipo: madera, piedra, oro, comida). Tiene `maxAmount` (si 0, se usa el amount inicial como máximo).
- **`HealthBarWorld`**: lee `IWorldBarSource` en el padre; mismo prefab para unidades, edificios y recursos. Solo visible cuando la entidad está seleccionada.
- **Fallback**: si la entidad tiene `IWorldBarSource` pero no tiene hijo `HealthBarWorld`, el `RTSSelectionController` instancia `healthBarFallbackPrefab` al seleccionar. Así los recursos (cuando se añada selección de recursos) también pueden mostrar barra sin tenerla en el prefab.

### 1.5 Orden de prioridad en selección

- Mantener: **Unidades** > **Edificios** > (nuevo) **Recursos**. Así un clic en un árbol no quita la selección de tropas si no aplica.

---

## 2. Recursos seleccionables e información

### 2.1 ResourceNode: cantidad actual y máxima

- Hoy: `amount` es la cantidad actual y se reduce con `Take()`.
- Añadir **cantidad máxima** para mostrar “240 / 300”:
  - Opción A: campo `maxAmount` en ResourceNode; al colocar (MapResourcePlacer) se setea `amount` y `maxAmount` (ej. 300).
  - Opción B: guardar `initialAmount` en el primer Awake/Start y usarlo como “máximo” para la UI.
- Propuesta: **`maxAmount`** (público, opcional). Si es 0, se inicializa una vez con el valor actual de `amount` (comportamiento backward compatible). La UI muestra `amount / maxAmount`.

### 2.2 Selección de recursos

- **ResourceSelectable**: componente en el mismo GameObject que `ResourceNode` (o en hijos con collider). Similar a UnitSelectable/BuildingSelectable: highlight opcional y marca “este objeto es el recurso seleccionado”.
- **RTSSelectionController**:
  - Añadir **resourceLayerMask** (capa Resource o la que use el generador).
  - En **ClickSelect** (un solo clic): después de Units y Buildings, hacer raycast contra resourceLayerMask. Si hay hit, seleccionar ese recurso (y deseleccionar unidades/edificios, o mantener “solo un recurso seleccionado” según UX).
- **Estado de selección**: guardar `ResourceSelectable _selectedResource` (o directamente ResourceNode). Método `GetSelectedResource()` para la UI.

### 2.3 UI de recurso seleccionado

- **Panel o línea de texto** que solo se muestra cuando hay un recurso seleccionado.
  - Texto: “Madera: 240 / 300” (o “Árbol – Madera 240/300”).
  - Se actualiza cada frame o por evento cuando el recurso cambie (Take) o se deseleccione.
- Opciones:
  - Reutilizar zona del HUD (debajo de recursos del jugador) o un panel a un lado.
  - Un **SelectionInfoHUD** o extender **ProductionHUD**/otro HUD: si hay edificio seleccionado → panel producción; si hay recurso → panel recurso (cantidad actual/máx).

---

## 3. Múltiples tipos de recurso por nodo (a futuro)

Objetivo: que un mismo nodo pueda dar varios recursos (ej. árbol → madera + leña para “energía calórica”).

### 3.1 Modelo de datos

- **Opción A – Contenedor por tipo**  
  En **ResourceNode**: en lugar de un solo `kind` + `amount`, tener una lista de “stocks”:
  - `[Serializable] struct ResourceStock { public ResourceKind kind; public int amount; public int maxAmount; }`
  - `List<ResourceStock> stocks;`  
  Recolector elige “qué recolectar” o se define un “primario” y el resto se otorga junto (ej. por cada 10 madera, 1 leña).

- **Opción B – Recurso principal + secundarios**  
  Mantener `kind` + `amount` como recurso principal. Añadir:
  - `[Serializable] struct SecondaryYield { public ResourceKind kind; public int amountTotal; public int amountRemaining; }`  
  - O “por cada N de principal, dar M de secundario” (ratio).  
  Así VillagerGatherer sigue trabajando con un solo “tipo llevado” (el principal) y al depositar se añade también el secundario según ratio.

Recomendación inicial: **Opción B** para no romper el flujo actual de recolección; la UI del nodo puede mostrar “Madera 240/300 · Leña 20/50” cuando existan secundarios.

### 3.2 ResourceKind

- Si se añade “Leña” (o energía calórica), ampliar el **enum ResourceKind** (ej. `Wood, Stone, Gold, Food, Firewood`) y **PlayerResources** (nuevo campo + Add/Subtract/Get/Has). DropOffPoint y edificios que acepten recursos tendrían que aceptar el nuevo tipo donde corresponda.

### 3.3 Pasos concretos (más adelante)

1. Añadir a **ResourceNode** una lista opcional de “yields secundarios” (tipo + cantidad máxima y restante, o ratio respecto al principal).
2. En **VillagerGatherer**: al hacer `Take()` del recurso principal, calcular y otorgar secundarios (ej. cada 10 madera → 1 leña al jugador).
3. **UI de recurso seleccionado**: mostrar también los secundarios (cantidad actual/máx o “hasta X leña”).

---

## 4. Orden de implementación sugerido

| Fase | Qué hacer |
|------|-----------|
| **1** | Health + IHealth; BuildingSO.maxHP; Health en BuildingInstance y en prefabs de unidades (inicializar desde UnitSO). OnDeath: destruir y, en edificios, liberar celdas. |
| **2** | ResourceNode: `maxAmount` (y lógica para “si 0, usar amount inicial”). MapResourcePlacer: asignar maxAmount al colocar. |
| **3** | ResourceSelectable + resourceLayerMask en RTSSelectionController; ClickSelect con prioridad Units → Buildings → Resources; GetSelectedResource(). |
| **4** | UI: panel o texto “Recurso: actual / máximo” cuando hay recurso seleccionado (SelectionInfoHUD o panel dedicado). |
| **5** | (Futuro) Múltiples yields en ResourceNode y extensión de recolección/UI. |

Con esto se tiene base de vida para ataque/daño, recursos seleccionables con info clara, y el diseño listo para extender a varios recursos por nodo (madera + leña, etc.) sin rehacer todo.
