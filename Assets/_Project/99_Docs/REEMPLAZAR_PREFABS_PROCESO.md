# Proceso para reemplazar prefabs de pruebas por definitivos (o modelos 3D de ensayo)

Guía paso a paso para cambiar prefabs placeholder por los definitivos o por ensayos con modelos 3D, sin romper referencias ni la lógica del juego.

---

## 1. Resumen del flujo

1. **Identificar** qué prefab quieres reemplazar y **dónde se usa** (qué ScriptableObject o componente lo referencia).
2. **Crear o importar** el nuevo prefab (modelo 3D definitivo o de ensayo).
3. **Configurar** el nuevo prefab (layer, componentes) para que el juego lo trate igual que al anterior.
4. **Asignar** el nuevo prefab en el SO o componente que lo usa (reemplazar la referencia).
5. **Probar** en Play (colocación, selección, población, etc.).
6. **(Opcional)** Guardar el prefab viejo como backup o borrarlo.

---

## 2. Dónde se usan los prefabs en este proyecto

| Tipo            | Dónde se asigna el prefab                         | Archivo / ubicación típica |
|-----------------|---------------------------------------------------|----------------------------|
| **Edificios**   | BuildingSO → campo **Prefab**                     | `04_Data/ScriptableObjecs/Buildings/` (House_SO, Barracks_SO, etc.) |
| **Unidades**    | UnitSO → campo **Prefab**                         | `04_Data/ScriptableObjecs/Units/` (Villager_UnitSO, Militia_UnitSO, etc.) |
| **Town Center** | RTS Map Generator → **Town Center SO** o **Town Center Prefab Override** | Objeto con RTSMapGenerator en escena |
| **Recursos**    | RTS Map Generator → **Tree Prefab**, **Stone Prefab**, etc. (y variantes) | Mismo objeto RTS Map Generator |

El **menú de construcción** (BuildMenuUI) no guarda prefabs directamente: usa **BuildingSO** (house, barracks, townCenter). Cambiar el prefab dentro del SO es suficiente.

---

## 3. Proceso por tipo

### A) Edificios (Casa, Barracks, Granary, Mining Camp, Lumber Camp, etc.)

1. **Saber qué SO usa ese edificio**  
   Ejemplo: casa → **House_SO**; cuartel → **Barracks_SO**. Están en `Assets/_Project/04_Data/ScriptableObjecs/Buildings/`.

2. **Crear el nuevo prefab**  
   - Importa el modelo 3D (FBX, etc.) o usa uno que ya tengas.  
   - Arrastra el modelo a la jerarquía o a la carpeta `08_Prefabs/Buildings/`.  
   - Crea prefab desde ese GameObject (arrastrar a la carpeta o **GameObject → Create Empty** y meter el modelo como hijo, luego crear prefab del padre).

3. **Configurar el prefab** (para que se comporte como edificio):  
   - **Layer**: **Building** (necesario para colocación y selección).  
   - **BuildingInstance**: Add Component → **Building Instance**; en **Building SO** asigna el SO correcto (ej. House_SO).  
   - **Collider**: al menos un **Box Collider** (o Mesh Collider) que cubra la base para que ocupe espacio y bloquee construcción encima.  
   - **Transform**: escala (1,1,1) o la que necesites; posición (0,0,0) en el prefab está bien.

   **Atajo:** Menú **Tools → Project → Reconfigurar prefab de edificio** (o “Reconfigurar prefab de edificio (selección actual)” con el prefab seleccionado). El script aplica Layer Building, BuildingInstance con el SO que elijas y BoxCollider si no hay collider.

4. **Asignar el nuevo prefab en el BuildingSO**  
   - Abre el SO (ej. **House_SO**).  
   - En el campo **Prefab** arrastra tu **nuevo prefab** (reemplazando el anterior).

5. **Probar**  
   - Play → menú de construcción → coloca el edificio, comprueba que se ve bien, que bloquea el suelo y que la población/subida de límite funcione si aplica.

---

### B) Unidades (Aldeano, Milicia, Arquero, etc.)

1. **Saber qué UnitSO usa esa unidad**  
   Ejemplo: aldeano → **Villager_UnitSO**; milicia/arquero → **Militia_UnitSO** / **Archer_UnitSO**. Están en `Assets/_Project/04_Data/ScriptableObjecs/Units/`.

2. **Crear el nuevo prefab**  
   - Importa el modelo de la unidad.  
   - Crea un prefab (por ejemplo en `08_Prefabs/Units/`).  
   - El prefab debe tener lo que el juego espera: normalmente un **Animator** (si usa animaciones), **NavMeshAgent**, **Collider** (para clic/raycast), y el script de **Unidad** si lo usas. Revisa el prefab actual (ej. Aldeano) y replica los componentes necesarios en el nuevo.

