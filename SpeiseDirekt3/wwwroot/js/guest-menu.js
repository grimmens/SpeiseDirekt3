/**
 * guest-menu.js — behaviour of the static guest menu (/qr/{id}, /menu/{id}, /menu/preview/{id}).
 *
 * Plain ES module, no framework, no build step, no network requests except the click tracking. The server
 * renders the complete menu (Blazor static SSR, Components/Guest); everything works without this module
 * (chips are #anchors, items are ?item=<id> links that the server answers with the item sheet open).
 * This module only enhances the page:
 *   - sheets: native <dialog data-sheet="name">, bottom sheet on phones, centered dialog from 768 px;
 *     Escape, backdrop tap and the back button close them; focus moves in and back to the opener; on phones the
 *     grab handle and the header band drag the sheet down (closes beyond 25 % of its height or on a flick); the page
 *     keeps its scroll position (a sheet's close puts it back if something moved it)
 *   - full-screen sheets on phones (.gm-sheet--full, the search sheet): anchored to the top, sized to the visual
 *     viewport (the area above the keyboard), no drag, the page behind cannot be scrolled
 *   - item sheet: clones <template data-item-template="<id>"> (server markup) into the item sheet,
 *     updates the URL (?item=<id>) and records the click
 *   - category bar: scroll-spy (IntersectionObserver), active chip scrolls into view, smooth jumps with an explicit
 *     target (real bar heights) and a safety net that corrects the position once the scroll has ended
 *   - focus rings only for keyboard use (data-gm-quiet on elements focused after a tap or by script)
 *   - search, filter sheet and favourites ("Merkliste") live in their own modules (guest-search-filter.js,
 *     guest-favorites.js), imported and registered as behaviors below
 * When it is done it sets data-guest-ready on <html> (CSS shows [data-js-only] controls, tests wait for it).
 *
 * EXTENSION POINTS (import from this module, or add code to it):
 *   registerBehavior(name, init)      init(ctx) runs once the page is ready (immediately if it already is).
 *                                     ctx = { root, menuId, isPreview, trackClicks, basePath, lang,
 *                                             openSheet, closeSheet, openItem, trackItemClick, onItemOpen,
 *                                             registerSheet, refreshCategoryNav, t, formatPrice }
 *   ctx.t(key, values)                a UI text of the menu's language from the server (GuestText.ScriptJson in the
 *                                     root's data-guest-strings): German menus German, all others English; {name}
 *                                     placeholders are replaced from values. Modules never hard-code UI text.
 *   ctx.formatPrice(cents)            "€ 9,90" / "€9.90" — the server's notation (prefix, separators from the JSON)
 *   registerSheet(name, hooks)        hooks.onOpen(dialog, detail) before it is shown, hooks.onShown(dialog, detail)
 *                                     right after showModal() — still inside the tap, so a focus there opens the iOS
 *                                     keyboard —, hooks.onClose(dialog) once it is closed, for <dialog data-sheet="name">.
 *                                     Any dialog[data-sheet] is a sheet anyway.
 *   openSheet(name, options)          options: { opener, history = true, detail }
 *   closeSheet(name?, then?, opts)    closes (via history.back() if the sheet pushed an entry), then calls then() once
 *                                     the close animation has ended; opts.restoreFocus = false leaves the focus to then()
 *   openItem(itemId, options)         options: { opener, history = true, record = true }; returns false if unknown
 *   onItemOpen(fn)                    fn(dialog, itemId) after the item sheet got its content (e.g. share button)
 *   trackItemClick(itemId)            POST /guest/api/item-click (skipped in the owner preview)
 *   scrollToCategory(anchorId)        smooth jump to a section (id "cat-…"), focuses its heading; only with no sheet open
 *   refreshCategoryNav()              after sections were hidden/shown (filter): the scroll-spy re-measures and picks
 *                                     the active chip among the visible sections
 * Child modules (behaviors) must NOT import this file: it is loaded by a fingerprinted URL, a plain import would
 * create a second instance. They get everything through ctx; this file imports and registers them.
 * Declarative hooks in the markup:
 *   [data-sheet-open="name"]  opens that sheet      [data-sheet-close]   closes the sheet it is in
 *   a[data-sheet-leave]       a link in a sheet that leaves the page (language versions): the sheet is closed first
 *                             (its history entry is taken back), then the link is followed — so the back button
 *                             returns to the page, not to an open sheet
 *   a[data-item-link]         opens the item sheet  [data-category-link="cat-…"] jumps to the section
 *   cards/rows: [data-item-id] with data-category-id, data-labels, data-allergens, data-price, data-has-image,
 *   data-filter-labels; [data-slot="favorite"] holds the heart button of each card/row.
 * Behaviour modules (imported below, registered further down; they must not import this module):
 *   guest-search-filter.js   search sheet, filter sheet and filter notice
 *   guest-favorites.js       hearts, floating button and the favourites sheet ("Merkliste")
 *   guest-restaurant.js      share button of the item sheet (the info sheet is plain markup)
 *   guest-preview.js         owner preview inside the menu editor only: shows the editor's unsaved draft (postMessage)
 * The language sheet needs no code of its own: the button is <a href="#gm-languages" data-sheet-open="language">, its
 * options are plain links to the other versions (a[data-sheet-leave]; no tracking, no MenuView).
 */

