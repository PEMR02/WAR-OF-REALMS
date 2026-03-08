# FBX: las texturas no cargan en Unity (en Blender se ven bien)

Cuando exportas un modelo desde Blender a FBX y lo importas en Unity, es muy común que **las texturas no aparezcan** y el modelo se vea gris, verde plano o con un material por defecto. Esto es normal: el FBX suele guardar solo la **geometría y los nombres de materiales**, no las rutas a las texturas de forma que Unity las resuelva solo.

## Por qué pasa

- En Blender las texturas están en tu archivo `.blend` o en carpetas de tu proyecto.
- El FBX exporta el mesh y los materiales, pero las **imágenes (PNG, JPG, TGA)** no van “dentro” del FBX por defecto.
- Unity no sabe dónde están esas imágenes a menos que **estén en el proyecto de Unity** y alguien (tú o el importador) las asocie al material.

## Solución recomendada (rápida)

### 1. Llevar las texturas al proyecto de Unity

1. En Blender, localiza las texturas del edificio:
   - **En español:** pestaña **Sombreado** (o **Shader**) en la parte superior, nodo **Imagen** o **Textura de imagen**.
   - Si no ves el nodo: selecciona el objeto → pestaña **Sombreado** → en el editor de nodos busca el nodo que tenga una imagen (color/difuso).
2. Guarda o copia esas imágenes (exportar como PNG/JPG, o copia el archivo desde la ruta que usa Blender).
3. En Unity, crea una carpeta junto a tu FBX, por ejemplo:
   - `Assets/_Project/08_Prefabs/Buildings/TownCenter/`
   - Pon ahí el **FBX** y una subcarpeta **Texturas** (o el nombre que quieras).
4. Arrastra las imágenes (color/diffuse, normal si la tienes, etc.) a esa carpeta de Unity.

### 2. Asignar la textura al material en Unity

1. En Unity, selecciona el **material** que usa el edificio:
   - Puede ser **material_0** (generado por el FBX) dentro del propio FBX en el Project.
   - O un material externo como **MAT_TownCenter** si ya lo usas en el prefab.
2. En el Inspector, en **Surface Inputs**:
   - En **Base Map** (o Albedo / Color map), arrastra la **textura de color** que copiaste (la imagen principal del edificio).
3. (Opcional) Si tienes mapa normal o de rugosidad, asígnalos en sus slots.
4. Guarda el material (Ctrl+S).

### 3. Si el prefab usa otro material

Si tu **PF_TownCenter** (o el edificio) usa **MAT_TownCenter** (u otro material) y no el que genera el FBX:

1. Abre ese material (**MAT_TownCenter**).
2. Asigna en **Base Map** la textura de color del edificio.
3. El prefab ya está usando ese material, así que al guardar se verá la textura en el modelo.

## Alternativa: exportar desde Blender con “Copy” de texturas

Al exportar el FBX en Blender con la interfaz **en español**:

1. **Archivo → Exportar → FBX (.fbx)**.
2. En el panel de opciones (derecha o abajo): **Modo de ruta** → **Copiar**. Opcional: **Incrustar texturas** (marcar si quieres que las texturas vayan dentro del FBX; Unity a veces las extrae).
3. Si usas **Copiar** y no incrustar: Blender copiará las texturas a la misma carpeta donde guardes el FBX. Luego **mueve esa carpeta entera** (FBX + imágenes) a tu proyecto de Unity (por ejemplo `Assets/_Project/08_Prefabs/Buildings/TownCenter/`).
4. En Unity, **Reimportar** el FBX (clic derecho sobre el FBX → Reimportar). Si Unity no enlaza las texturas solo, asigna la textura al material a mano como en "Asignar la textura al material en Unity" más arriba.

### Referencia rápida (Blender en español)

| Inglés           | Español (Blender)        |
|------------------|--------------------------|
| File             | Archivo                  |
| Export           | Exportar                 |
| Shading          | Sombreado / Shader       |
| Image Texture    | Imagen / Textura de imagen |
| Path Mode        | Modo de ruta             |
| Copy             | Copiar                   |
| Embed Textures   | Incrustar texturas       |
| Base Color       | Color base               |
| Save As          | Guardar como             |

## Resumen para tu Town Center (o cualquier edificio FBX)

| Paso | Acción |
|------|--------|
| 1 | Copiar/exportar desde Blender la imagen de la textura (color) del edificio. |
| 2 | Poner esa imagen en la carpeta del proyecto Unity (junto al FBX o en una subcarpeta). |
| 3 | Abrir el material que usa el modelo (material_0 del FBX o MAT_TownCenter). |
| 4 | En **Base Map** asignar esa textura. |
| 5 | Guardar. El edificio debería verse ya con textura. |

Si después de esto sigue en gris/verde, revisa que el **prefab** (PF_TownCenter) esté usando el material al que acabas de asignar la textura, y no otro material sin textura.
