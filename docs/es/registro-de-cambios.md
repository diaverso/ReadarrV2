# Readarr Community Fork — Registro de Cambios

Este documento registra los cambios realizados en el fork mantenido por la comunidad tras el retiro del equipo original de Servarr.

---

## [No publicado] — 2026-04-03 (más reciente)

### Añadido

#### Metadatos — Selección de Edición por Idioma Preferido
- Cuando el idioma de la UI es distinto del inglés (p.ej. español), `BookInfoProxy` ahora selecciona como **edición monitorizada** la edición en ese idioma, en lugar de la más popular
- El título del libro mostrado en la lista de libros del autor se actualiza al título de la edición en el idioma preferido cuando existe una coincidencia (p.ej. "El pájaro y el corazón de piedra" en lugar del título en inglés de la obra)
- La comparación de idioma es insensible a mayúsculas y comprueba el campo de idioma de la edición contra el código ISO 639-1 de dos letras, el código ISO 639-2 de tres letras y el nombre en inglés (p.ej. "es", "spa", "Spanish")
- Si no se encuentra ninguna edición en el idioma preferido, se mantiene el comportamiento anterior (edición más popular)

#### Metadatos — Fallback Automático a OpenLibrary
- `BookInfoProxy` ahora recurre automáticamente a **Open Library** cuando BookInfo (GoodReads) devuelve un error inesperado al actualizar un autor o un libro
- Fallback para `GetAuthorInfo`: busca el nombre del autor en la base de datos local y lo busca en Open Library por nombre
- Fallback para `GetBookInfo`: busca el título y nombre del autor en la base de datos local y busca en Open Library por título + autor
- Las excepciones `AuthorNotFoundException` y `BookNotFoundException` (respuestas 404) no se reintentan — se propagan inmediatamente
- Open Library sigue disponible como fuente primaria configurando `MetadataSource = "openlibrary"` en Ajustes

#### Notificaciones — Google Play Books
- Cambiado el backend de subida del endpoint deprecated de la Books API `useruploadedbooks` (devuelve 404) a la **API de Google Drive** (`/upload/drive/v3/files?uploadType=multipart`)
- Los libros subidos a Google Drive en la cuenta del usuario aparecen automáticamente en Google Play Libros
- Sonda de conectividad previa a la subida contra `GET /drive/v3/about?fields=user` — devuelve un mensaje de error claro si la API de Drive no está habilitada o el token no tiene el scope requerido
- Scope OAuth2 requerido cambiado de `https://www.googleapis.com/auth/books` a `https://www.googleapis.com/auth/drive.file`
- Script helper `get_google_refresh_token.py` actualizado para solicitar el scope `drive.file`

#### Cliente de Descarga — HTTP Blackhole (FlareSolverr)
- Nuevo campo opcional **FlareSolverr URL** en la configuración del cliente de descarga HTTP
- Cuando está configurado, FlareSolverr se prueba primero para saltarse los retos de DDoS-Guard / Cloudflare; si FlareSolverr no está disponible o devuelve error, se usa el solucionador PoW integrado

### Corregido

#### Cliente de Descarga — HTTP Blackhole
- **Nombres de archivo UTF-8**: el parser de la cabecera Content-Disposition ahora maneja correctamente la codificación RFC 5987 `filename*=charset'language'value` — corrige nombres de archivo corruptos como `pÃ¡jaro.epub` → `pájaro.epub`
- **Resolución de URLs en múltiples saltos**: reemplazado el chequeo de tipo de contenido de un solo paso por un bucle de hasta 4 saltos que re-evalúa `Content-Type` en cada respuesta — corrige la cadena JSON → HTML → binario de Z-Library
- **Descargas `.bin` de Z-Library**: el endpoint web `/dl/` requiere autenticación por cookie (`remix_userid` / `remix_userkey`); las cabeceras de autenticación ahora también establecen la cabecera `Cookie:` — corrige que se guardara la página de login HTML como `.bin`

---

## [No publicado] — 2026-04-03

### Añadido

#### Conexión Google Play Libros
- Nueva conexión/notificación **Google Play Books**: sube automáticamente los archivos EPUB y PDF importados a la biblioteca personal de Google Play Libros del usuario
- Autenticación mediante OAuth2 (Client ID + Client Secret + Refresh Token de Google Cloud Console con la API de Books habilitada)
- Omite formatos no compatibles (MOBI, AZW3, etc.) con un mensaje de debug — Google Play Libros solo acepta EPUB y PDF
- Los errores de subida se registran por archivo y no bloquean la subida del resto de archivos

