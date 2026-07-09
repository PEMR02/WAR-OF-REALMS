# War of Realms — Agent Guide

RTS en Unity (URP) inspirado en *Age of Empires II*. Ver `README.md` para la descripción del producto y la estructura de carpetas.

## Cursor Cloud specific instructions

Entorno: Ubuntu 24.04 headless (sin display físico, sin GPU). El proyecto es **Unity Editor `6000.4.5f1`** (ver `ProjectSettings/ProjectVersion.txt`).

### Editor y dependencias
- El Editor de Unity para Linux se instala en `/opt/unity/6000.4.5f1/Editor/Unity` (lo instala el update script de arranque; queda persistido en el snapshot). Binario: `UNITY_BIN=/opt/unity/6000.4.5f1/Editor/Unity`.
- No hay display físico: **todo comando de Unity debe ejecutarse bajo `xvfb-run -a`** (ej. `xvfb-run -a "$UNITY_BIN" -batchmode -nographics ...`).
- Las dependencias UPM (incluidos paquetes Git como `com.coplaydev.unity-mcp` y `com.game-crafters-guild.worldbuilding`, y el paquete local `com.pmg.unified-world-pipeline`) las resuelve el propio Editor al abrir el proyecto; no hay `npm/pip`.

### Licencia (REQUISITO BLOQUEANTE)
- Unity **no arranca en batchmode sin una licencia activada**. Sin ella cualquier comando termina con `No valid Unity Editor license found`.
- La activación requiere credenciales de una cuenta Unity, que se inyectan como secrets. Mecanismos soportados por `/opt/unity-setup/activate-license.sh`:
  - `UNITY_LICENSE` = contenido completo de un `.ulf` (activación manual; válido para Personal), **o**
  - `UNITY_EMAIL` + `UNITY_PASSWORD` (+ `UNITY_SERIAL` para Pro/Plus).
- Activar una vez por arranque de VM: `UNITY_BIN=/opt/unity/6000.4.5f1/Editor/Unity bash /opt/unity-setup/activate-license.sh`.
- La activación **no** está en el update script (depende de secrets y puede fallar); ejecútala manualmente tras el arranque.

### Compilar / testear / ejecutar (tras activar licencia)
- Escena jugable principal: `Assets/_Project/07_Scenes/SampleScene.unity` (única en Build Settings).
- Compilar / importar (genera `Library/`, primer arranque es lento): `xvfb-run -a "$UNITY_BIN" -batchmode -nographics -quit -projectPath /workspace -logFile /tmp/unity.log`
- Tests EditMode: `UNITY_BIN=/opt/unity/6000.4.5f1/Editor/Unity bash /opt/unity-setup/run-tests.sh` (usa `-runTests -testPlatform EditMode -testResults`). Tests existentes en `Assets/_Project/01_Gameplay/Map/Editor/MapPreviewTextureBuilderTests.cs`.
- Ejecutar el juego: en un VM headless usa batchmode con display virtual (`xvfb-run`); Play mode gráfico interactivo no está disponible sin display. Para lógica de juego, EditMode/PlayMode tests son la vía fiable.

### Notas
- La inspección en vivo vía MCP for Unity (`localhost:8090`, ver `README.md`) solo funciona con un Editor abierto con el plugin; no está activa por defecto en el VM.
- El primer import del proyecto (~500MB de Assets) tarda; no lo interrumpas. `Library/` está en `.gitignore` y se regenera.
