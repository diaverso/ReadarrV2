# Readarr — System Architecture

## Overview

Readarr is a book and audiobook collection manager built on the **Servarr** platform stack. The application is composed of a .NET backend and a React frontend that communicate via a REST API.

---

## Backend Architecture (.NET)

### Projects

| Project | Purpose |
|---------|---------|
| `NzbDrone.Core` | Business logic, services, database, metadata |
| `NzbDrone.Common` | Shared utilities, HTTP client, serialization |
| `NzbDrone.Api` | REST API controllers (Nancy/ASP.NET) |
| `NzbDrone.Host` | Application bootstrap, DI container (DryIoc) |
| `NzbDrone.Console` | Console entry point |

### Dependency Injection

Readarr uses **DryIoc** for IoC. All services implementing a registered interface are auto-registered. When multiple implementations of the same interface exist, the last registered wins unless explicitly controlled.

### Database

SQLite via **NzbDrone.Core.Datastore** (custom ORM layer built on Dapper). Migrations are in `src/NzbDrone.Core/Datastore/Migration/` and are numbered sequentially.

---

## Metadata Source Architecture

The metadata subsystem uses a three-level routing strategy:

```
IConfigService.MetadataSource
  ├── "" (empty/default)  → BookInfoProxy → rreading-glasses (api.bookinfo.pro)
  ├── "openlibrary"       → OpenLibraryProxy → openlibrary.org
  └── custom URL          → BookInfoProxy with that URL (self-hosted rreading-glasses)
```

### Key Interfaces

| Interface | Description |
|-----------|-------------|
| `IProvideAuthorInfo` | Fetch full author details by ID |
| `IProvideBookInfo` | Fetch book/work details by ID |
| `ISearchForNewBook` | Search for books (title, ISBN, ASIN, edition ID) |
| `ISearchForNewAuthor` | Search for authors by name |
| `ISearchForNewEntity` | Combined entity search |

### Metadata Providers

#### BookInfoProxy (`src/NzbDrone.Core/MetadataSource/BookInfo/`)
- **Primary provider** for all metadata operations
- Routes calls to `OpenLibraryProxy` when `MetadataSource == "openlibrary"`
- Otherwise calls **rreading-glasses** (`https://api.bookinfo.pro/v1/`)
- rreading-glasses is a community-hosted drop-in replacement for the original BookInfo API that proxies Goodreads data

#### OpenLibraryProxy (`src/NzbDrone.Core/MetadataSource/OpenLibrary/`)
- **Secondary provider** activated via configuration
- Calls the public Open Library REST API (`https://openlibrary.org`)
- No authentication required
- Returns `null` for `GetChangedAuthors()` (no incremental change feed available)

#### GoodreadsProxy (`src/NzbDrone.Core/MetadataSource/Goodreads/`)
- Used for series and list metadata enrichment
- Requires `GoodreadsToken` configured in Readarr Settings → General → Metadata
- Skips gracefully when token is not configured

---

## Frontend Architecture (React)

### Stack

| Technology | Version | Role |
|-----------|---------|------|
| React | 18.2 | UI framework |
| TypeScript | 5.x | Type safety (migration ongoing) |
| Redux | 4.x | State management |
| Webpack | 5.x | Bundler |
| CSS Modules | — | Component-scoped styles |

### Directory Structure

```
frontend/src/
├── Author/          # Author detail, index, cards, poster
├── Book/            # Book detail, index, editor
├── Components/      # Shared UI components (modals, buttons, labels)
├── Settings/        # All settings pages
├── Styles/          # CSS variables, global styles, theme definitions
│   ├── Themes/      # dark.js, light.js
│   └── Variables/   # dimensions, fonts, animations, z-indexes
├── Store/           # Redux store, actions, reducers
└── Utilities/       # Helper functions
```

### Theming

Themes are defined in JavaScript and injected as CSS custom properties:
- `frontend/src/Styles/Themes/dark.js` — Dark theme (default)
- `frontend/src/Styles/Themes/light.js` — Light theme
- `frontend/src/Styles/Themes/index.js` — Theme switcher

---

## API Layer

The REST API follows the Servarr convention:

```
GET    /api/v1/author         → List authors
GET    /api/v1/author/{id}    → Author detail
POST   /api/v1/author         → Add author
PUT    /api/v1/author/{id}    → Update author
DELETE /api/v1/author/{id}    → Delete author
GET    /api/v1/book           → List books
GET    /api/v1/search?term=   → Search metadata
```

API documentation: See `src/NzbDrone.Api/` for controllers.

---

## Configuration

Key configuration properties managed by `IConfigService` / `ConfigService`:

| Property | Default | Description |
|----------|---------|-------------|
| `MetadataSource` | `""` | Metadata provider (`""` = rreading-glasses, `"openlibrary"` = Open Library, or custom URL) |
| `GoodreadsToken` | `""` | API token for Goodreads series/list enrichment |
| `WriteBookTags` | varies | Whether to write metadata tags to files |
| `UpdateCovers` | `true` | Auto-update cover images |

---

## Data Flow: Adding an Author

```
User searches → API → BookInfoProxy.SearchForNewAuthor()
                         ↓
              [MetadataSource == "openlibrary"]?
              YES → OpenLibraryProxy.SearchForNewAuthor()
              NO  → GoodreadsSearchProxy.Search() → BookInfo API
                         ↓
              Results mapped to Author/Book/Edition models
                         ↓
              User confirms → Author saved to SQLite
                         ↓
              RefreshAuthorService triggers
                         ↓
              Downloads covers, fetches full book list
                         ↓
              RSS monitoring starts
```

---

## Build System

```bash
# Backend
dotnet build src/Readarr.sln

# Frontend
yarn install
yarn build          # Production
yarn start          # Development (watch)

# Tests
dotnet test src/Readarr.sln
yarn test
```

---

*Last updated: 2026-03-18*
