# Checklist exacta de verificación en Unity

Haz estas pruebas en una escena de test simple y también en tu escena principal.

---

## A. Placement sobre terreno irregular

### Prueba A1 — terreno plano

Coloca: **House**, **Barracks**, **Town Center**.

Revisa:
- [ ] Ghost bien apoyado
- [ ] Edificio final no flota
- [ ] No se entierra
- [ ] Base visual coincide con el terreno
- [ ] Footprint coincide con la ocupación real

### Prueba A2 — pendiente suave

Prueba construir sobre una ladera leve.

Revisa:
- [ ] El sistema permite construir
- [ ] Altura final coherente
- [ ] Edificio estable visualmente
- [ ] Ghost cambia correctamente entre válido/inválido

### Prueba A3 — pendiente fuerte

Busca una pendiente clara y trata de construir.

Resultado esperado:
- [ ] Rechaza la construcción
- [ ] Ghost muestra claramente que no se puede
- [ ] No permite colocar edificios absurdos

### Prueba A4 — edificios de distinto tamaño

Prueba al menos: **2x2**, **3x3**, **4x4**.

Revisa:
- [ ] avgHeight funciona bien en todos
- [ ] Los grandes se ven bien apoyados
- [ ] Ninguno requiere otra estrategia de anclaje evidente

---

## B. Footprint y grid

### Prueba B1 — footprint visible

Activa gizmos o debug si tienes.

Revisa:
- [ ] Footprint lógico
- [ ] Footprint visual
- [ ] Collider
- [ ] Tamaño del modelo

Todo debería calzar razonablemente.

### Prueba B2 — ocupación del grid

Coloca un edificio y trata de construir otro encima o parcialmente cruzado.

Resultado esperado:
- [ ] Bloqueo correcto
- [ ] Sin superposición rara
- [ ] Sin espacios invisibles absurdos

---

## C. Prefabs de edificios

Revisa uno por uno: **PF_TownCenter**, **PF_House**, **PF_Barracks**.

### Verifica en Inspector

- [ ] `buildingSO` asignado
- [ ] `Health.startWithPercentForTesting = false`
- [ ] `startPercent` irrelevante o limpio
- [ ] `barAnchor` correcto
- [ ] Collider principal coherente
- [ ] `NavMeshObstacle` correcto
- [ ] Renderers razonables
- [ ] Pivot bien ubicado

### Verifica en escena

- [ ] Selección funciona
- [ ] Barra de vida aparece en lugar correcto
- [ ] Clic selecciona bien el edificio
- [ ] No se ve desplazado respecto al suelo

---

## D. Prefab Aldeano (villager)

En el proyecto el villager es **PF_Peasant** (no existe Aldeano.prefab).

### Verifica en Inspector

- [ ] Escala correcta
- [ ] Collider coherente
- [ ] Animator asignado si corresponde
- [ ] UnitSelectable bien configurado
- [ ] barAnchor correcto
- [ ] Materiales correctos
- [ ] Pivot en pies

### Verifica en escena

- [ ] Se selecciona bien
- [ ] Se mueve bien
- [ ] No se hunde ni flota
- [ ] Barra de vida aparece donde corresponde
- [ ] Animación y rotación normales

---

## E. Selección RTS

### Prueba E1 — clic simple

Selecciona una unidad sola.

Revisa:
- [ ] Respuesta instantánea
- [ ] Highlight correcto
- [ ] Sin errores en consola

### Prueba E2 — box select

Selecciona varias unidades con arrastre.

Revisa:
- [ ] Unidades correctas entran
- [ ] Sin quedarse pegado
- [ ] Sin tirón raro de rendimiento

### Prueba E3 — doble clic

Doble clic sobre una unidad del mismo tipo.

Revisa:
- [ ] Selecciona las similares visibles o esperadas
- [ ] Respuesta rápida
- [ ] Sin comportamiento extraño

---

## F. Órdenes RTS

### Prueba F1 — mover

Selecciona varias unidades y manda mover.

Revisa:
- [ ] Respuesta limpia
- [ ] Sin stutter
- [ ] Sin retraso evidente

### Prueba F2 — gather

Clic sobre recurso.

Revisa:
- [ ] Resolver detecta bien el target
- [ ] Sin conflicto raro entre move y gather

### Prueba F3 — build site

Clic sobre sitio de construcción si aplica.

Revisa:
- [ ] Prioridad de target correcta
- [ ] No manda move cuando debería mandar construir

---

## G. Cámara RTS

### Prueba G1 — mover cámara

Mueve en todas direcciones.

Revisa:
- [ ] No se siente tosca
- [ ] Movimiento fluido
- [ ] Sin frenazos feos

### Prueba G2 — rotación

Gira cámara continuamente.

Revisa:
- [ ] Suavidad
- [ ] Consistencia
- [ ] Sin saltos

### Prueba G3 — zoom

Zoom in y out repetidamente.

Revisa:
- [ ] Suavidad
- [ ] Límites correctos
- [ ] Sin vibraciones o snaps bruscos

### Prueba G4 — bordes del mapa

Lleva cámara al límite.

Revisa:
- [ ] Clamp no se siente seco
- [ ] Edge smoothing hace bien su trabajo

---

## H. Rendimiento básico

Con **Profiler** abierto revisa: mover cámara, box select, mover grupo, construir, barras de vida visibles.

Mira especialmente:
- [ ] CPU Usage
- [ ] Scripts
- [ ] GC Alloc
- [ ] Timeline

Si algo pega tirones, anota:
- Qué acción
- Qué script aparece arriba
- Si hubo alloc o no
