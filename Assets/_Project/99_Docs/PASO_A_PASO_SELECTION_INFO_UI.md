# Paso a paso: Un solo panel para toda la selección (SelectionInfo_General)

Un único panel muestra la información al seleccionar **unidades**, **edificios** o **recursos**: nombre/tipo, barra (vida o cantidad restante) y texto "actual / max".

---

## 1. Dónde va el script

1. En la **Jerarquía**, clic derecho sobre **Canvas** → **Create Empty**. Renómbralo a **SelectionHealthBarController**.
2. Con ese objeto seleccionado, en el **Inspector** → **Add Component** → busca **Selection Health Bar UI** y añádelo.
3. En el componente **Selection Health Bar UI**, en **Selection** arrastra el GameObject **RTSSelection** (el que tiene el RTS Selection Controller).

---

## 2. Panel único (SelectionInfo_General)

### 2.1 Dónde colocar el panel (importante)

**SelectionInfo_General no puede estar dentro de hudGeneralRoot.**  
Si usas **HUDContextController**, al seleccionar aldeanos se oculta **hudGeneralRoot** y se muestra **hudVillagerRoot**. Si el panel de información está dentro de hudGeneralRoot, desaparece cuando seleccionas aldeanos.

- Crea **SelectionInfo_General** como hijo **directo del Canvas** (o de un objeto que esté siempre activo y no sea hudGeneralRoot ni hudVillagerRoot).
- Así el script **Selection Health Bar UI** es el único que controla si el panel se ve o no (según haya unidad/edificio/recurso seleccionado), y no se oculta por el cambio de contexto aldeano/general.

### 2.2 Crear el panel contenedor

1. Clic derecho en **Canvas** (no dentro de HUD_Villager ni del objeto que use HUDContextController como “general root”) → **UI** → **Panel**. Renómbralo a **SelectionInfo_General**.
2. Dentro de **SelectionInfo_General** ya hay un hijo **Panel**. Ese hijo es el que usarás como **Root Panel** (el que se muestra/oculta). Si prefieres, renómbralo a **Panel** y déjalo así.

### 2.3 Barra de vida que “baja” (fondo rojo + relleno verde)

Para que la barra se vea como una de vida de verdad (verde que se acorta, rojo donde falta vida), necesitas **dos imágenes**: una de fondo (roja) y otra de relleno (verde) encima.

**Paso A – Fondo de la barra (rojo):**

1. Clic derecho sobre el **Panel** que está dentro de SelectionInfo_General → **UI** → **Image**.  
   (Importante: elige **Image**, no “Raw Image”.)
2. Renombra ese objeto a **Bar_Background**.
3. En el **Inspector**, en el componente **Image**:
   - **Color** → ponla **roja** (o el tono que quieras para “sin vida”). Ejemplo: R 255, G 50, B 50, A 255.
   - Deja **Image Type** = Simple (no hace falta Filled en el fondo).
4. Con **Bar_Background** seleccionado, en **Rect Transform** ajusta ancho y alto para que sea la barra (ej. Width 200, Height 20). Puedes estirarla con el ancla si lo prefieres.

**Paso B – Relleno de la barra (verde):**

1. Clic derecho sobre **Bar_Background** → **UI** → **Image**.  
   (De nuevo **Image**, no Raw Image.)
2. Renombra ese objeto a **Bar_Fill**.
3. En el **Inspector**, en el componente **Image**:
   - **Source Image**: usa un sprite **cuadrado o rectangular** (p. ej. **"Square"** desde Create → 2D → Sprites → Square, o "White"/"UISprite"). **No uses "Knob"** (es redondo y se ve como óvalo). Este mismo Fill se usa para **vida** (unidades/edificios) y para **recursos** (cantidad restante), así que con un sprite cuadrado queda recto en ambos casos.
   - **Color** → **verde** (ej. R 50, G 255, B 50, A 255).
   - **Image Type** → **Filled**.
   - **Fill Method** → **Horizontal**.
   - **Fill Origin** → **Left**.
   - **Fill Amount** → 1 (para verla llena al editar).
