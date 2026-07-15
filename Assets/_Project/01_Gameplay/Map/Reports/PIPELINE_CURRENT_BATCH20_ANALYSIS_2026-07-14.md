# Análisis objetivo del pipeline actual — Batch 20 Lake First

**Fecha:** 2026-07-14  
**Modo:** revisión y ejecución de auditoría; sin cambios de código, configuración, escena ni assets de gameplay.  
**Pipeline:** `LakeFirstHydrology` / UWP frozen surface.  
**Batch ejecutado:** `PMG/Map/RTS/Analyze 20 Hydro LakeFirst (silent)`.  
**Reporte fuente:** `lake-first-hydro-batch-20-post-spill-cap.txt` (generado 2026-07-14 22:00).

---

## 1. Correcciones de criterio respecto de la revisión anterior

### TerrainData creado en runtime

Crear `TerrainData` en runtime es compatible con la naturaleza procedural del juego y **no es un defecto por sí mismo**. El warning de `TerrainExporter` debe interpretarse como información de lifecycle, no como evidencia de inestabilidad.

Lo que importa para el tuning es que, para una misma configuración y seed:

- la resolución y dimensiones sean deterministas;
- no se reutilice estado de una generación anterior;
- las capas y escalas se apliquen siempre en el mismo orden;
- el objeto temporal se destruya o reemplace correctamente;
- el batch y Play usen el mismo contrato de creación.

Durante este batch no hubo errores de generación atribuibles al `TerrainData`. Por tanto, **no se recomienda convertirlo en asset serializado sólo para afinar seeds**. Sí conviene reemplazar el warning por un log de auditoría que incluya resolución, tamaño, instancia nueva/reutilizada y seed.

### Cadena de ajustes visuales

La secuencia `growth → align → taper → sync → widen → cap → sync` fue necesaria para estabilizar un pipeline existente y permitió llegar a un resultado funcional. Su existencia no invalida el sistema.

La crítica objetiva es más acotada: actualmente el resultado depende del orden de varios pases que pueden pisarse entre sí. Esto aumenta el coste de modificar knobs, pero no justifica un rewrite inmediato mientras los invariantes se midan y el batch permanezca estable.

Decisión recomendada: **conservar el pipeline actual como baseline**, añadir métricas que detecten regresiones y refactorizar sólo cuando una etapa de tuning requiera tocar esa zona.

---

## 2. Resultado del batch 20 actual

| Métrica | Resultado |
|---|---:|
| Seeds evaluadas | 20/20 |
| Fallos de generación | 0 |
| Headwater presente | 20/20 |
| Seeds con 2 LakeSpill | 14/20 |
| Spills a menos de 12 celdas | 0/20 |
| Seeds con 6 ríos | 11/20 |
| Seeds con 5 ríos | 6/20 |
| Seeds con 4 ríos | 3/20 |
| Lagos promedio | 3.10 |
| LakeSpill promedio | 1.70 |
| Inland promedio | 1.70 |
| Seeds sin Inland | 2/20 |
| Headwater con receptor Inland | 17/20 |
| Headwater con receptor LakeSpill | 3/20 |
| Headwater con ángulo relajado | 6/20 |
| Headwater en modo emergencia | 2/20 |

Seeds que terminan con cuatro ríos:

- `6326234`: 2 spill, 0 inland, 1 headwater;
- `713765`: 2 spill, 0 inland, 1 headwater;
- `8034069`: 1 spill, 1 inland, 1 headwater.

Las dos primeras son la misma clase de fallo sistémico: el pipeline llena spills, no logra aceptar Inland y coloca el Headwater sobre un LakeSpill. No son dos excepciones a parchear por seed.

---

## 3. Comparación con el batch anterior

| Métrica | Batch anterior | Batch actual |
|---|---:|---:|
| LakeSpill promedio | 2.05 | 1.70 |
| Inland promedio | 1.55 | 1.70 |
| Headwater presente | 19/20 | 20/20 |
| Seeds con 6 ríos | 12/20 | 11/20 |
| Close spill pairs | 2/20 | 0/20 |

Conclusión:

- El cap/separación de spill resolvió completamente el problema que medía: `closeSpillPairs` bajó de 2 a 0.
- Mejoró la presencia de Headwater y subió ligeramente Inland promedio.
- No mejoró el llenado total de cupos: seis ríos bajó de 12/20 a 11/20.
- La mejora topológica es real, pero el siguiente cuello de botella ya no es la separación de spills; es la aceptación de Inland.

No conviene relajar nuevamente el spill cap para recuperar conteo. Eso intercambiaría una topología defectuosa conocida por un aumento pequeño de ocupación.

---

## 4. Rechazos y fallbacks supplemental

Resumen de consola para las 20 seeds:

| Métrica | Inland | Headwater |
|---|---:|---:|
| Rechazos promedio | 26.2 | 20.3 |
| Mediana de rechazos | 7 | 7 |
| Máximo | 72 | 72 |

La diferencia entre mediana 7 y promedio 26.2 muestra una distribución con cola larga: la mayoría de las seeds se resuelve razonablemente, pero varias agotan casi todo el presupuesto.

Casos relevantes:

- `6326234`: `inlandAccepted=0`, `inlandRejected=72`.
- `713765`: `inlandAccepted=0`, `inlandRejected=71`.
- `6184095`: `inlandAccepted=1`, `inlandRejected=70`.
- `8034069`: `inlandAccepted=1`, `inlandRejected=71`.
- `696405` y `5031255`: Headwater llega a `headwaterRejected=72` y usa emergencia.

