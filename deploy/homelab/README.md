# Durable book request repair

The image overlays only the tested Core and HTTP assemblies on the pinned official 0.9.965 image. Its source baseline is e67374f4b3fed01bbf08f18672add944062fcb49 and retains the deployed targeted single-work behavior. A book request must not import the author's entire catalog.

Cache migration 129 removes older duplicate HTTP responses by LastRefresh/Id and enforces one row per exact URL. Cache storage uses an atomic URL upsert, including concurrent cold misses. Existing metadata and no-store cache semantics remain intact.

Metadata HTTP 202 is preparation, not a missing work. The API exposes HTTP 503 with code `metadata_pending`, bounded `retryAfterSeconds`, and Retry-After. Callers retain the original approved request and retry later; this does not manufacture metadata or editions. A real 404 remains missing.

The Homelab Book Repair workflow runs the full Release test project with SQLite and an isolated PostgreSQL service before publishing an immutable GHCR digest. Existing dependency audit warnings in this preserved source baseline remain visible but are not converted into compilation errors.

Before rollout, create consistent backups of both chaptarr.db and cache.db. Deploy the reported immutable digest and remove the old single-work-core init container, its volume, and the two Core DLL/PDB mounts; otherwise the old volume would replace the repaired image assemblies. Preserve the independent both-formats UI extension and all service/storage configuration. Read back /app/homelab-build.json, verify cache migration/index persistence, and exercise pending metadata without creating fake library entries.
