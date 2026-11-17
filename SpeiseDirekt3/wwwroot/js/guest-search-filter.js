/**
 * guest-search-filter.js — search sheet and filter sheet of the guest menu. A behavior of guest-menu.js (which imports
 * and registers it; this module must not import guest-menu.js, see there). No network requests, no markup built from
 * menu text: the server renders everything (Components/Guest: GuestSearchSheet, GuestFilterSheet, GuestFilterNotice,
 * GuestFilterButton), this module only shows/hides it and writes counts. Every text it writes comes from the server in
 * the menu's UI language (ctx.t: German menus German, all others English).
 *
 * Filter: label chips (aria-pressed, data-filter-label = ItemLabel key; several = all must match, matched against the
 * cards' data-filter-labels, where a vegan item is vegetarian too) and allergen checkboxes (data-filter-allergen =
 * code; a checked allergen hides every item whose data-allergens contains it). The sheet edits a draft; "n Gerichte
 * anzeigen" applies it. Applied, non-matching cards/rows, sections without visible items and their category chips get
 * the hidden attribute; the notice at the top of the menu shows the count; the top bar button a badge. The applied
 * filter is kept per menu in sessionStorage (sd-filter:<menuId>), so reload and back/forward keep it and a new visit
 * starts clean.
 *
 * Search: shows the server-rendered result rows whose data-search-text contains every word of the query (case,
 * accents and ß ignored) and wraps the matched parts of their [data-hl] elements in <mark>. Results respect the
 * filter; while one is active a note says so (with how many matches it hides) and offers a reset. On phones the search
 * sheet is a full-screen panel (gm-sheet--full, guest-menu.js) whose field is focused inside the opening tap.
 */

const FILTER_STORAGE_PREFIX = 'sd-filter:';
const FILTER_STORAGE_VERSION = 1;
const NO_FILTER = Object.freeze({ labels: [], allergens: [] });

export function initSearchAndFilter(ctx) {
    const filter = setUpFilter(ctx);
    setUpSearch(ctx, filter);
}

// --- Filter ------------------------------------------------------------------------------------------------

