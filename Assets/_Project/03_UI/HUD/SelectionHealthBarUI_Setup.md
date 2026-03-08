# Barra de vida al seleccionar (SelectionHealthBarUI)

## Qué hace
Cuando seleccionas una **unidad** o un **edificio**, se muestra una barra de vida:
- **Verde** = vida llena (100 %)
- **Rojo** = sin vida (0 %)
- **Progresivo** = el color interpola entre verde y rojo según el porcentaje de vida.

## Configuración en Unity

### 1. Crear la UI en el Canvas
En tu **Canvas** (o en el mismo objeto donde está ProductionHUD):

1. **Crear panel**: Clic derecho en Canvas → **UI → Panel**. Renombrar a `Panel_SelectionHealth`.
2. **Posición**: Coloca el panel donde quieras (ej. abajo a la izquierda, encima del minimapa, o debajo del título del edificio). Por ejemplo:
   - Anchor: abajo-centro o abajo-izquierda
   - Pos Y: 120, Width: 200, Height: 20
3. **Crear barra de relleno** (hija del panel):
   - Clic derecho en `Panel_SelectionHealth` → **UI → Image**. Renombrar a `Fill`.
   - En el componente **Image**:
     - **Image Type**: `Filled`
     - **Fill Method**: `Horizontal`
     - **Fill Origin**: `Left`
     - **Fill Amount**: 1 (en el script se controla en runtime)
     - **Color**: puede quedar en blanco; el script lo pinta verde/rojo.
   - Ajustar **RectTransform** del Fill: stretch para ocupar todo el panel (Left=0, Right=0, Top=0, Bottom=0).

### 2. Asignar al script
1. Crear un **GameObject vacío** en el Canvas (o usar uno existente del HUD) y añadir el componente **SelectionHealthBarUI**.
2. En el inspector:
   - **Selection**: arrastrar el objeto que tiene `RTSSelectionController` (o dejar vacío para auto-buscar).
   - **Root Panel**: arrastrar `Panel_SelectionHealth`.
   - **Fill Image**: arrastrar el objeto `Fill` (la Image con Type = Filled).
   - **Color Full Health** / **Color No Health**: opcional; por defecto verde y rojo.

### 3. Comportamiento
- Si seleccionas **varias unidades**, se muestra la vida de la **primera** seleccionada.
- Si seleccionas un **edificio**, se muestra la vida del edificio.
- Al deseleccionar o si la entidad no tiene **Health**, el panel se oculta.
