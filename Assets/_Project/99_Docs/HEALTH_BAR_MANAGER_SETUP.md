# HealthBarManager – Barras de vida en pantalla (Screen Space)

Sistema centralizado: **un solo Canvas** en Screen Space, barras instanciadas dinámicamente y posicionadas con `WorldToScreenPoint`. Las barras siguen a la unidad al moverse y se ocultan cuando están detrás de la cámara.

## Escena (flujo principal: referencias explícitas)

1. **HealthBarManager**
   - Añade un GameObject en la escena (o en el objeto del HUD) con el componente **HealthBarManager**.
   - **World Camera:** asigna la cámara del juego; si está vacío usa `Camera.main` (se avisa por consola si también es null).
   - **Canvas:** asigna un Canvas en Screen Space Overlay (recomendado). Si está vacío, el manager crea un Canvas en runtime como **fallback solo para pruebas** y muestra un warning; en producción conviene tener un Canvas dedicado en escena.
   - **Health Bar Prefab:** asigna **PF_HealthBarUI** (`Assets/_Project/08_Prefabs/PF_HealthBarUI.prefab`). Si falta, se muestra un warning y no se crearán barras.

2. **Prefab PF_HealthBarUI**
   - Ruta: `08_Prefabs/PF_HealthBarUI.prefab`.
   - Minimalista para RTS: sin raycast (Image con Raycast Target desactivado), sin Layout Group ni otros componentes de layout. Pivot (0.5, 0.5), tamaño 120×14.
   - Raíz: RectTransform + HealthBarUI; hijo Fill: Image (Type = Filled, Horizontal, Left). Para marco/fondo, añade hijos Image bajo la raíz si lo necesitas.

## Unidades y edificios

- Cada entidad con **Health** puede mostrar barra al ser **seleccionada**.
- **Health** debe tener:
  - **Bar Anchor (opcional):** Transform hijo (p. ej. un empty "BarAnchor" sobre la cabeza). Si es null, se usa `transform.position + Fallback Offset`.
  - **Fallback Offset:** por defecto `(0, 2, 0)`.

En prefabs de unidad/edificio puedes añadir un hijo vacío **BarAnchor** y asignarlo en el componente Health. BuildSite y RTSMapGenerator asignan el anchor en runtime cuando crean el BarAnchor.

## API reutilizable

Para no acoplar la lógica solo al RTSSelectionController, usa la API estática por entidad (útil para selección, hover, unidades dañadas o en combate):

- **HealthBarManager.ShowBarForEntity(GameObject entity)** — Muestra la barra si la entidad tiene Health. Si no tiene Health, no hace nada (no se añade Health automáticamente).
- **HealthBarManager.HideBarForEntity(GameObject entity)** — Oculta la barra para esa entidad.

Registro directo por componente Health:

- **HealthBarManager.Instance.Register(Health health)** / **Unregister(Health health)** — Para cuando ya tienes la referencia al Health.

## Flujo

- **Selección:** RTSSelectionController llama a `ShowBarForEntity` / `HideBarForEntity` según selección.
- **Muerte/destrucción:** Health en `OnDestroy()` llama a `Unregister(this)`. Si la barra se destruye antes, su `OnDestroy` también llama a `Unregister` para evitar entradas huérfanas en el manager.

No hace falta lógica de barra dentro de cada prefab; todo lo gestiona el manager. Las entidades sin componente Health no muestran barra.

## Sistema antiguo (deprecado)

- **HealthBarWorld** (Canvas World Space por unidad) está marcado como `[Obsolete]`. No usar en prefabs nuevos.
- El fallback que instanciaba una barra como hijo de la unidad ya no se usa; las barras son siempre hijos del Canvas global.
