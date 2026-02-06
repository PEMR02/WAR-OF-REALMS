# Cómo reconfigurar PF_House (o cualquier edificio) después de cambiar el modelo

Si reemplazaste el modelo de la casa y "se desconfiguró todo", sigue estos pasos.

## Qué debe tener el prefab para que funcione

1. **Layer: Building**  
   El sistema de colocación y selección usa la capa "Building". Si el prefab está en Default u otra capa, puede no bloquear bien o no seleccionarse.

2. **BuildingInstance (opcional pero recomendado)**  
   Componente que guarda la referencia al BuildingSO (House_SO). Sirve para identificar el edificio (HUD, población, etc.).  
   - Añade el componente si no está.  
   - En el campo **Building SO** asigna **House_SO** (el ScriptableObject de la casa).

3. **Collider**  
   Para que el edificio ocupe espacio y no se pueda construir encima, debe tener al menos un Collider (BoxCollider o MeshCollider). Si el modelo no trae, añade un Box Collider que envuelva la base.

4. **Transform**  
   - **Position**: (0, 0, 0) en el prefab está bien; la posición la pone el BuildingPlacer.  
   - **Scale**: (1, 1, 1). Si el modelo nuevo es muy grande o pequeño, ajusta la escala aquí.  
   - **Rotation**: (0, 0, 0) normalmente.

5. **House_SO debe apuntar al prefab**  
   En **House_SO** (Assets → _Project → 04_Data → ScriptableObjects → Buildings → House_SO), el campo **Prefab** debe tener asignado tu **PF_House** (el que acabas de reconfigurar). Si al cambiar el modelo se rompió la referencia, arrastra de nuevo el prefab PF_House a ese campo.

## Pasos en Unity (manual)

1. Abre el prefab **PF_House** (doble clic o "Open Prefab").
2. Selecciona el **objeto raíz** del prefab.
3. En Inspector:
   - **Layer** → **Building** (si no existe, créalo en Tags & Layers).
   - **Tag** → Untagged está bien.
4. Añade **BuildingInstance** (Add Component → buscar "Building Instance"): asigna **House_SO** en **Building SO**.
5. Si no tiene **Collider**, Add Component → **Box Collider** (o Mesh Collider) y ajusta el tamaño a la base del edificio.
6. Revisa **Transform**: Scale (1,1,1); si el modelo se ve gigante o diminuto, cambia la escala.
7. Guarda el prefab (Ctrl+S o Apply en la barra del prefab).
8. Abre **House_SO** y comprueba que el campo **Prefab** apunte a **PF_House**.

## Atajo: script de editor

En el menú **Tools → Project → Reconfigurar prefab de edificio** puedes elegir un prefab y un BuildingSO; el script asignará Layer Building, añadirá BuildingInstance con ese SO y opcionalmente un BoxCollider si no hay collider. Ver el script en `Editor/FixBuildingPrefabEditor.cs`.
