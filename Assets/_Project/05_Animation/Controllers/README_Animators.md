# Controladores de animación (Animator Controllers)

**Ubicación:** `Assets/_Project/05_Animation/Controllers/`

Aquí van los **Animator Controller** (.controller) que usan los prefabs de unidades y que referencian las animaciones de Toony Tiny (u otras carpetas).

## Uso

1. **Crear un controller** para un prefab (ej. `TT_Archer_Animator.controller`).
2. En el **prefab** de la unidad, en el componente **Animator**, asignar el **Controller** a este asset.
3. El parámetro **Speed** (float) debe ser actualizado en runtime según la velocidad del personaje (ya lo hace `UnitAnimatorDriver` si está en el prefab).

## Prefabs y controllers

| Prefab / Unidad  | Controller                   | Animaciones TT (idle/walk/run)      |
|------------------|------------------------------|--------------------------------------|
| PF_Aldeano       | PF_Peasant_Animator          | animation_infantry/Infantry         |
| PF_Scout         | PF_Swordman_01_Animator      | animation_infantry/Infantry         |
| PF_Swordman (Milicia) | PF_Swordman_01_Animator | animation_infantry/Infantry         |
| PF_Lancero       | **PF_Lancero_Animator**      | animation_infantry/Spear            |
| PF_Archer        | PF_Archer_Animator           | animation_infantry/Archer           |
| PF_Mounted_King (Caballero) | PF_Mounted_King_Animator | animation_cavalry/cavalry           |
| PF_Cow           | PF_Cow_Animator              | Cow Animated.fbx (idle1)            |
| —                | **TT_Shield_Animator**      | animation_infantry/Shield           |
| —                | **TT_TwoHanded_Animator**   | animation_infantry/TwoHanded        |
| —                | **TT_Crossbow_Animator**    | animation_infantry/Crossbow        |
| —                | **TT_Staff_Animator**       | animation_infantry/Staff           |
| —                | **TT_Polearm_Animator**     | animation_infantry/Polearm         |
| —                | **TT_Settler_Animator**     | animation_machines/Cart (aldeano/carro) |

### Caballería (presets TT_RTS – animation_cavalry)

Cualquier unidad a caballo puede usar uno de estos controllers según el arma/estilo. Todos usan parámetro **Speed** y estados **Idle / Walk / Run**.

| Controller                       | Preset TT_RTS        | Uso típico              |
|----------------------------------|----------------------|-------------------------|
| PF_Mounted_King_Animator         | cavalry              | Caballero genérico      |
| **TT_Cavalry_Archer_Animator**   | cavalry_archer       | Jinete arquero          |
| **TT_Cavalry_Crossbow_Animator** | cavalry_crossbow     | Jinete ballestero       |
| **TT_Cavalry_Shield_Animator**   | cavalry_shield       | Jinete con escudo       |
| **TT_Cavalry_Spear_A_Animator**  | cavalry_spear_A      | Jinete lanza (estilo A) |
| **TT_Cavalry_Spear_B_Animator**  | cavalry_spear_B      | Jinete lanza (estilo B) |
| **TT_Cavalry_Staff_Animator**    | cavalry_staff        | Jinete con bastón       |

## Dónde está cada cosa

- **Controllers:** `Assets/_Project/05_Animation/Controllers/` (ej. `TT_Archer_Animator.controller`).
- **Script que mueve Speed:** `UnitAnimatorDriver.cs` en `Assets/_Project/01_Gameplay/Units/`. Añadido al prefab TT_Archer; para otros prefabs hay que añadirlo manualmente (Add Component → Unit Animator Driver).

## Asignar controller a un prefab

1. Abre el prefab de la unidad (ej. `PF_Lancero`).
2. En el componente **Animator**, en **Controller** asigna el asset correspondiente (ej. `PF_Lancero_Animator`).
3. Asegúrate de que el prefab tenga **Unit Animator Driver** (sincroniza el parámetro Speed con `NavMeshAgent.velocity`).

Todos los controllers de esta carpeta tienen parámetro **Speed** (float) y estados **Idle / Walk / Run** con transiciones (Speed &gt; 0.15 → Walk, Speed &gt; 2 → Run).
