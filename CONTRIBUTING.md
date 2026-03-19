# How to Contribute / Cómo Contribuir

## English

We welcome contributions! This is an active community fork of Readarr.

### Quick Links

| Document | Description |
|----------|-------------|
| [Development Setup](docs/en/development-setup.md) | Build and run locally |
| [Architecture Overview](docs/en/architecture.md) | How the system works |
| [Metadata Sources](docs/en/metadata-sources.md) | Metadata provider details |
| [Changelog](docs/en/changelog.md) | What has changed |

### Ways to Contribute

1. **Bug Reports** — Open a [GitHub Issue](https://github.com/Readarr/Readarr/issues) with reproduction steps
2. **Pull Requests** — Fork, make changes, open a PR
3. **Documentation** — Improve or translate docs in `/docs/en/` or `/docs/es/`

### Development Workflow

```bash
git clone https://github.com/YOUR_USERNAME/Readarr.git && cd Readarr
yarn install && dotnet restore src/Readarr.sln
git checkout -b feature/your-feature
# ... make changes ...
dotnet test src/Readarr.sln && yarn lint
git commit -m "feat: your change" && git push
# Open PR on GitHub
```

---

## Español

¡Damos la bienvenida a las contribuciones! Este es un fork comunitario activo de Readarr.

### Enlaces Rápidos

| Documento | Descripción |
|-----------|-------------|
| [Configuración de Desarrollo](docs/es/configuracion-desarrollo.md) | Compilar y ejecutar localmente |
| [Arquitectura](docs/es/arquitectura.md) | Cómo funciona el sistema |
| [Fuentes de Metadatos](docs/es/fuentes-de-metadatos.md) | Detalles de proveedores |
| [Registro de Cambios](docs/es/registro-de-cambios.md) | Qué ha cambiado |

### Formas de Contribuir

1. **Bugs** — Abre un [Issue en GitHub](https://github.com/Readarr/Readarr/issues) con pasos de reproducción
2. **Pull Requests** — Haz fork, realiza cambios, abre un PR
3. **Documentación** — Mejora o traduce documentación en `/docs/en/` o `/docs/es/`

---

*Original Servarr wiki: [wiki.servarr.com/readarr/contributing](https://wiki.servarr.com/readarr/contributing)*
