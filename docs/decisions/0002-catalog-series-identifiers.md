# ADR 0002: Optional edition identifiers on series catalog lookups

- Date: 2026-09-14
- Status: accepted for local implementation; proposed for upstream review

A request manager must resolve catalog members without adding them. Live testing
found that native series lookup succeeded while individual hc-prefixed book
lookup returned HTTP 503. Routing users to the native add UI does not resolve
that metadata dependency.

Add `includeBookIdentifiers=true` to the existing series lookup API. The default
query and response remain unchanged. Opted-in primary and fallback queries read
ISBN-13/ISBN-10 from editions belonging to each exact returned book. They return
optional `isbns` and `identifiersComplete` fields on that book. Chaptarr keeps its
existing Hardcover configuration and client. No new account, scheduler, database
schema or acquisition behavior is introduced.

Fetch 201 ordered editions as a sentinel for the 200-edition evidence limit. Mark
truncated, absent or malformed evidence incomplete; complete empty evidence is a
valid no-identifiers outcome. The request manager validates ISBN checksums,
reconciles work identities and must not treat incomplete evidence as a verified
match. This extension does not assert that the series preview is a complete
bibliography or change the existing primary-member filtering.

The live catalog provider API is described in the [official Hardcover API](https://api.hardcover.app/).
The fields and response behavior are verified through the opt-in backend query,
not inferred from series names or provider book IDs.

Validation covers default response omission, opt-in query construction and
serialization, exact book edition scope, duplicate identifiers, empty/malformed
responses and sentinel truncation. Existing scoped monitoring tests remain in the
focused suite. Release publication uses the existing .NET 10 runtime; the existing
NU1902 baseline warning remains recorded. Rollback removes the optional extension;
old request managers and normal catalog browsing retain their contract.
