# Readarr — Development Setup

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js | 18+ | `node --version` |
| Yarn | 1.22+ | `yarn --version` |
| Git | Any | — |

---

## Clone and Setup

```bash
git clone https://github.com/Readarr/Readarr.git
cd Readarr
```

### Install frontend dependencies

```bash
yarn install
```

### Restore backend packages

```bash
dotnet restore src/Readarr.sln
```

---

## Running in Development

### Backend (API server)

```bash
dotnet run --project src/NzbDrone.Console/Readarr.Console.csproj
```

The API will be available at `http://localhost:8787`.

### Frontend (hot reload)

```bash
yarn start
```

The webpack dev server proxies API calls to `:8787`. Access the UI at `http://localhost:3000`.

---

## Building for Production

```bash
# Full production build
yarn build
dotnet build src/Readarr.sln -c Release
```

Output is in `_output/`.

---

## Testing

### Backend unit tests

```bash
dotnet test src/Readarr.sln
```

### Backend tests for a specific project

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj
```

### Frontend linting

```bash
yarn lint          # Check
yarn lint-fix      # Auto-fix
yarn stylelint     # CSS linting
```

---

## Metadata Configuration (Development)

For development, the metadata source defaults to `https://api.bookinfo.pro/v1/`. This is a public community-hosted rreading-glasses instance.

To use Open Library locally during development:

1. Start Readarr
2. Go to **Settings → General**
3. Set **Metadata Source** to `openlibrary`

---

## Project Structure

```
Readarr/
├── src/
│   ├── NzbDrone.Core/          # Business logic
│   │   ├── MetadataSource/     # Metadata providers
│   │   │   ├── BookInfo/       # rreading-glasses proxy
│   │   │   ├── OpenLibrary/    # Open Library proxy
│   │   │   └── Goodreads/      # Goodreads enrichment
│   │   ├── Configuration/      # App configuration
│   │   └── Datastore/          # SQLite + migrations
│   ├── NzbDrone.Common/        # Shared utilities
│   ├── NzbDrone.Api/           # REST API
│   └── NzbDrone.Host/          # Bootstrap
├── frontend/
│   └── src/
│       ├── Author/             # Author components
│       ├── Book/               # Book components
│       ├── Components/         # Shared components
│       └── Styles/             # Themes and variables
└── docs/
    ├── en/                     # English documentation
    └── es/                     # Spanish documentation
```

---

## Code Style

### Backend (C#)

- Follow existing patterns in the codebase
- Use `_camelCase` for private fields
- Inject all dependencies via constructor
- Add guards for optional configuration (check `IsNullOrWhiteSpace()` before using tokens)
- No hardcoded API keys or secrets — use `IConfigService`

### Frontend (React/TypeScript)

- Prefer TypeScript (`.tsx`) for new components
- Use CSS Modules (`.css` files alongside components)
- Follow the existing Redux pattern for state management
- ESLint config enforced — run `yarn lint-fix` before committing

---

## Adding a New Metadata Provider

1. Create `src/NzbDrone.Core/MetadataSource/YourProvider/`
2. Add resource models in `Resources/` subfolder
3. Create `YourProviderProxy.cs` with public methods matching the expected signatures
4. Inject into `BookInfoProxy` constructor
5. Add routing logic in `BookInfoProxy` (check `_configService.MetadataSource`)
6. Register in DryIoc (automatic via interface detection for `IProvide*` interfaces)

---

*Last updated: 2026-03-18*
