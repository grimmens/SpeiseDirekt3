# HANDOVER AP15b: finish on the operator's Windows PC

> **This file and `ParityHarness.cs` (plus its hooks in `E2EAppFactory`, `E2EFixture`, `E2ETestBase` and
> `AdminScreenshotTour`) form the tip commit "WIP: parity harness + handover". They must not reach `master`.**
> Do not merge the tip commit. Integrate `HEAD~1` (step 4).

Written 2026-11-23 by the Claude session on the CI host, when the operator moved the remaining work to a local session.
The work was stopped mid-run; nothing failed.

## Where things are
- **PR #24** (draft): `feature/legal-privacy` → `master`. It holds AP15a (imprint `/impressum`, guest tracking without
  cookie/IP/user agent, migration `RemoveGuestTrackingIpAndUserAgent`); the preview of AP15a is verified.
- **This branch** `feature/legal-privacy-ap15b` = AP15a + AP15b:
  - `ee6db9b` Tailwind built locally with the pinned standalone CLI instead of the Play CDN
  - `4b55d37` CI: refuse a stale css/tailwind.css before building
  - `fceec75` Tailwind input: the same CSS from LF and CRLF working copies
  - `1abfe27` E2E: no Tailwind stand-in any more, external requests blocked in every context
  - `dfda351` build.ps1: paths via Path.Combine, so PowerShell 7 on Linux/macOS works too
  - `184d621` Tailwind: placeholders keep the Play CDN's #9ca3af
  - `7397909` CLAUDE.md: Tailwind built locally (AP15b), the check in CI, E2E without the Tailwind stand-in
  - tip: WIP parity harness + this file (drop)
- The repository `CLAUDE.md` documents AP15b ("Tailwind CSS (back office)" under Development Commands).

## The task (AP15b brief, short)
The back office must load nothing from another host (operator, 23.11.).
- Tailwind **3.4.17** (what `cdn.tailwindcss.com` served) is built by the standalone CLI from `tools/tailwind/`
  (`tailwind.config.js`, `input.css`) into the committed, minified `SpeiseDirekt3/wwwroot/css/tailwind.css`, linked
  fingerprinted in `App.razor` at the position where the Play CDN injected its `<style>`: last in `<head>`.
- `tools/tailwind/build.sh` / `build.ps1`: download the CLI once, verify its SHA-256 (pinned), cache it outside the repo,
  build; `--check` / `-Check` fails when the committed CSS is stale. `ci.yml` and `deploy.yml` run the check before build.
- **Acceptance:** the back office looks exactly as before (visual parity), and the full API and E2E suites are green.

## Parity check: done on the CI host (Linux, Chromium 1243)
- **Method:** `AdminScreenshotTour` before (`master` + CDN) vs after (this branch), with deterministic ids, e-mails and
  port (the harness, `E2E_PARITY=1`), compared pixel by pixel.
- **Result:** every difference is explained, and none comes from Tailwind:
  - the imprint link AP15a added to the auth footer;
  - the Chart.js grow animation still running at shot time;
  - random ids (new QR code, authenticator key);
  - 2-px anti-aliasing;
  - scroll-spy timing in the editor's guest iframe.
- **Counts:**
  - every-page tour: en 77/85 and de 78/85 identical;
  - review widths 390+1280: en 48/66 and de 47/66 identical, the rest explained as above.
- The full list is in the appendix.
- **No need to repeat it on Windows.** Font rendering differs there, so before and after must come from the same machine
  if you ever do.

## What is left (in this order)
1. **Windows setup:**
   - .NET 9 SDK;
   - `dotnet build`;
   - once: `pwsh SpeiseDirekt3.E2ETests\bin\Debug\net9.0\playwright.ps1 install chromium` (PowerShell 7). The E2E
     project also installs it on first run.

   The tests need **no SQL Server or LocalDB**: API tests use SQLite in memory, E2E uses an SQLite file. There is no
   build lock on the PC (`flock` is only for the shared CI host).