4. En **Rect Transform** de **Bar_Fill**: que tenga el **mismo tamaño** que el fondo (mismos anchors y same width/height que **Bar_Background**), para que esté encima y al 100% coincida. Si usas anchors estirados (left-right, top-bottom) con offset 0, ya quedan iguales.

Orden en la jerarquía (importante): **Bar_Background** primero (arriba en la lista), **Bar_Fill** debajo. Así el verde se dibuja encima y al bajar la vida se verá el rojo.

Resumen de la barra:

```
Panel (Root Panel)
  ├─ Bar_Background   ← Image, Color rojo, Type = Simple (fondo)
  │    └─ Bar_Fill    ← Image, Color verde, Type = Filled, Horizontal, Left (relleno)
```

### 2.4 Textos (nombre y “80 / 100”)

1. Clic derecho sobre el mismo **Panel** (el Root) → **UI** → **Text - TextMeshPro** (o **Text** si no usas TMP). Renómbralo a **Text_Title**. Este mostrará el nombre (Aldeano, Town Center, Madera, etc.).
2. Vuelve a clic derecho sobre el **Panel** → **UI** → **Text - TextMeshPro** (o **Text**). Renómbralo a **Text_HP**. Este mostrará “80 / 100” o “240 / 300”.
3. Coloca **Text_Title** y **Text_HP** donde quieras (arriba de la barra, abajo, etc.) y ajusta tamaño de fuente y anclas.

Estructura final sugerida:

```
SelectionInfo_General
  └─ Panel                    ← Root Panel (asignar en el script)
       ├─ Text_Title          ← Nombre o tipo de recurso
       ├─ Bar_Background      ← Fondo rojo (asignar como Background Image)
       │    └─ Bar_Fill       ← Relleno verde, Filled (asignar como Fill Image)
       └─ Text_HP             ← "80 / 100" o "240 / 300"
```

### 2.5 Asignar en el inspector (Selection Health Bar UI)

Selecciona el GameObject que tiene el script **Selection Health Bar UI** (ej. **SelectionHealthBarController**). En el componente:

| Campo | Qué arrastrar / asignar |
|-------|-------------------------|
| **Selection** | El GameObject **RTSSelection** (tiene RTS Selection Controller). |
| **Root Panel** | El **Panel** que está dentro de SelectionInfo_General (el que contiene título, barra y Text_HP). |
| **Frame Image** | Opcional: la **Image** del fondo del Panel (la que da el color de fondo). Si la asignas, el script la pondrá en **Frame Color** (ej. negro) para que actúe como marco. |
| **Frame Color** | Color del marco (por defecto negro). Solo se aplica si **Frame Image** está asignado. |
| **Fill Image** | **Bar_Fill** (la Image con Type = Filled, verde). |
| **Background Image** | **Bar_Background** (la Image roja de fondo). |
| **Title Text TMP** | **Text_Title**. |
| **HP Text TMP** | **Text_HP**. |

Si usas **Text** normal en lugar de TextMeshPro, usa **Title Text Legacy** y **HP Text Legacy** en lugar de los TMP.

---

## 3. Comportamiento en juego

- **Unidad (aldeano u otra)** → Título = nombre. Barra = vida (verde que se acorta, rojo donde falta). Texto = "HP actual / max".
- **Edificio** → Título = id del edificio. Barra = vida (igual). Texto = "HP actual / max".
- **Recurso** → Título = Madera / Piedra / Oro / Comida. Barra = cantidad restante (misma **Bar_Fill** con sprite **Square** para que se vea recta). Texto = "cantidad / máximo".
- **Nada seleccionado** → El panel se oculta.

