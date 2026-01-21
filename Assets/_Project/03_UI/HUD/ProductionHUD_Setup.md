# 🏭 ProductionHUD - Guía de Configuración

Sistema de entrenamiento de unidades estilo Age of Empires II.

---

## 📦 **1. Crear UnitSO (Unidades)**

### En Unity Editor:

1. **Carpeta**: `Assets/_Project/04_Data/ScriptableObjecs/Units/`
2. **Right Click** → `Create → Project → Unit`
3. **Crear 2 unidades de ejemplo:**

#### **Militia (Milicia)**
- **id**: `militia`
- **displayName**: `Milicia`
- **prefab**: `Unit_01` (o dejar vacío por ahora)
- **costs**:
  - Wood: 50
  - Food: 20
- **trainingTimeSeconds**: `12`
- **maxHP**: `100`
- **attack**: `8`
- **moveSpeed**: `3.5`

#### **Archer (Arquero)**
- **id**: `archer`
- **displayName**: `Arquero`
- **prefab**: `Unit_01` (o dejar vacío por ahora)
- **costs**:
  - Wood: 25
  - Gold: 45
- **trainingTimeSeconds**: `18`
- **maxHP**: `70`
- **attack**: `12`
- **moveSpeed**: `3`

---

## 📋 **2. Crear ProductionCatalog**

### En Unity Editor:

1. **Carpeta**: `Assets/_Project/04_Data/ScriptableObjecs/Units/`
2. **Right Click** → `Create → RTS → Production Catalog`
3. **Nombre**: `ProductionCatalog_Asset`

### Configurar entries:

#### **Barracks (Cuartel)**
- **Entry 1:**
  - buildingId: `barracks`
  - slot: `1`
  - unit: `Militia_UnitSO`
  
- **Entry 2:**
  - buildingId: `barracks`
  - slot: `2`
  - unit: `Archer_UnitSO`

#### **Archery Range (Arquería)** (opcional)
- **Entry 3:**
  - buildingId: `archery_range`
  - slot: `1`
  - unit: `Archer_UnitSO`

---

## 🖼️ **3. Configurar UI en Canvas**

### Jerarquía:

```
Canvas
├── HUD_Bottom
│   ├── HUD_Villager (ya existente)
│   └── HUD_Production (NUEVO)
│       ├── Panel_Units
│       │   ├── Title (TextMeshProUGUI)
│       │   ├── Button_Unit1 → Text: "1. Milicia"
│       │   ├── Button_Unit2 → Text: "2. Arquero"
│       │   ├── ... (hasta Button_Unit9)
│       ├── Panel_Queue
│       │   ├── Title (TextMeshProUGUI: "Cola de producción")
│       │   ├── Progress (TextMeshProUGUI: "Entrenando: Milicia (45%)")
│       │   └── QueueContainer (Vertical Layout Group)
└── ProductionHotkeyRouter (Component en Canvas)
```

---

## 🎨 **4. Detalles de GameObjects UI**

### **HUD_Production** (GameObject padre)
- **RectTransform**: Stretch horizontalmente, altura ~200px
- **Anchor**: Bottom-stretch
- **Position**: Junto a `HUD_Villager`

---

### **Panel_Units** (hijo de HUD_Production)
- **RectTransform**: Width: 600, Height: 150
- **Layout**: `Grid Layout Group`
  - Cell Size: 190 x 40
  - Spacing: 5, 5
  - Constraint: Fixed Column Count = 3
- **Background**: Image con color semi-transparente
- **Canvas Group**: 
  - Alpha: 1
  - Interactable: ✓
  - Block Raycasts: ✓

#### **Title** (hijo de Panel_Units)
- **Component**: TextMeshProUGUI
- **Text**: "Cuartel"
- **Font Size**: 18
- **Alignment**: Center

#### **Button_Unit1 a Button_Unit9** (9 botones)
- **Component**: Button + Image
- **Child**: TextMeshProUGUI
  - Text: "1. Milicia", "2. Arquero", etc.
  - Font Size: 14
  - Alignment: Left
- **Colors**: Normal (blanco), Highlighted (amarillo), Pressed (verde), Disabled (gris)

---

### **Panel_Queue** (hijo de HUD_Production)
- **RectTransform**: Width: 200, Height: 150
- **Layout**: `Vertical Layout Group`
  - Child Alignment: Upper Left
  - Child Force Expand: Width ✓
  - Spacing: 5
