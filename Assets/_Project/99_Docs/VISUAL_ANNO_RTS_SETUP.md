# Setup visual tipo Anno 117 / RTS moderno (URP)

Objetivo: **iluminación cálida lateral, ambient occlusion, niebla atmosférica y color grading** para que el RTS se vea coherente y con profundidad (Anno, Manor Lords, AoE IV).

---

## 1. Skybox procedural

1. En Unity: **Tools → Project → Crear assets de entorno RTS (Skybox procedural)**.
2. Se crea `Assets/_Project/06_Visual/Environment/MAT_Skybox_Procedural_RTS.mat`.
3. **Window → Rendering → Lighting → Environment** → en **Skybox Material** asigna ese material.
4. Si el shader no es Procedural (URP a veces lo cambia), crea un material manual: **Assets → Create → Material**, shader **Skybox/Procedural**, y ajusta:
   - Sun Size: **0.04**
   - Atmosphere Thickness: **1.1**
   - Sky Tint: azul claro
   - Ground Color: gris azulado
   - Exposure: **1.2**

---

## 2. Sol (Directional Light)

- **GameObject → Light → Directional Light**
- **Intensity:** 1.2  
- **Color:** amarillo cálido  
- **Shadow Type:** Soft  
- **Shadow Strength:** 0.9  
- **Rotación típica RTS:** X=50, Y=-30, Z=0 (sombras largas)

Opcional: asigna esta luz en **RTSLightingBootstrap** o **WeatherManager** para control por script.

---

## 3. Niebla atmosférica

- **Window → Rendering → Lighting → Environment**
- **Fog:** activado
- **Mode:** Exponential
- **Density:** 0.004 (subir a ~0.006–0.01 para más atmósfera)
- **Color:** azul grisáceo

También se aplica desde **RTSLightingBootstrap** (componente en escena) si asignas los valores ahí.

---

## 4. Post Processing (Global Volume)

1. **GameObject → Volume → Global Volume**
2. En el componente **Volume**: **Profile** → crear nuevo o usar uno existente (ej. `DefaultVolumeProfile`).
3. **Add Override** y añade:

| Componente           | Recomendación Anno/RTS                          |
|---------------------|--------------------------------------------------|
| **Color Adjustments** | Post Exposure: **+0.3**, Contrast: **15**, Saturation: **5** |
| **Ambient Occlusion** | Intensity: **0.4**, Radius: **0.2** (dar profundidad entre edificios) |
| **Bloom**             | Intensity: **0.2**, Threshold: **1** (muy suave) |

Con eso se gana profundidad y un look más cinematográfico sin tocar modelos.

---

## 5. Scripts de entorno (runtime)

- **RTSLightingBootstrap**  
  Aplica al inicio: skybox, fog (densidad y color) y opcionalmente rotación/intensidad del sol.  
  Colócalo en un GameObject que exista en la escena principal y asigna el material del skybox y la Directional Light si quieres control por código.

- **WeatherManager**  
  Cambia clima: **SetSunny()**, **SetCloudy()**, **SetStorm()**.  
  Modifica intensidad del sol, densidad de la niebla y exposición del skybox.  
  Asigna la **Directional Light** y el **Material del Skybox** en el inspector.

---

## 6. Rendimiento (mapas grandes)

- Mantener **Shadow Distance** razonable (ej. 80–120) en **Project Settings → Quality** o en el URP Asset.
- **Fog** y **AO** en post-proceso tienen coste bajo; Bloom muy suave también.
- Si hay muchos draw calls, revisar batching y LODs antes que quitar post-proceso.

---

## 7. Resumen rápido

1. Crear skybox procedural (menú **Tools → Project**) y asignarlo en **Lighting → Environment**.
2. Directional Light con Intensity 1.2, rotación (50, -30, 0), sombras suaves.
3. Fog Exponential, Density 0.004, color azul gris.
4. Global Volume con Color Adjustments, Ambient Occlusion y Bloom suave.
5. Opcional: **RTSLightingBootstrap** + **WeatherManager** en escena para control en runtime.

Con estas capas el estilo se acerca a Anno/Manor Lords incluso con modelos simples.
