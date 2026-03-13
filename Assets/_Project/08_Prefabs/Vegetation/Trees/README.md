# Vegetation — Tree Prefabs

Generadores procedurales de árboles para Blender MCP.  
Estilo: **RTS Stylized Realism** (Anno / Genshin inspired).

---

## Árbol base aprobado: Oak Realista

| Parámetro        | Valor         |
|------------------|---------------|
| Altura           | 8.5 m         |
| Radio de tronco  | 0.28 m (base) |
| Hojas            | ~16,950       |
| Polígonos LOD0   | ~137,511      |
| Materiales       | MyBark + MyLeaf (2 slots, single mesh) |

### Decisiones de diseño aprobadas
- **Tronco**: cónico uniforme, flare sutil (28%) solo en el 8% inferior
- **Ramas medias**: 7 ramas a 38-56° del eje Z
- **Ramas altas**: 9 ramas a 12-30°, longitud 2.8-4.2m para llegar a la copa superior
- **Clusters de hojas**: SOLO en puntas de ramitas (nivel 3). Sin clusters flotantes.
- **Puff doble**: cluster principal + sub-cluster lateral para aspecto natural

---

## Cómo regenerar en Blender

1. Abre Blender con el addon MCP activo
2. Ejecuta en Cursor con `execute_blender_code` el contenido de `tree_oak_generator.py`
   (la función `build_script()` genera el código listo para pegar)
3. El objeto `Tree_RealismOak` aparecerá en la escena

---

## Variantes generadas con LOD

| Variante           | Archivo principal             | Estado    | LOD0 polys | LOD0 hojas |
|--------------------|-------------------------------|-----------|------------|------------|
| Oak (base original)| `tree_oak_generator.py`       | ✅ Aprobado | ~137,511  | ~16,950    |
| Tall Forest Tree   | `tree_all_variants.py`        | ✅ Generado | ~111,180  | ~13,673    |
| Wide Oak           | `tree_all_variants.py`        | ✅ Generado | ~158,823  | ~19,598    |
| Young Dense        | `tree_all_variants.py`        | ✅ Generado | ~162,445  | ~20,019    |
| Wind Bent          | `tree_all_variants.py`        | ✅ Generado | ~125,184  | ~15,411    |
| Sparse Natural     | `tree_all_variants.py`        | ✅ Generado |  ~67,550  |  ~8,255    |

### LOD System
| LOD   | Factor | Hojas (%) | Uso recomendado Unity |
|-------|--------|-----------|----------------------|
| LOD0  | 1.0    | 100%      | 0 – 25 m             |
| LOD1  | 0.6    | 60%       | 25 – 50 m            |
| LOD2  | 0.3    | 30%       | 50 – 100 m           |

Configurar en Unity LOD Group con los tres sub-meshes exportados por separado.

---

## Materiales

### MyBark (slot 0)
- Ruido procesal → ColorRamp → Principled BSDF
- Oscuro: `(0.25, 0.13, 0.05)` — Claro: `(0.55, 0.38, 0.20)`
- Roughness: 0.88

### MyLeaf (slot 1)
- Principled BSDF puro
- Base Color: `(0.08, 0.35, 0.06)` verde saturado
- Roughness: 0.72 | Subsurface Weight: 0.08
- Backface Culling: OFF (hojas visibles por ambos lados)

---

## Notas de compatibilidad (Blender 5.0.1)
- `use_auto_smooth` eliminado — usar `p.use_smooth = True` por polígono
- Render engine: `BLENDER_EEVEE` (no `BLENDER_EEVEE_NEXT`)
- Subsurface: `"Subsurface Weight"` (no `"Subsurface"`)
