# Controladores de animación (Animator Controllers)

**Ubicación:** `Assets/_Project/05_Animation/Controllers/`

Aquí van los **Animator Controller** (.controller) que usan los prefabs de unidades y que referencian las animaciones de Toony Tiny (u otras carpetas).

## Uso

1. **Crear un controller** para un prefab (ej. `TT_Archer_Animator.controller`).
2. En el **prefab** de la unidad, en el componente **Animator**, asignar el **Controller** a este asset.
3. El parámetro **Speed** (float) debe ser actualizado en runtime según la velocidad del personaje (ya lo hace `UnitAnimatorDriver` si está en el prefab).

## Prefabs y controllers

| Prefab           | Controller                   | Carpeta de animaciones (Toony Tiny) |
|------------------|------------------------------|--------------------------------------|
| TT_Archer        | TT_Archer_Animator            | animation_infantry/Archer            |
| TT_Peasant       | TT_Peasant_Animator           | animation_infantry/Infantry         |
| TT_Mounted_King  | TT_Mounted_King_Animator      | animation_cavalry/cavalry           |
| PF_Swordman_01   | PF_Swordman_01_Animator       | animation_infantry/Infantry         |
| PF_Cow           | PF_Cow_Animator               | Cow Animated.fbx (idle1)            |
| (otros)          | (mismo patrón)                | según rig (infantry/cavalry)         |

## Dónde está cada cosa

- **Controllers:** `Assets/_Project/05_Animation/Controllers/` (ej. `TT_Archer_Animator.controller`).
- **Script que mueve Speed:** `UnitAnimatorDriver.cs` en `Assets/_Project/01_Gameplay/Units/`. Añadido al prefab TT_Archer; para otros prefabs hay que añadirlo manualmente (Add Component → Unit Animator Driver).

## Repetir para otros prefabs

1. Crear un controller en **esta misma carpeta** (`05_Animation/Controllers/`) con estados Idle/Walk/Run y parámetro **Speed** (float), y transiciones según Speed (p. ej. &gt; 0.15 Walk, &gt; 2 Run).
2. En el prefab: **Animator → Controller** = el nuevo controller.
3. Añadir al prefab el componente **Unit Animator Driver** (sincroniza Speed con `NavMeshAgent.velocity`).
