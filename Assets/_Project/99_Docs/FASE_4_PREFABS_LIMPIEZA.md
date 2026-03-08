# Fase 4 — Limpieza real de prefabs base (resumen y tabla)

## Prefabs revisados

- **Edificios:** PF_TownCenter, PF_House, PF_Barracks, PF_Granary, PF_LumberCamp, PF_MiningCamp, PF_Castillo.
- **Unidades:** PF_Peasant (villager/aldeano), PF_Swordman, PF_Archer, PF_Mounted_King.
- **Nota:** No existe `Aldeano.prefab` en el repo (fue eliminado); el villager actual es **PF_Peasant**.

---

## Tabla por prefab

| Prefab | Errores detectados | Corrección aplicada | Revisión manual pendiente en Unity |
|--------|--------------------|---------------------|------------------------------------|
| **PF_TownCenter** | `startWithPercentForTesting: 1` → vida al 50% al iniciar | `startWithPercentForTesting: 0` | Verificar buildingSO, BarAnchor, collider, que la barra de vida arranque al 100%. |
| **PF_House** | Idem (vida al 50%) | `startWithPercentForTesting: 0` | Idem; un Health tiene `barAnchor: 0` (usa fallbackOffset). |
| **PF_Barracks** | Ya tenía testing 0; `_currentHP: 0` (EnsureHPInitialized lo rellena en runtime) | Ninguna | Verificar buildingSO, BarAnchor, footprint vs collider. |
| **PF_Castillo** | Ya tenía testing 0; `_currentHP: 0` | Ninguna | Idem. |
| **PF_Granary** | Testing activo (vida al 50%) | `startWithPercentForTesting: 0` | buildingSO y BarAnchor asignados; revisar en escena. |
| **PF_LumberCamp** | Testing activo (vida al 52%) | `startWithPercentForTesting: 0` | Idem. |
| **PF_MiningCamp** | Testing activo (vida al 80%) | `startWithPercentForTesting: 0` | Idem. |
| **PF_Peasant** | Testing activo (vida al 62%) | `startWithPercentForTesting: 0` | Animator, collider, barAnchor (fallback), escala. |
| **PF_Swordman** | Testing activo (vida al 27%) | `startWithPercentForTesting: 0` | Idem. |
| **PF_Archer** | Testing activo (vida al 27%) | `startWithPercentForTesting: 0` | Idem. |
| **PF_Mounted_King** | Testing activo (vida al 27%) | `startWithPercentForTesting: 0` | Idem. |

---

## buildingSO y BarAnchor (auditoría rápida)

- **PF_TownCenter, PF_Granary, PF_LumberCamp, PF_MiningCamp, PF_House, PF_Castillo, PF_Barracks:** tienen `buildingSO` asignado (ScriptableObject por GUID).
- **BarAnchor:** todos los edificios listados tienen nodo/hijo BarAnchor y referencia asignada en el componente que la usa (Health/SelectionHealthBarUI); PF_House tiene un Health con `barAnchor: 0` (usa fallbackOffset).

---

## Coherencia footprint / collider / tamaño visual

- No se modificaron colliders ni tamaños en esta fase.
- **Revisión manual:** en Unity, colocar cada edificio en escena y comprobar que el collider no sea mucho mayor o menor que la base visual y que el footprint en BuildingSO coincida con el uso en placement.

---

## Riesgos

- Si algún sistema dependía de “vida al X% al inicio” para pruebas, ahora todas las unidades/edificios arrancan al 100% (o lo que marque `_currentHP` = maxHP en Awake/Start cuando testing está desactivado).
- PF_Castillo y PF_Barracks con `_currentHP: 0` en prefab: en runtime `Health.EnsureHPInitialized()` los deja en maxHP; no requiere cambio de prefab.
