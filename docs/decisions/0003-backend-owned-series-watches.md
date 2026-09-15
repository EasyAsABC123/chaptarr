# ADR 0003: Persistent series watches through the import-list lifecycle

- Date: 2026-09-15
- Status: local implementation, proposed for upstream review
- Confidence: high for fixture-tested contracts; live acquisition unverified

The current-series monitor endpoint changes existing local book flags only.
SeerrNG needs a persistent request for a provider series and format, including
books discovered after the request. Preserve that existing endpoint and add a
versioned series/watch capability, read and idempotent write API.

Use a Series Watch import-list provider rather than a second scheduler or an
author-wide future policy. The list stores an exact hc:/gr: identity, exactly one
format, destination/profile settings and primary/extras policy. The first sync is
queued when enabled; normal list timing (12-hour minimum refresh), failure backoff,
exclusions, identity resolution, pending imports and acquisition own the rest.
An already-enabled watch is reused on retries. The API rejects an implicit change
to its extras policy. Ebook and audiobook watches are independent.

Hardcover catalog fetches use Chaptarr's configured token, featured members by
default, explicit pagination and author identities. No credential is added to
SeerrNG. Goodreads reuses IProvideSeriesInfo and requires including extras. Never
cross-map provider series by title. Reject incomplete/failed catalog pages rather
than returning a misleading partial success. A bounded catalog exceeding 5000
rows fails visibly through normal import-list failure handling.

Reuse the existing dual-format import-list settings contract despite its historical
Goodreads name; it already accepts namespaced Hardcover book/author identities.
A broader interface rename is out of scope. Restrict the new provider's existing
book update path to its selected media type: old generic list behavior can update
companion rows, which is incompatible with this watch's scope. Keep unrelated
provider behavior unchanged. New authors use SpecificBook selection and no
future-author policy; existing author policies retain their normal semantics.

Stopping disables future list discovery. Previously monitored/downloaded books
and work already admitted by the scheduler are retained. Watches remain manageable
through Chaptarr import-list settings even when SeerrNG is unavailable. Disabling
or deleting SeerrNG is not a backend unmonitor operation. An enabled watch confirms
saved configuration, not successful catalog refresh, import, or playback.

Tests exercise idempotent creation/resume, separate formats, stop, invalid identity,
failed validation, conflicting extras, real catalog parsing/pagination and later
catalog additions. An ImportListSyncService fixture verifies later eligible books
are searched while companion and unrelated books retain their previous flags.
Upstream publication and live deployment remain separate from this local evidence.
Before reverting Chaptarr, disable/remove these new provider definitions; existing
import-list persistence cannot be interpreted by code without the provider class.
