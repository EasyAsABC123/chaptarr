# Series-monitoring contribution evidence

Recorded 2026-09-13 against upstream develop
`7a06004eee40e9861a553c743d0436c91fe0224d`. No PR, push, deployment, or real-library
mutation was performed.

## Validation results

- Baseline series tests: 44 passed before adding the endpoint.
- Full core/API suite after the contribution: **3,033 executed, 3,033 passed**.
  The TRX lists 3,034 total; one pre-existing diagnostic
  `production_reader_should_report_both_links` was ignored. The CLI summary
  reported zero skipped; this record preserves the TRX distinction.
- Eight frontend behavior tests passed, exercising actual component logic with
  fixture AJAX responses.
- Full Release console application build and production webpack build passed.
- Changed frontend files passed ESLint and stylelint; whitespace check passed.

Environment: .NET SDK 10.0.401 and Node 22.23.2, with the repository's locked
frontend dependencies. The baseline has NU1902 for `Microsoft.Build.Tasks.Git`
8.0.0 ([advisory](https://github.com/advisories/GHSA-23fw-v26w-5fgq)); restore normally
treats it as an error. Local runs used `-p:WarningsNotAsErrors=NU1902`, retaining
the warning. No package version, lockfile, or repository audit policy was changed.
Therefore these are successful compilation/test results with a documented audit
exception, not a claim that the baseline dependency audit is clean.

```sh
node frontend/test/series-navigation.test.cjs
./node_modules/.bin/webpack --env production --config frontend/build/webpack.config.js
dotnet test src/Chaptarr.Core.Test/Chaptarr.Core.Test.csproj -c Release -p:WarningsNotAsErrors=NU1902
dotnet build src/NzbDrone.Console/Chaptarr.Console.csproj -c Release -p:WarningsNotAsErrors=NU1902
```

## Control experiment: does the regression test detect the problem?

The four `series_monitor_should_preserve_unrelated_books_companion_format_and_author_policy`
cases run the actual `BookService` through the new API controller, with ebook and
audiobook monitor/unmonitor cases and companion synchronization enabled.

Temporarily replacing `SetMonitoredForMediaType` with the general `SetMonitored`
operation made **all four fail** on companion state and/or event-count assertions.
Restoring the scoped implementation made **all four pass**. The source was
restored before packaging. This demonstrates the tests detect the broader mutation,
instead of merely confirming a successful HTTP response.

## Running application and real SQLite state

The compiled console application started on loopback against a disposable empty
database and ran its actual migrations. Real HTTP requests verified:

- Authenticated status and series reads return 200.
- Unauthenticated monitoring returns 401.
- Unknown series returns 404; invalid selection returns 400.

Synthetic rows were then inserted into that disposable database: one ebook series
member, its audiobook companion sharing work identity, and an unrelated ebook by
the same author. Author companion synchronization was enabled. Real API requests
and independent SQLite queries produced:

| Row | Before | After unmonitoring the series member |
| --- | --- | --- |
| Selected ebook 701 | Monitored | Unmonitored |
| Companion audiobook 702 | Monitored | Monitored |
| Unrelated ebook 703 | Monitored | Monitored |
| Author monitoring and future-book policies | Initial fixture values | Identical values |

Selecting 701 plus nonmember 703 returned 400 without any mutation. Repeating the
valid unmonitor operation returned 200 with the same state. Trying to enable 701
without a configured root folder returned 409 identifying 701 and left saved
state intact. This checks persistence, routing, authentication, refusal reporting,
and scope in a running application; it does not involve real downloads.

SeerrNG's actual adapter then read this instance: book 701 → author 700 → series
710 → member 701, with native management path `/series/710`. No separate Hardcover
credential was configured. Both disposable application processes were stopped
after validation.

## Browser demonstration

Chromium exercised the actual book-header and series-page components in a fixture
application with a `/chaptarr` URL base. Checks covered click-through, direct reload,
ordered members, format filtering, individual/bulk monitoring, saved-state error
reporting, unresolved metadata fallback, and mobile rendering. No JavaScript errors
were observed. This component fixture does not claim full application-shell or
live-metadata coverage; the production webpack build is recorded separately.

## Remaining evidence before an upstream proposal

Real provider metadata quality and scheduled import-list discovery are outside
this change. No future-series subscription was added. Download/import behavior,
concurrent metadata refresh under load, and operator adoption remain unmeasured.
Collect a disposable real-metadata example and operator feedback about current-book
scope before proposing upstream. Maintainer acceptance is unknown. The additive
API and the narrower navigation change can be discussed separately if desired.

Raw logs, TRX, scripts, screenshots, and JSON state reports are retained in the
workspace contribution dossier; this repository record contains no credentials.
