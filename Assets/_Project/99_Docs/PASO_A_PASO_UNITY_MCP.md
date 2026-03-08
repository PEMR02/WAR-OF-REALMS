# Paso a paso: hacer funcionar Unity MCP en Cursor

El servidor **Unity MCP** aparece en Cursor, pero **no expone herramientas** hasta que el **plugin dentro de Unity** se conecta por el puerto 8080. Sigue estos pasos en orden.

---

## Orden al abrir los programas (día a día)

Cuando vuelvas a abrir todo (Cursor, Unity, Blender), hazlo en este orden para que el Unity MCP siga en verde:

1. **Cursor**  
   Abre Cursor y abre la carpeta del proyecto **WAR OF REALMS**.  
   Así se inicia el servidor MCP y se queda escuchando en el puerto **8080**.

2. **Unity**  
   Abre Unity con el proyecto **WAR OF REALMS**.  
   - **Window → AI Game Developer**  
   - **Server URL** = `http://localhost:8080`  
   - Pulsa **Start** y espera a que **MCP server** y **Unity** estén en **verde**.

3. **Blender** (opcional)  
   Puedes abrirlo cuando quieras; no usa el puerto 8080 ni depende de Unity.  
   El MCP de Blender en Cursor funciona por separado.

**Resumen:** Primero Cursor (servidor en 8080) → luego Unity (conectar a 8080). Si un día ves "address already in use", libera el 8080 (Stop en Unity, o `netstat` + `taskkill` del proceso que use 8080) y vuelve a seguir este orden.

---

## Por qué Cursor muestra "Error" y 0 tools (punto rojo)

En el log del MCP verás **"No connected clients. Retrying [1/10]..."** y al final **"Found 0 tools"**. Eso significa:

- El **unity-mcp-server.exe** sí arranca y Cursor se conecta por stdio.
- Pero la **lista de herramientas** la proporciona el **plugin de Unity** (que se conecta al servidor por HTTP en el puerto 8080).
- Si en el momento en que Cursor pide la lista de herramientas **Unity no está conectado**, el servidor no tiene "clientes" y devuelve **0 tools** → Cursor muestra error (punto rojo).

**Conclusión:** Unity tiene que estar **ya conectado** al puerto 8080 **antes** de que Cursor abra el MCP (o antes de que pida la lista de herramientas). El orden correcto está en el siguiente apartado.

---

## Si el log dice "address already in use" / "puerto en uso" (8080)

Eso significa que **el puerto 8080 ya está ocupado** (otra instancia del servidor, Unity u otra app). Opciones:

- **Usar otro puerto (recomendado):** En `.cursor/mcp.json` el servidor `ai-game-developer` puede usar `--port=8081`. En Unity, en **AI Game Developer**, pon **Server URL** = `http://localhost:8081` y **Start**. Así Cursor y Unity usan 8081 y no chocan con lo que ocupa 8080.
- **Liberar el 8080:** Cierra Unity por completo, en Cursor desactiva el MCP ai-game-developer, y en PowerShell ejecuta `netstat -ano | findstr :8080` para ver el PID que usa 8080; luego `taskkill /PID <número> /F`. Después vuelve a abrir Unity y Cursor con 8080.

---

## Orden crítico: primero Unity, luego Cursor

1. **Abre Unity** con el proyecto WAR OF REALMS.
2. **Window → AI Game Developer** → **Server URL** = el mismo puerto que en `mcp.json` (si usas 8081, pon `http://localhost:8081`; si usas 8080, `http://localhost:8080`) → pulsa **Start**.
3. Espera a que **MCP server** esté en **verde** ("Running (http)" o "Connected").
4. **Solo entonces** abre **Cursor** (o reinicia Cursor) y abre el proyecto.  
   Así, cuando el servidor MCP arranque y Cursor pida la lista de herramientas, Unity ya será un "connected client" y el servidor devolverá las 55 herramientas → Cursor mostrará el punto verde.

Si abres Cursor primero, el servidor arranca, Cursor pide tools de inmediato, Unity aún no está conectado → 0 tools → punto rojo. En ese caso: en Unity conecta (Start) al mismo puerto, luego **desactiva y vuelve a activar** el servidor "ai-game-developer" en Cursor (o reinicia Cursor) para que vuelva a pedir la lista con Unity ya conectado.

---

## Requisito importante: ruta sin espacios

La documentación oficial indica que **el path del proyecto no debe contener espacios**.

- Tu proyecto está en: `d:\PMG\WAR OF REALMS` (tiene espacios).
- Si después de seguir todo lo demás sigue sin funcionar, prueba:
  - Copiar el proyecto a una ruta sin espacios, por ejemplo: `d:\PMG\WAROFREALMS` o `d:\PMG\WarOfRealms`.
  - Abrir ese proyecto en Unity y volver a configurar el MCP (mismo `mcp.json` pero con la nueva ruta al `.exe`).

