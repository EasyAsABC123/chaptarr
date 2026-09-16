# ADR 0005: Preserve shared series membership during author refresh

- Status: accepted for local trial
- Confidence: high; reproduced in both media types before the fix

A series is shared across authors, but author metadata supplies only that author's
members. RefreshSeriesService reconciled that partial set against all persisted
series links. A refresh of George R.R. Martin therefore replaced Patrick Rothfuss's
Kingkiller books with the anthology Rogues. The defect also affected deletion of a
series absent from one author's metadata. SeerrNG correctly reflected the damaged
backend data; hiding a title in the carousel would conceal the underlying defect.

Limit reconciliation to book IDs owned by the author being refreshed. Pass a
per-call predicate to the existing child reconciliation pipeline, retaining its
normal behavior for all other callers. When a series disappears from an author's
metadata, remove only that author's links and delete the series row only if no
links remain. Keep format-scoped provider identity and narrator-variant behavior.
No mutable author context is stored on a shared service instance.

Regression tests reproduce author A -> author B -> author A refreshes, pruning
obsolete same-author links while preserving other authors, and removal of an
unmatched shared series. Both ebook and audiobook cases failed before the fix and
pass afterward. The full core suite passes 3,063 tests. Existing damaged links
require metadata refresh after deployment; the fix does not silently rewrite
library data on startup. Related anthologies remain backend metadata entries.
