# ADR 0004: Authenticated artwork for series integrations

- Status: accepted for local trial
- Confidence: high for reuse of the existing image policy

SeerrNG series previews receive registered MediaCoverProxy paths from Chaptarr.
Those UI routes require a browser session, so integrations using an API key receive
401 responses. Add GET /api/v1/MediaCoverProxy/{hash}/{filename} with normal API
authorization. Accept only registered SHA-256 identifiers and raster filenames,
then reuse the existing mapper, private caching, upstream size/SSRF restrictions,
placeholder suppression and missing-image behavior. Do not accept remote URLs or
change UI authentication. No database migration or acquisition action is involved.

Validate bytes, missing/placeholder handling and invalid paths in tests, and verify
authenticated image decoding plus rejection of unauthenticated requests live.