2. **Tailwind check on Windows:**
   - `powershell -ExecutionPolicy Bypass -File tools\tailwind\build.ps1 -Check` must pass on a fresh CRLF checkout.
     `build.ps1` has run with PowerShell 7 on Linux, but **never on Windows PowerShell 5.1**. Fix it if needed.
   - `bash tools/tailwind/build.sh --check` (Git Bash) should pass too.
3. **Full test runs:**
   - `dotnet test SpeiseDirekt.Api.IntegrationTests` (AP15a: 229/229);
   - `dotnet test SpeiseDirekt3.E2ETests` (AP15a on the host: 460/461; the one failure, `QrRedirectTests.MalformedQrId_…`,
     is an artefact of the host's newer Chromium and should pass on the PC).

   Fix real failures without weakening assertions. Some tests may behave differently now that the real Tailwind CSS
   loads instead of the old stand-in stylesheet; the targeted runs on the host were green.
4. **Integrate without the tip commit** (no force push needed; feature/legal-privacy is an ancestor):
   ```
   git fetch origin
   git push origin origin/feature/legal-privacy-ap15b~1:refs/heads/feature/legal-privacy
   ```
   Do this only after step 2/3 fixes are committed *below* the tip, or redo the order: e.g. drop the tip locally
   (`git reset --hard HEAD~1`), commit fixes, push to `feature/legal-privacy`.
5. **PR #24:**
   - add an AP15b section to the body (what changed, parity result, test counts);
   - `gh pr ready 24`;
   - check that the preview run's new step "Tailwind check" passes on the self-hosted ARM64 runner;
   - the back office on https://preview.speisedirekt.de loads nothing from another host. In DevTools → Network:
     only `preview.speisedirekt.de`.

   **Merge is the operator's.**
6. **Then:** a draft privacy policy for the operator to review, using AP15a's facts:
   - cookie list, guest storage, visitor key;
   - hosting at netcup GmbH, Daimlerstraße 25, 76185 Karlsruhe;
   - server logs 14 days;
   - Tailwind is no longer external.

   His own policy on webtechsys.de is the model for structure and tone. It is a draft for him or a lawyer, not legal
   advice; he decides where it goes (`Legal:PrivacyUrl`).

## Rules that still apply
- **Commits:** end every commit message with:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Fsn2tCi7bFwpVJfETrkVPs
  ```
- Never commit `bin/`, `obj/`, `*.db`, screenshots or scratch files.
- `.gitattributes` is `* text eol=crlf`. Binary types must be listed (`css/tailwind.css` is byte-exact on purpose; see
  `fceec75`).
- Merge to `master` = the operator. A push to `master` deploys to production (backup + migrations first).
- Keep the repository `CLAUDE.md` current.
- **Open product decisions** (ask the operator, do not assume):
  - the six questions for the free/premium plan package;
  - the contact e-mail for the imprint (`Landing:ContactEmail`).
- ⚠️ The GitHub repository is **public** while CI runs PRs on a self-hosted runner. The operator was advised to make it
  private.

## Appendix: parity summary (CI host)
```
== Capture_every_back_office_page en-US (before vs after): 85 shots, 77 identical
  desktop-account-login.png | 1879 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  desktop-account-register.png | 1879 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  desktop-analytics.png | 1780 px | Chart.js grow animation (1 s) still running when the tour shoots 700 ms after network idle: bar tops differ by 1-2 px, axes and grid identical; sign varies between runs
  desktop-qrcode-calendar-panel-conflict.png | 29 px | anti-aliasing of the conflict alert corners, max channel delta 2
  desktop-qrcode-new.png | 6481 px | the unsaved new QR code gets a random id from the app: QR image and link text differ
  phone-account-login.png | 6469 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  phone-account-register.png | 6469 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  phone-analytics.png | 5839 px | Chart.js grow animation (1 s) still running when the tour shoots 700 ms after network idle: bar tops differ by 1-2 px, axes and grid identical; sign varies between runs
== Capture_every_back_office_page de-DE (before2 vs after): 85 shots, 78 identical
  desktop-account-login.png | 2094 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  desktop-account-register.png | 2094 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  desktop-analytics.png | 7966 px | Chart.js grow animation (1 s) still running when the tour shoots 700 ms after network idle: bar tops differ by 1-2 px, axes and grid identical; sign varies between runs
  desktop-qrcode-new.png | 6914 px | the unsaved new QR code gets a random id from the app: QR image and link text differ
  phone-account-login.png | 7348 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  phone-account-register.png | 7348 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  phone-analytics.png | 5661 px | Chart.js grow animation (1 s) still running when the tour shoots 700 ms after network idle: bar tops differ by 1-2 px, axes and grid identical; sign varies between runs
== Capture_review_widths 390+1280 en-US (before vs after): 66 shots, 48 identical
  review/1280/access-denied.png | 1879 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/analytics.png | 1525 px | Chart.js grow animation (1 s) still running when the tour shoots 700 ms after network idle: bar tops differ by 1-2 px, axes and grid identical; sign varies between runs
  review/1280/forgot.png | 1879 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/lockout.png | 1879 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/login.png | 1879 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/manage-authenticator.png | 16792 px | Identity generates a new random authenticator key per visit: key text and QR image differ
  review/1280/qrcode-new.png | 6986 px | the unsaved new QR code gets a random id from the app: QR image and link text differ
  review/1280/register.png | 1879 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/reset.png | 1879 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/access-denied.png | 1863 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/analytics.png | 485 px | Chart.js grow animation (1 s) still running when the tour shoots 700 ms after network idle: bar tops differ by 1-2 px, axes and grid identical; sign varies between runs
  review/390/forgot.png | 1863 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/lockout.png | 1863 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/login.png | 1863 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/manage-authenticator.png | 16798 px | Identity generates a new random authenticator key per visit: key text and QR image differ
  review/390/qrcode-new.png | 7548 px | the unsaved new QR code gets a random id from the app: QR image and link text differ
  review/390/register.png | 1863 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/reset.png | 1863 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
== Capture_review_widths 390+1280 de-DE (before vs after): 66 shots, 47 identical
  review/1280/access-denied.png | 2094 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/analytics.png | 2436 px | Chart.js grow animation (1 s) still running when the tour shoots 700 ms after network idle: bar tops differ by 1-2 px, axes and grid identical; sign varies between runs
  review/1280/forgot.png | 2094 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/lockout.png | 2094 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/login.png | 2094 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/manage-authenticator.png | 12452 px | Identity generates a new random authenticator key per visit: key text and QR image differ
  review/1280/menu-edit.png | 1954 px | editor phone-frame iframe (guest page, no Tailwind): category chip strip scrolled a few px differently (scroll-spy timing)
  review/1280/qrcode-new.png | 10640 px | the unsaved new QR code gets a random id from the app: QR image and link text differ
  review/1280/register.png | 2094 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/1280/reset.png | 2094 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/access-denied.png | 2110 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/analytics.png | 1069 px | Chart.js grow animation (1 s) still running when the tour shoots 700 ms after network idle: bar tops differ by 1-2 px, axes and grid identical; sign varies between runs
  review/390/forgot.png | 2110 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/lockout.png | 2110 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/login.png | 2110 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/manage-authenticator.png | 12458 px | Identity generates a new random authenticator key per visit: key text and QR image differ
  review/390/qrcode-new.png | 7894 px | the unsaved new QR code gets a random id from the app: QR image and link text differ
  review/390/register.png | 2110 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
  review/390/reset.png | 2110 px | auth document (no Tailwind in either build): the "Imprint/Impressum" footer link AP15a added; master has none
```
