# Cómo usar la textura de rocas en el pack

Tienes la textura (ej. `Rocks_Stylized...`) en la misma carpeta que el pack. Para que las rocas la muestren.

---

## "No me deja editar el Inspector del material"

Si el material **RocksStylized_M** tiene los campos en gris (Base Map, etc.), es porque está **dentro del FBX** y Unity lo trata como solo lectura.

**Solución recomendada:** Crear un material nuevo (no el del FBX) y usarlo:

1. En la carpeta **Stone**, clic derecho → **Create** → **Material**. Nombre: **MAT_Rocks_Stylized**.
2. Selecciona **MAT_Rocks_Stylized** (el que acabas de crear). Este sí se puede editar.
3. En el Inspector:
   - **Shader**: **Universal Render Pipeline** → **Lit**.
   - **Base Map**: arrastra tu textura **Rocks_Stylized...**.
4. Asigna este material a las rocas (prefabs o en **RTS Map Generator** → **Stone Material Override**).

**Alternativa (extraer el material del FBX):**

1. Selecciona el **FBX** "Free Pack - Rocks Stylized" en Project.
2. En el Inspector, pestaña **Materials**.
3. Si aparece **Extract Materials**, úsala: crea copias editables en la carpeta. Luego edita esa copia y asigna la textura en Base Map.

---

## Opción 1: Material nuevo y asignarlo a las rocas

1. **Crear el material**
   - En la carpeta `Assets/_Project/08_Prefabs/Resources/Stone`, clic derecho → **Create** → **Material**.
   - Nombre: por ejemplo **MAT_Rocks_Stylized**.

2. **Configurar el material**
   - Selecciona el material (un solo clic).
   - En el Inspector:
     - **Shader**: **Universal Render Pipeline** → **Lit** (o busca "URP Lit").
     - En **Surface Inputs**:
       - **Base Map**: arrastra aquí tu textura **Rocks_Stylized...** desde la misma carpeta.
       - **Base Color**: blanco (1, 1, 1) para que se vea la textura tal cual.
   - Guarda (Ctrl+S).

3. **Asignar el material a las rocas**
   - Si usas **prefabs** hechos a partir de los SM_Rock:
     - Abre cada prefab de roca (doble clic).
     - En el **Mesh Renderer** del objeto con la malla, en **Materials** → **Element 0**, arrastra **MAT_Rocks_Stylized** (o asigna el material que creaste).
     - Guarda el prefab (Ctrl+S).
   - Si usas los **modelos del FBX** directamente (sin prefabs):
     - En Project, expande **Free Pack - Rocks Stylized.fbx** (flecha a la izquierda).
     - Verás los materiales (ej. RocksStylized_M) y las mallas (SM_Rock_01, etc.).
     - Clic en el **material** (RocksStylized_M) → en Inspector, **Base Map** = arrastra tu textura Rocks_Stylized.
     - Ese material lo comparten todas las mallas del FBX, así que con eso puede bastar.

---

## Opción 2: Arreglar el material que ya tiene el FBX

1. En **Project**, expande **Free Pack - Rocks Stylized.fbx** (clic en la flecha).
2. Verás un material (ej. **RocksStylized_M**). Selecciónalo.
3. En el **Inspector**:
   - **Shader**: que sea **Universal Render Pipeline / Lit**.
   - **Base Map**: arrastra tu textura **Rocks_Stylized...** desde la carpeta Stone.
4. Las mallas del FBX que usen ese material mostrarán ya la textura.

---

## Si las rocas se generan en juego (RTS Map Generator)

Los prefabs de rocas que pones en **Stone Prefab Variants** son los que se instancian. Asegúrate de que **esos prefabs** tengan asignado el material con la textura (pasos de la Opción 1, punto 3). Si no quieres tocar prefabs, en **RTS Map Generator** → **Stone Material Override** puedes asignar **MAT_Rocks_Stylized** y todas las piedras usarán ese material al colocarse.
