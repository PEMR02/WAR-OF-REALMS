# Problema: barras de vida en mundo no se mueven con la unidad

## Descripción del síntoma

En un juego RTS en Unity (URP), las **barras de vida en mundo** (world-space health bars) que aparecen encima de las unidades **se quedan fijas en el terreno** cuando la unidad se mueve. Es decir, la barra se dibuja correctamente encima de la unidad en el primer frame, pero al mover la unidad la barra no la acompaña y permanece en la posición del mundo donde estaba inicialmente.

- **Qué se ve:** La barra de vida flotante queda anclada a una posición del mapa; la unidad se aleja y la barra no la sigue.
- **Contexto:** Unidades seleccionadas muestran barra de vida (HealthBarWorld + Canvas en World Space). Algunas unidades tienen la barra ya en su prefab; otras reciben una barra “fallback” instanciada en runtime como hijo del GameObject de la unidad.

---

## Configuración técnica relevante

- **Motor:** Unity (URP).
- **Componente de la barra:** `HealthBarWorld` (MonoBehaviour) en un **Canvas** con `RenderMode.WorldSpace`.
- **Prefab de barra:** RectTransform raíz → hijo Canvas (con Canvas, CanvasScaler, HealthBarWorld, GraphicRaycaster deshabilitado). La barra usa `Image` (Filled) para el fill y opcionalmente marco/fondo.
- **Fuente de datos:** La barra obtiene vida (y opcionalmente colores) de `IWorldBarSource` en la jerarquía (p. ej. componente `Health` en la unidad o en un hijo como “Model”).
- **Parentesco en runtime:**  
  - **Fallback:** `Instantiate(healthBarFallbackPrefab, entity.transform)` → la barra queda como **hijo directo** del GameObject de la unidad.  
  - **Integrada:** En algunos prefabs la barra ya es hijo de la unidad (p. ej. Unidad → HealthBar → Canvas).
- **Actualización de posición:** En `HealthBarWorld.LateUpdate()` se hace:
  - Resolver el transform de la “fuente” (`_sourceTransform`, p. ej. el transform del componente que implementa `IWorldBarSource`).
  - Calcular la posición deseada en mundo (p. ej. encima del personaje con un offset).
  - Si la barra es **hija directa** del transform de la unidad (o de un ascendente que colgamos de la unidad), se convierte esa posición mundo a **espacio local del padre** de la barra y se asigna a `localPosition` del objeto raíz de la barra para que siga por jerarquía.
  - Si no, se asigna `transform.position = desiredWorldPos` en mundo.

Aun con esta lógica, el problema persiste: **las barras siguen sin moverse con la unidad**.

---

## Posibles causas (para depuración o ayuda externa)

1. **Jerarquía y “fuente” distintos**  
   Si `IWorldBarSource`/`Health` está en un **hijo** (p. ej. “Model”) y no en la raíz de la unidad, `_sourceTransform` es el del modelo. La barra puede estar colgando del **root** de la unidad. En ese caso hay que asegurar que la posición en mundo se convierta a espacio local del **padre real de la barra** (el root de la unidad), no del `_sourceTransform`, para que el movimiento del root mueva la barra. *(En el código se ha usado ya `followRoot.parent.InverseTransformPoint(desiredWorldPos)` para esto.)*

2. **Canvas / RectTransform en World Space**  
   Con Canvas en World Space, el RectTransform puede ser actualizado por el sistema de UI en un orden o en un frame distinto y “pisar” la posición que asignamos en `LateUpdate`. Conviene comprobar:
   - Si hay **Canvas Scaler** u otros componentes que recalculen layout.
   - Si algo más (otro script, animación, layout) modifica `position` o `localPosition` después de `LateUpdate`.

