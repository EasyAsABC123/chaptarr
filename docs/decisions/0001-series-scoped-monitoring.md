# ADR 0001: Native series navigation with format-scoped monitoring

- Date: 2026-09-13
- Status: accepted for local implementation; proposed for upstream review
- Upstream baseline: `7a06004eee40e9861a553c743d0436c91fe0224d` (develop, v0.9.964)
- Confidence: high for tested membership/format boundaries; production acquisition impact unmeasured

## Problem and intended value

A reader viewing a local book needs a direct path to its series and a way to
monitor the desired members. A series has narrower scope than an author and can
have multiple authors. Chaptarr also has separate ebook/audiobook resources and
companion-format synchronization. A generic book toggle is therefore not a safe
substitute for explicit series-and-format intent.

The proposed value is a direct book → series → current-book monitoring flow that
cannot broaden intent to unrelated books or the companion format. Reproducible
examples compare persisted states and published book-edit events. These are
behavioral evidence, not claims about adoption or successful downloads.

## Source evidence

At the baseline, `SeriesController` exposes native series listing and detail;
`SeriesBookLink` provides local book identity, position, and membership. The
existing general `BookController` monitor endpoint calls `IBookService.SetMonitored`,
which can synchronize companion formats. `BookService.SetMonitoredForMediaType`
already provides an explicitly format-scoped mutation and applies author/root-folder prerequisites.
It intentionally does not synchronize companions and can skip disallowed changes.

`AddSeriesController` uses author-level monitoring options. Its historical
semantics are not changed by this contribution. `GoodreadsSeriesImportList` already
owns provider-based series discovery on the import-list lifecycle (minimum refresh
interval 12 hours in this baseline). `SeriesLookupController` uses the backend's
Hardcover configuration. These are different capabilities, not a single universal
series-watch API.

## Decision

1. Link only exact local membership and format, including all matching series.
   Use local IDs and normal URL-base-aware navigation. Keep unresolved labels.
2. Load native series detail, then bounded member reads. Never issue an empty
   book-ID list that could retrieve the whole library. Cancel stale view requests.
3. Add `PUT /series/{id}/monitor` with explicit selected local book IDs and desired
   state. Validate all membership and format before mutation. Derive format from
   the persisted series. Delegate to `SetMonitoredForMediaType`.
4. Reload and verify actual saved state. Report blocked members as 409 rather than
   falsely reporting success. Preserve existing author prerequisites instead of
   changing author policy to force success. Refresh UI after errors because bulk
   policy can differ by author and the operation can partially succeed.
5. Reuse book-edit events and normal downstream processing. Monitoring alone does
   not promise an immediate missing-book search or completed acquisition.
6. Describe the UI as monitoring current books. Future discovery continues through
   existing import lists. Do not create a second polling job or implicit author watch.

## Alternatives

- **Frontend calls general `/book/monitor`:** rejected after source inspection showed
  cross-format synchronization. Client-only filtering cannot enforce API scope.
- **Set author `all`/`future` options:** rejected; can acquire unrelated series or
  standalone books, and changing it from a series page hides the breadth of intent.
- **Mutate rows directly:** rejected; bypasses author gates, events, and the backend
  monitoring service. Reuse the existing mutation boundary instead.
- **Change the general endpoint:** rejected for compatibility. Other callers rely
  on companion synchronization; additive series semantics are easier to review.
- **Require Goodreads/Hardcover credentials in SeerrNG:** rejected; metadata provider
  configuration and acquisition belong to Chaptarr. This endpoint is provider agnostic.
- **Introduce a persisted future-series watch now:** deferred to a separate decision.
  It needs import-list/provider identity mapping, primary/extras policy, multi-author
  handling, format/profile/root-folder rules, disable/delete semantics, and discovery
  failure visibility. The existing import-list scheduler should own its timing.
  A one-click promise without these semantics would overstate what works.
- **Transactional all-or-nothing policy:** not adopted. The existing book service
  applies per-author gates and emits events. The endpoint prevalidates membership,
  but reports partial policy failures explicitly rather than introducing a second
  author-policy implementation or changing the core transaction model.

## Compatibility, races, and operational limits

The endpoint is additive; no database schema, provider package, or existing API
contract changes. Old Chaptarr installations do not expose it. Frontend and API
must be shipped together. Ebook and audiobook are tested independently; no promise
is made for unknown media types. The general monitor endpoint keeps its existing
cross-format behavior.

Membership can change during a concurrent refresh between validation and mutation;
there is no cross-service lock. The operation acts on the validated member snapshot
and verifies saved state. Concurrent user edits have the existing last-write-wins
behavior. A native book removed during the operation can result in a retryable
failure; the UI refreshes. Batches larger than 500 are sequential client operations,
not one transaction. No monitoring mutation adds absent catalog books.

No new credential is needed. Normal Chaptarr authentication still governs access;
a SeerrNG link does not grant backend permissions. Author/root-folder prerequisites
must already be valid. The endpoint never relaxes them. A row can be marked
monitored while its author is paused; existing acquisition eligibility still
applies, and a successful row change does not promise downloads. Later explicit
format-reconciliation operations retain their existing semantics.

## Upstream readiness and rollback

This is a local contribution package, not an accepted upstream feature. No PR,
push, deployment, or live library mutation was made. Reviewable source, tests,
build evidence, and this decision record are prepared first. Before proposing:
run a disposable backend demonstration using real library metadata, collect operator
feedback on the current-members scope, and confirm maintainers want the additive
endpoint. Keep future discovery as an explicit separate discussion.

Rollback is a code revert; no migration is needed. Monitoring changes already
made by an operator remain ordinary book state and are not automatically undone.

## Live-trial boundary: container format and playback compatibility

A real trial imported three xHE-AAC Expanse audiobooks that could not decode using
Audiobookshelf's installed FFmpeg. A known-working Dungeon Crawler Carl AAC-LC
file passed the same check, and an approved MP3 replacement for Cibola Burn passed
samples from every imported file. Download/import success therefore does not by
itself establish compatibility with a given playback/transcoding path.

Do not reject all M4B files or change series monitoring semantics in response.
M4B container labels do not establish the AAC profile or client capabilities.
Any future policy for decoder validation, release rejection or compatible retries
belongs with the provider's acquisition/import pipeline and must consider direct
play as well as server transcoding. Keep that proposal separate from current-member
series navigation and scoped monitoring. Preserve original files when investigating
compatibility and record bounded sample tests separately from full-file validation.
