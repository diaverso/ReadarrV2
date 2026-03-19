# Readarr Community Fork — Registro de Cambios

Este documento registra los cambios realizados en el fork mantenido por la comunidad tras el retiro del equipo original de Servarr.

---

## [No publicado] — 2026-03-18

### Corregido

#### Fuentes de Metadatos
- **[CRÍTICO]** Restaurada la funcionalidad de metadatos redirigiendo las peticiones por defecto a `api.bookinfo.pro` (instancia comunitaria de rreading-glasses), reemplazando el endpoint inactivo `api.bookinfo.club`
- **[CRÍTICO]** Eliminadas las API keys hardcodeadas de Goodreads en `GoodreadsProxy.cs`. El token ahora se configura vía Ajustes → General → Token de Goodreads
- El proxy de Goodreads ahora omite de forma elegante con un mensaje de debug cuando no hay token configurado (sin crashes ni errores)
- Renombrado `SearchByGoodreadsBookId(int)` → `SearchByForeignEditionId(string)` para desacoplar la interfaz de los IDs enteros específicos de Goodreads

#### Configuración
- Añadida propiedad `GoodreadsToken` a `IConfigService` y `ConfigService`
- Añadida propiedad `MetadataSource` a `IConfigService` (ya existía pero no estaba completamente conectada)

#### Integración con Open Library (Nuevo)
- Añadido `OpenLibraryProxy` — implementación completa de `IProvideAuthorInfo`, `IProvideBookInfo`, `ISearchForNewBook`, `ISearchForNewAuthor`, `ISearchForNewEntity` respaldada por la API de Open Library
- Añadidos modelos de recursos: `OpenLibraryAuthorResource`, `OpenLibraryWorkResource`, `OpenLibraryEditionResource`, `OpenLibrarySearchResource`
- Añadido `PolymorphicStringConverter` para manejar los campos bio/description de Open Library en formato dual (texto plano u objeto tipado)
- `BookInfoProxy` ahora delega a `OpenLibraryProxy` cuando `MetadataSource` está configurado como `"openlibrary"`
- Las portadas de autores de Open Library usan la API de `covers.openlibrary.org`

### Mejorado (Modernización UI)

#### Pulido Visual
- El indicador de ítem activo en la barra lateral ahora usa el color primario índigo (`--primaryColor`) en lugar del rojo — coherente con la paleta de acento índigo de la app
- El padding de los enlaces de navegación lateral aumentado a 11px para mayor espaciado vertical
- El overlay del encabezado de detalle de Autor y Libro cambiado de plano (`opacity 0.7`) a degradado direccional (`rgba 25% → rgba 92%`) — la imagen de portada se aprecia en la zona superior
- El título de la página de detalle de Libro actualizado a `font-weight: 600`, `letter-spacing: -0.02em` y `text-wrap: balance` (igual que el de Autor)
- Tamaño de título móvil armonizado: Páginas de detalle de Autor y Libro usan 28px / peso 500 en pantallas pequeñas
- Las tarjetas del índice de libros (posters) ahora comparten el mismo efecto hover que las de autores: elevación `translateY(-2px)`, `box-shadow: 0 8px 24px rgba(0,0,0,0.3)`, y `backdrop-filter: blur(4px)` en los controles de acción
- Filas de tabla: `min-height: 42px` añadido; transición de hover acelerada de 500ms a 200ms ease para mayor reactividad
- Padding de celdas de tabla: `10px 8px` (antes `8px`) para filas más legibles
- `font-feature-settings: 'kern' 1, 'liga' 1` añadido globalmente para mejores ligaduras y kerning de Inter

### Corregido (Backports de PRs)

- **[PR #4103]** Corregidas las imágenes de autor sobredimensionadas en el modal Añadir Autor — las imágenes ya no desbordan el contenedor del modal
- **[PR #4086]** Corregidos los caracteres literales `{` y `}` en los formatos de renombrado de archivos — usa `{{` y `}}` para escapar llaves en las plantillas de nombres
- **[PR #3996]** Aplicado límite de 255 bytes en el nombre de archivo para archivos renombrados, previniendo errores del sistema de archivos en Windows/Linux
- **[PR #3948]** Añadido polyfill de `Object.groupBy` para compatibilidad con Firefox ESR 115 — previene que el calendario falle en navegadores más antiguos
- **[PR #3899]** Aplicado CSS `text-wrap: balance` al título de la página de detalle de autor para un salto de línea más equilibrado
- **[PR #4099]** Añadido KEPUB como formato de conversión de salida de Calibre (soportado nativamente desde Calibre 8.0)
- **[PR #4091]** Suprimidos eventos `AuthorEditedEvent` redundantes durante ciclos de actualización de autor en segundo plano — reduce recargas innecesarias de la UI. `EnsureAuthorCovers`/`EnsureBookCovers` ahora devuelven si las portadas fueron realmente descargadas; `MediaCoversUpdatedEvent` tiene nueva propiedad `Updated`; `AuthorController` solo emite cambios UI cuando algo cambió realmente

---

## [1.4.x] — Readarr Original (pre-retiro)

Consulta el [repositorio original de Readarr](https://github.com/Readarr/Readarr) y el [historial de versiones](https://github.com/Readarr/Readarr/releases) para changelogs anteriores.

---

*Este fork es mantenido por la comunidad. Para soporte, consulta [docs/es/](.) o abre un Issue en GitHub.*
