# Estándar RTS: footprint lógico, collider y mesh visual (WAR OF REALMS)

Referencia: AoE II, StarCraft II, Anno 1800 — **tres capas separadas**, sin usar el mesh como verdad del grid.

---

## 1. Las tres capas del edificio

| Capa | Qué es | Fuente de verdad |
|------|--------|-------------------|
| **1. Footprint lógico** | Celdas que ocupa, snap, bloqueo, ocupación | `BuildingSO.size` (en celdas) |
| **2. Volumen físico / colisión** | BoxCollider, NavMeshObstacle | Debe seguir el footprint lógico (size × cellSize) |
| **3. Mesh visual** | Modelo 3D (puede sobresalir un poco) | Libre artísticamente; la base jugable es el footprint |

**Regla:** El grid y el placement **nunca** dependen del mesh. Si un asset viene descentrado o con pivot raro, se corrige en el hijo **Visual**, no en el root ni en el SO.

---

## 2. Estructura objetivo del prefab

```
BuildingRoot (centro del footprint; idealmente position 0,0,0 scale 1,1,1)
 ├── Visual          ← mesh; aquí se corrige escala/offset/rotación del asset
 ├── Footprint       ← objeto auxiliar: escala exacta = size × cellSize (referencia/gizmo)
 ├── Collider        ← en muchos prefabs está en el root (BoxCollider); debe seguir footprint
 ├── BarAnchor       ← barra de vida
 └── SpawnPoint      ← solo edificios de producción
```

- **Root:** Centro del footprint en XZ; Y = punto de apoyo (o lo compensa BuildingAnchorSolver).
- **Footprint:** Child vacío (o con `BuildingFootprintGizmo`) cuya `localScale` = `(size.x * cellSize, 0.1, size.y * cellSize)`. Sirve para ver en Scene View “esto es lo que ocupa realmente”.
- **Visual:** Hijo con el modelo; si el mesh viene mal, se ajusta aquí (escala, posición, rotación) sin tocar el root.
- **Collider:** En runtime `BuildingController` aplica tamaño desde `BuildingSO.size * cellSize`; puede estar en el root.

---

## 3. Tamaño del Footprint (objeto auxiliar)

`cellSize = 2.5` m (GridConfig / MapGrid).

| Edificio | BuildingSO.size | Tamaño teórico (m) | Footprint localScale (X, 0.1, Z) |
|----------|-----------------|--------------------|-----------------------------------|
| TownCenter | 3×3 | 7.5 × 7.5 | (7.5, 0.1, 7.5) |
| House | 3×3 | 7.5 × 7.5 | (7.5, 0.1, 7.5) |
| Barracks | 2×2 | 5 × 5 | (5, 0.1, 5) |

El mesh puede ser un poco mayor (p. ej. 8 m) si la **base** jugable sigue clara.

---

## 4. Qué está implementado

- **BuildingFootprintGizmo.cs:** Componente que dibuja en Scene View un wire del tamaño del transform (localScale = tamaño en metros). Se puede poner en el child **Footprint**.
- **Editor:** Menú **Tools → Project → Añadir o actualizar Footprint (edificio seleccionado)** y **Tools → Project → Añadir Footprint a TownCenter, House, Barracks**. Añade o actualiza el child **Footprint** con escala `BuildingSO.size × cellSize` y agrega `BuildingFootprintGizmo` si falta.
- **Prefabs:**
  - **PF_TownCenter:** Child **Footprint** añadido, scale (7.5, 0.1, 7.5). Ejecutar el menú una vez en Unity para añadir el Gizmo si se desea ver el cuadro en Scene View.
  - **PF_Barracks:** Child **Footprint** añadido, scale (5, 0.1, 5).
  - **PF_House:** Estructura anidada; usar **Tools → Project → Añadir Footprint a TownCenter, House, Barracks** para que se añada el Footprint al abrir Unity.

---

## 5. Cómo usar el Footprint para validar

1. Abrir el prefab en Unity.
2. Seleccionar el child **Footprint** (o el root del edificio).
3. En Scene View, si tiene `BuildingFootprintGizmo`, se dibuja un wire del tamaño exacto del footprint.
4. Comprobar si el **mesh visual** cabe dentro (o sobresale poco). Si no cabe: ajustar escala/posición del hijo **Visual**, no el BuildingSO ni el root del footprint.
5. Si el prefab no tiene child Footprint: **Tools → Project → Añadir o actualizar Footprint (edificio seleccionado)** con el prefab seleccionado.

---

## 6. Tabla de validación (referencia)

| Edificio | size (celdas) | Tamaño teórico (m) | Tamaño visual estimado | Corrección recomendada |
|----------|----------------|--------------------|-------------------------|-------------------------|
| TownCenter | 3×3 | 7.5×7.5 | Root scale 7.5 → ~7.5 m | Footprint añadido; validar en escena |
| House | 3×3 | 7.5×7.5 | Modelo scale 3.75 → ~7.5 m | Añadir Footprint con menú; validar encaje |
| Barracks | 2×2 | 5×5 | Root scale 5,5,5 → 5×5 m | Footprint añadido; validar en escena |

---

## 7. Nivel siguiente (recomendado)

- **Validación automática:** Script de editor que lea `BuildingSO.size` y el transform **Footprint** del prefab y avise si la escala no coincide (p. ej. “Prefab Footprint mide 8.2 m → inconsistente con 3×3 = 7.5 m”).
- **Ghost en placement:** Usar el mismo tamaño del Footprint para dibujar el ghost de colocación y que el jugador vea exactamente lo que ocupa.

---

## 8. Resumen

- **Footprint lógico** = `BuildingSO.size` → manda en snap, celdas y bloqueo.
- **Footprint (child)** = objeto con escala exacta para no pelear con el mesh; opcionalmente con Gizmo para verlo en editor.
- **Mesh visual** = libre; si no calza, se corrige en el hijo Visual, no en el código del placement.

Documento generado a partir del estándar AoE/Anno/SC2 para WAR OF REALMS.
