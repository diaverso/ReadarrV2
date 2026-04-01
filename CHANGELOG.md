# Changelog

## [Unreleased]

### Added
- **Z-Library indexer**: Support for raw session cookies (`SessionCookies` field) for mirrors like z-lib.cv that use Laravel-based auth
- **Z-Library indexer**: Support for `remix_userid` / `remix_userkey` manual cookie fields for singlelogin.rs
- **Download Clients UI**: New "Other" section in Add Download Client modal to display HTTP-protocol clients (e.g. HTTP Download)

### Changed
- **Z-Library indexer**: Updated default API URL from `singlelogin.re` (seized by FBI May 2024) to `singlelogin.rs`
- **Z-Library indexer**: All fallback URLs updated from `singlelogin.re` to `singlelogin.rs`
- **Z-Library indexer**: Removed `ValidRootUrl()` validator on `BaseUrl` field — field is optional and falls back to default when omitted
- **Z-Library indexer**: Authentication now tries in order: (1) raw session cookies, (2) remix user ID+key, (3) email/password EAPI login

### Fixed
- **Z-Library indexer**: 400 Bad Request on Save/Test when `BaseUrl` was not explicitly set (Advanced field not submitted)
- **Z-Library indexer**: `NullReferenceException` on `BaseUrl.TrimEnd('/')` when field was null — replaced with null-safe `?.TrimEnd('/') ?? "https://singlelogin.rs"` in all usages
- **Z-Library indexer**: Download URL and info URL construction now null-safe

---

## Previous Changes

### Z-Library & Anna's Archive indexers
- Added Z-Library indexer with EAPI support (`/eapi/book/search`)
- Added Anna's Archive indexer
- Added HTTP Download client (`HttpBlackhole`) for direct file downloads
- Z-Library parser handles both JSON array and `{books: [...]}` / `{data: [...]}` response shapes
- HTTP Download client resolves Z-Library JSON responses (`download_url` field) and Anna's Archive HTML pages (📚 download link)
- Z-Library download URLs resolved via `/eapi/book/{id}/{hash}` endpoint

### Infrastructure
- Added Dockerfile and docker-compose for containerized deployment
- First-run authentication modal: Save button disabled until auth method is selected
- Direct Download indexers shown in separate section in Add Indexer modal
