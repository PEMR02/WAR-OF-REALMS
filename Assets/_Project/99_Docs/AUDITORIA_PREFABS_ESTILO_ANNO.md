# Auditoría prefabs — estilo Anno (anti-flotar)

Revisión de prefabs de edificios y unidades para que la colocación sea coherente con el grid y el terreno (sin flotar).

---

## 1. BuildingSO asignado

| Prefab | BuildingSO | Estado |
|--------|------------|--------|
| PF_TownCenter | TownCenter_BuildingSO | Asignado (corregido) |
| PF_Granary | Granary_BuildingSO | Asignado (corregido) |
| PF_LumberCamp | LumberCamp_BuildingSO | Asignado (corregido) |
| PF_MiningCamp | MiningCamp_BuildingSO | Asignado (corregido) |
| PF_House | House_SO | OK |
| PF_Barracks | Barracks_SO | OK |
| PF_Castillo | Casttle_SO | OK |
| PF_Buildsite | (runtime) | N/A |

Todos los edificios jugables tienen ya BuildingSO asignado en el componente BuildingInstance.

---

## 2. Estructura por prefab (edificios)

Todos los edificios revisados tienen:
- **BuildingInstance** + **Health** + **BarAnchor** (hijo con nombre "BarAnchor").
- **HealthBarWorld** (legacy): presente en todos; el flujo actual usa HealthBarManager + PF_HealthBarUI. Para evitar doble barra se puede desactivar el GameObject que contiene HealthBarWorld en Unity, o migrar más adelante.

Recomendación para no flotar:
- **Pivot del prefab raíz:** Debe estar en la **base** del modelo (donde toca el suelo). Si el modelo viene con pivot en el centro, en Unity: abre el prefab, coloca el pivot en la base (o usa un GameObject raíz vacío con pivot en suelo y el modelo como hijo con offset).
- **BarAnchor:** Debe ser un hijo vacío con Transform encima del edificio (techo); posición Y alta para que la barra de vida se dibuje encima. Health y HealthBarManager usan este punto.
- **Collider:** Debe coincidir con el footprint en celdas (BuildingSO.size). BoxCollider con size = (size.x * cellSize, altura, size.y * cellSize) aproximado.

---

## 3. Unidades

Prefabs: PF_Peasant, PF_Archer, PF_Swordman, PF_Mounted_King.

Para que no floten al moverse:
- **Pivot en los pies** del modelo (no en el centro del cuerpo).
- **NavMeshAgent** y **collider** coherentes con la base.
- **BarAnchor** hijo encima de la cabeza (opcional; HealthBarManager lo usa si existe).

---

## 4. BuildSite (PF_Buildsite)

- Debe tener **BuildSite** y el visual de fundación.
- La **base visual** del sitio (mesh o sprite) debe coincidir con el pivot para que `AlignBuildSiteToTerrain` no desplace de más. Si el pivot está en la base del mesh, la colocación por footprint (avgHeight) ya lo deja apoyado.

---

## 5. Checklist rápido en Unity (por edificio)

- [ ] Pivot del prefab en la base del modelo.
- [ ] BuildingInstance.buildingSO asignado (ya aplicado en TownCenter, Granary, LumberCamp, MiningCamp).
- [ ] BarAnchor como hijo, posición encima del techo.
- [ ] Collider según footprint (no más grande que el edificio).
- [ ] NavMeshObstacle si aplica, coherente con el tamaño.

---

*Auditoría prefabs estilo Anno. BuildingSO corregido en 4 prefabs; el resto de ajustes (pivot, collider) se revisan en el Editor.*
