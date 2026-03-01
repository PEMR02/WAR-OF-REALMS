# Barra de vida sobre unidad/edificio (estilo RTS)

La barra flotante **solo se ve cuando la unidad o el edificio está seleccionado**. Verde = vida llena, rojo = dañado (progresivo).

## Configuración en prefabs

### Unidades (Aldeano, Milicia, Arquero, etc.)

1. Abre el **prefab** de la unidad (ej. en `08_Prefabs/Units/`).
2. **Crear hijo** del root: Clic derecho en la raíz del prefab → **Create Empty**. Nombrar `HealthBar`.
3. En **HealthBar**:
   - **Transform**: **Position** (0, 2, 0) para que quede encima del modelo (ajusta la Y según la altura del personaje).
   - Añadir componente **Canvas**:
     - **Render Mode**: `World Space`
     - **Event Camera**: asignar la cámara principal (o dejar vacío; el script usa Camera.main).
     - En el **RectTransform** del Canvas: **Width** 100, **Height** 15, **Scale** (0.01, 0.01, 0.01) para que la barra se vea a tamaño razonable en el mundo.
   - Añadir componente **HealthBarWorld** (script).
4. **Crear hijo de HealthBar** para la barra:
   - Clic derecho en HealthBar → **UI → Image**. Nombrar `Fill`.
   - En **Fill** (Image):
     - **Image Type**: `Filled`
     - **Fill Method**: `Horizontal`
     - **Fill Origin**: `Left`
     - **Color**: blanco (el script lo pinta verde/rojo).
   - **RectTransform** de Fill: Stretch (Left=0, Right=0, Top=0, Bottom=0) para ocupar el Canvas.
5. **(Opcional, mejor aspecto)** Crear un **fondo** para la barra:
   - Clic derecho en **Canvas** (no en HealthBar) → **UI → Image**. Nombrar `Background`.
   - Mover **Background** para que sea el **primer hijo** del Canvas (arriba de Fill en la lista), así se dibuja detrás.
   - **RectTransform** de Background: **Stretch (0,0,0,0)** igual que Fill, para que no se vea corrido. El script también fuerza esta alineación al activar.
   - En **HealthBarWorld** asignar **Background Image** a este objeto.
6. En el componente **HealthBarWorld** del objeto **Canvas**:
   - **Fill Image**: arrastrar el objeto `Fill`.
   - **Bar Scale Multiplier**: 0.7 (o menos) para una barra más discreta.
   - **Local Offset**, **Billboard**: según prefieras.
7. **Desactivar** el GameObject **HealthBar** (o Canvas) en el prefab si quieres que solo se vea al seleccionar.

Guarda el prefab.

### Edificios (Casa, Town Center, Cuartel, etc.)

Mismo proceso que las unidades:

1. En el **prefab** del edificio, crear hijo vacío `HealthBar`.
2. **Position** del HealthBar: (0, 3, 0) o la altura que quieras sobre el edificio (edificios suelen ser más altos).
3. Añadir **Canvas** (World Space) + **HealthBarWorld** en HealthBar.
4. Crear hijo **UI → Image** llamado `Fill`, Type = Filled, Horizontal, Left; RectTransform stretch.
5. Asignar **Fill Image** en HealthBarWorld.
6. Opcional: desactivar HealthBar en el prefab.

### Resumen

- **HealthBar** = hijo del unit/building, con **Canvas (World Space)** y **HealthBarWorld**.
- **Fill** = hijo de HealthBar, **Image** con Type Filled, Horizontal.
- El **RTSSelectionController** ya está preparado: al seleccionar muestra la barra; al deseleccionar la oculta.

Si el prefab no tiene HealthBarWorld, no pasa nada: simplemente no se mostrará barra encima (el HUD de selección sigue funcionando si lo tienes configurado).