**Importante:** La misma **Fill Image** (Bar_Fill) y la misma **Background Image** (Bar_Background) se usan para vida y para recursos. Si en Bar_Fill pusiste **Source Image = Square** (o otro sprite rectangular), la barra se verá recta tanto con unidades/edificios como con recursos; no hace falta configurar nada más para la info de recursos.

### 2.6 Marco negro y tamaño de la barra

- **Fondo negro como marco:** El Panel que usas como Root Panel suele tener un componente **Image** (fondo). Asigna esa Image a **Frame Image** en el script y deja **Frame Color** en negro. Así el fondo del panel se verá negro y actuará de marco.
- **Verde y rojo “en una medida”:** Para que la barra (verde + rojo) se vea proporcionada y dentro del marco:
  - **Bar_Background** (rojo) debe tener el tamaño que quieras para la barra (por ejemplo ancho 200, alto 18). No hace falta que ocupe todo el panel.
  - **Bar_Fill** (verde) debe tener **exactamente el mismo tamaño** que Bar_Background (mismos anchors, mismo Width y Height, o anchors stretch con offset 0). Así el verde y el rojo comparten la misma medida y el relleno se acorta bien.
  - Deja un poco de margen entre el borde del Panel y la barra (ajustando Pos X/Y o anchors de Bar_Background) para que se vea el marco negro alrededor.

---

## 4. Checklist rápido

- [ ] GameObject con **Selection Health Bar UI** creado y **Selection** asignado.
- [ ] **Root Panel** = el Panel dentro de SelectionInfo_General.
- [ ] **Frame Image** = Image del fondo del Panel (opcional, para marco negro). **Frame Color** = negro.
- [ ] **Bar_Background** (Image roja, Simple) con el tamaño deseado de la barra, asignada a **Background Image**.
- [ ] **Bar_Fill** (Image verde, **Filled**, Horizontal, Left) como hijo de Bar_Background, asignada a **Fill Image**.
- [ ] **Text_Title** y **Text_HP** (o Legacy) asignados.

---

## 5. Barra en mundo (prefabs unidades/aldeanos con HealthBarWorld)

Mismo estilo que el HUD: **marco negro** y **verde/rojo en una medida**.

### Estructura del prefab (hijo HealthBarWorld)

Orden de hijos (el primero se dibuja atrás):

1. **Border (Image)** → Marco negro. Asigna una **Image** con **Source Image = Square** (no Knob). El script la pinta con **Color Border** (negro). Debe ser el **primer hijo** del Canvas (más atrás).
2. **Background (Image)** → Fondo rojo de la barra. Mismo tamaño que el relleno (el script alinea ambos).
3. **Fill (Image)** → Relleno verde. **Source Image = Square**, **Image Type = Filled**, Horizontal, Left.

En **Health Bar World (Script)**:
- **Border Image** = la Image del marco (el primer hijo).
- **Background Image** = la Image roja.
- **Fill Image** = la Image verde (Filled).
- **Color Border** = negro (es el color del marco).
- **Border Width** + **Bar Padding** = margen entre el marco y la barra; así la barra (verde+rojo) queda contenida en el marco. Por defecto 2 y 2.

El script hace que **Background** y **Fill** tengan la misma medida (mismo rect), así el verde y el rojo quedan alineados.

## 6. Dejar solo un sistema: quitar ResourceInfoUI

**Recomendado:** usar **solo Selection Health Bar UI** para todo (unidades, edificios y recursos). Un solo panel, una sola barra, mismos colores si quieres.

- **Desactiva o elimina** el GameObject (o el componente) **ResourceInfoUI** en la escena. Si no, al seleccionar un recurso podrías ver dos paneles o colores raros.
- En **Selection Health Bar UI** hay una opción **Use Life Colors For Resources** (en Colores):
  - **Activada** (por defecto): la barra de recursos se ve igual que la de vida (verde/rojo).
  - **Desactivada**: la barra usa colores por tipo (madera marrón, oro amarillo, etc.).
