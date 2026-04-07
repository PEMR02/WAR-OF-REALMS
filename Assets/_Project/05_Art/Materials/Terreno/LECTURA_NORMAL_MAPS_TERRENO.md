# Normal maps del terreno — por qué se ven mal

## Qué se revisó

En los **Terrain Layers** del proyecto (`Texture_Grass`, `Texture_Dirt`, `Texture_Rock`, `Texture_Sand`) tienes asignadas texturas en el slot **Normal Map**:

| Terrain Layer | Textura usada como Normal Map | Import actual |
|---------------|-------------------------------|----------------|
| Texture_Grass | Grass_01.png                  | **Default** (color) |
| Texture_Dirt  | Dirt.png                     | **Default** (color) |
| Texture_Rock  | Rock.png                     | **Default** (color) |
| Texture_Sand  | Sand.png                     | **Default** (color) |

En Unity, en el importador de texturas, **Texture Type = Normal map** corresponde a `textureType: 1` en el `.meta`; **Default** es `textureType: 0`.

`Sand.png` y `Dirt.png` en este proyecto son **color / detalle**, no normales tangentes. Si las marcas como Normal map **y** las pones en el slot Normal del Terrain Layer, Unity trata cada píxel como vector → **manchas en cuadrícula**, sombras raras en la arena, etc.

**Normales válidos** (tangent-space, tonos azul/violeta típicos): `Grass_01.png` y `Rock.png` siguen como Normal map (`textureType: 1`, sin sRGB).

---

## Opciones para arreglarlo

### Opción A: Quitar el normal map (rápido)

Si no tienes normal maps reales:

1. En **Project** abre cada Terrain Layer: `Texture_Grass`, `Texture_Dirt`, `Texture_Rock`, `Texture_Sand`.
2. En **Normal Map Texture** deja el slot **vacío** (None).
3. Guarda.

El terreno se verá más plano pero **correcto**, sin iluminación rara. El color (Diffuse) sigue igual.

---

### Opción B: Usar normal maps de verdad (mejor resultado)

1. **Conseguir normal maps**  
   - Que vengan con el pack de texturas (ej. `Grass_01_Normal.png`), o  
   - Generarlos desde el difuso (Photoshop, GIMP, o herramientas como [NormalMap-Online](https://cpetry.github.io/NormalMap-Online/)).

2. **Importar en Unity**  
   - Selecciona la textura del normal (ej. `Grass_01_Normal.png`).  
   - En **Inspector → Texture Importer**: **Texture Type** = **Normal map**.  
   - **Apply**.

3. **Asignar en el Terrain Layer**  
   - Abre el Terrain Layer (ej. `Texture_Grass`).  
   - En **Normal Map Texture** arrastra la textura que importaste como Normal map (no la de color).

Repite para Grass, Dirt, Rock y Sand. Así el terreno tendrá relieve y luces coherentes.

---

## Resumen

- **Problema:** Las texturas del slot “Normal Map” son de **color** (Default), no normales → mal resultado.
- **Solución rápida:** Dejar **Normal Map Texture** en vacío en los 4 Terrain Layers.
- **Solución buena:** Usar texturas que sean **normal maps** e importarlas con **Texture Type = Normal map**.

El código del proyecto **no** modifica los normal maps; el fallo viene solo de la configuración de las texturas y de los Terrain Layers.
