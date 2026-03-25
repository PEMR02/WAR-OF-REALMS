# DiagnÃ³stico: puerta de muro no abre / unidades no pasan

## DescripciÃ³n del problema

- **QuÃ© ocurre:** Una puerta integrada en un muro compuesto (arco con portcullis, prefab `Muro_Puerta`) deberÃ­a abrirse cuando una unidad se acerca y permitir el paso. En la prÃ¡ctica la puerta **no abre** (el portcullis sigue cerrado) y las unidades **no pueden cruzar**.
- **Contexto:** La puerta se coloca de dos formas:
  1. **Reemplazar tramo de muro:** edificio seleccionado = **Muro_Puerta_SO**, clic sobre un muro â†’ se destruyen 3 segmentos y se instancia el prefab de la puerta como hijo del **root del muro**.
  2. **Al construir el path del muro:** edificio **Muro_SO**, **Shift+clic** en un punto del path â†’ ahÃ­ se instancia **compoundGatePrefab** (prefab Muro_Puerta) como hijo del compound root.

## Datos y prefabs

| Asset | Uso |
|-------|-----|
| **Muro_Puerta_SO** | id `Muro_Puerta`, prefab de la puerta, costes, buildTime, **gateReplacementRotationOffset** (90,0,0). Solo para **reemplazar** un segmento de muro por puerta desde el menÃº. |
| **Muro_SO** | Muro compuesto: compoundPathMode, compoundSegmentPrefab (tramo), **compoundGatePrefab** = prefab Muro_Puerta. Shift+clic en el path = punto â€œpuertaâ€. |
| **Muro_Puerta.prefab** | Root: **GateController**, **NavMeshObstacle** (carving on/off), GateOpener (deshabilitado por GateController). Hijos: GateTrigger, EntryPoint, ExitPoint. |

## Scripts y componentes que intervienen

### 1. `GateController.cs` (control principal)

- DetecciÃ³n en **plano XZ** (openRadius), Entry/Exit sobre NavMesh, NavMeshObstacle siempre enabled (solo **carving** on/off). Si existe **GateOpener** en el mismo objeto, se deshabilita en Awake.

### 2. `GateOpener.cs` (legacy)

- Si el prefab tiene GateOpener y GateController, **GateController lo deshabilita**. No usar como control principal.

### 3. Prefab `Muro_Puerta`

- Root: GateController, NavMeshObstacle, GateOpener (deshabilitado). Hijos: GateTrigger (BoxCollider IsTrigger), EntryPoint, ExitPoint. MenÃº: **Tools â†’ Project â†’ Configurar prefab Muro_Puerta**.

### 4. `BuildingPlacer.cs` (reemplazo por puerta)

- Si **selectedBuilding.id** es `Muro_Puerta` y el clic impacta un muro compuesto: **TryReplaceWallSegmentWithGate** destruye 3 segmentos, instancia `selectedBuilding.prefab` (Muro_Puerta) como hijo del root del muro, aplica **gateReplacementRotationOffset**, destruye NavMeshObstacle del root del muro y pone el Collider del root en trigger.

### 5. `BuildSite.cs` (muro compuesto)

- En edificios compuestos (muro), el **root** del muro no tiene `NavMeshObstacle` (o se destruye) y el **BoxCollider** del root se configura como **trigger** para no bloquear el hueco de la puerta.
- Los **segmentos** del muro tienen cada uno su propio `NavMeshObstacle` y BoxCollider (tamaÃ±o limitado a ~1 celda y factor 0.9 para no solapar el hueco).

### 6. Animator de la puerta

- Controller: `Wall_B_Gate_Animator` (Assets/_Project/05_Animation/Controllers/).
- ParÃ¡metro booleano **"Open"**: cuando es true deberÃ­a mostrar la animaciÃ³n de puerta abierta (portcullis arriba).

## Flujo esperado (resumido)

