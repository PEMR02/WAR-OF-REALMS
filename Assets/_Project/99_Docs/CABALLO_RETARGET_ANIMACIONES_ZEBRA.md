# Pasar animaciones de la Zebra al caballo (primer modelo)

Objetivo: usar la **malla y el rig del primer caballo** (Horse Rigged Game Ready) con las **animaciones Idle, Walk y Run** del segundo modelo (Cartoon Zebra with 7 animations). Las animaciones de la Zebra solo llegan a Blender si descargas el modelo **manualmente** desde Sketchfab en FBX.

## Pasos

### 1. Descargar la Zebra con animaciones desde Sketchfab

1. Abre: [Cartoon Zebra with 7 animations](https://sketchfab.com/3d-models/cartoon-zebra-with-7-animations-c77056f81b564805a1001f3933d5d0fc)
2. Pulsa **Download 3D Model**.
3. Elige un formato que incluya animaciones (por ejemplo **FBX** o **glTF**).
4. Guarda el archivo en tu proyecto, por ejemplo:  
   `WAR OF REALMS\Assets\_Project\Art\Zebra_7anim.fbx`  
   (crea la carpeta `Art` si no existe.)

### 2. En Blender

1. **Tener el caballo en escena**  
   Si no lo tienes: descarga de nuevo el primer modelo (Horse Rigged Game Ready) con el MCP de Blender o impórtalo desde un FBX que ya tengas.

2. **Importar la Zebra**  
   - **File → Import → FBX** (o el formato que hayas descargado).  
   - Selecciona el archivo de la Zebra (ej. `Zebra_7anim.fbx`).  
   - Así tendrás en la misma escena: caballo (armature `GLTF_created_0`) y zebra (p. ej. armature `Object_4`).

3. **Ejecutar el script de retarget**  
   - **Scripting** (workspace o pestaña).  
   - **Open** → abre:  
     `Tools/Blender/retarget_zebra_to_horse.py`  
   - Pulsa **Run Script**.  
   - En la consola deberían aparecer mensajes del tipo:  
     `Retarget listo: Idle -> Idle_horse`, etc.

4. **Resultado**  
   - En el caballo (`GLTF_created_0`) se crean las acciones:  
     **Idle_horse**, **Walk_horse**, **Run_horse**.  
   - Puedes asignarlas en el **Dope Sheet** o en el **Animator** del caballo.

5. **Opcional: quitar la Zebra**  
   - Borra los objetos de la zebra (malla, armature, empties) para dejar solo el caballo con sus animaciones.

### 3. Exportar a Unity

- **File → Export → FBX**.  
- Incluye el armature y **Bake Animations** (y **All Actions** si quieres todos los clips).  
- En Unity, configura **Rig → Generic** y en **Animation** marca **Loop** en Idle, Walk y Run si lo necesitas.

---

Si el FBX de la Zebra no trae animaciones (sigue en 0 keyframes en Blender), prueba a descargar en **otro formato** (p. ej. glTF con animaciones) y volver a importar en Blender antes de ejecutar el script.
