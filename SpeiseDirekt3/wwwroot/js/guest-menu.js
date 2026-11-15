/**
 * guest-menu.js — behaviour of the static guest menu (/qr/{id}, /menu/{id}, /menu/preview/{id}).
 *
 * Plain ES module, no framework, no build step, no network requests except the click tracking. The server
 * renders the complete menu (Blazor static SSR, Components/Guest); everything works without this module
 * (chips are #anchors, items are ?item=<id> links that the server answers with the item sheet open).
 * This module only enhances the page:
 *   - sheets: native <dialog data-sheet="name">, bottom sheet on phones, centered dialog from 768 px;
 *     Escape, backdrop tap and the back button close them; focus moves in and back to the opener
 *   - item sheet: clones <template data-item-template="<id>"> (server markup) into the item sheet,
 *     updates the URL (?item=<id>) and records the click
 *   - category bar: scroll-spy (IntersectionObserver), active chip scrolls into view, smooth jumps
 *   - search, filter sheet and favourites ("Merkliste") live in their own modules (guest-search-filter.js,
 *     guest-favorites.js), imported and registered as behaviors below
 * When it is done it sets data-guest-ready on <html> (CSS shows [data-js-only] controls, tests wait for it).
 *
 * EXTENSION POINTS (import from this module, or add code to it):
 *   registerBehavior(name, init)      init(ctx) runs once the page is ready (immediately if it already is).
 *                                     ctx = { root, menuId, isPreview, trackClicks, basePath,
 *                                             openSheet, closeSheet, openItem, trackItemClick, onItemOpen,
 *                                             registerSheet, refreshCategoryNav }
 *   registerSheet(name, hooks)        hooks.onOpen(dialog, detail) / hooks.onClose(dialog) for
 *                                     <dialog data-sheet="name">. Any dialog[data-sheet] is a sheet anyway.
 *   openSheet(name, options)          options: { opener, history = true, detail }
 *   closeSheet(name?, then?)          closes (via history.back() if the sheet pushed an entry), then calls then()
 *   openItem(itemId, options)         options: { opener, history = true, record = true }; returns false if unknown
 *   onItemOpen(fn)                    fn(dialog, itemId) after the item sheet got its content (e.g. share button)
 *   trackItemClick(itemId)            POST /guest/api/item-click (skipped in the owner preview)
 *   scrollToCategory(anchorId)        smooth jump to a section (id "cat-…")
 *   refreshCategoryNav()              after sections were hidden/shown (filter): the scroll-spy re-measures and picks
 *                                     the active chip among the visible sections
 * Child modules (behaviors) must NOT import this file: it is loaded by a fingerprinted URL, a plain import would
 * create a second instance. They get everything through ctx; this file imports and registers them.
 * Declarative hooks in the markup:
 *   [data-sheet-open="name"]  opens that sheet      [data-sheet-close]   closes the sheet it is in
 *   a[data-item-link]         opens the item sheet  [data-category-link="cat-…"] jumps to the section
 *   cards/rows: [data-item-id] with data-category-id, data-labels, data-allergens, data-price, data-has-image,
 *   data-filter-labels; [data-slot="favorite"] holds the heart button of each card/row.
 * Behaviour modules (imported below, registered further down; they must not import this module):
 *   guest-search-filter.js   search sheet, filter sheet and filter notice
 *   guest-favorites.js       hearts, floating button and the favourites sheet ("Merkliste")
 *   guest-restaurant.js      share button of the item sheet (the info sheet is plain markup)
 */

import { initSearchAndFilter } from './guest-search-filter.js';
import { initFavorites } from './guest-favorites.js';
import { initRestaurant } from './guest-restaurant.js';

const ITEM_CLICK_URL = '/guest/api/item-click';
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

const state = {
    root: null,
    menuId: null,
    basePath: location.pathname,
    trackClicks: false,
    isPreview: false,
    ready: false,
    sheets: new Map(),          // name -> { dialog, hooks, opener, pushed }
    openName: null,
    openItemId: null,
    pendingAfterClose: [],
    behaviors: [],
    itemOpenHandlers: [],
    setActiveCategory: () => {},
    refreshCategoryNav: () => {},
    heldCategory: null          // a jump target stays active until the guest scrolls by hand
};

// --- Public API --------------------------------------------------------------------------------------

/** Runs init(ctx) once the guest page is ready (immediately when it already is). */
export function registerBehavior(name, init) {
    const behavior = { name, init };
    state.behaviors.push(behavior);
    if (state.ready) runBehavior(behavior);
}

