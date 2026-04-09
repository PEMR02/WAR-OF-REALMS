# fix-minimal

Aplica solo el fix mínimo aprobado para el bug actual.

Objetivo:
- editar lo mínimo posible
- preservar comportamiento existente que ya funciona
- mantener diff pequeño y reversible

Proceso:
1. Repite causa raíz aprobada.
2. Enumera archivos exactos a tocar.
3. Aplica el fix mínimo.
4. Explica qué cambió.
5. Lista riesgos de regresión.
6. Entrega checklist de no-regresión.
7. Entrega pasos concretos de prueba en Unity.

Restricciones:
- No refactor general.
- No renombres públicos.
- No mover archivos.
- No tocar más de 3 archivos sin detenerte.

This command will be available in chat with /fix-minimal
