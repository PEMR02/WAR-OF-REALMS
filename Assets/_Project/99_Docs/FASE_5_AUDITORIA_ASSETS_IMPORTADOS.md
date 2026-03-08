# Fase 5 — Auditoría de assets importados

## Objetivo

Detectar posibles problemas en assets usados por unidades y edificios (FBX, materiales, animaciones) y proponer un checklist manual en Unity.

---

## 1. Posibles problemas a revisar

| Área | Problema | Efecto típico |
|------|----------|----------------|
| **Pivot** | Pivot del FBX/root en centro o en la cabeza | Ghost/placement con offset; edificio flotando o enterrado; barra de vida mal posicionada. |
| **Scale factor** | Scale Factor ≠ 1 en Import Settings del FBX | Unidad o edificio demasiado grande/pequeño respecto al grid; collider desproporcionado. |
| **Rotación importada** | Rotation en Model o Root no (0,0,0) | Modelo tumbado o girado respecto al prefab. |
| **Materiales** | Materiales del FBX no asignados o Missing | Mesh rosa o blanco; texturas no visibles. |
| **Animator** | Controller no asignado o Avatar incorrecto | Animaciones no se disparan; retargeting fallido. |
| **Avatar** | Humanoid con huesos mal mapeados | Animaciones retargetadas se ven rotas (manos, pies). |

---

## 2. Prefabs que dependen de modelos importados

- **Unidades:** PF_Peasant, PF_Swordman, PF_Archer, PF_Mounted_King (y variantes). Referencian meshes y a menudo un FBX como modelo raíz (p. ej. Toony Tiny, caballo). Si el FBX está mal importado (pivot en pies vs centro), el prefab hereda el problema.
- **Edificios:** PF_TownCenter, PF_House, PF_Barracks, PF_Granary, PF_LumberCamp, PF_MiningCamp, PF_Castillo. Suelen tener un FBX o modelo en la jerarquía (Town Center Base, Casa Base, etc.). Pivot en la base = correcto para placement.
- **Recursos:** Árboles, piedra, oro, animales (PF_Cow, etc.) colocados por MapResourcePlacer. Si el prefab del recurso tiene pivot en la base, `SnapResourceBottomToTerrain` funciona bien.

---

## 3. Checklist manual en Unity (FBX y prefabs)

### FBX / Model

- [ ] **Pivot:** En el FBX Import, comprobar "Model" → Pivot (o en el root del modelo). Para unidades: suelo en los pies. Para edificios: centro de la base.
- [ ] **Scale Factor:** 1 (o el valor que uses de forma consistente). Si cambias, reajustar colliders y tamaños en prefab.
- [ ] **Rotation:** Dejar en 0,0,0 en Import si el modelo ya viene orientado; si no, ajustar solo lo necesario (p. ej. X=90 si viene tumbado).
- [ ] **Rig / Animation:** Si es Humanoid, comprobar que el Avatar está asignado y que el mapeo de huesos es correcto (Preview en el Import).
- [ ] **Materials:** Crear/assign Materials en "Materials" o "Extract Materials"; evitar "Use External Materials" con rutas rotas.

### Prefabs de unidades

- [ ] **Root transform:** Posición (0,0,0), escala (1,1,1); rotación según necesidad.
- [ ] **Animator:** Controller asignado; Avatar del mismo FBX o compatible.
- [ ] **Collider:** Capsule/Box en los pies; no demasiado grande (evitar selección desde lejos).
- [ ] **BarAnchor / fallback:** Health usa barAnchor o fallbackOffset; comprobar que la barra aparece encima de la cabeza.

### Prefabs de edificios

- [ ] **Root pivot:** En la base del edificio (donde toca el suelo). Si el FBX tiene pivot en centro, ajustar con un hijo vacío en la base o con offset en el prefab.
- [ ] **BuildingSO.size:** Debe coincidir con el footprint visual y con el collider (celdas × cellSize).
- [ ] **NavMeshObstacle / Collider:** Tamaño coherente con el modelo; carving si quieres que el NavMesh evite el edificio.

### Animaciones

- [ ] **Clip:** Loop correcto para Idle/Walk; eventos si los usas.
- [ ] **Retargeting:** Si usas el mismo controller para varias unidades (p. ej. humano sobre caballo), comprobar que el Avatar y el rig son compatibles (ver CABALLO_RETARGET_ANIMACIONES_ZEBRA.md si aplica).

---

## 4. Prefabs que podrían depender de asset mal importado

| Prefab | Revisar sobre todo |
|--------|---------------------|
| PF_Peasant / villager | Pivot (pies), escala, Animator + Avatar. |
| PF_Mounted_King | Pivot del caballo; retarget de animaciones humano/caballo. |
| PF_TownCenter | Pivot en la base; BuildingSO.size vs visual. |
| PF_House, PF_Barracks, PF_Castillo | Pivot base; materiales de las submeshes. |
| Recursos (árboles, piedra) | Pivot en la base para SnapResourceBottomToTerrain; materiales (evitar rosa/blanco). |

---

## 5. Acciones recomendadas

- No se ha cambiado ningún asset en esta fase; la auditoría es documental.
- Ejecutar el checklist en Unity por cada tipo (unidad estándar, edificio estándar, recurso) y anotar en este doc o en AUDITORIA_ASSETS_JUEGO.md los hallazgos (pivot corregido, scale factor unificado, etc.).
- Si se detectan FBX con pivot incorrecto de forma sistemática, valorar un script de Editor que ajuste el pivot o que cree un hijo vacío en la base y lo use como referencia para placement.
