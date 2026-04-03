# Readarr Community Fork — Changelog

This document tracks changes made in the community-maintained fork after the original Servarr team retired the project.

---

## [Unreleased] — 2026-04-03 (latest)

### Added

#### Metadata — Language-Preferred Edition Selection
- When the UI language is set to a non-English language (e.g. Spanish), `BookInfoProxy` now selects the edition in that language as the **monitored edition** instead of the most popular one
- The book title displayed in the author's book list is updated to the preferred-language edition's title when a match exists (e.g. "El pájaro y el corazón de piedra" instead of the English work title)
- Language matching is case-insensitive and checks the edition's language field against the ISO 639-1 two-letter code, ISO 639-2 three-letter code, and English name (e.g. "es", "spa", "Spanish")
- Falls back to the most popular edition (previous behavior) when no edition in the preferred language is found

#### Metadata — OpenLibrary Automatic Fallback
- `BookInfoProxy` now automatically falls back to **Open Library** when BookInfo (GoodReads) returns an unexpected error for an author or book refresh
- Fallback for `GetAuthorInfo`: looks up the author's name from the local database and searches Open Library by name
- Fallback for `GetBookInfo`: looks up the book title and author name from the local database and searches Open Library by title + author
- `AuthorNotFoundException` and `BookNotFoundException` (404 responses) are not retried — they are propagated immediately
- OpenLibrary continues to be available as the primary source by setting `MetadataSource = "openlibrary"` in Settings

#### Notifications — Google Play Books
- Switched upload backend from the deprecated Books API `useruploadedbooks` endpoint (returns 404) to the **Google Drive API** (`/upload/drive/v3/files?uploadType=multipart`)
- Books uploaded to Google Drive in the user's account automatically appear in Google Play Books
- Pre-upload connectivity probe against `GET /drive/v3/about?fields=user` — returns a clear error message if the Drive API is not enabled or the token lacks the required scope
- Required OAuth2 scope changed from `https://www.googleapis.com/auth/books` to `https://www.googleapis.com/auth/drive.file`
- `get_google_refresh_token.py` helper script updated to request `drive.file` scope

#### Download Client — HTTP Blackhole (FlareSolverr)
- New optional **FlareSolverr URL** field in the HTTP Download client settings
- When configured, FlareSolverr is tried first to bypass DDoS-Guard / Cloudflare challenges; falls back to the built-in PoW solver if FlareSolverr is unavailable or returns an error

### Fixed

#### Download Client — HTTP Blackhole
- **UTF-8 filenames**: Content-Disposition header parser now correctly handles RFC 5987 `filename*=charset'language'value` encoding — fixes garbled filenames like `pÃ¡jaro.epub` → `pájaro.epub`
- **Multi-hop URL resolution**: Replaced the single-pass content-type check with a loop (up to 4 hops) that re-evaluates `Content-Type` on every response — fixes Z-Library's JSON → HTML → binary chain
- **Z-Library `.bin` downloads**: The `/dl/` web endpoint requires cookie-based auth (`remix_userid` / `remix_userkey`); auth headers now also set the `Cookie:` header — fixes HTML login page being saved as `.bin`

---

## [Unreleased] — 2026-04-03

### Added

#### Google Play Books Connection
- New **Google Play Books** notification/connection: automatically uploads imported EPUB and PDF files to the user's personal Google Play Books library
- Authentication via OAuth2 (Client ID + Client Secret + Refresh Token from Google Cloud Console with Books API enabled)
- Skips unsupported formats (MOBI, AZW3, etc.) with a debug log — only EPUB and PDF are accepted by Google Play Books
- Upload errors are logged per-file and do not block other files from uploading

#### Anna's Archive Indexer
- New indexer for Anna's Archive — scrapes the search page HTML and extracts results using the site's CSS selectors (`div.flex.pt-3.pb-3.border-b`, `a.js-vim-focus`, `div.text-gray-800.font-semibold.text-sm`)
- Parses format, file size, and language from the metadata bar (separated by `·`)
- MD5-based deduplication to avoid duplicate results
- Fallback to simple `/md5/` link scan if block-based parsing yields no results
- **API Key** field (Advanced): when a member API key is provided, uses `/dyn/api/fast_download.json` to resolve direct download URLs instead of the DDoS-protected slow download page
- Default URL set to `https://annas-archive.gd` — working mirrors: `annas-archive.gd`, `annas-archive.org`, `annas-archive.se`, `annas-archive.li`

