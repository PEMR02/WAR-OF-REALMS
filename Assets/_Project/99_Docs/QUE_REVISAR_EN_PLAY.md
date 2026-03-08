# Qué revisar en modo Play (WAR OF REALMS)

Checklist basado en las capturas y el flujo actual del juego.

---

## 1. Ya corregido en código

- **Rocas/piedras flotantes:** `MapResourcePlacer` ahora llama a `SnapResourceBottomToTerrain` tras colocar cada recurso (piedra, oro, árbol, etc.). La base del mesh se apoya en el terreno. Si aún ves una flotando, regenera el mapa o revisa el prefab de esa variante (pivot en la base).
- **Pop vs Ociosos:** `PopulationManager` tiene `RegisterExistingVillagersOnStart`: al iniciar cuenta los `VillagerGatherer` en escena y los suma a la población, para que "Pop" coincida con los aldeanos (y "Ociosos" tenga sentido). Si Pop sigue en 0, comprueba que haya un **PopulationManager** en escena y que `registerExistingVillagersOnStart` esté en true.

---

## 2. "New Text" en pantalla

- **Origen:** En la escena **SampleScene** hay uno o más objetos de texto (Text o TextMeshPro) con el contenido por defecto "New Text".
- **Qué hacer:** En Unity, abre la escena que usas para jugar, en la jerarquía busca por "Text" o "New Text", selecciona el objeto y:
  - Si es un placeholder que no debe verse: desactívalo o elimínalo.
  - Si debe mostrar algo: asigna el texto correcto o enlaza el componente a un script que lo actualice (por ejemplo el mismo que usa PopulationHUD/ResourcesHUD).

---

## 3. Edificios y unidades

- **Edificios bien apoyados:** Con el sistema de footprint (centro + esquinas + bordes) y `TerrainPlacementValidator`, el ghost y el edificio final deberían quedar apoyados. Si alguno flota o se hunde, revisa el **pivot del prefab** (debe estar en la base del modelo).
- **Unidades en el suelo:** Si un aldeano o unidad se ve flotando al moverse, revisa el pivot del prefab de la unidad (debe estar en los pies).

---

## 4. Recursos y colocación

- **Recursos flotantes:** Además de piedra, la misma lógica aplica a oro, árboles y comida. Si algún tipo sigue flotando, el prefab de ese recurso puede tener el pivot mal; como alternativa, se puede revisar en código que ese prefab pase por `SnapResourceBottomToTerrain` (ya se aplica a todos los colocados por MapResourcePlacer).

---

## 5. Otras cosas en las que fijarse

| Revisión | Descripción |
|----------|-------------|
| **Pathfinding** | Que las unidades lleguen a recursos y edificios sin atascarse (NavMesh, obstáculos). |
| **Recolección** | Que al ordenar a un aldeano ir a árbol/piedra/oro, se acerque, recoja y actualice Wood/Stone/Gold/Food. |
| **Construcción** | Que al colocar fundación y asignar aldeanos, el edificio se complete y aparezca el edificio final bien apoyado. |
| **Producción** | Que el Town Center (y otros productores) produzcan unidades y que Pop y Ociosos se actualicen. |
| **Límites del mapa** | Que la cámara no salga de los límites y que el grid/mapa se vean coherentes. |
| **Rendimiento** | En escenas con muchos árboles/recursos, revisar FPS y uso de CPU (Profiler). |
| **Capas (Layers)** | Que los raycasts de selección y de órdenes (suelo, recursos, edificios) usen las capas correctas para no dar clics “al vacío”. |

---

## 6. Resumen rápido

1. Quitar o configurar los textos "New Text" en la escena.
2. Comprobar que Pop refleja a los aldeanos (PopulationManager con registro al inicio).
3. Rocas/recursos ya se apoyan en el terreno con `SnapResourceBottomToTerrain`; si algo sigue flotando, revisar pivot del prefab.
4. Revisar pathfinding, recolección, construcción y producción en Play.
5. Revisar pivots de prefabs de edificios y unidades si algo se ve flotando o hundido.
