# Revisión prefabs de unidades — 08_Prefabs/Units

## Prefabs revisados

| Prefab | Root | Layer | Collider | NavMeshAgent | UnitMover | FactionMember |
|--------|------|-------|----------|--------------|-----------|---------------|
| **PF_Aldeano** | PF_Aldeano | 8 (Unit) | CapsuleCollider, no trigger | ✓ | ✓ | ✗ |
| **PF_Archer** | PF_Archer | 8 (Unit) | CapsuleCollider, no trigger | ✓ | ✓ | ✗ |
| **PF_Lancero** | PF_Lancero | 8 (Unit) | CapsuleCollider, no trigger | ✓ | ✓ | ✗ |
| **PF_Scout** | PF_Scout | 8 (Unit) | CapsuleCollider, no trigger | ✓ | ✓ | ✗ |
| **PF_Swordman** | PF_Swordman | 8 (Unit) | CapsuleCollider, no trigger | ✓ | ✓ | ✗ |
| **PF_Mounted_King** | PF_Mounted_King | 8 (Unit) | CapsuleCollider, no trigger | ✓ | ✓ | ✗ |

Además: **PF_WorldHealthBar** (prefab de barra de vida, no es unidad).

---

## Compatibilidad con puertas (GateController)

- **Capa:** Todas las unidades están en **Layer 8 = "Unit"**. Si en la puerta `GateController` usas **Unit Layer = Everything** (-1), se detectan bien. Si asignas una máscara concreta, incluye la capa **Unit** (bit 8).
- **Collider:** Todas tienen **CapsuleCollider** en el **root**, con **Is Trigger = false**. Eso es correcto: el OverlapSphere de la puerta y el trigger del gate detectan el collider; `GetComponentInParent<NavMeshAgent>` devuelve el agente del root.
- **NavMeshAgent + UnitMover:** En el mismo GameObject root en todos los prefabs. Correcto para movimiento y para “force pass through” de la puerta.

**Conclusión:** La configuración actual es válida para que las unidades abran la puerta por proximidad y pasen por ella. No hace falta cambiar los prefabs para que las puertas funcionen.

---

## Facciones (opcional)

- **FactionMember:** Ningún prefab de unidad tiene el componente **FactionMember**.
- En `GateController`, si `allowEnemies = false` y la puerta tiene `FactionMember`, solo se consideran “válidas” unidades que no sean hostiles. Si la unidad no tiene `FactionMember`, se considera no hostil y **puede abrir la puerta**.
- Si más adelante quieres que solo las unidades aliadas abran (por ejemplo puertas de tu base), añade **FactionMember** al root de cada unidad y asigna la facción (p. ej. Player). En la puerta (o en el edificio padre del muro) pon también **FactionMember** y deja `allowEnemies = false`.

---

## Resumen por prefab

1. **PF_Aldeano** — Tiene además **VillagerGatherer** y **Builder**. Correcto para construcción y recursos.
2. **PF_Archer, PF_Lancero, PF_Scout, PF_Swordman, PF_Mounted_King** — Configuración homogénea: movimiento, selección, vida; sin Builder ni Gatherer.

No se detectaron problemas que impidan movimiento o paso por puertas. La capa **Unit** (8) y los colliders no trigger son los esperados para el sistema actual.