function setUpFilter(ctx) {
    const { root } = ctx;
    const listeners = [];
    const entries = [...root.querySelectorAll('section[data-category-id] > ul > li[data-item-id]')].map(el => ({
        el,
        id: el.dataset.itemId,
        section: el.closest('section'),
        labels: tokens(el.dataset.filterLabels),
        allergens: tokens(el.dataset.allergens)
    }));
    const byId = new Map(entries.map(e => [e.id, e]));
    const sections = [...new Set(entries.map(e => e.section))];

    let applied = NO_FILTER;
    const api = {
        active: () => isActive(applied),
        allows: id => { const e = byId.get(id); return !e || !isActive(applied) || matches(e, applied); },
        onChange: fn => listeners.push(fn)
    };

    const sheet = root.querySelector('dialog[data-sheet="filter"]');
    if (!sheet) return api;   // the menu has neither labels nor allergens: no filter

    const chips = [...sheet.querySelectorAll('[data-filter-label]')];
    const boxes = [...sheet.querySelectorAll('[data-filter-allergen]')];
    const applyButton = sheet.querySelector('[data-filter-apply]');
    const button = root.querySelector('[data-sheet-open="filter"]');
    const badge = button?.querySelector('[data-filter-badge]');
    const notice = root.querySelector('[data-filter-notice]');
    const noticeCount = notice?.querySelector('[data-filter-count]');
    const empty = root.querySelector('[data-filter-empty]');
    const main = root.querySelector('main');
    const categoryLinks = [...root.querySelectorAll('[data-category-link]')];
    const labelOrder = chips.map(c => c.dataset.filterLabel);
    const allergenOrder = boxes.map(b => b.dataset.filterAllergen);
    const storageKey = FILTER_STORAGE_PREFIX + (ctx.menuId ?? location.pathname);

    /** Only known keys/codes, each once, in the order of the sheet (a changed menu drops unknown ones). */
    const clean = f => ({
        labels: labelOrder.filter(k => f?.labels?.includes?.(k)),
        allergens: allergenOrder.filter(c => f?.allergens?.includes?.(c))
    });
    const countFor = f => entries.reduce((n, e) => n + (matches(e, f) ? 1 : 0), 0);

    const readDraft = () => clean({
        labels: chips.filter(c => c.getAttribute('aria-pressed') === 'true').map(c => c.dataset.filterLabel),
        allergens: boxes.filter(b => b.checked).map(b => b.dataset.filterAllergen)
    });
    const writeDraft = f => {
        chips.forEach(c => c.setAttribute('aria-pressed', String(f.labels.includes(c.dataset.filterLabel))));
        boxes.forEach(b => { b.checked = f.allergens.includes(b.dataset.filterAllergen); });
    };
    const dishes = n => ctx.t(n === 1 ? 'dishOne' : 'dishOther', { n });
    const updateApplyButton = () => {
        applyButton.textContent = ctx.t('filterApply', { dishes: dishes(countFor(readDraft())) });
    };

    function render() {
        const active = isActive(applied);
        const shownSections = new Set();
        let shown = 0;
        for (const e of entries) {
            const show = !active || matches(e, applied);
            if (e.el.hidden === show) e.el.hidden = !show;
            if (show) { shown++; shownSections.add(e.section); }
        }
        for (const s of sections) s.hidden = !shownSections.has(s);
        for (const link of categoryLinks) {
            link.hidden = document.getElementById(link.dataset.categoryLink)?.hidden === true;
        }

        root.toggleAttribute('data-filter-active', active);
        if (notice) notice.hidden = !active;
        if (noticeCount) noticeCount.textContent = dishes(shown);
        if (empty) empty.hidden = !(active && shown === 0);

        const n = applied.labels.length + applied.allergens.length;
        if (badge) { badge.textContent = String(n); badge.hidden = n === 0; }
        button?.setAttribute('aria-label', n === 0 ? ctx.t('filterButton') : ctx.t('filterButtonActive', { n }));

        ctx.refreshCategoryNav?.();
        listeners.forEach(fn => fn());
    }

    function setApplied(f) {
        applied = clean(f);
        saveFilter(storageKey, applied);
        render();
    }

    /** After applying: when the guest is further down, continue at the top of the (filtered) menu. */
    function revealTop() {
        if (!main) return;
        const offset = parseFloat(getComputedStyle(root).getPropertyValue('--gm-sticky-offset')) || 112;
        if (main.getBoundingClientRect().top < offset - 1) {
            window.scrollTo({ top: Math.max(0, main.getBoundingClientRect().top + window.scrollY - offset), behavior: 'auto' });
        }
    }

    chips.forEach(chip => chip.addEventListener('click', () => {
        chip.setAttribute('aria-pressed', String(chip.getAttribute('aria-pressed') !== 'true'));
        updateApplyButton();
    }));
    boxes.forEach(box => box.addEventListener('change', updateApplyButton));
    sheet.querySelector('[data-filter-reset]')?.addEventListener('click', () => {
        writeDraft(NO_FILTER);
        updateApplyButton();
    });
    applyButton.addEventListener('click', () => {
        setApplied(readDraft());
        ctx.closeSheet('filter', revealTop);
    });

    // "Zurücksetzen" in the notice, the empty state and the search sheet
    root.addEventListener('click', e => {
        const reset = e.target instanceof Element ? e.target.closest('[data-filter-reset-all]') : null;
        if (!reset) return;
        e.preventDefault();
        const inSheet = reset.closest('dialog[data-sheet]');
        setApplied(NO_FILTER);
        if (!inSheet) {
            // The button disappears with the notice: continue at the filter button
            button?.focus({ preventScroll: true });
            revealTop();
        }
    });

    ctx.registerSheet('filter', {
        onOpen() { writeDraft(applied); updateApplyButton(); }
    });

    applied = clean(loadFilter(storageKey));
    render();
    return api;
}

function isActive(f) {
    return f.labels.length + f.allergens.length > 0;
}

function matches(entry, f) {
    return f.labels.every(k => entry.labels.has(k)) && !f.allergens.some(c => entry.allergens.has(c));
}

function loadFilter(key) {
    try {
        const raw = window.sessionStorage.getItem(key);
        if (!raw) return NO_FILTER;
        const data = JSON.parse(raw);
        if (data?.v !== FILTER_STORAGE_VERSION) return NO_FILTER;
        return {
            labels: Array.isArray(data.labels) ? data.labels.filter(x => typeof x === 'string') : [],
            allergens: Array.isArray(data.allergens) ? data.allergens.filter(x => typeof x === 'string') : []
        };
    } catch {
        return NO_FILTER;
    }
}

function saveFilter(key, f) {
    try {
        if (isActive(f)) window.sessionStorage.setItem(key, JSON.stringify({ v: FILTER_STORAGE_VERSION, ...f }));
        else window.sessionStorage.removeItem(key);
    } catch {
        // storage blocked (private mode, embedded): the filter lasts until the page is left
    }
}

// --- Search ------------------------------------------------------------------------------------------------

