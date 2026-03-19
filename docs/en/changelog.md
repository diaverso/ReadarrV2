# Readarr Community Fork — Changelog

This document tracks changes made in the community-maintained fork after the original Servarr team retired the project.

---

## [Unreleased] — 2026-03-18

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
