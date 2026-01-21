# 🚀 Configuración de Mejoras del ProductionHUD

---

## ✅ **Mejoras Implementadas:**

1. ✅ Barra de progreso visual (Slider)
2. ✅ Mostrar costos en botones (con emojis 🪵🪨🪙🍖)
3. ✅ Click derecho para cancelar unidad en cola
4. ✅ Sistema de límite de población

---

## 📋 **1. Agregar Barra de Progreso (Slider)**

### **En Unity Editor:**

1. **Seleccionar** `Panel_Queue` en Hierarchy
2. **Click derecho** → `UI → Slider`
3. **Nombre**: `ProgressBar`
4. **Configurar Slider:**
   - **Min Value**: 0
   - **Max Value**: 1
   - **Interactable**: ❌ (desmarcar)
   
5. **RectTransform del Slider:**
   - **Width**: 280
   - **Height**: 20
   - **Anchors**: Top-stretch
   
6. **Personalizar apariencia:**
   - **Background**: Color gris oscuro (opcional)
   - **Fill**: Color verde o amarillo
   - **Handle**: Ocultar (desactivar GameObject "Handle Slide Area")

7. **Asignar en ProductionHUD Inspector:**
   - **progressBar**: Arrastrar el Slider

---

## 👥 **2. Configurar Sistema de Población**

### **A) Crear PopulationManager:**

1. **En Hierarchy**, crear `Empty GameObject`
2. **Nombre**: `GameManagers`
3. **Add Component** → `PopulationManager`
4. **Configurar en Inspector:**
   - **Current Population**: 0 (se actualiza automáticamente)
   - **Max Population**: 200 (límite absoluto)
   - **Current Housing Capacity**: 5 (Town Center inicial)

---

### **B) Crear PopulationHUD (UI):**

1. **En Canvas**, crear **Text - TextMeshPro**
2. **Nombre**: `Text_Population`
3. **RectTransform:**
   - **Anchors**: Top-Right
   - **Position**: X: -100, Y: -30
   - **Width**: 150
   - **Height**: 30

4. **TextMeshProUGUI:**
   - **Text**: `👥 0/5`
   - **Font Size**: 20
   - **Alignment**: Right
   - **Color**: White

5. **Add Component** → `PopulationHUD`
6. **Asignar en Inspector:**
   - **populationManager**: Arrastrar `GameManagers` (con el component)
   - **populationText**: Auto-asignado (mismo GameObject)

---

### **C) Configurar Edificios que dan Población:**

Para **House** (Casa):

1. **Abrir prefab** `PF_House`
2. **Add Component** → `BuildingController` (si no existe)
3. **En script BuildingController**, agregar:
   ```csharp
   void Start()
   {
       var popManager = FindFirstObjectByType<PopulationManager>();
       if (popManager != null)
           popManager.AddHousingCapacity(5); // Casa da +5 población
   }
   
   void OnDestroy()
   {
       var popManager = FindFirstObjectByType<PopulationManager>();
       if (popManager != null)
           popManager.RemoveHousingCapacity(5);
   }
   ```

---

## 🎨 **3. Ajustes Visuales (Opcionales)**

### **Botones de Unidades:**

Los botones ahora muestran:
```
1. Arquero
🪵:25 | 🪙:45
```

**Para mejor legibilidad:**
1. **Seleccionar** cada `Button_UnitX` → `Text`
2. **Font Size**: 12 (más pequeño para 2 líneas)
3. **Alignment**: Left + Top
4. **RectTransform del botón**:
   - **Height**: 50 (más alto para 2 líneas)

### **Cola de Producción:**

Los items ahora muestran:
- **Primera unidad**: `1. Arquero (45%)` en amarillo
- **Resto**: `2. Arquero` en blanco

**Click derecho en cualquier item para cancelar**

---

## 🎮 **4. Cómo Usar**

### **Entrenar Unidades:**

1. **Seleccionar** Cuartel
2. **Presionar tecla 1-2** o **click en botón**
3. **Ver progreso** en:
   - Texto: `Entrenando: Arquero (45%)`
   - Barra visual: Verde avanzando
   - Cola: `1. Arquero (45%)`

### **Cancelar Unidades:**

- **Click derecho** en item de la cola
- **Devuelve 50%** de recursos
- **NO devuelve** espacio de población (la población se libera cuando la unidad muere)

### **Población:**

- **Top-right**: `👥 5/10`
  - **Blanco**: Normal
  - **Amarillo**: 80%+ lleno
  - **Rojo**: Límite alcanzado

- **No puedes entrenar** si no hay espacio de población
- **Construir casas** para aumentar límite

---

## 🔍 **5. Troubleshooting**

### **La barra de progreso no aparece:**
- Verificar que `progressBar` esté asignado en ProductionHUD
- Verificar que el Slider esté activo en Hierarchy
- El Slider se oculta cuando no hay producción (esto es normal)

### **Los costos no se muestran:**
- Verificar que las unidades tengan costs asignados en el Inspector
- Verificar que el texto del botón tenga altura suficiente (50px)

### **La población no se actualiza:**
- Verificar que `GameManagers` con `PopulationManager` esté en la escena
- Verificar que `PopulationHUD` tenga la referencia asignada
- Revisar consola por errores

### **No puedo entrenar unidades:**
- Verificar recursos suficientes
- Verificar espacio de población: `👥 5/5` = lleno
- Construir más casas para aumentar límite

### **Click derecho no cancela:**
- Asegurarse de que estás haciendo click derecho en el texto de la cola
- Verificar que haya unidades en cola
- Revisar consola por errores

---

## 📊 **6. Stats de Población Recomendados**

### **Unidades:**
- **Aldeano**: 1 población
- **Milicia**: 1 población
- **Arquero**: 1 población
- **Caballería**: 2 población (futuro)
- **Unidades pesadas**: 3-4 población (futuro)

### **Edificios:**
- **Town Center**: +5 población inicial
- **Casa**: +5 población
- **Keep/Castillo**: +10 población (futuro)

### **Límites:**
- **Máximo absoluto**: 200 población
- **Inicial**: 5 (solo Town Center)
- **1 Casa**: 10 población
- **10 Casas**: 55 población
- **40 Casas**: 205 → límite en 200

---

## ✅ **7. Checklist de Configuración**

- [ ] Slider agregado a Panel_Queue
- [ ] ProgressBar asignado en ProductionHUD Inspector
- [ ] GameManagers con PopulationManager creado
- [ ] Text_Population con PopulationHUD creado
- [ ] Referencias asignadas en PopulationHUD
- [ ] Botones de unidades con altura 50px
- [ ] UnitSO assets tienen populationCost = 1
- [ ] Probar entrenar unidad y ver progreso
- [ ] Probar click derecho para cancelar
- [ ] Probar límite de población (entrenar 5 unidades)

---

## 🚀 **8. Próximas Mejoras Sugeridas**

- [ ] **Rally Point**: Punto de reunión para unidades entrenadas
- [ ] **Icono de unidad**: Sprites en botones y cola
- [ ] **Sonidos**: SFX al entrenar/completar unidad
- [ ] **Hotkeys de cancelación**: ESC o Delete para cancelar seleccionado
- [ ] **Múltiple selección de edificios**: Entrenar desde varios cuarteles
- [ ] **Cola infinita visual**: Scroll en cola si hay >5 unidades
- [ ] **Teclas de grupo**: Ctrl+1, Ctrl+2 para edificios
- [ ] **Auto-entrenamiento**: Opción de entrenar continuamente

---

¿Listo? ¡Prueba el sistema y cuéntame cómo funciona! 🎯