Esto indica que el motor funciona bien en la zona central de la distribución, pero tiene una clase de geometrías donde el muestreo repite candidatos que no pueden satisfacer el contrato.

### Receptor de Headwater

El contrato de producto prefiere `Headwater → Inland mid-body`. El batch obtiene:

- 17/20 sobre Inland;
- 3/20 sobre LakeSpill (`6326234`, `713765`, `5031255`).

El fallback a LakeSpill es legítimo si el producto prefiere una red incompleta antes que omitir Headwater. Debe, sin embargo, medirse como **resultado degradado**, no mezclarse con el éxito nominal.

En `5031255` el log incluye `mode=emergency_inland`, pero el `receiver=1` corresponde a un LakeSpill según los tipos del reporte. Esto puede ser sólo una etiqueta imprecisa, pero conviene corregir la telemetría antes de usarla como función objetivo.

---

## 5. Evaluación de consola

### Relacionado con el batch

- No hubo `FAIL_GEN` ni excepciones del generador.
- El batch cerró correctamente con `evaluated=20/20`.
- No aparecieron `NullReferenceException`, errores de carve o fallos de construcción causados por las 20 generaciones.
- La restauración final de `MapGenerator.config` se ejecutó según el evaluador.

### Ruido no atribuible al batch hídrico

Persisten dos problemas previos del Editor:

- un Behaviour con script faltante;
- caché de Visual Scripting con 104 node options obsoletas.

No hay evidencia de que hayan afectado los resultados del batch. Deben mantenerse fuera de la función objetivo hidrológica, aunque conviene limpiarlos en una tarea separada para que futuros errores sean visibles.

### Severidad de logs

Los warnings de auditoría fueron útiles para llegar al estado actual. No se recomienda eliminarlos durante esta etapa. Cuando el tuning se estabilice, conviene conservar la misma información en un reporte estructurado o bajo un flag de auditoría; el objetivo es reducir ruido sin perder trazabilidad.

---

## 6. Limitaciones del evaluador actual

El batch hidro actual sólo mide conteos y distancia entre extremos de LakeSpill. No mide todavía:

- receptor real de Headwater;
- porcentaje de receptores Inland vs Spill;
- uso de relaxed/emergency;
- motivos de rechazo agregados;
- distancia y posición normalizada del join Headwater sobre el receptor;
- ángulo real del join;
- longitud, span y windiness por tipo;
- continuidad mesh/carve;
- cobertura del ribbon;
- foam, islas o calidad visual;
- tiempo por seed y candidatos evaluados.

Además, la línea final dice siempre `Interpret: closeSpillPairs = 2...`, aunque el valor real sea cero. Es texto fijo obsoleto y no debe usarse como dato.

El batch sirve para vigilar el spill cap, pero todavía es insuficiente para “afinar todo”.

---

## 7. Prioridad objetiva de tuning

### P0 — Mejorar observabilidad antes de mover varios knobs

Extender el reporte por seed con:

```text
seed
lakes/spill/inland/headwater/rivers
inlandAccepted/inlandRejected
headwaterAccepted/headwaterRejected
headwaterReceiverKind
headwaterRelaxed/headwaterEmergency
rejectReason histogram
pathLength/span/windiness por tipo
joinAlong/joinAngle/joinTipDistance
generationMs
```

Sin esto, relajar una restricción puede aumentar conteo ocultando caminos cortos, tortuosos o joins malos.

### P1 — Inland acceptance

Objetivo inicial razonable:

- `inland >= 1` en 20/20;
- `inland = 2` en al menos 18/20;
- mantener `closeSpillPairs = 0`;
- no reducir Headwater 20/20;
- no degradar los límites de longitud/span/windiness.

Investigar primero histogramas de motivos para `6326234`, `713765`, `6184095` y `8034069`. Ajustar el motivo dominante global, no las seeds.

### P2 — Headwater nominal vs degradado

Objetivo:

- receptor Inland en al menos 19/20;
- relaxed por debajo de 20%;
- emergency por debajo de 5%;
- mantener Headwater presente 20/20.

No prohibir inmediatamente el fallback a Spill: primero mejorar la disponibilidad de Inland. Si no existe Inland válido, el fallback actual es una decisión de resiliencia razonable.

### P3 — Calidad geométrica y visual

Una vez verdes P1/P2:

- ejecutar el evaluador geométrico actualizado;
- verificar cobertura mesh/carve y continuidad;
- revisar visualmente una muestra estratificada: nominal, relaxed, emergency y mínimo conteo;
- ajustar anchos/profundidad sólo después de fijar topología.

---

## 8. Conclusión

El pipeline actual **es funcional y estable en ejecución**: 20/20 seeds generan, Headwater aparece siempre y el problema de spills cercanos está resuelto. Los pases acumulados no deben reemplazarse sólo por razones estéticas del código.

El principal problema para el fine tuning ya no es estabilidad técnica ni `TerrainData` runtime. Es la cola larga de rechazo supplemental:

- 2/20 seeds no consiguen ningún Inland;
- 3/20 Headwater terminan en Spill;
- 6/20 necesitan restricciones relajadas;
- 2/20 agotan el presupuesto y usan emergencia;
- sólo 11/20 llenan los seis ríos.

La siguiente mejora debe concentrarse en **diagnosticar y elevar la aceptación Inland manteniendo las restricciones de producto**, mientras se amplía el batch para medir receptores, fallbacks y geometría. Afinar anchos o añadir excepciones antes de resolver esa observabilidad produciría conclusiones ambiguas.

Este análisis no recomienda un rewrite ni parches por seed. Recomienda mantener el baseline actual, instrumentar el contrato y atacar la clase de fallo dominante con evidencia agregada.