/** Adds hooks to a sheet: { onOpen(dialog, detail), onClose(dialog) }. */
export function registerSheet(name, hooks = {}) {
    const sheet = state.sheets.get(name);
    if (sheet) Object.assign(sheet.hooks, hooks);
    else state.sheets.set(name, { dialog: null, hooks: { ...hooks }, opener: null, pushed: false });
}

/** Called with (dialog, itemId) whenever the item sheet shows an item (right away if one is open now). */
export function onItemOpen(fn) {
    state.itemOpenHandlers.push(fn);
    const dialog = state.sheets.get('item')?.dialog;
    if (state.openName === 'item' && state.openItemId && dialog) fn(dialog, state.openItemId);
}

/**
 * Opens <dialog data-sheet="name"> as a modal sheet. Another open sheet is closed without animation.
 * history: true pushes a history entry, so the back button closes the sheet.
 */
export function openSheet(name, { opener = document.activeElement, history: useHistory = true, detail, url } = {}) {
    const sheet = state.sheets.get(name);
    if (!sheet?.dialog) return false;

    const previous = state.openName;
    if (previous && previous !== name) closeNow(previous, { restoreFocus: false, animate: false });

    if (opener instanceof HTMLElement && opener !== document.body && !sheet.dialog.contains(opener)) sheet.opener = opener;
    sheet.hooks.onOpen?.(sheet.dialog, detail);
    sheet.dialog.classList.remove('is-closing');
    if (!sheet.dialog.open) sheet.dialog.showModal();
    lockScroll(true);
    state.openName = name;

    if (useHistory) {
        const entry = { gm: { sheet: name, item: detail?.itemId ?? null } };
        // Switching from one of our sheets to another (search → item) reuses that entry
        if (previous && window.history.state?.gm) window.history.replaceState(entry, '', url ?? location.href);
        else window.history.pushState(entry, '', url ?? location.href);
        sheet.pushed = true;
    } else {
        sheet.pushed = false;
    }
    return true;
}

/**
 * Closes the open sheet (or the named one). If it pushed a history entry, goes back in history (the popstate
 * handler closes it); then() runs once the sheet is closed.
 */
export function closeSheet(name = state.openName, then) {
    if (!name || state.openName !== name) { then?.(); return; }
    const sheet = state.sheets.get(name);
    if (then) state.pendingAfterClose.push(then);

    if (sheet.pushed && window.history.state?.gm?.sheet === name) {
        window.history.back();
        // Safety net if popstate does not arrive (should not happen)
        const fallback = setTimeout(() => { if (state.openName === name) finishClose(name); }, 600);
        state.pendingAfterClose.push(() => clearTimeout(fallback));
        return;
    }

    if (name === 'item' && new URLSearchParams(location.search).has('item')) {
        // Opened by the server (?item= deep link): drop the parameter instead of leaving the page
        window.history.replaceState(null, '', urlWithoutItem());
    }
    finishClose(name);
}

/** Shows the item's server-rendered detail in the item sheet. Returns false if the item is unknown. */
export function openItem(itemId, { opener, history: useHistory = true, record = true } = {}) {
    const template = state.root?.querySelector(`template[data-item-template="${cssEscape(itemId)}"]`);
    const sheet = state.sheets.get('item');
    if (!template || !sheet?.dialog) return false;

    fillItemSheet(sheet.dialog, itemId, template);
    openSheet('item', { opener, history: useHistory, detail: { itemId }, url: itemUrl(itemId) });
    state.openItemId = itemId;
    for (const fn of state.itemOpenHandlers) {
        try { fn(sheet.dialog, itemId); } catch (e) { console.error(e); }
    }
    if (record) trackItemClick(itemId);
    return true;
}

/** Records that the guest opened an item (not in the owner preview). Resolves to true when recorded. */
export function trackItemClick(itemId) {
    if (!state.trackClicks || !state.menuId) return Promise.resolve(false);
    return fetch(ITEM_CLICK_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ menuId: state.menuId, itemId }),
        credentials: 'same-origin',
        keepalive: true
    }).then(r => r.ok).catch(() => false);
}

