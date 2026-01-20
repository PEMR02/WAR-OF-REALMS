# Configuración de UI para VillagerBuildHUD

## Estructura de Canvas

Crea la siguiente jerarquía en tu Canvas (o en el GameObject que contenga el HUD de aldeanos):

```
Canvas
└── HUD_Villager (GameObject - activo solo cuando hay aldeanos seleccionados)
    ├── VillagerBuildHUD (Componente - script VillagerBuildHUD.cs)
    │
    ├── Panel_Categories (GameObject - Panel con fondo)
    │   ├── Button_Category_1_Econ (Button)
    │   │   └── Text (TextMeshProUGUI) - "1. Económico"
    │   ├── Button_Category_2_Military (Button)
    │   │   └── Text (TextMeshProUGUI) - "2. Militar"
    │   ├── Button_Category_3_Defenses (Button)
    │   │   └── Text (TextMeshProUGUI) - "3. Defensas"
    │   └── Button_Category_4_Special (Button)
    │       └── Text (TextMeshProUGUI) - "4. Especial"
    │
    ├── Panel_Slots (GameObject - Panel con fondo)
    │   ├── Button_Slot_1 (Button)
    │   │   └── Text (TextMeshProUGUI) - "1."
    │   ├── Button_Slot_2 (Button)
    │   │   └── Text (TextMeshProUGUI) - "2."
    │   ├── Button_Slot_3 (Button)
    │   │   └── Text (TextMeshProUGUI) - "3."
    │   ├── Button_Slot_4 (Button)
    │   │   └── Text (TextMeshProUGUI) - "4."
    │   ├── Button_Slot_5 (Button)
    │   │   └── Text (TextMeshProUGUI) - "5."
    │   ├── Button_Slot_6 (Button)
    │   │   └── Text (TextMeshProUGUI) - "6."
    │   ├── Button_Slot_7 (Button)
    │   │   └── Text (TextMeshProUGUI) - "7."
    │   ├── Button_Slot_8 (Button)
    │   │   └── Text (TextMeshProUGUI) - "8."
    │   └── Button_Slot_9 (Button)
    │       └── Text (TextMeshProUGUI) - "9."
    │
    └── Text_Title (TextMeshProUGUI) - Muestra estado/categoría actual
```

## Pasos de Configuración

### 1. Crear Panel_Categories
- **Tipo**: GameObject con componente `Image` (Panel)
- **Nombre**: `Panel_Categories`
- **Configuración**:
  - RectTransform: Posición donde quieras el panel (ej: abajo izquierda)
  - Image: Color de fondo opcional (ej: negro semi-transparente)
  - Layout: Opcionalmente usa `Horizontal Layout Group` o `Grid Layout Group` para organizar botones

### 2. Crear 4 Botones de Categoría
Para cada categoría (1-4):
- **Tipo**: GameObject con componente `Button`
- **Nombres**: 
  - `Button_Category_1_Econ`
  - `Button_Category_2_Military`
  - `Button_Category_3_Defenses`
  - `Button_Category_4_Special`
- **Configuración**:
  - Hijo: GameObject con `TextMeshProUGUI` llamado `Text`
  - TextMeshProUGUI: Texto inicial (se actualizará automáticamente)
  - Button: Configurar colores normal/highlighted/pressed según tu tema

### 3. Crear Panel_Slots
- **Tipo**: GameObject con componente `Image` (Panel)
- **Nombre**: `Panel_Slots`
- **Configuración**:
  - RectTransform: Misma posición que Panel_Categories (se alternarán)
  - Image: Color de fondo opcional
  - Layout: Usa `Grid Layout Group` con 3 columnas para organizar 9 botones en grid 3x3

### 4. Crear 9 Botones de Slot
Para cada slot (1-9):
- **Tipo**: GameObject con componente `Button`
- **Nombres**: `Button_Slot_1`, `Button_Slot_2`, ..., `Button_Slot_9`
- **Configuración**:
  - Hijo: GameObject con `TextMeshProUGUI` llamado `Text`
  - TextMeshProUGUI: Texto inicial "1.", "2.", etc. (se actualizará automáticamente)
  - Button: Configurar colores normal/highlighted/pressed

### 5. Crear Text_Title
- **Tipo**: GameObject con componente `TextMeshProUGUI`
- **Nombre**: `Text_Title`
- **Configuración**:
  - RectTransform: Posición superior del panel (ej: encima de los botones)
  - TextMeshProUGUI: Texto inicial vacío o "Construcción"
  - Font: Usa la fuente que prefieras
  - Font Size: 18-24 según tu diseño

### 6. Asignar Referencias en VillagerBuildHUD

En el Inspector del componente `VillagerBuildHUD`:

**Refs:**
- `Build`: Asignar el GameObject con `BuildModeController` (o dejar null para auto-buscar)
- `Selection`: Asignar el GameObject con `RTSSelectionController` (o dejar null para auto-buscar)

**Panels:**
- `Root Panel Categories`: Arrastrar `Panel_Categories`
- `Root Panel Slots`: Arrastrar `Panel_Slots`

**Category Buttons:**
- `Btn Category Econ`: Arrastrar `Button_Category_1_Econ`
- `Btn Category Military`: Arrastrar `Button_Category_2_Military`
- `Btn Category Defenses`: Arrastrar `Button_Category_3_Defenses`
- `Btn Category Special`: Arrastrar `Button_Category_4_Special`

**Slot Buttons:**
- `Btn Slot 1` a `Btn Slot 9`: Arrastrar cada `Button_Slot_X` correspondiente

**Text Labels:**
- `Title Text`: Arrastrar `Text_Title`

## Comportamiento Esperado

1. **Sin aldeanos seleccionados**: Ambos paneles ocultos
2. **Con aldeanos, estado Idle**: Ambos paneles ocultos
3. **Con aldeanos, estado BuildRoot**: Panel_Categories visible, Panel_Slots oculto
4. **Con aldeanos, estado Category**: Panel_Categories oculto, Panel_Slots visible con edificios de esa categoría
5. **Con aldeanos, estado Placing**: Panel_Slots visible (mostrando qué estás colocando)

## Notas de Diseño

- Los botones de slot se ocultan automáticamente si no hay edificio en ese slot del BuildCatalog
- Los textos se actualizan automáticamente según el BuildCatalog
- El título muestra el estado actual o la categoría seleccionada
- Los clicks en botones llaman directamente a `build.EnterCategory()` y `build.PickSlot()`
