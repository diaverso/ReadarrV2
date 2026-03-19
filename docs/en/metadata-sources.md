# Readarr — Metadata Sources Guide

## Current Status

Readarr's original metadata server (BookInfo / `api.bookinfo.club`) was decommissioned. This fork has restored full metadata functionality using the following sources:

| Source | Endpoint | Status | Auth |
|--------|----------|--------|------|
| **rreading-glasses** (default) | `https://api.bookinfo.pro/v1/` | ✅ Active | None |
| **Open Library** | `https://openlibrary.org` | ✅ Active | None |
| **Goodreads** (enrichment) | `https://www.goodreads.com` | ⚠️ Token required | API Token |

---

## Default Provider: rreading-glasses

[rreading-glasses](https://github.com/blampe/rreading-glasses) is a community-maintained server that exposes the same BookInfo API that Readarr was originally built around, but backed by Goodreads data.

**No configuration needed** — this is the default and works out of the box.

### Switching to a Self-Hosted Instance

If you want to use your own rreading-glasses instance:

1. Go to **Settings → General → Metadata Source**
2. Enter your instance URL (e.g., `https://my-bookinfo.example.com/v1/{route}`)

### Hardcover Backend (via rreading-glasses)

The community also maintains a Hardcover-backed rreading-glasses instance:
- URL: `https://hardcover.bookinfo.pro/v1/{route}`
- Enter this in **Settings → General → Metadata Source**

---

## Secondary Provider: Open Library

[Open Library](https://openlibrary.org) is a free, open metadata database maintained by the Internet Archive. It contains millions of books.

### Enabling Open Library

1. Go to **Settings → General → Metadata Source**
2. Enter: `openlibrary`
3. Save

When set to `openlibrary`, all metadata fetches bypass rreading-glasses and use the Open Library API directly.

### Limitations vs rreading-glasses

| Feature | rreading-glasses | Open Library |
|---------|-----------------|--------------|
| Author profiles | ✅ Rich | ✅ Good |
| Book covers | ✅ Goodreads quality | ✅ Available |
| Series data | ✅ Full | ⚠️ Minimal |
| Ratings | ✅ Goodreads ratings | ⚠️ Limited |
| ASIN search | ✅ | ❌ |
| ISBN search | ✅ | ✅ |
| Change feed | ✅ Incremental refresh | ❌ Full refresh only |

### Open Library IDs

Open Library uses string IDs instead of Goodreads integers:
- Authors: `OL23919A`
- Works: `OL45804W`
- Editions: `OL7353617M`

You can search using prefixes in the Readarr search bar:
- `author:OL23919A` — fetch author by OL ID
- `work:OL45804W` — fetch book/work by OL ID
- `edition:OL7353617M` — fetch specific edition

---

## Enrichment: Goodreads

Goodreads provides series groupings and reading lists.

### Configuring a Goodreads Token

1. Create an account on [Goodreads](https://www.goodreads.com)
2. Go to **Settings → General → Metadata**
3. Enter your Goodreads API key in the **Goodreads Token** field

If no token is configured, Readarr logs a debug message and skips Goodreads enrichment gracefully — no errors occur.

---

## Troubleshooting

### "No results found" when searching

1. Check **Settings → General → Metadata Source** — should be empty (for rreading-glasses) or `openlibrary`
2. Try a different search term
3. Check Readarr logs for HTTP errors

### Author/book not updating

For Open Library sources: Readarr cannot detect incremental changes. Trigger a manual refresh from the author page.

For rreading-glasses: Ensure `api.bookinfo.pro` is reachable from your server.

### Using a local metadata mirror

Set **Metadata Source** to your local URL, e.g.:
```
http://192.168.1.100:8080/v1/{route}
```

---

*Last updated: 2026-03-18*