/** Smoothly scrolls to a category section (id "cat-…"); scroll-margin-top keeps it below the sticky bars. */
export function scrollToCategory(anchorId) {
    const section = document.getElementById(anchorId);
    if (!section) return;
    state.setActiveCategory(anchorId);
    state.heldCategory = anchorId;
    section.scrollIntoView({ behavior: reducedMotion.matches ? 'auto' : 'smooth', block: 'start' });
    section.querySelector('h2[tabindex]')?.focus({ preventScroll: true });
}

/** Sections or chips were hidden/shown (e.g. by the filter): re-measure the scroll-spy and pick the active chip. */
export function refreshCategoryNav() {
    state.refreshCategoryNav();
}

// --- Sheets ------------------------------------------------------------------------------------------

function setUpSheets(root) {
    for (const dialog of root.querySelectorAll('dialog[data-sheet]')) {
        const name = dialog.dataset.sheet;
        const existing = state.sheets.get(name);
        if (existing) existing.dialog = dialog;
        else state.sheets.set(name, { dialog, hooks: {}, opener: null, pushed: false });

        // Escape: our close (history aware) instead of the native one
        dialog.addEventListener('cancel', e => { e.preventDefault(); closeSheet(name); });
        // Backdrop tap: the click lands on the <dialog> itself (the panel fills the rest)
        dialog.addEventListener('click', e => { if (e.target === dialog) closeSheet(name); });
        // Closed by the browser itself (e.g. a close watcher ignoring preventDefault): catch up
        // (the event is async: ignore it when the dialog is open again, e.g. the deep link conversion below)
        dialog.addEventListener('close', () => { if (state.openName === name && !dialog.open) closeSheet(name); });
    }

    window.addEventListener('popstate', onPopState);
}

/**
 * The server renders ?item= as an open, non-modal dialog plus a scrim link (no-JS fallback). Make it a real
 * modal sheet; the history entry stays the page the guest landed on (no push). Runs after the behaviors, so
 * their onItemOpen handlers see this item too.
 */
function openServerRenderedItem(root) {
    root.querySelectorAll('[data-scrim]').forEach(el => el.remove());
    const item = state.sheets.get('item')?.dialog;
    if (!item?.open) return;
    const itemId = item.dataset.itemId;
    item.close();
    openSheet('item', { history: false, detail: { itemId } });
    state.openItemId = itemId;
    for (const fn of state.itemOpenHandlers) {
        try { fn(item, itemId); } catch (e) { console.error(e); }
    }
}

function onPopState(e) {
    const target = e.state?.gm ?? null;
    const open = state.openName;

    if (open && (!target || target.sheet !== open || (open === 'item' && target.item !== state.openItemId))) {
        finishClose(open);
    }
    // Forward into a sheet entry: reopen it without pushing again
    if (target && state.openName !== target.sheet) {
        if (target.sheet === 'item' && target.item) openItem(target.item, { history: false, record: false });
        else openSheet(target.sheet, { history: false });
        const sheet = state.sheets.get(target.sheet);
        if (sheet) sheet.pushed = true;
    }
    runPendingAfterClose();
}

function finishClose(name) {
    closeNow(name, { restoreFocus: true, animate: true });
    runPendingAfterClose();
}

function runPendingAfterClose() {
    if (state.openName) return;
    const callbacks = state.pendingAfterClose.splice(0);
    // After the browser's own popstate work (scroll restoration) so a jump wins
    requestAnimationFrame(() => callbacks.forEach(fn => fn()));
}

function closeNow(name, { restoreFocus, animate }) {
    const sheet = state.sheets.get(name);
    if (!sheet?.dialog) return;
    const dialog = sheet.dialog;
    if (state.openName === name) state.openName = null;
    if (name === 'item') state.openItemId = null;
    sheet.pushed = false;
    lockScroll(false);
    const opener = sheet.opener;
    sheet.opener = null;

    const done = () => {
        dialog.classList.remove('is-closing');
        if (state.openName === name) return;     // reopened while the close animation ran
        if (dialog.open) dialog.close();
        sheet.hooks.onClose?.(dialog);
        // After close(): while the modal dialog is open the rest of the page is inert and cannot take focus
        if (restoreFocus) restoreFocusAfterClose(name, dialog, opener);
    };
    if (animate && dialog.open && !reducedMotion.matches) {
        dialog.classList.add('is-closing');
        let finished = false;
        const finish = () => { if (!finished) { finished = true; done(); } };
        dialog.addEventListener('animationend', finish, { once: true });
        setTimeout(finish, 320);
    } else {
        done();
    }
}