import { initSearchAndFilter } from './guest-search-filter.js';
import { initFavorites } from './guest-favorites.js';
import { initRestaurant } from './guest-restaurant.js';
import { initPreview } from './guest-preview.js';

const ITEM_CLICK_URL = '/guest/api/item-click';
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
// From 768 px sheets are centered dialogs (guest-menu.css); below they are bottom sheets that can be dragged down
const centeredSheets = window.matchMedia('(min-width: 768px)');

// A jump puts the section's top this many px under the sticky bars (its padding keeps the heading clear of them);
// the same as scroll-margin-top in guest-menu.css, which only serves the #cat-… links without JS
const JUMP_OVERLAP = 8;
// Drag to dismiss: close beyond this share of the panel height, or on a downward flick at least this fast (px/ms)
const DRAG_CLOSE_SHARE = 0.25;
const DRAG_FLICK_SPEED = 0.5;

const state = {
    root: null,
    menuId: null,
    basePath: location.pathname,
    trackClicks: false,
    isPreview: false,
    lang: 'de',
    strings: {},
    price: { prefix: '€ ', decimal: ',', group: '.' },
    ready: false,
    sheets: new Map(),          // name -> { dialog, hooks, opener, pushed }
    openName: null,
    openItemId: null,
    closing: new Set(),         // sheets whose close animation still runs
    pendingAfterClose: [],
    skipFocusRestore: false,    // the next close leaves the focus to its then() (category jump)
    backFallback: 0,
    behaviors: [],
    itemOpenHandlers: [],
    setActiveCategory: () => {},
    revealActiveChip: () => {},
    refreshCategoryNav: () => {},
    heldCategory: null,         // a jump target stays active until the guest scrolls by hand
    jump: null,                 // the running category jump: { section, stop() }
    lockedScroll: null,         // the page's scroll position while a sheet is open: { x, y }
    viewport: null              // the full-screen sheet that follows the visual viewport: { dialog, stop() }
};

// --- Public API --------------------------------------------------------------------------------------

/** Runs init(ctx) once the guest page is ready (immediately when it already is). */
export function registerBehavior(name, init) {
    const behavior = { name, init };
    state.behaviors.push(behavior);
    if (state.ready) runBehavior(behavior);
}

/** Adds hooks to a sheet: { onOpen(dialog, detail), onShown(dialog, detail), onClose(dialog) }. */
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
    sheet.dialog.classList.remove('is-closing', 'is-dragging', 'is-settling');
    resetDragStyle(sheet.dialog);
    state.closing.delete(name);
    // Before showModal(): its focus (autofocus) may already make iOS scroll the page, and a full-screen sheet must be at
    // its final place and size when its field takes the focus
    lockScroll(true);
    if (isFullSheet(sheet.dialog)) followViewport(sheet.dialog);
    if (!sheet.dialog.open) sheet.dialog.showModal();
    state.openName = name;
    sheet.hooks.onShown?.(sheet.dialog, detail);

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
 * handler closes it); then() runs once the sheet is fully closed (after its close animation, when the page is no
 * longer inert). restoreFocus: false leaves the focus to then() instead of returning it to the opener.
 */
