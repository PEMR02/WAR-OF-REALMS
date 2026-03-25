# Estrategia de verificación: por qué las unidades dan la vuelta / no pasan por la puerta

Comprueba **cada pieza en este orden**. La primera que falle suele ser la causa.

---

## 1. ¿El grid (A*) está enviando la ruta por fuera?

**Problema:** El MapGrid marca las celdas del muro (y de la puerta) como **ocupadas**. A* nunca puede pasar por ahí, así que calcula una ruta **rodeando** el muro. La unidad sigue esa ruta y “da la vuelta”.

**Qué se hizo en código:** En `UnitMover.MoveTo` se comprueba si la **línea unidad → destino final** cruza una puerta (`FindGateOnSegment`). Si cruza, **no se usa A*** para el primer tramo y se fuerza ir al Entry/Exit de la puerta. Así la unidad va directo a la puerta aunque el grid esté ocupado.

**Comprobar:**
- Con **Debug Logs** activado en el `UnitMover` de una unidad, ordena mover al otro lado del muro. Debe salir un log tipo: `"Destino al otro lado del muro → forzando paso por puerta '...'"`. Si **nunca** sale, esa unidad no está detectando la puerta en el segmento (radio 6 m o puerta no registrada).
- En la **Scene**, con la puerta seleccionada revisa los Gizmos: el **repathRadius** (verde) debe ser ≥ 6 para que `FindGateOnSegment` la encuentre cuando la unidad está a distancia.

---

## 2. ¿La puerta se abre cuando se acerca una unidad?

**Comprobar:**
- En el **GateController** de la puerta activa **Debug Logs**. Acerca una unidad (o haz que path hacia la puerta). En Consola deben aparecer mensajes `Opening` y `Open`.
- Si **nunca** abre: o no hay unidades en **openRadius**, o el **Unit Layer** no incluye la capa de la unidad (Layer 8 = Unit), o el **OverlapSphere** no las detecta (collider desactivado, unidad en otro layer).

**Gizmos:** Con la puerta seleccionada en Scene, el círculo **openRadius** (azul/cyan) debe cubrir la zona por donde la unidad se acerca. Si la unidad se queda fuera de ese círculo, la puerta no abrirá por proximidad.

**Altura:** La detección de “unidad cerca” usa **distancia en plano XZ** (se ignora la altura). Así, si el pivot de la puerta está alto (arco), las unidades en el suelo siguen contando como “cerca” mientras estén dentro de **openRadius** en horizontal. Los destinos Entry/Exit se proyectan sobre la NavMesh para que el agente no reciba un punto en el aire.

---

## 3. ¿El NavMeshObstacle deja de tallar al abrir?

**Problema:** Si el obstáculo sigue con `carving = true` cuando la puerta está “abierta”, la NavMesh sigue con un hueco y el agente no puede pasar.

**Nota:** Si el prefab tiene **GateOpener** y **GateController**, GateController en Awake deshabilita GateOpener y deja el NavMeshObstacle siempre **enabled**, tocando solo **carving** (on/off). Así se evita que GateOpener desactive el componente entero y deje la puerta en estado inconsistente.

**Comprobar:**
- En **Play**, selecciona la instancia de la puerta. En el **Nav Mesh Obstacle**:
  - El componente debe estar **Enabled**.
  - Con la puerta **cerrada**: **Carving** debe estar **on**.
  - Cuando una unidad está cerca y la puerta abre: **Carving** debe pasar a **off**.
- Si el obstáculo aparece deshabilitado, comprueba que GateController esté en la puerta (deshabilita GateOpener al inicio).

---

## 4. ¿EntryPoint y ExitPoint están bien colocados?

**Problema:** Si Entry/Exit están al revés o muy lejos del hueco, el agente puede recibir un destino del “lado equivocado” o en una zona no transitable.

**Comprobar:**
- En **Scene** (con la puerta seleccionada), los Gizmos muestran **EntryPoint** (cian) y **ExitPoint** (cian). Deben estar **cada uno a un lado del muro**, en suelo transitable.
- **gateCenter.forward** define el lado: `dot > 0` → destino = ExitPoint, `dot < 0` → EntryPoint. Comprueba que la orientación del prefab (forward del root) coincida con cómo colocas el muro.
- Comprueba que **EntryPoint** y **ExitPoint** estén **sobre NavMesh** (en Play, o con **Window > AI > Navigation** y visualizar la malla). Si están dentro de un hueco tallado o en obstáculo, el agente no podrá path hasta ellos hasta que la puerta abra. En código, **ForcePassThrough** usa `NavMesh.SamplePosition` para enviar al agente a un punto sobre la malla (evita destinos en el aire si el prefab tiene Entry/Exit altos).

---

## 5. ¿Hay trigger en la puerta y está en un objeto activo?

**Comprobar:**
- En el prefab de la puerta debe haber un **Collider con Is Trigger = true** (p. ej. en un hijo “GateTrigger”). Si no hay ningún trigger, `GateController` muestra un warning en Consola y solo cuenta la detección por **proximidad** (OverlapSphere). Para contar bien “quién está dentro” y cerrar bien, conviene tener el trigger.
- El GameObject que tiene el trigger debe estar **activo**. Si está desactivado, OnTriggerEnter/Exit no se disparan.

---

## 6. ¿La unidad tiene los componentes y capa correctos?

**Comprobar:**
- **NavMeshAgent** y **UnitMover** en el **root** de la unidad.
- **CapsuleCollider** (u otro collider) en el root, **no** en trigger, para que el OverlapSphere de la puerta y el trigger la detecten.
- Unidad en capa **Unit (8)** (o la que tenga asignada el **Unit Layer** del GateController). Si el GateController usa **Everything** (-1), no importa la capa.

---

## 7. Orden recomendado de comprobación (resumen)

1. **UnitMover:** ¿Sale el log “forzando paso por puerta” al ordenar ir al otro lado? Si no → puerta no en segmento o radio pequeño.
2. **GateController:** ¿Sale “Opening” / “Open” al acercar unidad? Si no → detección (radio, capas, collider).
3. **Nav Mesh Obstacle:** ¿Carving se desactiva al abrir? Si no → lógica de apertura o referencia `obstacle`.
4. **Entry/Exit:** ¿Están a cada lado y sobre NavMesh? Si no → reubicar en prefab.
5. **Trigger:** ¿Existe y está activo? ¿Unidad en capa correcta?

**Componente opcional:** Añade **GateDiagnostics** al mismo GameObject que el GateController (o como hijo). En Play, con **Debug Enabled** activado, cada 2 s se imprime en Consola: estado de la puerta, unidades detectadas en radio, si el obstáculo está tallando (Carving) y si Entry/Exit están en NavMesh. En Scene, con la puerta seleccionada, los Gizmos muestran Entry/Exit en **verde** si están en NavMesh y en **rojo** si no.