---

## Paso 1: Instalar el plugin en Unity

1. Descarga el instalador desde:  
   https://github.com/IvanMurzak/Unity-MCP/releases/latest  
   Archivo: **AI-Game-Dev-Installer.unitypackage**.

2. En Unity:
   - **Assets → Import Package → Custom Package**
   - Selecciona el `.unitypackage` descargado e importa todo.

3. Alternativa (si usas OpenUPM): en la carpeta del proyecto ejecuta:
   ```bash
   openupm add com.ivanmurzak.unity.mcp
   ```

---

## Paso 2: Descargar el ejecutable del servidor MCP (si la carpeta está vacía)

Si instalaste el plugin por **OpenUPM**, la carpeta `Library/mcp-server/win-x64/` puede estar **vacía**. El plugin no incluye el .exe; hay que descargarlo aparte.

### Opción recomendada: ruta sin espacios

1. Descarga el servidor para Windows (64 bits):  
   **https://github.com/IvanMurzak/Unity-MCP/releases/latest**  
   Archivo: **unity-mcp-server-win-x64.zip** (en "Assets" del release, p. ej. 0.48.1).

2. Crea la carpeta `d:\PMG\mcp-server-win-x64\` (sin espacios).

3. Extrae **todo el contenido del .zip** dentro de esa carpeta. Debe quedar por ejemplo:
   - `d:\PMG\mcp-server-win-x64\unity-mcp-server.exe`
   - y el resto de archivos del zip en la misma carpeta.

4. En `.cursor/mcp.json` el `command` del servidor `ai-game-developer` debe ser:
   ```json
   "command": "d:/PMG/mcp-server-win-x64/unity-mcp-server.exe"
   ```
   (Así Cursor usa una ruta sin espacios y no hace falta el .bat.)

### Opción alternativa: dentro del proyecto

Si prefieres tener el servidor dentro del proyecto:

1. Descarga **unity-mcp-server-win-x64.zip** desde el [release latest](https://github.com/IvanMurzak/Unity-MCP/releases/latest).

2. Extrae todo el contenido en:
   `d:\PMG\WAR OF REALMS\Library\mcp-server\win-x64\`

3. Comprueba que exista `unity-mcp-server.exe` en esa carpeta. En ese caso sigue usando el `.bat` en `d:\PMG\run-unity-mcp-server.bat` como hasta ahora (o la config que tengas en `mcp.json`).

---

## Paso 3: Configurar Cursor (mcp.json)

Tu `.cursor/mcp.json` debe tener algo así (ya lo tienes; solo verifica):

```json
{
  "mcpServers": {
    "ai-game-developer": {
      "command": "d:/PMG/WAR OF REALMS/Library/mcp-server/win-x64/unity-mcp-server.exe",
      "args": [
        "--port=8080",
        "--plugin-timeout=10000",
        "--client-transport=stdio"
      ]
    }
  }
}
```

- Si moviste el proyecto a una ruta sin espacios, cambia `command` a la nueva ruta del `.exe`.

---

## Paso 4: Abrir Unity y conectar al mismo puerto que Cursor

1. Abre **Unity** con el proyecto **WAR OF REALMS** (o el que uses).
2. Menú: **Window → AI Game Developer (Unity-MCP)** (ventana "Game Developer").
3. **Importante — puerto correcto:**  
   En la ventana verás **Server URL**. Debe apuntar al **mismo puerto** que usa el servidor MCP en Cursor (`8080`).
   - Si pone `http://localhost:57577` (u otro número), **cámbialo a:** `http://localhost:8080`
   - Busca el campo **Server URL** o **Port** en esa ventana y pon **8080** (o la misma URL con `:8080`).
4. Transport: deja **http** (el servidor que inicia Cursor escucha por HTTP en 8080; Unity se conecta a él).
5. Pulsa **Start** si la conexión no arranca sola. Comprueba:
   - **Unity:** en verde ("Connecting..." o "Connected").
   - **MCP server:** debe pasar a **verde** cuando Unity se conecte al puerto 8080.
6. Si "MCP server" sigue en rojo/naranja:
   - Cierra Cursor y vuelve a abrirlo (para que arranque `unity-mcp-server.exe`).
   - Con Cursor abierto, en Unity vuelve a comprobar Server URL = `http://localhost:8080` y **Start**.
   - Asegúrate de que ningún otro programa use el puerto 8080.

---

## Paso 5: Reiniciar Cursor

1. Con Unity abierto y el plugin en **Listening**:
2. Cierra **Cursor** por completo (no solo la pestaña).
3. Vuelve a abrir Cursor y abre el mismo proyecto.