3. **Asignar el nuevo prefab en el UnitSO**  
   - Abre el SO (ej. **Villager_UnitSO**).  
   - En **Prefab** arrastra el **nuevo prefab** de la unidad.

4. **Probar**  
   - Play → producir unidad desde Town Center (o el edificio que la genere) y comprobar movimiento, selección y que no dé errores en consola.

---

### C) Town Center

1. **Dónde se asigna**  
   - Objeto de la escena que tiene **RTS Map Generator**.  
   - Campos: **Town Center SO** (recomendado) y opcionalmente **Town Center Prefab Override**.  
   - Si usas **Town Center SO**, el prefab que se usa es el del **TownCenter_BuildingSO** (campo Prefab).  
   - Si usas **Town Center Prefab Override**, ese GameObject se usa en lugar del prefab del SO.

2. **Reemplazar el modelo del Town Center**  
   - **Opción A:** Crear un nuevo prefab de Town Center, configurarlo como edificio (Layer Building, BuildingInstance con TownCenter_BuildingSO, Collider). Luego abrir **TownCenter_BuildingSO** y asignar ese prefab en **Prefab**.  
   - **Opción B:** Asignar un prefab directamente en **Town Center Prefab Override** en el RTS Map Generator (el generador lo usará al colocar los TC en el mapa).

3. **Probar**  
   - Play → generar mapa y comprobar que los Town Centers se colocan con el nuevo modelo y que producen aldeanos si corresponde.

---

### D) Recursos (árboles, piedra, oro, bayas, animales)

1. **Dónde se asignan**  
   - Objeto con **RTS Map Generator** (Inspector).  
   - Campos: **Tree Prefab**, **Stone Prefab**, **Gold Prefab**, **Berry Prefab**, **Animal Prefab** y sus **Variantes** (arrays).

2. **Reemplazar un recurso**  
   - Crea el nuevo prefab del recurso (modelo 3D + si hace falta **ResourceNode**, **Collider**, layer **Resource**; el generador puede añadir algunos si faltan).  
   - En el RTS Map Generator, en el campo correspondiente (ej. **Tree Prefab** o **Stone Prefab**) arrastra el **nuevo prefab**.  
   - Si quieres varias formas, rellena el array de **variantes** (ej. **Tree Prefab Variants**).

3. **Texturas / materiales**  
   - Puedes usar **Stone Material Override** o **Tree Material Overrides** en el generador para forzar materiales; lo ideal es tener los materiales ya en el prefab.

4. **Probar**  
   - Play → generar mapa y comprobar que los recursos aparecen, se ven bien y se pueden recolectar.

---

## 4. Checklist rápido por tipo

**Edificio**  
- [ ] Prefab con Layer **Building**  
- [ ] **BuildingInstance** con el **BuildingSO** correcto  
- [ ] **Collider** (Box o Mesh)  
- [ ] **BuildingSO** → campo **Prefab** apuntando al nuevo prefab  

**Unidad**  
- [ ] Prefab con **NavMeshAgent** (y lo que use tu proyecto: Animator, script de unidad, etc.)  
- [ ] **UnitSO** → campo **Prefab** apuntando al nuevo prefab  

**Town Center**  
- [ ] Mismo checklist que edificio  
- [ ] **TownCenter_BuildingSO** → **Prefab** asignado (o **Town Center Prefab Override** en RTS Map Generator)  

**Recurso**  
- [ ] Prefab con **ResourceNode** (o dejarlo para que el generador lo añada)  
- [ ] Layer **Resource** (o el que use tu máscara de recursos)  
- [ ] **Collider** si el modelo no trae  
- [ ] RTS Map Generator → campo correspondiente (Tree/Stone/Gold/…) o variantes asignados  

---

## 5. Buenas prácticas

- **Backup:** Antes de reemplazar, duplica el prefab viejo (Ctrl+D) y renómbralo (ej. `PF_House_OLD`) o guárdalo en una carpeta `_Backup`. Así puedes volver atrás si algo falla.  
- **Un cambio a la vez:** Reemplaza un tipo de prefab, prueba, y luego el siguiente. Así sabes qué rompió si algo falla.  
- **Mismo ID en SO:** No cambies el **id** del BuildingSO/UnitSO al reemplazar el prefab; el juego (menús, producción, población) suele depender de ese id.  
- **Prefabs definitivos:** Cuando tengas el modelo final, repite el mismo proceso: nuevo prefab → configurar → asignar en el SO o en el RTS Map Generator.

Si algo se “desconfigura” (referencias rotas, edificio sin población, recursos que no se ven), revisa: (1) que el prefab esté asignado en el SO correcto, (2) Layer y componentes (BuildingInstance, Collider, ResourceNode, etc.) y (3) la guía **RECONFIGURAR_PREFAB_CASA.md** y el menú **Tools → Project → Reconfigurar prefab de edificio** para edificios.