1. Unidad (con **UnitMover** y collider) se acerca al gate (dentro de `openRadius` en **plano XZ**).
2. **GateController** comprueba `AnyValidUnitInOpenRadius()` (OverlapSphere + filtro por distancia XZ).
3. Si hay unidad vÃ¡lida: estado â†’ Opening â†’ Open; NavMeshObstacle **carving = false**; Animator "Open" = true.
4. La puerta se ve abierta y el pathfinding puede pasar; el root del muro es trigger.

## Posibles causas por las que â€œno abre la cosaâ€

1. **DetecciÃ³n no dispara**
   - **GateController** usa distancia en **XZ** (altura ignorada). Comprobar que la unidad estÃ© a menos de **openRadius** en horizontal del **gateCenter**.
   - **Unit Layer:** el LayerMask del GateController debe incluir la capa de la unidad (p. ej. Unit = 8). La unidad debe tener **UnitMover** y collider.
 
2. **GateController vs GateOpener**
   - El prefab debe tener **GateController** en el root. Si tambiÃ©n tiene GateOpener, GateController lo deshabilita en Awake.

3. **Animator / Controller**
   - Si el Animator no tiene asignado el controller **Wall_B_Gate_Animator** o el parÃ¡metro no se llama exactamente **"Open"**, la puerta no se anima (pero el pathfinding sÃ­ podrÃ­a desbloquearse si el obstÃ¡culo se desactiva).

4. **NavMeshObstacle no deja de tallar**
   - **GateController** mantiene el NavMeshObstacle **siempre enabled** y solo cambia **Carving** (on al cerrar, off al abrir). Comprobar que la referencia `obstacle` estÃ© asignada en el prefab.

5. **Root del muro sigue bloqueando**
   - Si el muro se creÃ³ antes de los cambios que ponen el root en trigger y quitan su NavMeshObstacle, una instancia antigua podrÃ­a seguir con collider no-trigger u obstÃ¡culo activo. En ese caso, al reemplazar un tramo por puerta, `BuildingPlacer` fuerza el collider del root a trigger y destruye el NavMeshObstacle del root.

## CÃ³mo depurar (en Unity)

1. **Log de GateController**
   - En la instancia de la puerta, en **GateController** activar **Debug Logs**.
Al acercar una unidad deben aparecer mensajes `Opening` y `Open`.
Si nunca aparecen, el fallo estÃ¡ en la **detecciÃ³n** (openRadius en XZ, Unit Layer, collider/UnitMover).

2. **Comprobar posiciÃ³n y radio**
   - En Scene, con la puerta seleccionada, **GateController** dibuja Gizmos (openRadius, repathRadius). La unidad debe quedar dentro del radio **en el plano horizontal** (XZ).

3. **Comprobar unidad**
   - Que el prefab de la unidad tenga **UnitMover** en el root o en un hijo.
Que tenga al menos un **Collider** (no trigger si debe ser â€œsÃ³lidoâ€ para gameplay) y que ese collider estÃ© en un GameObject activo cuando la unidad estÃ¡ cerca de la puerta.

4. **Comprobar NavMeshObstacle**
   - En Play, seleccionar la puerta: el **Nav Mesh Obstacle** debe seguir **Enabled**; al abrir, solo **Carving** debe pasar a **off**.

## Resumen para pedir ayuda externa

- **Proyecto:** Unity (RTS), muro compuesto con puerta (prefab Muro_Puerta, SO Muro_Puerta_SO / Muro_SO).
- **Script principal:** `GateController.cs` (detecciÃ³n en XZ, carving, Entry/Exit). GateOpener queda deshabilitado si existe.
- **DÃ³nde mirar primero:** Con **Debug Logs** en GateController, si no aparecen Opening/Open â†’ detecciÃ³n (openRadius XZ, Unit Layer). Si aparecen pero no cruzan â†’ Entry/Exit, NavMesh, UnitMover (llegada en XZ).
