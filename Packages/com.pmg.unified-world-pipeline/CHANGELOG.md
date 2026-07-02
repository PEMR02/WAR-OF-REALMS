# Changelog

## 1.0.4

- Materiales de agua: GUIDs de shader corregidos + texturas foam/olas incluidas en el paquete.
- Terrain layers: texturas diffuse/normal empaquetadas con los GUIDs que referencian los `.terrainlayer`.
- Export `.tgz` UPM: contenido bajo carpeta `package/` (formato npm/Unity Pack).

## 1.0.3

- `WaterStylizedIntegration` público (WaterAuthoring del juego host).
- `RuntimeMapGenerationSettings`: API pública para `MarkLegacyResourceFallbackFromScene` y `SemanticRegions`.
- `MatchConfig` usa `ScriptableObject`/`int` en slots (sin stubs duplicados con el juego host).
- Eliminados `GameplayHostStubs` del Runtime (evita CS0029/CS0266 en WOR).

## 1.0.2

- Incluye `WebMapRiverSpline` en el paquete (requerido por `RiverSurfaceMeshBuilder`).

## 1.0.1

- Paquete autocontenido: `MatchConfig`, `MatchConfigCompiler`, `MapGenConfigFactory` en `Runtime/Host/`.
- Eliminada dependencia de `RTSMapGenerator` en editor UWP (`IUwpSceneVisualBindings`, `UwpGridLayoutUtility`).
- Stubs mínimos para tipos del juego host (`AIDifficulty`, `BuildingSO`, `UnitSO`, `WaterMeshMode`).

## 1.0.0

- Primer empaquetado UPM del motor `MapGenerator` + herramienta UWP.
- Shaders WOR, materiales, terrain layers y config por defecto incluidos.
- `MatchConfigCompiler` permanece en el proyecto host (lobby/RTS).