function setUpSearch(ctx, filter) {
    const container = ctx.root.querySelector('[data-search]');
    if (!container) return;
    const input = container.querySelector('[data-search-input]');
    const clear = container.querySelector('[data-search-clear]');
    const status = container.querySelector('[data-search-status]');
    const note = container.querySelector('[data-search-filter-note]');
    const noteText = note?.querySelector('[data-search-filter-text]');
    const empty = container.querySelector('[data-search-empty]');
    const emptyText = empty?.querySelector('[data-search-empty-text]');
    const rows = [...container.querySelectorAll('[data-search-row]')].map(row => ({
        row,
        id: row.dataset.itemId,
        text: normalize(row.dataset.searchText),
        parts: [...row.querySelectorAll('[data-hl]')].map(el => ({ el, text: el.textContent, map: null, marked: false }))
    }));

    const update = () => {
        const raw = input.value.trim();
        const terms = [...new Set(normalize(raw).split(/\s+/).filter(Boolean))];
        let count = 0;
        let hiddenByFilter = 0;
        for (const r of rows) {
            const found = terms.length > 0 && terms.every(t => r.text.includes(t));
            const show = found && filter.allows(r.id);
            if (found && !show) hiddenByFilter++;
            if (r.row.hidden === show) r.row.hidden = !show;
            if (show) { count++; highlight(r.parts, terms); }
        }

        const none = terms.length > 0 && count === 0;
        status.textContent = terms.length === 0 ? ''
            : none ? ctx.t('searchNoResults', { query: raw })
            : ctx.t(count === 1 ? 'searchResultOne' : 'searchResultOther', { n: count });
        status.classList.toggle('gm-visually-hidden', none);   // the empty state shows the same text, larger
        if (empty) empty.hidden = !none;
        if (emptyText) emptyText.textContent = none ? ctx.t('searchNoResults', { query: raw }) : '';

        if (note) {
            const show = filter.active() && terms.length > 0;
            note.hidden = !show;
            if (show && noteText) {
                noteText.textContent = hiddenByFilter === 0 ? ctx.t('searchFilterOnly')
                    : ctx.t(hiddenByFilter === 1 ? 'searchFilterHiddenOne' : 'searchFilterHiddenOther', { n: hiddenByFilter });
            }
        }
        if (clear) clear.hidden = raw.length === 0;
    };

    input.addEventListener('input', update);
    clear?.addEventListener('click', () => { input.value = ''; update(); input.focus(); });
    input.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); input.blur(); } });
    filter.onChange(() => {
        // e.g. "Filter zurücksetzen" in the search sheet: results and note follow, the focus stays in the field
        update();
        const focus = document.activeElement;
        if (container.closest('dialog')?.open && (!container.contains(focus) || note?.contains(focus))) input.focus();
    });

    ctx.registerSheet('search', {
        onOpen() { update(); },
        // Right after showModal() and still inside the tap: iOS opens the keyboard only for a focus during the gesture.
        // The field already has it in most browsers (autofocus); either way it sits at its final place by now (on phones a
        // full-screen panel that fades in, see guest-menu.js), so iOS has nothing to scroll to reveal it.
        onShown() { if (document.activeElement !== input) input.focus({ preventScroll: true }); }
    });
}

/** Case-, accent- and ß-insensitive form used for matching ("Tiramisù" → "tiramisu", "Straße" → "strasse"). */
function normalize(text) {
    return (text ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/ß/g, 'ss');
}

/**
 * Wraps the parts of each element's original text that match a term in <mark class="gm-mark">. Built from text nodes
 * and createElement only (never innerHTML), so menu text stays text.
 */
function highlight(parts, terms) {
    for (const part of parts) {
        const ranges = findRanges(part, terms);
        if (ranges.length === 0) {
            if (part.marked) { part.el.textContent = part.text; part.marked = false; }
            continue;
        }
        const fragment = document.createDocumentFragment();
        let pos = 0;
        for (const [start, end] of ranges) {
            if (start > pos) fragment.append(part.text.slice(pos, start));
            const mark = document.createElement('mark');
            mark.className = 'gm-mark';
            mark.textContent = part.text.slice(start, end);
            fragment.append(mark);
            pos = end;
        }
        if (pos < part.text.length) fragment.append(part.text.slice(pos));
        part.el.replaceChildren(fragment);
        part.marked = true;
    }
}

/** Ranges [start, end) in the original text of every occurrence of a term in its normalized form, merged. */
function findRanges(part, terms) {
    const { norm, starts, ends } = part.map ??= mapNormalized(part.text);
    const ranges = [];
    for (const term of terms) {
        for (let at = norm.indexOf(term); at !== -1; at = norm.indexOf(term, at + 1)) {
            ranges.push([starts[at], ends[at + term.length - 1]]);
        }
    }
    ranges.sort((a, b) => a[0] - b[0]);
    const merged = [];
    for (const r of ranges) {
        const last = merged[merged.length - 1];
        if (last && r[0] <= last[1]) last[1] = Math.max(last[1], r[1]);
        else merged.push([...r]);
    }
    return merged;
}

/**
 * The normalized text plus, per normalized character, where its source character starts and ends in the original
 * (normalizing can change the length: "ß" → "ss", combining accents disappear).
 */
function mapNormalized(text) {
    let norm = '';
    const starts = [];
    const ends = [];
    let index = 0;
    for (const ch of text) {
        const n = normalize(ch);
        if (n.length === 0 && ends.length > 0) ends[ends.length - 1] = index + ch.length;  // accent joins its letter
        for (let k = 0; k < n.length; k++) { starts.push(index); ends.push(index + ch.length); }
        norm += n;
        index += ch.length;
    }
    return { norm, starts, ends };
}

// --- Helpers -----------------------------------------------------------------------------------------------

function tokens(value) {
    return new Set((value ?? '').split(/\s+/).filter(Boolean));
}

