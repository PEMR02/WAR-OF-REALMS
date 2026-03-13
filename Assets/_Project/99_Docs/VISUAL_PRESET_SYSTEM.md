# Sistema visual modular RTS

Sistema de presets visuales con varios estilos, clima y ciclo día/noche. Optimizado para RTS (solo actualiza parámetros, no recrea materiales).

---

## PART 1 — VisualPreset (ScriptableObject)

**Ruta:** `Assets/_Project/04_Data/VisualPreset.cs`

Propiedades:

- **Directional Light:** lightIntensity, lightColor, lightRotation, shadowStrength  
- **Fog:** fogEnabled, fogDensity, fogColor  
- **Skybox:** skyboxExposure  
- **Color Grading:** postExposure, contrast, saturation  
- **Rim Lighting:** rimColor, rimIntensity, rimPower  
- **Terrain Style:** grassColor, shadowTint, highlightTint  
- **Vegetation:** vegetationSaturation, vegetationBrightness  

Crear nuevos presets: **Assets → Create → Project → Visual Preset**.

---

## PART 2 — VisualPresetManager

**Ruta:** `Assets/_Project/01_Gameplay/Environment/VisualPresetManager.cs`

- **ApplyPreset(VisualPreset preset):** aplica el preset (luz, niebla, skybox, post-processing, rim, terreno).
- **Inspector:** array **Presets** (arrastrar los ScriptableObject) e **Current Preset Index** (dropdown numérico).
- **SetPresetByIndex(int):** para cambiar desde UI o código.
- Referencias opcionales: Directional Light, Volume, Skybox Material, Rim Lighting Material, Terrain Material (si no se asignan, se buscan).

---

## PART 3–5 — Presets creados por menú

En Unity: **Tools → Project → Crear presets visuales (Stylized / Anime / Realistic)**.

Se crean en `Assets/_Project/04_Data/VisualPresets/`:

- **StylizedFantasyPreset** — Luz cálida, sombras azuladas, saturación moderada, rim activo.  
- **AnimeFantasyPreset** — Estilo Genshin: luz 1.45, niebla azul clara, saturación 30, rim 0.3, verdes vivos.  
- **RealisticRTSPreset** — Menos saturación, sombras fuertes, colores de terreno más neutros.  

Después de crearlos, asigna los tres al array **Presets** del **VisualPresetManager**.

---

## PART 6 — WeatherManager

**Ruta:** `Assets/_Project/01_Gameplay/Environment/WeatherManager.cs`

- **Tipos:** Sunny, Cloudy, Storm, **Foggy**.
- **Transición:** blend suave en el tiempo (**Blend Duration** en segundos).
- **SetSunny() / SetCloudy() / SetStorm() / SetFoggy()** — **SetPreset(0..3)**.
- Modifica sobre el preset actual (intensidad sol, densidad niebla, exposición skybox).

---

## PART 7 — DayNightCycle

**Ruta:** `Assets/_Project/01_Gameplay/Environment/DayNightCycle.cs`

- **Fases:** Morning, Day, Sunset, Night.
- **Sol:** rotación en Y, color (día / atardecer / noche), intensidad.
- **Skybox:** exposición día/noche.
- **Fog:** color según fase.
- **Cycle Duration Seconds:** duración de un ciclo completo.
- **Time Of Day:** 0–1 (avanza solo si **Auto Advance** está activo).

---

## PART 8 — Grid Visibility

**GridVisibility** y **GridGizmoRenderer** ya dejan la cuadrícula **solo visible al colocar edificios** (`showOnlyInBuildMode = true`). No hay que cambiar nada.

---

## PART 9 — Optimización

- Solo se actualizan parámetros (luces, RenderSettings, Volume overrides, materiales asignados).
- No se crean ni destruyen materiales ni luces al cambiar preset/clima/hora.
- Referencias reutilizadas (Light, Volume, Materials).

---

## Uso en el Inspector

1. **Visual Style:** en el objeto con **VisualPresetManager**, asigna los presets al array y elige **Current Preset Index** (0 = Stylized, 1 = Anime, 2 = Realistic) o llama **SetPresetByIndex** desde código/UI.
2. **Weather:** en **WeatherManager**, usa **SetSunny / SetCloudy / SetStorm / SetFoggy** o **SetPreset(0..3)**.
3. **Time of Day:** en **DayNightCycle**, ajusta **Time Of Day** y **Cycle Duration Seconds**; activa **Auto Advance** para que avance solo.

---

## Crear los presets por primera vez

Ejecuta en Unity: **Tools → Project → Crear presets visuales (Stylized / Anime / Realistic)**.  
Luego asigna los tres assets al **VisualPresetManager → Presets**.
