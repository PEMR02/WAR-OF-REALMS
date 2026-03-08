# Fase 8 — Revisión cámara RTS (resumen y tuning)

## Qué quedó bien

- **Separación input / aplicación:** el input se lee en `Update` (WASD, Q/E, rueda, edge scroll, drag) y se escriben en `_targetRigPosition`, `_targetYaw`, `_targetDistance`. La aplicación con suavizado se hace en `LateUpdate` (una sola vez por frame, después de la física y lógica).
- **Suavizado:** uso correcto de `Vector3.SmoothDamp` para el rig, `Mathf.SmoothDampAngle` para la rotación y `Mathf.SmoothDamp` para la distancia de zoom. Con `moveSmoothTime`, `rotationSmoothTime` y `zoomSmoothTime` > 0 la cámara no va a tirones.
- **Zoom por distancia:** estilo AoE2 (FOV fijo, cámara se acerca/aleja). Límites `minDistance` / `maxDistance` y protección para que la cámara no quede bajo el suelo (`toCam.y < 0.15f`).
- **Bounds:** límites XZ desde `terrainForBounds` o `mapGridForBounds` + `boundsMargin`; el target del rig se clampa en XZ en cada Update.
- **Pitch fijo:** `pitchAngle` (55° por defecto) aplicado en el pivot o en el rig; no hay zoom por FOV.
- **Resolución de referencias con throttle:** `cam` y `selection` se resuelven con timer (no cada frame) cuando son null.
- **Focus al inicio:** opción para centrar en el Town Center del jugador 1 tras un delay.

---

## Qué conviene afinar (opcional)

1. **`smoothEdgeMovement` — ya implementado**  
   Cuando está activo y `smoothEdgeSpeed > 0`, en lugar de un clamp duro se usa `Mathf.MoveTowards` para acercar `_targetRigPosition` al límite (clamped) con un paso máximo de `smoothEdgeSpeed * dt` por frame. Así la cámara no se corta en seco al llegar al borde sino que frena de forma suave.

2. **Sensación “tosca” posible**  
   - **Clamp duro en el borde:** al llegar al límite del mapa el rig se detiene de golpe. Si molesta, implementar el punto anterior (smoothEdgeMovement).  
   - **Aceleración:** SmoothDamp ya da aceleración/desaceleración; si se siente lento al empezar a mover, bajar un poco `moveSmoothTime` (ej. 0.05–0.08).  
   - **Delta time:** se usa `Time.unscaledDeltaTime` en Update y LateUpdate, coherente para que el suavizado no dependa de timeScale.

3. **Rotación Q/E**  
   La rotación se acumula en `_yaw` en Update y en LateUpdate se suaviza hacia `_targetYaw` (= `_yaw`). Si la rotación se siente con retraso, bajar `rotationSmoothTime` (ej. 0.03–0.05).

---

## Valores recomendados de tuning

| Parámetro | Valor actual (típico) | Recomendación | Nota |
|-----------|------------------------|---------------|------|
| `moveSmoothTime` | 0.08 | 0.05–0.1 | Menor = más reactivo, mayor = más “flotante”. |
| `zoomSmoothTime` | 0.06 | 0.05–0.12 | Igual criterio. |
| `rotationSmoothTime` | 0.05 | 0.04–0.08 | Si la rotación se siente lenta, bajar a 0.03–0.04. |
| `moveSpeed` | 25 | 20–30 | Ajustar al tamaño del mapa. |
| `edgeSpeed` | 20 | 15–25 | Idem. |
| `dragSpeed` | 0.8 | 0.6–1.2 | Si el drag se siente brusco, bajar. |
| `pitchAngle` | 55 | 50–60 | Más bajo = vista más alta; más alto = más isométrico. |
| `fixedFov` | 45 | 40–50 | Estilo AoE2 suele estar en 40–45. |

---

## Checklist manual en Unity

- [ ] WASD: movimiento suave, sin sacudidas.
- [ ] Edge scroll: velocidad aceptable; al llegar al borde del mapa comprobar si el corte seco molesta.
- [ ] Rueda: zoom suave entre min y max distance.
- [ ] Q/E: rotación suave; si hay lag notable, bajar `rotationSmoothTime`.
- [ ] Drag (botón central): pan fluido.
- [ ] Al cargar la escena: si `focusOnTownCenterAtStart` está activo, la cámara debe centrarse en el TC del jugador 1 tras el delay.

---

## Cambios realizados

- **smoothEdgeMovement:** Implementado. Si `smoothEdgeMovement && smoothEdgeSpeed > 0`, el target en XZ se acerca al límite con `MoveTowards(..., smoothEdgeSpeed * dt)` en lugar de clamp directo.
