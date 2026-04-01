# Readarr Community Fork — Changelog

This document tracks changes made in the community-maintained fork after the original Servarr team retired the project.

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
