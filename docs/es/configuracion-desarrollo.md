# Readarr — Configuración del Entorno de Desarrollo

## Requisitos Previos

| Herramienta | Versión | Verificación |
|-------------|---------|--------------|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js | 18+ | `node --version` |
| Yarn | 1.22+ | `yarn --version` |
| Git | Cualquiera | `git --version` |

---

## Clonar y Configurar

```bash
git clone https://github.com/Readarr/Readarr.git
cd Readarr
```

### Instalar dependencias del frontend

```bash
yarn install
```

### Restaurar paquetes del backend

```bash
dotnet restore src/Readarr.sln
```

---

## Ejecutar en Desarrollo

### Backend (servidor API)

```bash
dotnet run --project src/NzbDrone.Console/Readarr.Console.csproj
```

La API estará disponible en `http://localhost:8787`.

### Frontend (recarga en caliente)

```bash
yarn start
```

El servidor webpack dev proxy redirige las llamadas API a `:8787`. Accede a la UI en `http://localhost:3000`.

---

## Compilar para Producción

```bash
# Compilación completa de producción
yarn build
dotnet build src/Readarr.sln -c Release
```

El output se genera en `_output/`.

---

## Tests

### Tests unitarios del backend

```bash
dotnet test src/Readarr.sln
```

### Tests de un proyecto específico

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj
```

### Linting del frontend

```bash
yarn lint          # Comprobar
yarn lint-fix      # Auto-corregir
yarn stylelint     # Linting de CSS
```

---

## Configuración de Metadatos (Desarrollo)

Para desarrollo, la fuente de metadatos usa por defecto `https://api.bookinfo.pro/v1/`, una instancia comunitaria pública de rreading-glasses.

Para usar Open Library durante el desarrollo:

1. Arranca Readarr
2. Ve a **Ajustes → General**
3. Establece **Fuente de Metadatos** a `openlibrary`

---

## Estructura del Proyecto

```
Readarr/
├── src/
│   ├── NzbDrone.Core/          # Lógica de negocio
│   │   ├── MetadataSource/     # Proveedores de metadatos
│   │   │   ├── BookInfo/       # Proxy rreading-glasses
│   │   │   ├── OpenLibrary/    # Proxy Open Library
│   │   │   └── Goodreads/      # Enriquecimiento Goodreads
│   │   ├── Configuration/      # Configuración de la app
│   │   └── Datastore/          # SQLite + migraciones
│   ├── NzbDrone.Common/        # Utilidades compartidas
│   ├── NzbDrone.Api/           # API REST
│   └── NzbDrone.Host/          # Bootstrap
├── frontend/
│   └── src/
│       ├── Author/             # Componentes de autor
│       ├── Book/               # Componentes de libro
│       ├── Components/         # Componentes compartidos
│       └── Styles/             # Temas y variables
└── docs/
    ├── en/                     # Documentación en inglés
    └── es/                     # Documentación en español
```

---

## Estilo de Código

### Backend (C#)

- Sigue los patrones existentes en el código base
- Usa `_camelCase` para campos privados
- Inyecta todas las dependencias vía constructor
- Añade guards para configuración opcional (comprueba `IsNullOrWhiteSpace()` antes de usar tokens)
- Sin API keys ni secretos hardcodeados — usa `IConfigService`

### Frontend (React/TypeScript)

- Prefiere TypeScript (`.tsx`) para nuevos componentes
- Usa CSS Modules (archivos `.css` junto a los componentes)
- Sigue el patrón Redux existente para la gestión de estado
- La configuración de ESLint se aplica — ejecuta `yarn lint-fix` antes de hacer commit

---

## Añadir un Nuevo Proveedor de Metadatos

1. Crea `src/NzbDrone.Core/MetadataSource/TuProveedor/`
2. Añade modelos de recursos en la subcarpeta `Resources/`
3. Crea `TuProveedorProxy.cs` con métodos públicos que coincidan con las firmas esperadas
4. Inyecta en el constructor de `BookInfoProxy`
5. Añade lógica de enrutamiento en `BookInfoProxy` (comprueba `_configService.MetadataSource`)
6. Registra en DryIoc (automático vía detección de interfaz para interfaces `IProvide*`)

---

*Última actualización: 2026-03-18*