#### Docker / Deployment
- Dockerfile rewritten as a 3-stage multi-stage build: Node.js frontend build → .NET backend build → runtime image. A fresh `docker compose up` now builds everything from source with no manual pre-steps required
- `entrypoint.sh`: on first start, waits for Readarr to be ready and automatically creates "Download z-Library" and "Download Annas Archive" HTTP Download clients pointing to `/downloads`
- `docker-compose.yml`: added `/downloads:/downloads` volume mount
- `tsconfig.json` added to the Docker build context (required by `ForkTsCheckerWebpackPlugin` during webpack build)

#### HTTP Download Client
- Default download folder changed to `/downloads`

### Fixed

#### Anna's Archive Indexer
- Removed `output=json` parameter from search requests (ignored by the site — always returns HTML)
- Changed `HttpAccept` from `Json` to `Html`
- Fixed `ParseSize` to handle sizes without a space between number and unit (e.g. `15.0MB`)
- Default URL corrected from the defunct `annas-archive.gl` to `annas-archive.gd`

---

## [Unreleased] — 2026-04-01

### Added

#### Z-Library Indexer
- **Session Cookies** field: paste raw browser session cookies directly (supports Laravel-based mirrors like z-lib.cv: `z_lib_session=...; zl_logged_in=1`)
- **Remix User ID** and **Remix User Key** fields (Advanced): manual cookie auth for singlelogin.rs without relying on programmatic login
- Authentication now attempted in order: (1) raw session cookies → (2) remix_userid + remix_userkey → (3) EAPI email/password login

#### UI — Download Clients
- New **Other** section in the Add Download Client modal to display clients with `unknown` protocol (e.g. HTTP Download)

### Fixed

#### Z-Library Indexer
- **[CRITICAL]** `400 Bad Request` on Save/Test when `BaseUrl` was not explicitly set (Advanced field not submitted by the frontend) — removed `ValidRootUrl()` validator that rejected null values
- `NullReferenceException` on `BaseUrl.TrimEnd('/')` when field was null — replaced with null-safe `?.TrimEnd('/') ?? "https://singlelogin.rs"` in all usages (RequestGenerator, Parser, main class)
- Default URL updated from `singlelogin.re` (domain seized by the FBI in May 2024) to `singlelogin.rs`

---

## [Unreleased] — 2026-03-28

### Fixed

#### Metadata Sources
- **[CRITICAL]** Restored metadata functionality by routing default requests to `api.bookinfo.pro` (rreading-glasses community instance), replacing the defunct `api.bookinfo.club` endpoint
- **[CRITICAL]** Removed hardcoded Goodreads API keys from `GoodreadsProxy.cs`. Token is now configured via Settings → General → Goodreads Token
- Goodreads proxy now gracefully skips with a debug log message when no token is configured (no crashes or errors)
- Renamed `SearchByGoodreadsBookId(int)` → `SearchByForeignEditionId(string)` to decouple the interface from Goodreads-specific integer IDs

#### Configuration
- Added `GoodreadsToken` property to `IConfigService` and `ConfigService`
- Added `MetadataSource` property to `IConfigService` (was already present but not fully wired)

#### Open Library Integration (New)
- Added `OpenLibraryProxy` — full implementation of `IProvideAuthorInfo`, `IProvideBookInfo`, `ISearchForNewBook`, `ISearchForNewAuthor`, `ISearchForNewEntity` backed by Open Library API
- Added resource models: `OpenLibraryAuthorResource`, `OpenLibraryWorkResource`, `OpenLibraryEditionResource`, `OpenLibrarySearchResource`
- Added `PolymorphicStringConverter` to handle Open Library's dual-format bio/description fields (plain string or typed object)
- `BookInfoProxy` now delegates to `OpenLibraryProxy` when `MetadataSource` is set to `"openlibrary"`
- Open Library author covers use `covers.openlibrary.org` API