#### Indexer Anna's Archive
- Nuevo indexer para Anna's Archive — extrae resultados del HTML de la página de búsqueda usando los selectores CSS del sitio (`div.flex.pt-3.pb-3.border-b`, `a.js-vim-focus`, `div.text-gray-800.font-semibold.text-sm`)
- Parsea formato, tamaño de archivo e idioma desde la barra de metadatos (separada por `·`)
- Deduplicación por MD5 para evitar resultados duplicados
- Fallback a escaneo simple de enlaces `/md5/` si el parseo por bloques no da resultados
- Campo **API Key** (Avanzado): cuando se proporciona una API key de miembro, usa `/dyn/api/fast_download.json` para resolver URLs de descarga directa en lugar de la página de descarga lenta protegida por DDoS-Guard
- URL por defecto establecida en `https://annas-archive.gd` — mirrors funcionales: `annas-archive.gd`, `annas-archive.org`, `annas-archive.se`, `annas-archive.li`

#### Docker / Despliegue
- Dockerfile reescrito como build multi-etapa de 3 fases: build frontend con Node.js → build backend con .NET → imagen runtime. Un `docker compose up` desde cero ahora compila todo desde el código fuente sin pasos manuales previos
- `entrypoint.sh`: en el primer arranque, espera a que Readarr esté listo y crea automáticamente los clientes de descarga HTTP "Download z-Library" y "Download Annas Archive" apuntando a `/downloads`
- `docker-compose.yml`: añadido volumen `/downloads:/downloads`
- `tsconfig.json` añadido al contexto de build de Docker (requerido por `ForkTsCheckerWebpackPlugin` durante el build de webpack)

#### Cliente de Descarga HTTP
- Carpeta de descarga por defecto cambiada a `/downloads`

### Corregido

#### Indexer Anna's Archive
- Eliminado el parámetro `output=json` de las peticiones de búsqueda (ignorado por el sitio — siempre devuelve HTML)
- Cambiado `HttpAccept` de `Json` a `Html`
- Corregido `ParseSize` para manejar tamaños sin espacio entre número y unidad (p.ej. `15.0MB`)
- URL por defecto corregida del dominio inactivo `annas-archive.gl` a `annas-archive.gd`

---

## [No publicado] — 2026-04-01

### Añadido

#### Indexer Z-Library
- Campo **Session Cookies**: permite pegar las cookies de sesión del navegador directamente (soporta mirrors tipo z-lib.cv con auth Laravel: `z_lib_session=...; zl_logged_in=1`)
- Campos **Remix User ID** y **Remix User Key** (Avanzado): autenticación manual con cookies de singlelogin.rs sin depender del login programático
- La autenticación ahora se intenta en orden: (1) cookies de sesión raw → (2) remix_userid + remix_userkey → (3) login EAPI con email/contraseña

#### UI — Clientes de Descarga
- Nueva sección **Other** en el modal "Añadir Cliente de Descarga" para mostrar clientes con protocolo `unknown` (e.g. HTTP Download)

### Corregido

#### Indexer Z-Library
- **[CRÍTICO]** `400 Bad Request` al guardar/probar el indexer cuando el campo `BaseUrl` no estaba explícitamente definido (campo Avanzado no enviado por el frontend) — eliminado el validador `ValidRootUrl()` que rechazaba el valor null
- `NullReferenceException` en `BaseUrl.TrimEnd('/')` cuando el campo era null — reemplazado con `?.TrimEnd('/') ?? "https://singlelogin.rs"` en todos los usos (RequestGenerator, Parser, clase principal)
- URL por defecto actualizada de `singlelogin.re` (dominio confiscado por el FBI en mayo de 2024) a `singlelogin.rs`

---

## [No publicado] — 2026-03-29

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

### Corregido (Modal de Autenticación en Primera Ejecución)

- **[CRÍTICO]** Corregido el modal "Configurar Autenticación" de primera ejecución: hacer click en **Guardar** ahora persiste correctamente `AuthenticationMethod=Basic` en `config.xml`, crea el usuario en la base de datos y cierra el modal. Anteriormente el modal reaparecía en cada carga de página independientemente de lo ingresado.
  - **Causa raíz (backend):** Kestrel deshabilita la E/S síncrona por defecto. `SaveHostConfig` intentaba una lectura síncrona del cuerpo con `StreamReader.ReadToEnd()`, lo que lanzaba `InvalidOperationException: Synchronous operations are disallowed` — el guardado nunca se completaba. Corregido releyendo el cuerpo de forma asíncrona (`ReadToEndAsync`) usando el stream con seek proporcionado por `BufferingMiddleware`, y parseando los campos de auth con `STJson`.
  - **Causa raíz (frontend):** `createSaveHandler` llamaba a `getSectionState(getState(), section)` sin el flag `isFullStateTree`, leyendo de una ruta de estado Redux incorrecta y produciendo un objeto vacío. Corregido pasando `true` como tercer argumento.
  - Añadido override de `ValidateResource` en `HostConfigController` para rellenar los campos no-auth requeridos (puerto, dirección de enlace, rama, intervalos de backup) desde la configuración actual cuando se recibe un PUT parcial desde el modal de primera ejecución — previene que FluentValidation rechace la petición.
  - `SaveConfigDictionary` puede omitir campos cuyos valores coincidan con la configuración actual; añadidas llamadas explícitas a `SetValue("AuthenticationMethod", ...)` y `SetValue("AuthenticationRequired", ...)` para garantizar que el XML siempre se actualice.

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