function restoreFocusAfterClose(name, dialog, opener) {
    if (opener?.isConnected && opener.getClientRects().length > 0) {
        opener.focus({ preventScroll: true });
    } else if (name === 'item' && dialog.dataset.itemId) {
        // Opened by a deep link: continue at the item's card
        const id = cssEscape(dialog.dataset.itemId);
        state.root.querySelector(`[data-testid][data-item-id="${id}"] [data-item-link]`)?.focus();
    }
}

function fillItemSheet(dialog, itemId, template) {
    const body = dialog.querySelector('[data-sheet-body]');
    body.replaceChildren(template.content.cloneNode(true));
    body.scrollTop = 0;
    dialog.dataset.itemId = itemId;
    const title = body.querySelector('h2[id]');
    if (title) dialog.setAttribute('aria-labelledby', title.id);
    else dialog.removeAttribute('aria-labelledby');
    const close = dialog.querySelector('[data-sheet-close]');
    if (close) close.setAttribute('href', urlWithoutItem());
}

let lockCount = 0;
function lockScroll(lock) {
    lockCount = lock ? 1 : 0;   // one sheet at a time
    document.documentElement.classList.toggle('gm-locked', lockCount > 0);
}

// --- Clicks (delegated) ------------------------------------------------------------------------------

function onClick(e) {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const target = e.target instanceof Element ? e.target : null;
    if (!target) return;

    const itemLink = target.closest('a[data-item-link]');
    if (itemLink) {
        const id = itemLink.closest('[data-item-id]')?.dataset.itemId ?? new URL(itemLink.href).searchParams.get('item');
        if (id && openItem(id, { opener: itemLink })) e.preventDefault();
        return;
    }

    const categoryLink = target.closest('[data-category-link]');
    if (categoryLink) {
        e.preventDefault();
        const anchor = categoryLink.dataset.categoryLink;
        if (state.openName) closeSheet(state.openName, () => scrollToCategory(anchor));
        else scrollToCategory(anchor);
        return;
    }

    const opener = target.closest('[data-sheet-open]');
    if (opener) {
        e.preventDefault();
        openSheet(opener.dataset.sheetOpen, { opener });
        return;
    }

    const closer = target.closest('[data-sheet-close]');
    if (closer) {
        const dialog = closer.closest('dialog[data-sheet]');
        if (dialog) {
            e.preventDefault();
            closeSheet(dialog.dataset.sheet);
        }
    }
}

// --- Category bar: scroll-spy --------------------------------------------------------------------------

function setUpCategoryNav(root) {
    const nav = root.querySelector('[data-catnav]');
    const scroller = nav?.querySelector('[data-catnav-scroll]');
    const sections = [...root.querySelectorAll('section[data-category-id]')];
    if (!nav || !scroller || sections.length === 0) return;

    const topbar = root.querySelector('[data-topbar]');
    let active = null;

    state.setActiveCategory = anchorId => {
        if (anchorId === active) return;
        active = anchorId;
        for (const chip of root.querySelectorAll('[data-category-link]')) {
            const on = chip.dataset.categoryLink === anchorId;
            chip.classList.toggle('is-active', on);
            if (on) chip.setAttribute('aria-current', 'true');
            else chip.removeAttribute('aria-current');
        }
        const chip = scroller.querySelector(`[data-category-link="${cssEscape(anchorId)}"]`);
        if (chip) {
            const left = chip.offsetLeft - (scroller.clientWidth - chip.offsetWidth) / 2;
            scroller.scrollTo({ left: Math.max(0, left), behavior: reducedMotion.matches ? 'auto' : 'smooth' });
        }
    };

    // Sections touching the band between the sticky bars and 45 % of the viewport are "visible";
    // the first of them (document order) is the active one.
    const visible = new Set();
    let observer = null;
    // Sections hidden by the filter (hidden attribute) never count
    const shown = () => sections.filter(s => !s.hidden);
    const pick = () => {
        if (state.heldCategory) { state.setActiveCategory(state.heldCategory); return; }
        const candidates = shown();
        if (candidates.length === 0) return;
        const atBottom = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 4;
        if (atBottom && window.scrollY > 0) { state.setActiveCategory(candidates[candidates.length - 1].id); return; }
        const first = candidates.find(s => visible.has(s.id));
        state.setActiveCategory((first ?? candidates[0]).id);
    };
    const observe = () => {
        observer?.disconnect();
        visible.clear();
        const offset = (topbar?.offsetHeight ?? 0) + nav.offsetHeight;
        observer = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (entry.isIntersecting) visible.add(entry.target.id);
                else visible.delete(entry.target.id);
            }
            pick();
        }, { rootMargin: `-${offset + 1}px 0px -55% 0px`, threshold: 0 });
        sections.forEach(s => observer.observe(s));
    };
    observe();
    state.refreshCategoryNav = () => {
        if (state.heldCategory && document.getElementById(state.heldCategory)?.hidden) state.heldCategory = null;
        observe();
        requestAnimationFrame(pick);
    };

    let resizeTimer = 0;
    window.addEventListener('resize', () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(observe, 150); });
    let ticking = false;
    window.addEventListener('scroll', () => {
        if (ticking) return;
        ticking = true;
        requestAnimationFrame(() => { ticking = false; pick(); });
    }, { passive: true });
    // Scrolling by hand ends the hold of a jump target (the smooth jump itself fires only scroll events)
    for (const type of ['wheel', 'touchstart', 'pointerdown', 'keydown']) {
        window.addEventListener(type, () => { if (state.heldCategory) { state.heldCategory = null; pick(); } },
            { passive: true, capture: true });
    }

    // Category sheet: focus the active chip when it opens
    registerSheet('categories', {
        onOpen(dialog) {
            requestAnimationFrame(() => dialog.querySelector('.gm-chip.is-active')?.focus({ preventScroll: false }));
        }
    });

    active = sections[0].id;
}

