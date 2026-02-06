# Lista de verificación: agua no se ve

Revisa cada punto en **Play** (o justo después de generar el mapa). Marca cuando lo compruebes.

---

## 1. Configuración del generador (RTS Map Generator)

- [ ] **Water Height** no está en **-999**  
  - Si está en -999 el agua está desactivada.  
  - Pon un valor en **world units** (ej. 3 o 4 si Height Multiplier = 8) **o** usa **Water Height Relative** (ej. 0,25–0,4).

- [ ] **Water Height Relative** (si lo usas): entre **0** y **1**  
  - Ej. 0,25 = agua hasta el 25% del rango de altura del terreno (depresiones).

- [ ] **Show Water** está **activado** (checkbox marcado).

- [ ] **Water Mesh Mode**: probado tanto **FullPlaneIntersect** como **Chunks**  
  - Por si uno falla por límites de mesh o culling.

---

## 2. Consola (logs al generar)

- [ ] Tras generar, aparece algo como:  
  **"Water Height (relativo X%): Y world units"** o **"Rango terreno (world Y): min=… max=…"**  
  - Confirma que se calculó la cota de agua.

- [ ] No aparece: **"0 celdas bajo el nivel"** o **"0 celdas de agua"**  
  - Si sale eso, no hay geometría de agua: sube **Water Height** o **Water Height Relative**.

- [ ] Aparece **"Water mesh: N chunks"** o **"Water (FullPlaneIntersect): 1 mesh, X vértices…"**  
  - Confirma que se generó al menos un mesh.

---

## 3. Hierarchy (en Play)

- [ ] Existe el objeto **Water** en la **raíz** de la escena (no necesariamente hijo de RTS Map Generator).

- [ ] **Water** está **activo** (icono de ojo/checkbox activo).

- [ ] Dentro de **Water** hay hijos: **Water_FullPlaneIntersect** (modo FullPlaneIntersect) o **WaterChunk_X_Y** (modo Chunks).

- [ ] Ese hijo tiene **MeshFilter** (con mesh asignado) y **MeshRenderer** (componente activo).

---

## 4. Cámara (Game view)

- [ ] **Main Camera** (o la cámara que dibuja el Game view) tiene **Culling Mask** que incluye la capa del agua.  
  - Por defecto el agua usa capa **0 (Default)**.  
  - Culling Mask = **Everything** la incluye; si usas capas concretas, incluye **Default** (o la que pongas en **Water Layer Override**).

- [ ] La cámara está **orientada** de forma que el mapa (y el agua) queden en vista (no mirando al cielo o bajo el suelo).

---

## 5. URP (si usas Universal Render Pipeline)

- [ ] El **Renderer** usado por tu pipeline (ej. Universal Renderer) tiene en **Filtering** → **Opaque Layer Mask** una máscara que incluye la capa del agua (por defecto **Default**).  
  - Si el agua está en otra capa por **Water Layer Override**, esa capa debe estar en Opaque Layer Mask.

- [ ] No hay un **Render Feature** o **Override** que excluya la capa del agua.

---

## 6. Material del agua

- [ ] En **RTS Map Generator** → **Agua (visual)** está asignado **Water Material** (ej. MAT_Water).  
  - Si no, se usa un fallback que a veces no se ve bien en Game view.

- [ ] **MAT_Water** (o el material que uses) usa un **shader visible** en tu pipeline (ej. URP Lit, URP Unlit, etc.).  
  - Shaders Built-in antiguos pueden no dibujarse en URP.

- [ ] El material no tiene **Alpha/Transparency** que lo haga invisible por configuración (a menos que quieras agua transparente y lo tengas configurado).

---

## 7. Posición y escala

- [ ] El **Terrain** y el **Water** comparten el mismo espacio en XZ (origen del grid).  
  - El generador alinea el agua al grid; si el Terrain está desplazado, podría haber desajuste.

- [ ] **Water Surface Offset** es pequeño (ej. 0,05).  
  - Valores muy grandes podrían alejar el agua del nivel visual esperado.

---

## Resumen rápido

| Revisado | Qué descartas |
|----------|----------------|
| 1        | Agua desactivada o cota mal configurada |
| 2        | Que no se genere geometría (0 celdas agua) |
| 3        | Que el objeto Water no exista o esté desactivado/sin mesh |
| 4        | Que la cámara no dibuje la capa del agua |
| 5        | Que URP no dibuje la capa del agua |
| 6        | Material nulo o shader incompatible |
| 7        | Desplazamiento o offset excesivo |

Cuando encuentres el primer ítem que **no** se cumple, ese suele ser el origen del problema. Anota cuál falla y ajusta solo eso; si quieres, comparte ese punto y te digo el cambio exacto.