### Improved (UI Modernization)

#### Visual Polish
- Sidebar active indicator now uses indigo primary color (`--primaryColor`) instead of red — cohesive with the app's indigo accent palette
- Sidebar navigation link padding increased to 11px for more comfortable vertical spacing
- Author and Book detail header backdrop changed from flat overlay to directional gradient (`rgba 25% → rgba 92%`) — cover art visible through the top portion
- Book detail page title updated to `font-weight: 600`, `letter-spacing: -0.02em`, and `text-wrap: balance` (matches author page)
- Mobile title sizing harmonized: Author and Book detail pages now use 28px / weight 500 on small screens
- Book index poster cards now share the same hover effect as author cards: `translateY(-2px)` lift, `box-shadow: 0 8px 24px rgba(0,0,0,0.3)`, and `backdrop-filter: blur(4px)` on action controls
- Table rows: `min-height: 42px` added; hover transition sped up from 500ms to 200ms ease for snappier feedback
- Table cell padding: `10px 8px` (up from `8px`) for more readable rows
- `font-feature-settings: 'kern' 1, 'liga' 1` added globally for better Inter ligatures and kerning

### Fixed (First-Run Authentication Modal)

- **[CRITICAL]** Fixed the first-run "Set Up Authentication" modal: clicking **Save** now correctly persists `AuthenticationMethod=Basic` to `config.xml`, creates the user in the database, and closes the modal. Previously the modal reappeared on every page load regardless of what was entered.
  - **Root cause (backend):** Kestrel disables synchronous I/O by default. `SaveHostConfig` attempted a synchronous `StreamReader.ReadToEnd()` on the request body, which threw `InvalidOperationException: Synchronous operations are disallowed` — the save never completed. Fixed by re-reading the body asynchronously (`ReadToEndAsync`) using the seekable stream provided by `BufferingMiddleware`, then parsing auth fields with `STJson`.
  - **Root cause (frontend):** `createSaveHandler` called `getSectionState(getState(), section)` without the `isFullStateTree` flag, reading from the wrong Redux state path and producing an empty object. Fixed by passing `true` as the third argument.
  - Added `ValidateResource` override in `HostConfigController` to fill in required non-auth fields (port, bind address, branch, backup intervals) from the current config when a partial PUT is received from the first-run modal — prevents FluentValidation from rejecting the request.
  - `SaveConfigDictionary` may skip fields whose values match the current config; added explicit `SetValue("AuthenticationMethod", ...)` and `SetValue("AuthenticationRequired", ...)` calls to guarantee the XML is always updated.

### Fixed (PR Backports)

- **[PR #4103]** Fixed oversized author images in the Add Author modal — images no longer overflow the modal container
- **[PR #4086]** Fixed literal `{` and `}` characters in file rename formats — use `{{` and `}}` to escape curly braces in naming templates
- **[PR #3996]** Enforced 255-byte filename limit for renamed files to prevent filesystem errors on Windows/Linux
- **[PR #3948]** Added `Object.groupBy` polyfill for Firefox ESR 115 compatibility — prevents calendar from failing to load on older browsers
- **[PR #3899]** Applied CSS `text-wrap: balance` to author detail page title for even multi-line text wrapping
- **[PR #4099]** Added KEPUB as a supported Calibre conversion output format (supported natively since Calibre 8.0)
- **[PR #4091]** Suppressed redundant `AuthorEditedEvent` during background author refresh cycles — reduces unnecessary UI reloads. `EnsureAuthorCovers`/`EnsureBookCovers` now return whether covers were actually downloaded; `MediaCoversUpdatedEvent` gained an `Updated` flag; `AuthorController` only broadcasts UI changes when covers changed

---

## [1.4.x] — Original Readarr (pre-retirement)

See the original [Readarr repository](https://github.com/Readarr/Readarr) and [release history](https://github.com/Readarr/Readarr/releases) for prior changelogs.

---

*This fork is maintained by the community. For support, see [docs/en/](.) or open a GitHub Issue.*