// --- Hero: the top bar title appears once the big heading is gone ---------------------------------------

function setUpHero(root) {
    const title = root.querySelector('[data-hero-title]');
    const topbar = root.querySelector('[data-topbar]');
    if (!title) return;
    const observer = new IntersectionObserver(([entry]) => {
        root.toggleAttribute('data-hero-hidden', !entry.isIntersecting);
    }, { rootMargin: `-${topbar?.offsetHeight ?? 56}px 0px 0px 0px` });
    observer.observe(title);
}

// --- Helpers ---------------------------------------------------------------------------------------------

function itemUrl(itemId) {
    return `${state.basePath}?item=${encodeURIComponent(itemId)}`;
}

function urlWithoutItem() {
    const url = new URL(location.href);
    url.searchParams.delete('item');
    url.hash = '';
    return url.pathname + url.search;
}

function cssEscape(value) {
    return window.CSS?.escape ? CSS.escape(value) : String(value).replace(/["\\]/g, '\\$&');
}

function runBehavior(behavior) {
    try {
        behavior.init({
            root: state.root, menuId: state.menuId, isPreview: state.isPreview, trackClicks: state.trackClicks,
            basePath: state.basePath, openSheet, closeSheet, openItem, trackItemClick, onItemOpen,
            registerSheet, refreshCategoryNav
        });
    } catch (e) {
        console.error(`guest-menu behavior "${behavior.name}" failed`, e);
    }
}

/** Mobile browser chrome in the page color of the theme (from the CSS variable, no color in JS). */
function setThemeColor() {
    const color = getComputedStyle(state.root ?? document.documentElement).getPropertyValue('--gm-bg').trim();
    if (!color) return;
    let meta = document.querySelector('meta[name="theme-color"]');
    if (!meta) {
        meta = document.createElement('meta');
        meta.name = 'theme-color';
        document.head.append(meta);
    }
    meta.content = color;
}

// --- Behaviors in their own modules ------------------------------------------------------------------------

registerBehavior('search-filter', initSearchAndFilter);
registerBehavior('favorites', initFavorites);
registerBehavior('restaurant', initRestaurant);

// --- Start ------------------------------------------------------------------------------------------------

function init() {
    const root = document.querySelector('[data-guest-root]');
    if (root) {
        state.root = root;
        state.menuId = root.dataset.menuId ?? null;
        state.basePath = root.dataset.basePath || location.pathname;
        state.trackClicks = root.dataset.trackClicks === 'true';
        state.isPreview = root.dataset.preview === 'true';

        setUpCategoryNav(root);
        setUpHero(root);
        setUpSheets(root);
        document.addEventListener('click', onClick);
    }

    // Inside the menu editor's phone frame: no scrollbar, like on a phone
    if (window.top !== window) document.documentElement.setAttribute('data-embedded', '');
    setThemeColor();

    state.ready = true;
    state.behaviors.forEach(runBehavior);
    if (root) openServerRenderedItem(root);
    document.documentElement.setAttribute('data-guest-ready', '');
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, { once: true });
else init();