export function closeSheet(name = state.openName, then, { restoreFocus = true } = {}) {
    if (!name || state.openName !== name) { then?.(); return; }
    const sheet = state.sheets.get(name);
    if (then) state.pendingAfterClose.push(then);
    if (!restoreFocus) state.skipFocusRestore = true;

    if (sheet.pushed && window.history.state?.gm?.sheet === name) {
        window.history.back();
        // Safety net if popstate does not arrive (should not happen)
        clearTimeout(state.backFallback);
        state.backFallback = setTimeout(() => { if (state.openName === name) finishClose(name); }, 600);
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

/** A UI text from the server (data-guest-strings) with {placeholders} replaced; the key itself if it is missing. */
export function t(key, values = {}) {
    const text = state.strings[key] ?? key;
    return text.replace(/\{(\w+)\}/g, (match, name) => (name in values ? String(values[name]) : match));
}

/** Integer cents → "€ 9,90" / "€9.90" (thousands grouped), in the notation the server uses for this menu. */
export function formatPrice(cents) {
    const { prefix, decimal, group } = state.price;
    const value = Math.round(Number(cents) || 0);
    const sign = value < 0 ? '-' : '';
    const abs = Math.abs(value);
    const whole = String(Math.floor(abs / 100)).replace(/\B(?=(\d{3})+(?!\d))/g, group);
    return `${sign}${prefix}${whole}${decimal}${String(abs % 100).padStart(2, '0')}`;
}

/**
 * Jumps to a category section (id "cat-…"): its heading ends up right under the sticky bars (smooth, instant with
 * reduced motion) and takes the focus. Call it only while no sheet is open (closeSheet(name, then) runs then() after
 * the close). The target is computed from the real bar heights; once the scroll has ended, a safety net corrects
 * the position if the scroll was dropped or stopped short (scrollend where supported, else scroll idle).
 */
export function scrollToCategory(anchorId) {
    const section = document.getElementById(anchorId);
    if (!section) return;
    state.jump?.stop();
    state.heldCategory = anchorId;
    // The chip bar follows once the page scroll has ended, and the focus moves before it starts: nothing else
    // scrolls or takes the focus while the smooth scroll runs
    state.setActiveCategory(anchorId, { reveal: false });
    section.querySelector('h2[tabindex]')?.focus({ preventScroll: true });
    const top = categoryTop(section);
    if (Math.abs(window.scrollY - top) <= 1) { state.revealActiveChip(); return; }
    window.scrollTo({ top, behavior: reducedMotion.matches ? 'auto' : 'smooth' });
    watchJump(section);
}

/** Scroll position that puts the section's top JUMP_OVERLAP px under the sticky bars (top bar + chip bar). */
function categoryTop(section) {
    const nav = state.root?.querySelector('[data-catnav]');
    const bars = nav ? (parseFloat(getComputedStyle(nav).top) || 0) + nav.offsetHeight : 0;
    const top = section.getBoundingClientRect().top + window.scrollY - Math.max(0, bars - JUMP_OVERLAP);
    const max = document.documentElement.scrollHeight - window.innerHeight;
    return Math.round(Math.max(0, Math.min(max, top)));
}

/**
 * Safety net of a jump: once the scroll has ended (scrollend, else 150 ms without scroll events; a scroll that has
 * not moved the page after 700 ms counts as dropped; at the latest after 2.5 s) the position is checked against the
 * target, measured again, and corrected instantly when it is off by more than 2 px (at most twice). Then the chip bar
 * centers the active chip. A touch, wheel, key or a new jump stops it (state.jump.stop()).
 */
function watchJump(section) {
    const hasScrollEnd = 'onscrollend' in window;
    const startY = window.scrollY;
    let corrections = 0, startTimer = 0, idleTimer = 0, maxTimer = 0;

    const clearTimers = () => { clearTimeout(startTimer); clearTimeout(idleTimer); clearTimeout(maxTimer); };
    const stop = () => {
        clearTimers();
        window.removeEventListener('scroll', onScroll);
        window.removeEventListener('scrollend', onScrollEnd);
        if (state.jump === jump) state.jump = null;
    };
    const check = () => {
        if (state.jump !== jump) return;
        clearTimers();
        const top = categoryTop(section);
        // The page may end before the section can reach the bars (on phones the toolbar changes the viewport)
        const cannotGoFurther = top > window.scrollY && window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 2;
        if (Math.abs(window.scrollY - top) > 2 && !cannotGoFurther && corrections < 2) {
            // Instant: the smooth scroll was dropped, stopped short or the page moved under it
            corrections++;
            window.scrollTo({ top, behavior: 'auto' });
            requestAnimationFrame(() => requestAnimationFrame(check));
            return;
        }
        stop();
        state.revealActiveChip();
    };
    const onScroll = () => {
        clearTimeout(startTimer);
        if (!hasScrollEnd) {
            clearTimeout(idleTimer);
            idleTimer = setTimeout(check, 150);
        }
    };
    const onScrollEnd = () => requestAnimationFrame(check);

    const jump = { section, stop };
    state.jump = jump;
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('scrollend', onScrollEnd);
    // No scroll event yet: dropped, unless the page is moving anyway (an engine that reports late)
    startTimer = setTimeout(() => { if (Math.abs(window.scrollY - startY) <= 1) check(); }, 700);
    maxTimer = setTimeout(check, 2500);
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
        // Backdrop tap: the click lands on the <dialog> itself (the panel fills the rest). Only when the press began
        // there too: a press on the panel released over the backdrop (a drag, a text selection) is no backdrop tap
        let pressedOnBackdrop = false;
        dialog.addEventListener('pointerdown', e => { pressedOnBackdrop = e.target === dialog; });
        dialog.addEventListener('click', e => { if (e.target === dialog && pressedOnBackdrop) closeSheet(name); });
        // Closed by the browser itself (e.g. a close watcher ignoring preventDefault): catch up
        // (the event is async: ignore it when the dialog is open again, e.g. the deep link conversion below)
        dialog.addEventListener('close', () => { if (state.openName === name && !dialog.open) closeSheet(name); });
        setUpSheetDrag(dialog, name);
        if (isFullSheet(dialog)) guardFullSheetTouch(dialog);
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
    clearTimeout(state.backFallback);
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
    const restoreFocus = !state.skipFocusRestore;
    state.skipFocusRestore = false;
    closeNow(name, { restoreFocus, animate: true });
}

/** Runs the then() callbacks of closeSheet once no sheet is open and no close animation runs any more. */
function runPendingAfterClose() {
    if (state.openName || state.closing.size > 0 || state.pendingAfterClose.length === 0) return;
    const callbacks = state.pendingAfterClose.splice(0);
    // After the browser's own popstate work (scroll restoration), so a jump wins
    requestAnimationFrame(() => {
        if (state.openName) { state.pendingAfterClose.unshift(...callbacks); return; }   // another sheet opened meanwhile
        callbacks.forEach(fn => {
            try { fn(); } catch (e) { console.error(e); }
        });
    });
}

function closeNow(name, { restoreFocus, animate }) {
    const sheet = state.sheets.get(name);
    if (!sheet?.dialog) return;
    const dialog = sheet.dialog;
    if (state.openName === name) state.openName = null;
    if (name === 'item') state.openItemId = null;
    sheet.pushed = false;
    const scroll = state.lockedScroll;
    lockScroll(false);
    const opener = sheet.opener;
    sheet.opener = null;
    dialog.classList.remove('is-dragging', 'is-settling');

    const done = () => {
        state.closing.delete(name);
        dialog.classList.remove('is-closing');
        if (state.openName === name) return;     // reopened while the close animation ran
        resetDragStyle(dialog);
        if (state.viewport?.dialog === dialog) stopFollowingViewport();
        if (dialog.open) dialog.close();
        sheet.hooks.onClose?.(dialog);
        // Once more now that the dialog is gone (e.g. the keyboard went down meanwhile); before the focus moves back
        // (a deep-linked item continues at its card) and before then() (a category jump wins)
        if (!state.openName && scroll) restoreScroll(scroll);
        // After close(): while the modal dialog is open the rest of the page is inert and cannot take focus
        if (restoreFocus) restoreFocusAfterClose(name, dialog, opener);
        runPendingAfterClose();
    };
    if (animate && dialog.open && !reducedMotion.matches) {
        // A dragged sheet slides on from where the finger left it (the keyframes have no "from")
        state.closing.add(name);
        dialog.classList.add('is-closing');
        let finished = false;
        const onEnd = e => { if (e.target === dialog) finish(); };
        const finish = () => {
            if (finished) return;
            finished = true;
            dialog.removeEventListener('animationend', onEnd);
            done();
        };
        dialog.addEventListener('animationend', onEnd);
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

// --- Sheets: drag down to close (phones) -------------------------------------------------------------------
// The grab handle (its hit area is ≥ 44 px tall, guest-menu.css) and the header band (.gm-sheet-head, except its
// buttons and links) follow the finger: downwards 1:1, upwards a small rubber band; the backdrop fades with the
// distance. Released beyond DRAG_CLOSE_SHARE of the panel height or flicked down → closeSheet (history aware,
// focus back to the opener; the close animation continues from where the panel is), else it springs back.
// Pointer events with capture (touch and mouse); only in bottom-sheet mode (< 768 px).

function setUpSheetDrag(dialog, name) {
    if (isFullSheet(dialog)) return;     // fills the screen on phones: no edge to pull (see the section below)
    const panel = dialog.querySelector('.gm-sheet-panel');
    const zones = [dialog.querySelector('.gm-sheet-handle'), dialog.querySelector('.gm-sheet-head')].filter(Boolean);
    if (!panel || zones.length === 0) return;
    let drag = null;

    const onDown = e => {
        if (drag || !e.isPrimary || (e.pointerType === 'mouse' && e.button !== 0)) return;
        if (centeredSheets.matches || !dialog.open || state.openName !== name || dialog.classList.contains('is-closing')) return;
        if (e.target instanceof Element && e.target.closest('button, a, input, select, textarea, label')) return;   // stays a tap
        drag = { id: e.pointerId, zone: e.currentTarget, startY: e.clientY, y: 0, moved: false,
                 height: panel.getBoundingClientRect().height || 1, samples: [[e.timeStamp, e.clientY]] };
        try { e.currentTarget.setPointerCapture(e.pointerId); } catch { /* the pointer is already gone */ }
    };

    const onMove = e => {
        if (!drag || e.pointerId !== drag.id) return;
        const dy = e.clientY - drag.startY;
        if (!drag.moved) {
            if (Math.abs(dy) < 4) return;       // below the slop a tap stays a tap
            drag.moved = true;
            dialog.classList.remove('is-settling');
            dialog.classList.add('is-dragging');
        }
        drag.samples.push([e.timeStamp, e.clientY]);
        while (drag.samples.length > 2 && e.timeStamp - drag.samples[0][0] > 100) drag.samples.shift();
        // Downwards 1:1, upwards a rubber band of at most 16 px
        drag.y = dy >= 0 ? dy : -16 * (1 - Math.exp(dy / 60));
        dialog.style.translate = `0 ${drag.y}px`;
        dialog.style.setProperty('--gm-drag-fade', String(Math.min(1, Math.max(0, drag.y / drag.height))));
    };

    const onEnd = e => {
        if (!drag || e.pointerId !== drag.id) return;
        const { moved, y, height, samples, zone } = drag;
        drag = null;
        try { zone.releasePointerCapture(e.pointerId); } catch { /* released already */ }
        dialog.classList.remove('is-dragging');
        // Not moved, or closed meanwhile (Escape, back button): the close resets the drag styles
        if (!moved || !dialog.open || state.openName !== name) return;
        const [t0, y0] = samples[0];
        const [t1, y1] = samples[samples.length - 1];
        // px/ms over the last 100 ms of movement, > 0 = downwards; a finger that rested before letting go has none
        const speed = t1 > t0 && e.timeStamp - t1 < 100 ? (y1 - y0) / (t1 - t0) : 0;
        const close = e.type === 'pointerup' && (y > height * DRAG_CLOSE_SHARE || (speed > DRAG_FLICK_SPEED && y > 8));
        if (close) closeSheet(name);       // the panel stays where it is until the close animation takes it down
        else settleBack(dialog);
    };

    for (const zone of zones) {
        zone.addEventListener('pointerdown', onDown);
        zone.addEventListener('pointermove', onMove);
        zone.addEventListener('pointerup', onEnd);
        zone.addEventListener('pointercancel', onEnd);
        // Capture lost without a pointerup (e.g. Escape closed the sheet mid-drag): end the drag, spring back
        zone.addEventListener('lostpointercapture', onEnd);
    }
    dialog.addEventListener('close', () => { drag = null; });
}

/** Springs a dragged sheet back to its place (no animation with reduced motion). */
function settleBack(dialog) {
    if (!reducedMotion.matches) {
        dialog.classList.add('is-settling');
        const end = () => dialog.classList.remove('is-settling');
        dialog.addEventListener('transitionend', e => { if (e.target === dialog) end(); }, { once: true });
        setTimeout(end, 400);
    }
    resetDragStyle(dialog);
}

function resetDragStyle(dialog) {
    dialog.style.removeProperty('translate');
    dialog.style.removeProperty('--gm-drag-fade');
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

// --- Sheets: full-screen on phones (.gm-sheet--full) ---------------------------------------------------------------
// Why the search sheet differs from the others: its field takes the focus when it opens (inside the tap, so iOS opens
// the keyboard), and iOS then scrolls whatever it must to reveal the focused field. The keyboard shrinks only the
// visual viewport; position: fixed and dvh keep referring to the full layout viewport. As a sliding bottom sheet the
// search panel was still below the screen (transform) when its field got the focus; on the operator's iPhone the page
// behind then showed its end above the keyboard and the panel was off-screen (Nov 2026). A full sheet is anchored to
// the top and fades in (guest-menu.css), so its field is at its final place when it gets the focus, and it follows the
// visual viewport while it is open: --gm-vv-top/--gm-vv-height = the visible area above the keyboard, so the results
// end at the keyboard and nothing of the page shows. A touch that could only scroll the page behind it is cancelled.
// No drag to close (a panel that fills the screen has no edge to pull). From 768 px it is a centered dialog like the
// others (the CSS ignores the variables there). window.visualViewport is read when the sheet opens, so a test can
// replace it with a stand-in to simulate a keyboard.

function isFullSheet(dialog) {
    return dialog.classList.contains('gm-sheet--full');
}

function followViewport(dialog) {
    stopFollowingViewport();
    const vv = window.visualViewport;
    if (!vv) return;        // the CSS fallback: the full layout viewport
    const fit = () => {
        // Pinch-zoomed: the visual viewport is a magnified part of the page, not the free area — keep the CSS size
        if (Math.abs((vv.scale || 1) - 1) > 0.01) { clearViewportStyle(dialog); return; }
        dialog.style.setProperty('--gm-vv-top', `${Math.max(0, vv.offsetTop)}px`);
        dialog.style.setProperty('--gm-vv-height', `${vv.height}px`);
    };
    vv.addEventListener('resize', fit);
    vv.addEventListener('scroll', fit);
    state.viewport = {
        dialog,
        stop() {
            vv.removeEventListener('resize', fit);
            vv.removeEventListener('scroll', fit);
            clearViewportStyle(dialog);
        }
    };
    fit();
}

function stopFollowingViewport() {
    state.viewport?.stop();
    state.viewport = null;
}

function clearViewportStyle(dialog) {
    dialog.style.removeProperty('--gm-vv-top');
    dialog.style.removeProperty('--gm-vv-height');
}

/** Full sheets on phones: a one-finger move that cannot scroll the sheet body would scroll or pan the page behind. */
function guardFullSheetTouch(dialog) {
    dialog.addEventListener('touchmove', e => {
        if (centeredSheets.matches || !e.cancelable || e.touches.length > 1) return;     // pinch zoom stays possible
        const target = e.target instanceof Element ? e.target : null;
        if (target?.closest('input, textarea, select')) return;                          // caret and selection
        const body = target?.closest('[data-sheet-body]');
        if (body && body.scrollHeight > body.clientHeight + 1) return;   // the results scroll (overscroll-behavior: contain)
        e.preventDefault();
    }, { passive: false });
}

// One sheet at a time. While it is open <html> does not scroll (gm-locked) and the page's position is remembered:
// something may still move the page behind a sheet (iOS scrolls it to reveal a focused field), the close puts it back.
function lockScroll(lock) {
    if (lock && !state.lockedScroll) state.lockedScroll = { x: window.scrollX, y: window.scrollY };
    if (!lock && state.lockedScroll) {
        restoreScroll(state.lockedScroll);
        state.lockedScroll = null;
    }
    document.documentElement.classList.toggle('gm-locked', lock);
}

function restoreScroll({ x, y }) {
    if (Math.abs(window.scrollX - x) > 1 || Math.abs(window.scrollY - y) > 1) window.scrollTo({ left: x, top: y, behavior: 'auto' });
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
        if (state.openName) {
            // From the categories sheet: the chip turns active right away; the jump starts once the sheet is fully
            // closed (page no longer inert, no focus restoration to the opener — the section heading takes it)
            state.heldCategory = anchor;
            state.setActiveCategory(anchor, { reveal: false });
            closeSheet(state.openName, () => scrollToCategory(anchor), { restoreFocus: false });
        } else {
            scrollToCategory(anchor);
        }
        return;
    }

    const opener = target.closest('[data-sheet-open]');
    if (opener) {
        e.preventDefault();
        openSheet(opener.dataset.sheetOpen, { opener });
        return;
    }

    const leave = target.closest('a[data-sheet-leave]');
    if (leave) {
        const dialog = leave.closest('dialog[data-sheet]');
        if (dialog && state.openName === dialog.dataset.sheet) {
            e.preventDefault();
            const href = leave.href;
            closeSheet(dialog.dataset.sheet, () => location.assign(href));
        }
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
    let revealPending = false;      // the active chip changed without being scrolled into the bar yet

    // The active chip scrolls to the middle of the bar (not during a jump: see scrollToCategory)
    state.revealActiveChip = () => {
        revealPending = false;
        const chip = active && scroller.querySelector(`[data-category-link="${cssEscape(active)}"]`);
        if (!chip) return;
        const left = Math.max(0, Math.round(chip.offsetLeft - (scroller.clientWidth - chip.offsetWidth) / 2));
        if (Math.abs(scroller.scrollLeft - left) < 1) return;
        scroller.scrollTo({ left, behavior: reducedMotion.matches ? 'auto' : 'smooth' });
    };
    state.setActiveCategory = (anchorId, { reveal = true } = {}) => {
        if (anchorId === active) {
            if (reveal && revealPending && !state.jump) state.revealActiveChip();
            return;
        }
        active = anchorId;
        revealPending = true;
        for (const chip of root.querySelectorAll('[data-category-link]')) {
            const on = chip.dataset.categoryLink === anchorId;
            chip.classList.toggle('is-active', on);
            if (on) chip.setAttribute('aria-current', 'true');
            else chip.removeAttribute('aria-current');
        }
        if (reveal) state.revealActiveChip();
    };

    // Sections touching the band between the sticky bars and 45 % of the viewport are "visible";
    // the first of them (document order) is the active one.
    const visible = new Set();
    let observer = null;
    // Sections hidden by the filter (hidden attribute) never count
    const shown = () => sections.filter(s => !s.hidden);
    const pick = (options) => {
        if (state.heldCategory) { state.setActiveCategory(state.heldCategory, options); return; }
        const candidates = shown();
        if (candidates.length === 0) return;
        const atBottom = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 4;
        if (atBottom && window.scrollY > 0) { state.setActiveCategory(candidates[candidates.length - 1].id, options); return; }
        const first = candidates.find(s => visible.has(s.id));
        state.setActiveCategory((first ?? candidates[0]).id, options);
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
    // Touching or scrolling by hand ends a running jump and the hold of its target (the jump itself fires only
    // scroll events); the chip bar follows on the next scroll, not now (a tap on a chip starts a new jump)
    for (const type of ['wheel', 'touchstart', 'pointerdown', 'keydown']) {
        window.addEventListener(type, () => {
            state.jump?.stop();
            if (state.heldCategory) { state.heldCategory = null; pick({ reveal: false }); }
        }, { passive: true, capture: true });
    }

    // Category sheet: the focus goes to the active chip (a ring only for keyboard use, see setUpFocusModality)
    registerSheet('categories', {
        onOpen(dialog) {
            requestAnimationFrame(() => dialog.querySelector('.gm-chip.is-active')?.focus({ preventScroll: false }));
        }
    });

    active = sections[0].id;
}

// --- Focus rings only for keyboard use ------------------------------------------------------------------------
// Script focus (a sheet's first control, the active chip, the section heading after a jump, the opener after a
// close) counts as :focus-visible in WebKit even after a tap, so phones showed rings. An element that gets the focus
// while the last input was a pointer (or before any input) carries data-gm-quiet, and guest-menu.css hides its
// ring. The first key press removes the mark from the focused element: keyboard users always see where they are.

function setUpFocusModality() {
    let pointer = true;
    const modifierKeys = new Set(['Shift', 'Control', 'Alt', 'Meta', 'CapsLock', 'Fn']);
    document.addEventListener('pointerdown', () => { pointer = true; }, true);
    document.addEventListener('keydown', e => {
        if (modifierKeys.has(e.key)) return;
        pointer = false;
        if (document.activeElement instanceof Element) document.activeElement.removeAttribute('data-gm-quiet');
    }, true);
    document.addEventListener('focusin', e => {
        if (pointer && e.target instanceof Element) e.target.setAttribute('data-gm-quiet', '');
    }, true);
    document.addEventListener('focusout', e => {
        if (e.target instanceof Element) e.target.removeAttribute('data-gm-quiet');
    }, true);
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
            basePath: state.basePath, lang: state.lang, openSheet, closeSheet, openItem, trackItemClick, onItemOpen,
            registerSheet, refreshCategoryNav, t, formatPrice, setThemeColor
        });
    } catch (e) {
        console.error(`guest-menu behavior "${behavior.name}" failed`, e);
    }
}

/** Texts and price notation of the menu's UI language (GuestText.ScriptJson, attribute data-guest-strings). */
function readStrings(root) {
    try {
        const data = JSON.parse(root.dataset.guestStrings || '{}');
        if (typeof data.lang === 'string') state.lang = data.lang;
        if (data.strings && typeof data.strings === 'object') state.strings = data.strings;
        if (data.price && typeof data.price === 'object') state.price = { ...state.price, ...data.price };
    } catch (e) {
        console.error('guest-menu: data-guest-strings is not valid JSON', e);
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
registerBehavior('preview', initPreview);

// --- Start ------------------------------------------------------------------------------------------------

function init() {
    const root = document.querySelector('[data-guest-root]');
    if (root) {
        state.root = root;
        state.menuId = root.dataset.menuId ?? null;
        state.basePath = root.dataset.basePath || location.pathname;
        state.trackClicks = root.dataset.trackClicks === 'true';
        state.isPreview = root.dataset.preview === 'true';
        readStrings(root);

        setUpFocusModality();
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