- **Background**: Image con color semi-transparente

#### **Title** (hijo de Panel_Queue)
- **Component**: TextMeshProUGUI
- **Text**: "Cola de producción"
- **Font Size**: 16

#### **Progress** (hijo de Panel_Queue)
- **Component**: TextMeshProUGUI
- **Text**: ""
- **Font Size**: 12
- **Color**: Amarillo

#### **QueueContainer** (hijo de Panel_Queue)
- **Component**: Vertical Layout Group
- **Aquí se generarán dinámicamente los items de la cola**

---

## 🔧 **5. Asignar Referencias en Inspector**

### **ProductionHUD** (Component en HUD_Production GameObject)

#### **Refs:**
- **selection**: Arrastrar `RTSSelectionController` desde la escena
- **catalog**: Arrastrar `ProductionCatalog_Asset`

#### **Panels:**
- **panelUnits**: Arrastrar `Panel_Units`
- **panelQueue**: Arrastrar `Panel_Queue`

#### **Unit Buttons (1-9):**
- **btnUnit1**: Arrastrar `Button_Unit1`
- **btnUnit2**: Arrastrar `Button_Unit2`
- ... (hasta btnUnit9)

#### **Queue Display:**
- **queueContainer**: Arrastrar `QueueContainer`
- **queueItemPrefab**: (Opcional - dejar vacío por ahora)

#### **Text Labels:**
- **titleText**: Arrastrar `Title` (hijo de Panel_Units)
- **progressText**: Arrastrar `Progress` (hijo de Panel_Queue)

---

### **ProductionHotkeyRouter** (Component en Canvas)

#### **Refs:**
- **productionHUD**: Arrastrar el GameObject `HUD_Production`

---

## 🏢 **6. Configurar Edificios (Barracks)**

### Prefab del Cuartel (Barracks):

1. **Abrir** el prefab del cuartel en `Assets/_Project/08_Prefabs/Buildings/`
2. **Add Component** → `BuildingInstance`
   - **buildingSO**: Asignar el `BuildingSO` correspondiente (si existe)
3. **Add Component** → `ProductionBuilding`
   - **owner**: Se auto-asignará en runtime (o asignar PlayerResources)
   - **spawnPoint**: Crear un GameObject hijo vacío `SpawnPoint`
     - Position: Delante del edificio (ej: X: 0, Y: 0, Z: 5)
4. **Add Component** → `BuildingSelectable` (si no existe)
   - Layer: `Building` (Layer 9)

---

## 🎮 **7. Cómo Usar**

### **En el juego:**

1. **Seleccionar** un cuartel (click izquierdo)
2. **Ver** el panel de unidades (aparece automáticamente)
3. **Entrenar unidad**:
   - **Click** en botón de unidad
   - O presionar **tecla 1-9**
4. **Ver cola** de producción en tiempo real
5. **Cancelar** unidad en cola (click derecho en item - próxima versión)

---

## 🔍 **8. Troubleshooting**

### **El panel no aparece al seleccionar edificio:**
- Verificar que el edificio tenga `ProductionBuilding` component
- Verificar que el edificio tenga `BuildingSelectable` component
- Verificar que `ProductionHUD.selection` esté asignado

### **Los botones están vacíos:**
- Verificar que `ProductionCatalog` esté asignado
- Verificar que `buildingId` en catalog coincida con el id del edificio
- Verificar que las unidades estén asignadas en las entries

### **Las teclas no funcionan:**
- Verificar que `ProductionHotkeyRouter` esté en el Canvas
- Verificar que la referencia a `productionHUD` esté asignada

### **Las unidades no se entrenan:**
- Verificar que el jugador tenga recursos suficientes
- Verificar que `ProductionBuilding.owner` esté asignado a `PlayerResources`
- Verificar que `UnitSO.prefab` esté asignado

---

## ✅ **9. Próximas Mejoras**

- [ ] Icono de unidad en botones
- [ ] Barra de progreso visual
- [ ] Click derecho para cancelar unidad en cola
- [ ] Sonidos de entrenamiento
- [ ] Rally point para unidades
- [ ] Límite de población
- [ ] Teclas de grupo (Ctrl+1, Ctrl+2...)
- [ ] Múltiple selección de edificios
