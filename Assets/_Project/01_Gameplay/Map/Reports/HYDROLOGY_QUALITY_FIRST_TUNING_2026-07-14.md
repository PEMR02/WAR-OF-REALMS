# Fine tuning hidrológico quality-first — 2026-07-14

## Objetivo

Dejar el motor procedural operativo por sí mismo. `riverCount`, `lakeCount` y los targets de feeders son referencias de composición, no cuotas que justifiquen una red peor. No se permiten excepciones por seed.

## Contrato de calidad

### Invariantes duros

- Cada río aceptado tiene centerline finita y no degenerada.
- Cada tributario aceptado conserva receptor y confluencia válidos.
- La unión pertenece tanto al tributario como al receptor, sin gap funcional.
- La superficie visual congelada existe para cada río aceptado y no queda marcada `Skipped`.
- Centerline visual, ancho mesh y ancho de máscara tienen cardinalidad coherente.
- En generación completa, cada superficie aceptada informa mesh y carve aplicados con longitud positiva.

Un incumplimiento se clasifica `FAIL_HARD` o `FAIL_MESH_CARVE`.

### Preferencias blandas

- Cantidad deseada de ríos, lagos e Inland/Headwater.
- Separación estética amplia entre desembocaduras LakeSpill.
- Preferir Headwater sobre Inland antes que sobre LakeSpill.
- Preferir ángulos estrictos antes que relajados.

Si no queda un candidato que preserve los invariantes, el resultado correcto es detener la expansión. Una red válida bajo el target se clasifica `PASS_SPARSE`, no como fallo.

## Cambio de política

`MapGenConfig.uwpHydrologyQualityFirst` queda activo por defecto. El builder mantiene:

1. Headwater sobre receptores preferidos con validación estricta.
2. Segundo pase sobre los mismos receptores con ángulo relajado, manteniendo las demás validaciones.
3. Si ambos fallan, detiene la expansión y registra `QualityStopReason`.

Con quality-first no cambia a receptores LakeSpill ni fabrica una ruta de emergencia únicamente para completar el contador. El comportamiento anterior permanece disponible al desactivar explícitamente la opción para comparación.

## Validación

- Batch headless de 20 semillas: topología, confluencias y superficie visual congelada.
- Auditoría de mapa completo: `PMG/Map/RTS/Audit Current Hydro Mesh-Carve`.
- Consola: errores de compilación, excepciones y auditorías de agua.
- Reporte: `lake-first-hydro-batch-20-post-spill-cap.txt`.

### Resultado medido

- Batch final: `20/20` evaluadas, `0 FAIL_HARD`, `7 PASS_QUALITY`, `13 PASS_SPARSE` y `6 qualityStops` explícitos.
- Superficie headless: todos los ríos aceptados terminaron con cache válida; `skipped=0` en las 20 semillas.
- Uniones: `15` semillas con dos LakeSpill, `0` pares cercanos bajo la preferencia de 12 celdas.
- Mapa completo en Play Mode, seed runtime `424242`: `PASS_QUALITY`, `6/6` ríos con superficie, mesh y carve aplicados y longitud positiva.
- Compilación: sin errores C# ni excepciones de hidrología.

### Correcciones estructurales incluidas

- LakeSpill inicializa explícitamente rol, receptor, lista de receptores y ratio de ancho; se elimina la reconstrucción heurística posterior de esos datos.
- El trim de tributarios busca la mejor confluencia dentro del tramo final válido. Esto evita que un meandro visual del receptor cerca del origen colapse una ruta completa a un solo punto.
- La métrica batch de unión usa la confluencia del grafo Lake First. Medir el último punto de la lista era incorrecto porque la orientación LakeSpill no garantiza que ese extremo sea la unión.

### Hallazgos fuera del alcance hidrológico

Play Mode sigue informando un `Behaviour` con script faltante y `104 node options failed to load` de Visual Scripting. Son problemas preexistentes y no intervinieron en las auditorías de agua, pero deben entrar en una limpieza separada.

También se observó que ejecuciones aisladas de una misma seed no siempre reprodujeron exactamente la misma composición durante la sesión de Editor. Antes de usar regresiones por snapshot conviene auditar estado aleatorio compartido y orden de inicialización; no se agregó ninguna excepción por seed.

## Checklist

- [x] Crear baseline versionado antes de modificar comportamiento (`05d189a8`).
- [x] Publicar baseline en `origin/codex/hydrology-quality-engine`.
- [x] Separar invariantes duros de preferencias blandas.
- [x] Implementar parada quality-first sin reglas por seed.
- [x] Agregar telemetría `PASS_QUALITY` / `PASS_SPARSE` / `FAIL_HARD`.
- [x] Agregar auditoría independiente de mesh/carve para generación completa.
- [x] Completar batch 20 posterior al cambio.
- [x] Revisar individualmente cualquier fallo duro; el batch final quedó en cero.
- [x] Ejecutar auditoría mesh/carve sobre el mapa actual completo.
- [x] Revisar consola final sin errores ni excepciones hidrológicas relevantes.
- [x] Actualizar conclusiones con resultados medidos.

## Criterio de cierre

El cambio se acepta cuando el batch no contiene fallos duros y el mapa completo no contiene ríos aceptados sin mesh/carve. `PASS_SPARSE` es aceptable y esperado en semillas donde la geometría disponible no admite otro tributario limpio.
