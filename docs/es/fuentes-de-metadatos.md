# Readarr — Guía de Fuentes de Metadatos

## Estado Actual

El servidor de metadatos original de Readarr (BookInfo / `api.bookinfo.club`) fue dado de baja. Este fork ha restaurado la funcionalidad completa de metadatos usando las siguientes fuentes:

| Fuente | Endpoint | Estado | Auth |
|--------|----------|--------|------|
| **rreading-glasses** (defecto) | `https://api.bookinfo.pro/v1/` | ✅ Activo | Sin auth |
| **Open Library** | `https://openlibrary.org` | ✅ Activo | Sin auth |
| **Goodreads** (enriquecimiento) | `https://www.goodreads.com` | ⚠️ Requiere token | API Token |

---

## Proveedor por Defecto: rreading-glasses

[rreading-glasses](https://github.com/blampe/rreading-glasses) es un servidor mantenido por la comunidad que expone la misma API BookInfo para la que Readarr fue construido originalmente, pero respaldado por datos de Goodreads.

**Sin configuración necesaria** — es el proveedor predeterminado y funciona de inmediato.

### Cambiar a una Instancia Self-Hosted

Si quieres usar tu propia instancia de rreading-glasses:

1. Ve a **Ajustes → General → Fuente de Metadatos**
2. Introduce la URL de tu instancia (ej: `https://mi-bookinfo.ejemplo.com/v1/{route}`)

### Backend Hardcover (vía rreading-glasses)

La comunidad también mantiene una instancia de rreading-glasses respaldada por Hardcover:
- URL: `https://hardcover.bookinfo.pro/v1/{route}`
- Introdúcela en **Ajustes → General → Fuente de Metadatos**

---

## Proveedor Secundario: Open Library

[Open Library](https://openlibrary.org) es una base de datos de metadatos gratuita y abierta mantenida por Internet Archive. Contiene millones de libros.

### Activar Open Library

1. Ve a **Ajustes → General → Fuente de Metadatos**
2. Escribe: `openlibrary`
3. Guarda

Cuando está configurado como `openlibrary`, todas las búsquedas de metadatos omiten rreading-glasses y usan directamente la API de Open Library.

### Limitaciones vs rreading-glasses

| Característica | rreading-glasses | Open Library |
|----------------|-----------------|--------------|
| Perfiles de autor | ✅ Completos | ✅ Buenos |
| Portadas de libros | ✅ Calidad Goodreads | ✅ Disponibles |
| Datos de series | ✅ Completos | ⚠️ Mínimos |
| Valoraciones | ✅ Valoraciones Goodreads | ⚠️ Limitadas |
| Búsqueda por ASIN | ✅ | ❌ |
| Búsqueda por ISBN | ✅ | ✅ |
| Feed de cambios | ✅ Actualización incremental | ❌ Solo actualización completa |

### IDs de Open Library

Open Library usa IDs de texto en lugar de enteros de Goodreads:
- Autores: `OL23919A`
- Works: `OL45804W`
- Ediciones: `OL7353617M`

Puedes buscar usando prefijos en la barra de búsqueda de Readarr:
- `author:OL23919A` — obtener autor por ID OL
- `work:OL45804W` — obtener libro/work por ID OL
- `edition:OL7353617M` — obtener edición específica

---

## Enriquecimiento: Goodreads

Goodreads proporciona agrupaciones de series y listas de lectura.

### Configurar un Token de Goodreads

1. Crea una cuenta en [Goodreads](https://www.goodreads.com)
2. Ve a **Ajustes → General → Metadatos**
3. Introduce tu API key de Goodreads en el campo **Token de Goodreads**

Si no hay token configurado, Readarr registra un mensaje de debug y omite el enriquecimiento de Goodreads de forma elegante — sin errores.

---

## Solución de Problemas

### "Sin resultados" al buscar

1. Comprueba **Ajustes → General → Fuente de Metadatos** — debe estar vacío (para rreading-glasses) o `openlibrary`
2. Prueba un término de búsqueda diferente
3. Revisa los logs de Readarr para errores HTTP

### El autor/libro no se actualiza

Para fuentes de Open Library: Readarr no puede detectar cambios incrementales. Activa una actualización manual desde la página del autor.

Para rreading-glasses: Verifica que `api.bookinfo.pro` es accesible desde tu servidor.

### Usar un mirror de metadatos local

Configura **Fuente de Metadatos** con tu URL local, ej:
```
http://192.168.1.100:8080/v1/{route}
```

---

*Última actualización: 2026-03-18*
