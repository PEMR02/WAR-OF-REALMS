# Configuración: Notificaciones y Atajos (Unity Editor)

Pasos recomendados en el Editor de Unity para dejar operativos las notificaciones de gameplay, el botón de cancelar construcción, la reparación con aldeanos y el atajo al Town Center.

---

## 1. GameplayNotifications (toasts)

Para que mensajes como "Unidad creada" o "Edificio completado" se muestren en pantalla:

1. **Crear un GameObject** en la escena (o en el Canvas del HUD), por ejemplo `GameplayNotifications`.
2. **Añadir el componente** `GameplayNotifications` (script en `Assets/_Project/03_UI/HUD/GameplayNotifications.cs`).
3. **Canvas + TextMeshPro:**
   - Opción A: Como hijo, crear un **Panel** (RectTransform) y dentro un **Text - TextMeshPro** (o TextMeshProUGUI). Asignar ese RectTransform a **Container** y el TextMeshProUGUI a **Message Text**.
   - Opción B: Si solo quieres texto flotante, asigna un RectTransform a **Container** y un TextMeshProUGUI a **Message Text** (pueden ser el mismo objeto o el texto hijo del container).
4. **Opcional:** Ajustar en el inspector `Display Duration`, `Fade Out Duration`, `Queue Messages` y `Max Queue Size`.

**Nota:** Si `Container` o `Message Text` están vacíos al inicio, el sistema no romperá; simplemente no mostrará notificaciones hasta que se asignen.

---

## 2. ProductionHUD – Botón Cancelar construcción

Cuando el jugador selecciona un solar en construcción (BuildSite), el HUD de producción puede mostrar un botón para cancelar:

1. Abre el **GameObject** que tiene el componente **ProductionHUD** (por ejemplo el panel HUD_Production).
2. En el inspector, en la sección **Build Site (solar en construcción)**, asigna el **Cancel Construction Button** al botón de UI que debe cancelar la construcción (por ejemplo "Cancelar" o "Cancel Construction").

Si no asignas el botón, esa funcionalidad no aparecerá; el resto del HUD sigue funcionando.

---

## 3. Aldeano – Reparar edificios (Repairer)

Para que los aldeanos puedan **reparar edificios**:

- **Prefab del aldeano:** `Assets/_Project/08_Prefabs/Units/Aldeano.prefab`
- **Acción en Unity:** En el prefab Aldeano, **añade el componente `Repairer`** (script en `Assets/_Project/01_Gameplay/Units/Repairer.cs`).

Sin este componente, el aldeano no tendrá la capacidad de reparar; con él, se podrá asignar la orden de reparar a edificios dañados (según la lógica del proyecto).

---

## 4. Town Center – Atajo de teclado

El componente **TownCenterHotkey** centra la cámara en el primer Town Center del jugador.

- **Tecla por defecto:** `Home` (configurada en el script como `Key.Home`).
- **Dónde configurar:** En el GameObject que tenga **TownCenterHotkey**, en el inspector puedes cambiar el campo **Key** si quieres otra tecla.
- **Identificación del Town Center:** El script busca instancias de **BuildingInstance** cuyo `buildingSO.id` coincida con los valores de **Town Center Ids** (por defecto: "TownCenter", "PF_TownCenter", "Town Centre"). Si tu edificio usa otro id, añádelo al array **Town Center Ids** en el inspector.

No es necesario asignar nada más para el atajo si usas la tecla por defecto y un id de Town Center ya incluido en la lista.

---

## Resumen rápido

| Elemento | Acción en Editor |
|----------|-------------------|
| **GameplayNotifications** | Objeto con componente + Canvas/TextMeshPro asignados a Container y Message Text. |
| **ProductionHUD** | Asignar **Cancel Construction Button** al botón de cancelar construcción. |
| **Aldeano** | Añadir componente **Repairer** al prefab `08_Prefabs/Units/Aldeano.prefab`. |
| **Town Center hotkey** | Por defecto tecla **Home**; opcionalmente cambiar Key o Town Center Ids en TownCenterHotkey. |
