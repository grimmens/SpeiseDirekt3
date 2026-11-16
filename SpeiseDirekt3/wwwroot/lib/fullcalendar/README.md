# FullCalendar (vendored)

FullCalendar **Standard Bundle 6.1.22** (MIT, `LICENSE.md` = the tarball's license file), used by the back office's
calendar wrapper (`Components/Shared/Calendar`, `js/fullcalendar-interop.js`), which loads it lazily on the pages that
show a calendar. Never loaded by the guest pages.

| | |
|---|---|
| Package | `fullcalendar@6.1.22` (npm tag `legacy`; released 2026-10-16) |
| Tarball | https://registry.npmjs.org/fullcalendar/-/fullcalendar-6.1.22.tgz |
| `dist.integrity` | `sha512-Nd5NGL2ELAQkRWMORFJIrrHJjdwlgfyw9ZXUB6O1j9biXhtDtdWZTqYS/7mk9mys10ehhNbSiwZetFZGiOC5rw==` |
| `dist.shasum` | `014770f25c7d42780aeb45855fcea8152afd6932` |
| Files taken | `package/index.global.min.js` (core + daygrid + timegrid + list + multimonth + interaction; injects its own CSS), `package/LICENSE.md` |
| sha256 `index.global.min.js` | `65f927f1802cb35428b9c5e2a8d2276633880a89ee5ea5f9a7727f79892786ea` |
| sha256 `LICENSE.md` | `4c0b0dae26c99e3b11be70b990a703be4deb75e21884104582e0e217786d3209` |

Only the MIT standard plugins are in this bundle; no premium plugin (timeline, resource, …) and no license key.
The English locale is built in, so no locale file is needed (the back office is English).

## How the files were verified (2026-11-16)

1. `npm view fullcalendar@6.1.22 dist` for the tarball URL, `integrity` and `shasum`.
2. Downloaded the tarball into an empty directory and checked
   `"sha512-$(openssl dgst -sha512 -binary fullcalendar-6.1.22.tgz | base64 -w0)"` against `dist.integrity` and
   `sha1sum` against `dist.shasum` (both equal), then extracted it.
3. Copied the two files unchanged; `cmp` against the extracted files, sha256 as above. The bundle contains no URL it
   loads from (only links in comments and messages, plus the SVG namespace); its icon font is an inline `data:` URL.

The repository normalizes text files to CRLF (`.gitattributes`); `*.js` in this folder is excluded (`-text`), so the
checked-out bundle stays byte-identical to the tarball.

## Why 6.1.22 and not 7.x

7.x is stable (7.0.0 since 2026-06-19), but its vanilla package `fullcalendar@7` is an ES module that imports
`preact`, `@fullcalendar/core`, `@full-ui/headless-calendar` and the peer `temporal-polyfill` by bare specifier —
without a bundler that means vendoring five packages and an import map. 6.1.22 is a single self-contained,
dependency-free file and still maintained on the `legacy` tag. It was the newest 6.x and more than two weeks old;
the newest 7.x older than two weeks was 7.1.3.

## Upgrading

Pick a version at least two weeks old, repeat the steps above, replace both files, update this README and the
version in the repository `CLAUDE.md`. `LocalAssetsTests` checks that the calendar page loads it locally.