3. **Orden de ejecución (Script Execution Order)**  
   Si el movimiento de la unidad se aplica **después** de `LateUpdate` (p. ej. en otro LateUpdate con orden mayor, o en un sistema que se ejecuta después), nuestra barra leería la posición de la unidad del frame anterior y quedaría un frame atrás o estable en una posición antigua. En ese caso habría que ejecutar la actualización de la barra **después** del movimiento (orden de script o llamar desde el mismo sistema que mueve).

4. **Barra no es realmente hija del transform que se mueve**  
   Si en algún flujo la barra se instancia bajo otro padre (p. ej. un canvas global o un contenedor estático), no seguiría a la unidad. Comprobar en la jerarquía en Play mode que el padre del root de la barra es efectivamente el GameObject que se mueve (el de la unidad).

5. **Unidad que “no mueve” su root**  
   Si el movimiento se hace solo en un hijo (p. ej. solo se traslada el “Model” por animación o script) y el GameObject raíz de la unidad no cambia de posición, la barra, al ser hija del raíz, no se movería. En ese caso la barra debería ser hija del transform que realmente se mueve, o la posición deseada debe calcularse desde ese transform.

6. **Escala y anclas del RectTransform**  
   En World Space, anclas y pivot pueden afectar cómo se interpreta `localPosition`. Si hay escalas no uniformes o padres con escala, la posición local puede no coincidir con lo esperado. Revisar que el offset en mundo sea el deseado y que no se esté compensando mal por escala.

---

## Opciones de solución (para probar o proponer)

- **A) Solo jerarquía (sin escribir posición cada frame)**  
  Cuando la barra sea hija de la unidad, **no** escribir `position` ni `localPosition` cada frame; asignar una sola vez un `localPosition` fijo (p. ej. `(0, 2, 0)`) y dejar que la barra siga a la unidad solo por ser hijo. Comprobar si con esto ya se mueve; si no, el fallo está en el parentesco o en qué transform se mueve.

- **B) Actualizar después del movimiento**  
  Asegurar que el script que posiciona la barra se ejecute **después** del que mueve la unidad (Script Execution Order o invocar desde el sistema de movimiento).

- **C) No usar World Space Canvas para “seguir”**  
  Alternativa: mantener la barra en **Screen Space** y cada frame convertir la posición 3D de la unidad a pantalla y asignar la posición 2D del RectTransform. Así la barra siempre sigue en 2D; el coste es un world-to-screen cada frame y posible parpadeo si la cámara se mueve.

- **D) Objeto intermedio “follower”**  
  Un GameObject vacío hijo de la unidad, que cada frame se posiciona en la posición deseada en mundo (o se le asigna un `localPosition` fijo). La barra sería hija de ese objeto y no se le tocaría la posición; así se aísla de posibles sobrescrituras del Canvas.

- **E) Comprobar que no haya “override” de posición**  
  Buscar en el proyecto cualquier otro script o componente que modifique `transform.position` o `localPosition` del mismo GameObject (o de su padre) que la barra.

---

## Resumen para copiar/pegar (ayuda externa)

**Título:** World-space health bar (Canvas) doesn’t follow moving unit in Unity RTS  

**Problema:** Health bar is a child of the unit GameObject (Instantiate with parent = unit.transform), uses a Canvas in World Space and HealthBarWorld script. In LateUpdate we set the bar root’s localPosition from the desired world position (InverseTransformPoint with the bar’s parent). The unit moves correctly but the bar stays fixed in world position and doesn’t follow.  

**Setup:** Unity URP, RectTransform root → Canvas (World Space) → HealthBarWorld. Data from IWorldBarSource (e.g. Health on unit or on a child like “Model”). Bar is parented to unit root.  

**Need:** Bar should move with the unit. Possible causes considered: Canvas/RectTransform overwriting position, script execution order, or bar not actually parented to the moving transform. Looking for proven approaches for world-space UI that follows a moving transform (parenting, execution order, or alternative setup).

---

*Documento generado para describir el problema y facilitar búsqueda de ayuda externa (foros, Unity Answers, etc.).*
