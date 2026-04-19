# War of Realms

RTS en Unity inspirado en la línea clásica tipo *Age of Empires II*: economía, construcción, selección de unidades y combate sobre mapa 3D.

## Requisitos

- **Unity Editor:** `6000.3.9f1` (ver `ProjectSettings/ProjectVersion.txt`).
- **Render pipeline:** URP (`com.unity.render-pipelines.universal` en `Packages/manifest.json`).

## Estructura principal del proyecto

| Ruta | Contenido |
|------|-----------|
| `Assets/_Project/` | Código, prefabs, datos y escenas del juego |
| `Assets/_Project/07_Scenes/` | Escenas jugables y de prueba |
| `Assets/_Project/04_Data/` | ScriptableObjects y datos |
| `Assets/_Project/03_UI/` | UI y HUD |
| `Packages/manifest.json` | Dependencias UPM |
| `Packages/packages-lock.json` | Resolución exacta de paquetes |
| `ProjectSettings/` | Ajustes del proyecto y del Editor |

## Inspección en vivo (MCP)

El repositorio incluye **MCP for Unity** (`com.coplaydev.unity-mcp` en el manifiesto).

> **Nota:** Uses MCP for Unity (CoplayDev) on `http://localhost:8090` for live inspection via Cursor.

Con ese servidor activo, las herramientas MCP permiten inspeccionar jerarquía, escenas y estado del Editor sin depender del antiguo stack IvanMurzak / `unity-mcp-cli` sobre `/api/tools/`.

## Licencia

Sin licencia pública definida en este repositorio; uso interno o según acuerdo del propietario.
