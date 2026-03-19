# Readarr — Arquitectura del Sistema

## Visión General

Readarr es un gestor de colecciones de ebooks y audiolibros construido sobre la plataforma **Servarr**. La aplicación se compone de un backend .NET y un frontend React que se comunican a través de una API REST.

---

## Backend (.NET)

### Proyectos

| Proyecto | Propósito |
|---------|-----------|
| `NzbDrone.Core` | Lógica de negocio, servicios, base de datos, metadatos |
| `NzbDrone.Common` | Utilidades compartidas, cliente HTTP, serialización |
| `NzbDrone.Api` | Controladores REST (Nancy/ASP.NET) |
| `NzbDrone.Host` | Bootstrap de la aplicación, contenedor DI (DryIoc) |
| `NzbDrone.Console` | Punto de entrada por consola |

### Inyección de Dependencias

Readarr usa **DryIoc** para IoC. Todos los servicios que implementan una interfaz registrada se auto-registran. Cuando existen múltiples implementaciones de la misma interfaz, la última registrada tiene prioridad.

### Base de Datos

SQLite vía **NzbDrone.Core.Datastore** (capa ORM personalizada sobre Dapper). Las migraciones están en `src/NzbDrone.Core/Datastore/Migration/` y están numeradas secuencialmente (actualmente hasta la #40).

---

## Arquitectura de Fuentes de Metadatos

El subsistema de metadatos usa una estrategia de enrutamiento por configuración:

```
IConfigService.MetadataSource
  ├── "" (vacío/por defecto)  → BookInfoProxy → rreading-glasses (api.bookinfo.pro)
  ├── "openlibrary"           → OpenLibraryProxy → openlibrary.org
  └── URL personalizada        → BookInfoProxy con esa URL (rreading-glasses self-hosted)
```

### Interfaces Clave

| Interfaz | Descripción |
|----------|-------------|
| `IProvideAuthorInfo` | Obtener detalles completos de un autor por ID |
| `IProvideBookInfo` | Obtener detalles de libro/work por ID |
| `ISearchForNewBook` | Buscar libros (título, ISBN, ASIN, ID de edición) |
| `ISearchForNewAuthor` | Buscar autores por nombre |
| `ISearchForNewEntity` | Búsqueda combinada de entidades |

### Proveedores de Metadatos

#### BookInfoProxy (`src/NzbDrone.Core/MetadataSource/BookInfo/`)
- **Proveedor principal** para todas las operaciones de metadatos
- Redirige llamadas a `OpenLibraryProxy` cuando `MetadataSource == "openlibrary"`
- Si no, llama a **rreading-glasses** (`https://api.bookinfo.pro/v1/`)
- rreading-glasses es un reemplazo comunitario que expone la misma API BookInfo pero con datos de Goodreads

#### OpenLibraryProxy (`src/NzbDrone.Core/MetadataSource/OpenLibrary/`)
- **Proveedor secundario** activado mediante configuración
- Llama a la API REST pública de Open Library (`https://openlibrary.org`)
- Sin autenticación requerida
- Devuelve `null` en `GetChangedAuthors()` (no hay feed de cambios incremental disponible)

#### GoodreadsProxy (`src/NzbDrone.Core/MetadataSource/Goodreads/`)
- Usado para enriquecimiento de series y listas
- Requiere `GoodreadsToken` configurado en Ajustes → General → Metadatos
- Se omite de forma elegante cuando no hay token configurado

---

## Frontend (React)

### Stack

| Tecnología | Versión | Rol |
|-----------|---------|-----|
| React | 18.2 | Framework UI |
| TypeScript | 5.x | Tipado estático (migración en curso) |
| Redux | 4.x | Gestión de estado |
| Webpack | 5.x | Bundler |
| CSS Modules | — | Estilos con scope por componente |

### Estructura de Directorios

```
frontend/src/
├── Author/          # Detalle de autor, índice, tarjetas, póster
├── Book/            # Detalle de libro, índice, editor
├── Components/      # Componentes UI compartidos (modales, botones, etiquetas)
├── Settings/        # Todas las páginas de ajustes
├── Styles/          # Variables CSS, estilos globales, definiciones de tema
│   ├── Themes/      # dark.js, light.js
│   └── Variables/   # dimensions, fonts, animations, z-indexes
├── Store/           # Redux store, actions, reducers
└── Utilities/       # Funciones auxiliares
```

### Sistema de Temas

Los temas se definen en JavaScript y se inyectan como propiedades CSS personalizadas:
- `frontend/src/Styles/Themes/dark.js` — Tema oscuro (predeterminado)
- `frontend/src/Styles/Themes/light.js` — Tema claro
- `frontend/src/Styles/Themes/index.js` — Gestor de temas

---

## API REST

La API REST sigue la convención Servarr:

```
GET    /api/v1/author         → Listar autores
GET    /api/v1/author/{id}    → Detalle de autor
POST   /api/v1/author         → Añadir autor
PUT    /api/v1/author/{id}    → Actualizar autor
DELETE /api/v1/author/{id}    → Eliminar autor
GET    /api/v1/book           → Listar libros
GET    /api/v1/search?term=   → Buscar en metadatos
```

---

## Configuración Clave

| Propiedad | Defecto | Descripción |
|-----------|---------|-------------|
| `MetadataSource` | `""` | Proveedor de metadatos (`""` = rreading-glasses, `"openlibrary"` = Open Library, o URL propia) |
| `GoodreadsToken` | `""` | Token de API para enriquecimiento de series/listas de Goodreads |
| `WriteBookTags` | varía | Si escribir etiquetas de metadatos en los archivos |
| `UpdateCovers` | `true` | Actualización automática de portadas |

---

## Flujo de Datos: Añadir un Autor

```
Usuario busca → API → BookInfoProxy.SearchForNewAuthor()
                         ↓
              [MetadataSource == "openlibrary"]?
              SÍ → OpenLibraryProxy.SearchForNewAuthor()
              NO → GoodreadsSearchProxy.Search() → API BookInfo
                         ↓
              Resultados mapeados a modelos Author/Book/Edition
                         ↓
              Usuario confirma → Author guardado en SQLite
                         ↓
              RefreshAuthorService se activa
                         ↓
              Descarga portadas, obtiene lista completa de libros
                         ↓
              Monitorización RSS comienza
```

---

*Última actualización: 2026-03-18*
