# Iconos en Unity: un sprite por icono vs atlas único

## Recomendación de Unity

**Unity recomienda usar Sprite Atlas** para UI e iconos: varias imágenes empaquetadas en **una o varias texturas** en lugar de cientos de texturas sueltas.

| Enfoque | Ventajas | Inconvenientes |
|--------|----------|-----------------|
| **Varios sprites en un atlas (una textura)** | Menos draw calls, mejor batching, menos cambios de textura, menos VRAM si se agrupa bien | Hay que organizar por contexto de uso |
| **Un archivo de imagen por icono** | Organización clara en carpetas, versionado fácil | Más draw calls si no se empaquetan; muchas texturas en memoria si no usas atlas |
| **Un solo PNG con todos los iconos (sprite sheet)** | Un solo asset, fácil de exportar desde arte | Corte en Sprite Editor (Grid By Cell Size); si cambias uno, tocas el sheet entero |

---

## Cómo trabajar en la práctica

### Opción A: Una sola imagen con todos los iconos (sprite sheet)

1. El arte te entrega **un PNG** con una cuadrícula de iconos (ej. 64×64 px por icono).
2. En Unity: importar la textura → **Texture Type**: Sprite (2D and UI) → **Sprite Mode**: **Multiple**.
3. Abrir **Sprite Editor** → **Slice** → **Grid By Cell Size** (ej. 64×64), Pivot según necesites (Center, Top Left, etc.) → **Slice**.
4. Quedan **varios sprites** (sub-assets) de una sola textura. En código o UI asignas `Image.sprite = iconoSprite`.
5. Opcional: crear un **Sprite Atlas** y añadir esa textura a "Objects for Packing" para que Unity la empaquete con otras y optimice (menos huecos, tamaños por plataforma).

**Ventaja:** un solo asset de arte, pocas texturas. **Desventaja:** cualquier cambio de un icono obliga a tocar el PNG completo (a menos que separes por filas/columnas en varios PNG).

### Opción B: Un archivo por icono y Unity los empaqueta (recomendada para equipos)

1. Cada icono es un **PNG propio** (ej. `icon_wood.png`, `icon_gold.png`, `icon_house.png`).
2. Importar como **Sprite (2D and UI)**; Sprite Mode puede ser **Single**.
3. Crear uno o varios **Sprite Atlas** (clic derecho en Project → Create → **Sprite Atlas**).
4. En cada atlas, en **Objects for Packing**, añadir las **carpetas o texturas** que correspondan (ej. `Assets/_Project/Art/UI/Icons_HUD`).
5. **Pack Preview** (o dejar que se empaquete en build). En runtime, cuando usas cualquier sprite de ese atlas, Unity carga **el atlas entero** (una textura).

**Ventaja:** arte puede tocar iconos de uno en uno; Unity genera el atlas automáticamente. **Desventaja:** hay que agrupar bien por uso (ver abajo).

---

## Cómo organizar atlases (importante)

- **No pongas todos los iconos del juego en un solo atlas gigante:** si en una escena solo usas 5 iconos, cargarías toda la textura.
- **Agrupa por contexto:**
  - Un atlas para **menú principal** (logo, botones, fondos de menú).
  - Otro para **HUD in-game** (recursos, unidades, edificios, minimapa).
  - Otro para **iconos de mundo** (marcadores, pings, estados de unidad) si son muchos.
- Objetivo: en cada escena, que **la mayoría de sprites visibles vengan del mismo atlas** para mejorar batching.

---

## En este proyecto (WAR OF REALMS)

- **Minimapa** (`RuntimeMinimapBootstrap`): los “iconos” son hoy solo **color** (`Image.sprite = null`). Cuando quieras iconos reales, conviene un **Sprite Atlas** solo para minimapa/HUD (por ejemplo `Icons_HUD`) y asignar sprites por tipo (unidad, edificio, recurso).
- **IdleVillagerIcon**: igual, actualmente un quad con material de color. Si más adelante usas sprite, puede salir del mismo atlas de HUD.
- **UI de edificios/recursos**: si tendrás barras de recursos, botones de edificios, etc., un atlas **Icons_Game** o **Icons_HUD** con un PNG por icono (opción B) suele ser lo más flexible.

---

## Resumen

| Pregunta | Respuesta |
|----------|-----------|
| ¿Uno por uno o un solo sprite con todo? | **Ambos son válidos.** Un solo PNG con todo (sprite sheet) = Opción A. Muchos PNG y Unity los empaqueta en atlases = Opción B (recomendada por Unity para mantener y escalar). |
| ¿Qué usa Unity por debajo? | En ambos casos lo ideal es que esos sprites formen parte de un **Sprite Atlas** para reducir draw calls y memoria. |
| ¿Dónde leer más? | [Unity – Sprite Atlas workflow](https://docs.unity3d.com/Manual/SpriteAtlasWorkflow.html), [Optimize Sprite Atlas usage](https://docs.unity3d.com/Manual/optimize-sprite-atlas-usage-size-improved-performance.html), [Learn: UI Sprite Atlasing](https://learn.unity.com/tutorial/ui-sprite-atlasing-1). |

**Configuración necesaria:** En **Edit > Project Settings > Editor**, en **Sprite Packer**, modo **Enabled for Builds** (o Always Enabled si quieres ver atlases empaquetados en el editor).
