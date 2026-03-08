# Bloque 3 — Auditoría y normalización de prefabs

## 1. Tabla por prefab (edificios)

| Prefab | BuildingSO | BuildingInstance | BuildingSelectable | Health | BarAnchor | Notas |
|--------|------------|-----------------|--------------------|--------|----------|--------|
| PF_TownCenter | **null** | ✓ | (revisar) | (revisar) | (revisar) | **Asignar TownCenter_BuildingSO** |
| PF_Barracks | ✓ | ✓ | (revisar) | (revisar) | (revisar) | OK SO |
| PF_House | ✓ | ✓ | (revisar) | (revisar) | (revisar) | OK SO |
| PF_Castillo | ✓ | ✓ | (revisar) | (revisar) | (revisar) | OK SO |
| PF_Granary | **null** | ✓ | (revisar) | (revisar) | (revisar) | **Asignar Granary_BuildingSO** |
| PF_LumberCamp | **null** | ✓ | (revisar) | (revisar) | (revisar) | **Asignar LumberCamp_BuildingSO** |
| PF_MiningCamp | **null** | ✓ | (revisar) | (revisar) | (revisar) | **Asignar MiningCamp_BuildingSO** |
| PF_Buildsite | N/A | BuildSite | buildingSO en runtime | - | (BuildSite asigna) | OK |

**Acción inmediata en Unity:** En cada edificio con `buildingSO: {fileID: 0}`, asignar el ScriptableObject correspondiente en el componente BuildingInstance (TownCenter_BuildingSO, Granary_BuildingSO, etc.).

---

## 2. Estructura jerárquica recomendada

### Edificios (raíz del prefab)

```
Raíz
├── Transform (pivot en base lógica del edificio)
├── BuildingInstance (buildingSO asignado)
├── BuildingController
├── Health (si aplica)
├── BuildingSelectable
├── NavMeshObstacle
├── Collider principal (según footprint)
├── Visual (hijo: mesh/material)
├── BarAnchor (hijo vacío, posición sobre techo/punto alto)
├── SelectionBounds (opcional)
├── SpawnPoint (si produce unidades)
└── DropOffAnchor (si aplica)
```

### Unidades (raíz del prefab)

```
Raíz
├── NavMeshAgent
├── UnitMover
├── UnitSelectable
├── Health
├── Scripts de rol (VillagerGatherer, Builder, Repairer…)
├── Collider de selección
├── Visual (hijo)
├── BarAnchor (hijo)
├── Animator
└── CarryAnchor (si aplica)
```

---

## 3. Checklist de revisión manual en Unity

### Por edificio
- [ ] **buildingSO** asignado en BuildingInstance (nunca null en prefab final).
- [ ] **Pivot** en la base real del modelo (no en el centro del mundo).
- [ ] **Collider** según footprint (no “a ojímetro”); mismo tamaño que BuildingSO.size en celdas.
- [ ] **BarAnchor** sobre el techo o punto alto visible (para barras de vida).
- [ ] **Renderers** definidos; sin referencias rotas.
- [ ] **NavMeshObstacle** consistente con el tamaño.
- [ ] Sin flags de testing/debug activas en prefab final.

### Por unidad
- [ ] **Escala** real del modelo (1 unidad = 1m o tu estándar).
- [ ] **Pivot** en los pies.
- [ ] **Collider** coherente con el cuerpo (selección).
- [ ] **Animator** y **Avatar** correctamente asignados.
- [ ] **Materiales** correctos (no blancos/rosa).
- [ ] **BarAnchor** bien puesto (encima de la cabeza).

### Assets importados (FBX/modelos)
- [ ] Scale Factor correcto.
- [ ] Rotación de importación.
- [ ] Materiales enlazados.
- [ ] Rig (Humanoide o Genérico).
- [ ] Avatar válido.
- [ ] Animaciones importadas.
- [ ] Root transform correcto.

---

## 4. Propuesta de normalización sin cambiar jugabilidad

1. **Asignar BuildingSO** en todos los prefabs de edificios que tengan `buildingSO: 0` (TownCenter, Granary, LumberCamp, MiningCamp).
2. Revisar **pivot** de cada edificio: debe estar en la base para que el placement por footprint quede coherente.
3. Unificar **BarAnchor**: un hijo vacío con Transform en el punto donde debe aparecer la barra; HealthBarManager lo usa.
4. No eliminar componentes existentes que el juego use; solo rellenar referencias vacías y ajustar jerarquía en nuevos prefabs.

---

*Auditoría Bloque 3. Asignaciones de BuildingSO y ajustes de pivot/BarAnchor deben hacerse en el Editor de Unity.*
