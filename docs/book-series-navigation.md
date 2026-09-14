# Series navigation and current-book monitoring

A book's linked local series and position now open `/series/<local database ID>`
under Chaptarr's URL base. Multiple memberships link separately. Unresolved series
labels remain text; titles are never used to guess a database ID. Ebook and
audiobook memberships stay separate.

The series page supports direct navigation and refresh, shows current local books
in series order, and offers individual and bulk monitoring. Opening a page is
read-only. Monitoring uses a new native endpoint that validates series membership
and uses Chaptarr's existing format-scoped book-monitoring service. It never
changes author monitoring policy or synchronizes companion-format books.

Existing author monitoring prerequisites still apply. If Chaptarr refuses a
change, the page reloads saved state and displays the error. A bulk operation may
partially succeed when author policy differs between members; it is not a database
transaction across all authors. The response identifies books whose requested
state was not saved. Refresh before retrying. Repeating the same desired state is
idempotent.

## Native API

`PUT /api/v1/series/{seriesId}/monitor`

```json
{ "bookIds": [11, 12], "monitored": true }
```

- `bookIds`: 1–500 positive local book IDs; duplicates are collapsed.
- `monitored`: required boolean, including explicit `false` for unmonitoring.
- Format is derived from the local series; the client cannot override it.
- 200: `{ "bookIds": [11, 12], "monitored": true, "mediaType": "ebook" }`.
- 400: invalid input or any ID outside this series; nothing is written.
- 404: no such local series.
- 409: missing/cross-format membership before writing, or requested states not
  saved under existing policy after writing. In the latter case `bookIds` contains
  the blocked IDs, and other selected books may already have changed.

Uses normal authenticated v1 API routing. No credentials, migrations, additional
scheduler, metadata provider, or dependency are added. Existing book-monitor and
series-add endpoints keep their behavior.

This controls **current members**, not a persistent watch subscription. Future
series discovery remains with existing backend import lists and their refresh
schedule. Configuring a supported series import list can discover future entries;
this page does not create one or enable all future books by an author.

## Validation

```sh
node frontend/test/series-navigation.test.cjs
./node_modules/.bin/eslint --config frontend/.eslintrc.js frontend/src/Book/Details/BookSeriesLinks.js frontend/src/Book/Details/BookDetailsHeader.js frontend/src/Series/Details/*.js
./node_modules/.bin/stylelint frontend/src/Series/Details/SeriesDetails.css --config frontend/.stylelintrc
./node_modules/.bin/webpack --env production --config frontend/build/webpack.config.js
dotnet test src/Chaptarr.Core.Test/Chaptarr.Core.Test.csproj -c Release
```

See [the decision record](decisions/0001-series-scoped-monitoring.md) and
[recorded evidence](series-monitoring-evidence.md), including the baseline NuGet
advisory and exact invocation used locally.

Catalog request managers may call
`GET /api/v1/series/lookup?foreignSeriesId=hc:123&includeBookIdentifiers=true`.
Returned books then include edition `isbns` and `identifiersComplete`. Only complete
identifier evidence should enable exact work reconciliation. This is optional;
default catalog responses and acquisition behavior are unchanged. See ADR 0002.
