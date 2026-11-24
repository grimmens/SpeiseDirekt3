# AP16: Plausible on every page, conversion events (handover to the operator's PC)

**This file is a WIP commit. Drop it before the PR** (`git rebase -i` is fine locally; the result must not contain this
file).

## Branch
- `feature/plausible`, created from `feature/legal-privacy` (PR #24, not merged yet when this was written). Its first
  commit is this file.
- If PR #24 has been **squash-merged** by the time you open the PR: `git rebase --onto origin/master
  origin/feature/legal-privacy` first, so none of #24's commits are carried twice.
- Open the PR against `master` as a draft. Merge = operator.

## Operator's decisions (24.11.2026)
- The operator wants to see how many people use the site, and above all the **conversion**: visitor → account →
  active restaurant.
- Plausible CE runs on server3 (`stats.webtechsys.de`, the CI host). The operator created the site
  **`app.speisedirekt.de`** there. Its tracker is the new configurable script `pa-HMenG5yzSYvzPC1S-wZp7.js`:
  - it carries the domain;
  - outbound links, file downloads and form submissions are switched on in the Plausible UI;
  - it reads `plausible.init({...})` options: `endpoint`, `transformRequest` (return a falsy value to drop the event),
    `customProperties`, `autoCapturePageviews`, `captureOnLocalhost`, `logging`.
- **Everything comes from the same host.** Since 24.11. the production vhost of `app.speisedirekt.de` on server2
  proxies exactly two paths to server3 (done, outside the repo):
  - `/_s/script.js` → the `pa-…js` above;
  - `/_s/event` → `/api/event`.
  - The proxy strips Cookie and Referer, does not pass the host name on, and sends the visitor IP as `X-Plausible-IP`.
  - Every other `/_s/*` path reaches the app (empty 404).
  - The app never names `stats.webtechsys.de` or the `pa-` id.
- **Every page** gets the script: landing `/`, `/demo`, `/impressum`, the account pages, the guest menus, and the back
  office. **Except** the editor's phone preview `/menu/preview/{id}`: it reloads on every change and is not a guest.
- **Production only.** Preview (a copy of production on server2, where `/_s/` does not exist), E2E and Development
  render nothing. Neither the tag nor the init script.

## Part 1: the tag
- **Config:** a section `Plausible` with `ScriptPath` (`/_s/script.js`) and `Endpoint` (`/_s/event`). Empty means off.
  - Put the production values into a new `appsettings.Production.json`. The unit on server2 runs
    `ASPNETCORE_ENVIRONMENT=Production`; Preview, E2E and Development never load that file.
  - `appsettings.json` keeps the section empty.
- **All four documents** of `Components/App.razor` render the tag when it is on: Guest, Auth, Landing and back office
  (`PageDocument`, `OwnDocumentAttribute`). The guest document leaves it out for `/menu/preview/*`.
- **Order in `<head>`:**
  1. a small own script, a file in `wwwroot/js` with `@Assets` and no inline script. It defines the queue stub from
     Plausible's snippet (`window.plausible = window.plausible || function(){(plausible.q=plausible.q||[]).push(arguments)}`,
     `plausible.init = …`) and calls `plausible.init({ endpoint, transformRequest })`;
  2. then `<script async src="/_s/script.js">`.
  - Take the endpoint from the config, e.g. via a `data-` attribute on the script tag.
  - **No word** like `plausible`, `analytics`, `stats`, `track` or `metrics` in the file name, the path or the attributes
    the tag carries: ad blockers match on them.
- **The back office** navigates with `history.pushState`. The tracker captures that by itself
  (`autoCapturePageviews`). Check that one interactive navigation gives exactly one pageview.

## Part 2: URL masking (`transformRequest`)
Applies to every event: pageviews, engagement, outbound, downloads, form submissions and our own.
- **Ids:** replace each path segment that is a GUID (or any other id a route carries; check every `@page` template
  with a parameter) with `:id`. For example `/qr/3f2a…` → `/qr/:id`, `/MenuItems/item/…/edit` → `/MenuItems/item/:id/edit`.
- **Query:** keep only `utm_source`, `utm_medium`, `utm_campaign`, `utm_content`, `utm_term`, `ref` and `source`.
  Drop everything else: `ReturnUrl`, `code`, `userId`, `email`, `item`, …. The account pages carry tokens in the query.
- **Hash:** drop it.
- **Referrer `r`:** keep external referrers. Drop it when it is the site itself.
- Return a falsy value (drop the event) for `/menu/preview/*`, as a second fence behind the missing tag.

## Part 3: conversion events
Event names are German: the operator reads them in the dashboard and creates goals from them. Names are exact.

| Event | When, exactly | Props |
|---|---|---|
| `Kostenlos starten` | click on any "register free" call to action of the landing page: header, mobile menu, hero, steps section, closing CTA, footer (`data-testid` `landing-register`, `hero-register`, `steps-register`, `cta-register`, `footer-register`, the `data-lp-menu-link` one) | `position`: `header`, `menu`, `hero`, `steps`, `cta`, `footer` |
| `Konto angelegt` | once, after a **successful self-registration** (`Account/Pages/Register.razor`; also the external-login registration if that path can create an account). Never for a team member the owner invites (`InviteUserPage`, API). Never again on reload, back button or a second tab. | none |
| `Erste Speisekarte` | the tenant's **first** menu is saved (the owner's menu count goes from 0 to 1), whoever of the team saves it | none |
| `Erster QR-Code` | the tenant's first QR code is saved | none |
| `QR-Code heruntergeladen` | click on "Download PNG" (`QrCodeShareActions`, `data-testid="qr-download"`). The PNG path has no file extension, so the tracker's own download tracking does not see it. | none |
| `QR-Link kopiert` | the copy-link button of the same component, after the copy worked | none |

- **No personal data** in names or props: no e-mail, no restaurant name, no ids.
- Static SSR pages (landing, auth) fire from their own JS; the landing has `js/landing.js`. Interactive back-office pages
  fire through one small C# service over `IJSRuntime` (`window.plausible?.(...)`) that does nothing when Plausible is
  off.
  - `Konto angelegt` happens in a static SSR form post followed by a redirect. Carry a one-shot marker to the page that
    renders after it, e.g. the redirect target with a flag that is consumed once.
  - The landing and guest pages must stay **without cookies** (`CookielessPages`); auth pages may use what they already
    have.
- **"First" means first in the database.**
  - Decide it on the server at save time. A tenant that deletes its only menu and creates a new one has a 0 → 1
    transition again; that is acceptable.
  - Preview seeding and the demo never fire.
- "Premium gebucht" comes later with the tariff work, not now.

## Tests
On the PC: full API and full E2E at the end.
- **Off (default):** no tag, no init script and no `/_s/` request on any document. The existing `LocalAssetsTests`
  and external blocking stay as they are.
- **On:** an E2E fixture variant with the config set.
  - Serve `/_s/script.js` from a Playwright route with a **recording fake** that keeps `plausible.q` and `plausible.o`.
    Nothing may leave the test, and the real tracker is external.
  - Every document carries the tag and the init, with endpoint `/_s/event`. `/menu/preview/{id}` carries neither.
  - **Masking:** call the init options' `transformRequest` in the page with GUID paths, token queries, utm parameters,
    a hash and internal/external referrers, and assert the results.
  - **Events:**
    - each landing call to action sends `Kostenlos starten` with its `position`;
    - registration sends `Konto angelegt` exactly once: not after a reload, not for an invited member;
    - the first menu and the first QR code fire once, the second does not;
    - download and copy fire.
  - **Back office:** one pushState navigation gives one pageview. You may test that with the real tracker's behaviour
    documented instead, if a fake cannot show it; say so.
- **Unchanged:** no external request anywhere, and no cookie on guest and landing pages.

## Docs
- Repository `CLAUDE.md`: the `/_s/` proxy (it lives in server2's vhost, not in the repository), the config, the event
  list, the masking rules, and "production only".
- The **privacy policy** text is not part of this package. List in your report what it must say: data sent per event,
  the proxy, server3, no cookie and no stored IP.

## Report
- Changed files, the event table as built, the masking examples.
- API and E2E counts.
- Anything you could not do.
