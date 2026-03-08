# Alinear BuildSite (plantilla) con la huella del edificio

Para que la construcción se sienta bien y el aldeano no tenga que estar "dentro" del edificio:

1. **BuildingSO.size** = tamaño en **celdas** (ej. 4×4).
2. **MapGrid** usa un **cellSize** (metros por celda). La huella en mundo = `size.x * cellSize` × `size.y * cellSize`.
3. El **prefab del BuildSite** (la plantilla de construcción) debería:
   - Tener el **pivote en el centro** del footprint (así coincide con donde BuildingPlacer coloca el sitio).
   - El **visual** (mesh/plano de la fundación) con la **misma extensión** en mundo que el edificio final (o muy parecida).
   - Un **Collider** (BoxCollider) que cubra esa misma zona: **Size** = (size.x * cellSize, algo bajo, size.y * cellSize) si el MapGrid tiene cellSize 2, y un edificio 4×4 → (8, 0.2, 8). Así el Builder usa “distancia al punto más cercano del collider” y el aldeano puede trabajar desde el borde.

## Pasos en Unity

1. Revisa **MapGrid** (o GridConfig): anota **cellSize** (ej. 2).
2. Para cada **BuildingSO** (Casa, Cuartel, etc.): anota **Size** en celdas (ej. 4, 4).
3. Abre el **prefab del BuildSite** que usa BuildingPlacer:
   - **Transform**: posición (0,0,0), rotación (0,0,0). El BuildingPlacer ya coloca el sitio en el mundo.
   - **Visual**: escala o tamaño del mesh para que en mundo mida **size.x * cellSize** × **size.y * cellSize** (ej. 8×8 m).
   - **BoxCollider** (o el que uses): **Center** (0, 0, 0), **Size** (size.x * cellSize, 1, size.y * cellSize) para que ocupe la misma huella. Así el Builder mide la distancia al borde del sitio.
4. Usa **el mismo BuildSite prefab** para todos los edificios **o** un prefab por tipo; si es uno por tipo, ajusta su tamaño/collider según el BuildingSO que represente.

Cuando la plantilla y la huella coinciden, el **buildRange** del Builder puede quedarse en un valor razonable (por defecto 2,5) y el aldeano trabajará desde el borde sin tener que subir “al centro”.

---

## Colliders y pathfinding (rutas raras)

- **Colliders de edificios**: Si el BoxCollider del prefab de edificio es **más grande** que la huella (o que el modelo), los aldeanos pueden “quedarse mirando” lejos del edificio y el NavMesh genera **rutas raras** (rodeos innecesarios). Por eso:
  - Los prefabs de edificio deben tener **BoxCollider con Size = (buildingSO.size.x × cellSize, altura, buildingSO.size.y × cellSize)**.
  - Usa **Tools → Project → Reconfigurar prefab de edificio** (o “selección actual”): el editor ajusta el BoxCollider a la huella del BuildingSO.
- **BuildSite**: El Builder usa **siempre la huella** (buildingSO.size × cellSize) para el “punto más cercano” al construir, así el aldeano va al borde real; el collider del BuildSite es solo fallback.
- **NavMeshObstacle**: Si usas obstáculos dinámicos, su tamaño también debe coincidir con la huella para no bloquear de más.