Así Cursor vuelve a conectar con el MCP y el servidor podrá registrar las herramientas cuando Unity esté conectado.

---

## Paso 6: Probar que funciona

En el chat de Cursor escribe algo como:

- *"Crea un cubo rojo en la posición (0, 0, 0) y nómbralo MiPrimerCubo."*

Si el MCP está bien conectado, Cursor usará herramientas de Unity (por ejemplo `gameobject-create`, `assets-material-create`) y en la escena de Unity debería aparecer el cubo.

---

## Si Unity dice "conexión denegada" (Connection refused)

Ese error significa que **nada está escuchando en el puerto 8080**. O el servidor no arranca, o no abre el puerto. Prueba en este orden:

### 1. Orden de arranque: primero Cursor, luego Unity

El proceso `unity-mcp-server.exe` lo inicia **Cursor** al abrir el proyecto. Si abres Unity antes que Cursor, el puerto 8080 estará vacío.

- Cierra Unity y la ventana "AI Game Developer" (pulsa **Stop** para dejar de reintentar).
- Abre **Cursor** y abre la carpeta del proyecto (WAR OF REALMS).
- Espera unos segundos.
- Abre **Unity** y la ventana **Window → AI Game Developer**.
- Server URL = `http://localhost:8080`, pulsa **Start**.

### 2. Comprobar si el servidor arranca con la ruta actual

La ruta del proyecto tiene **espacios** (`WAR OF REALMS`). A veces el proceso no se inicia bien por eso.

**Prueba manual (PowerShell):**

1. Abre PowerShell.
2. Ejecuta (sustituye por tu ruta si es distinta):
   ```powershell
   & "d:\PMG\WAR OF REALMS\Library\mcp-server\win-x64\unity-mcp-server.exe" --port=8080 --plugin-timeout=10000 --client-transport=stdio
   ```
3. Si el servidor arranca, deberías ver algo como que está escuchando en 8080 (o sin errores). No lo cierres.
4. En Unity, ventana AI Game Developer, Server URL = `http://localhost:8080`, **Start**.
5. Si así Unity conecta, el fallo está en que **Cursor no está iniciando bien el .exe** (p. ej. por los espacios en la ruta).

### 3. Usar ruta sin espacios (recomendado si sigue fallando)

- Copia o mueve el proyecto a una carpeta **sin espacios**, por ejemplo: `d:\PMG\WAROFREALMS`.
- Abre ese proyecto en Unity y en Cursor.
- En `.cursor/mcp.json` cambia `command` a la nueva ruta, por ejemplo:
  ```json
  "command": "d:/PMG/WAROFREALMS/Library/mcp-server/win-x64/unity-mcp-server.exe"
  ```
- Reinicia Cursor, luego abre Unity y conecta a `http://localhost:8080`.

### 4. Comprobar que nada más use el puerto 8080

En PowerShell:

```powershell
netstat -ano | findstr :8080
```

Si sale alguna línea con LISTENING, otro proceso está usando 8080. Cierra ese programa o cambia de puerto (p. ej. 8081 en `mcp.json` y en la ventana de Unity).

### 5. Firewall o antivirus

Comprueba que el Firewall de Windows (o el antivirus) no esté bloqueando `unity-mcp-server.exe` o las conexiones en `localhost:8080`. Prueba temporalmente desactivar o añadir excepción para ese ejecutable.

---

## Resumen de comprobaciones

| Comprobación                         | Qué hacer |
|-------------------------------------|------------|
| ¿Existe `unity-mcp-server.exe`?     | Ruta: `Library/mcp-server/win-x64/` |
| ¿Unity abierto con el proyecto?     | Abre el proyecto correcto |
| ¿Ventana "AI Game Developer" abierta? | Window → AI Game Developer |
| ¿Status = Listening/Connected?      | Si no, revisa puerto 8080 y firewall |
| ¿Cursor reiniciado después de Unity?| Cierra y abre Cursor de nuevo |
| ¿Ruta del proyecto con espacios?    | Si falla todo, prueba copiar proyecto a ruta sin espacios |

---

## Referencias

- [Unity-MCP Wiki - Getting Started](https://github.com/IvanMurzak/Unity-MCP/wiki/Getting-Started)
- [Unity-MCP Wiki - Installation Guide](https://github.com/IvanMurzak/Unity-MCP/wiki/Installation-Guide)
- [Unity-MCP Wiki - Configuration](https://github.com/IvanMurzak/Unity-MCP/wiki/Configuration)
- [Unity-MCP Wiki - AI Tools Reference](https://github.com/IvanMurzak/Unity-MCP/wiki/AI-Tools-Reference)
