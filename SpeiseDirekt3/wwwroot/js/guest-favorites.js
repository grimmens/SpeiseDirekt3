/**
 * guest-favorites.js — the favourites list ("Merkliste") of the guest menu. A behavior of guest-menu.js (which imports
 * and registers it; this module must not import guest-menu.js, see there).
 *
 * Hearts ([data-fav-toggle], data-fav-item = item id) on every card and list row and one in the item sheet (it follows
 * the item shown, via onItemOpen) add or remove an item; aria-pressed and the aria-label (server texts in
 * data-label-add / data-label-remove) follow. The floating button ([data-fav-fab], count) is visible while the list is
 * not empty and opens the "favorites" sheet. The sheet's rows are server-rendered (GuestFavoritesSheet: one hidden row
 * per item in menu order, with the variant <select> and its prices); this module shows the remembered ones, sets the
 * variant and quantity (1–20), writes line totals ("2 × € 3,40" below them) and the sum in the menu's price notation
 * (ctx.formatPrice: "€ 3,40" on German menus, "€3.40" on all others) and handles remove and "Liste leeren" with a
 * confirmation step. It never builds markup from menu text and never talks to the server.
 *
 * Storage: localStorage "sd-merkliste:<menuId>" = { v: 1, items: [{ id, variant?, qty }] }. Unknown items are
 * dropped and unknown variants fall back to the default (the menu changed); every access is in try/catch and the list
 * works in memory when storage is blocked. Other tabs of the same menu follow via the storage event.
 */

const STORAGE_PREFIX = 'sd-merkliste:';
const STORAGE_VERSION = 1;
const MAX_QTY = 20;

export function initFavorites(ctx) {
    const { root } = ctx;
    const sheet = root.querySelector('dialog[data-sheet="favorites"]');
    if (!sheet) return;

    const fab = root.querySelector('[data-fav-fab]');
    const fabCount = fab?.querySelector('[data-fav-count]');
    const sum = sheet.querySelector('[data-fav-sum]');
    const empty = sheet.querySelector('[data-fav-empty]');
    const footer = sheet.querySelector('[data-fav-footer]');
    const actions = sheet.querySelector('[data-fav-actions]');
    const confirm = sheet.querySelector('[data-fav-confirm]');
    const clearButton = sheet.querySelector('[data-fav-clear]');
    const rows = new Map([...sheet.querySelectorAll('[data-fav-row]')].map(row => [row.dataset.itemId, {
        row,
        select: row.querySelector('[data-fav-variant]'),
        qty: row.querySelector('[data-fav-qty]'),
        dec: row.querySelector('[data-fav-dec]'),
        inc: row.querySelector('[data-fav-inc]'),
        line: row.querySelector('[data-fav-line]'),
        unit: row.querySelector('[data-fav-unit]'),
        price: cents(row.dataset.price)
    }]));
    const storageKey = STORAGE_PREFIX + (ctx.menuId ?? location.pathname);
    const euro = ctx.formatPrice;

    /** itemId → { variant: string | null, qty: number }, in the order the guest added them */
    let list = sanitize(readStorage(storageKey), rows);

    function persist() {
        writeStorage(storageKey, list.size === 0 ? null : {
            v: STORAGE_VERSION,
            items: [...list].map(([id, e]) => (e.variant ? { id, variant: e.variant, qty: e.qty } : { id, qty: e.qty }))
        });
    }

    function render() {
        for (const heart of root.querySelectorAll('[data-fav-toggle]')) {
            const on = !!heart.dataset.favItem && list.has(heart.dataset.favItem);
            heart.setAttribute('aria-pressed', String(on));
            const label = on ? heart.dataset.labelRemove : heart.dataset.labelAdd;
            if (label) heart.setAttribute('aria-label', label);
        }

        const count = list.size;
        if (fab) fab.hidden = count === 0;
        if (fabCount) fabCount.textContent = String(count);
        root.toggleAttribute('data-fav-active', count > 0);   // room for the floating button below the last card

        let total = 0;
        for (const [id, r] of rows) {
            const entry = list.get(id);
            if (r.row.hidden === !!entry) r.row.hidden = !entry;
            if (!entry) continue;
            const option = selectedOption(r, entry);
            if (r.select && option && r.select.value !== option.value) r.select.value = option.value;
            const unit = option ? cents(option.dataset.price) : r.price;
            const line = unit * entry.qty;
            total += line;
            if (r.qty.textContent !== String(entry.qty)) r.qty.textContent = String(entry.qty);   // aria-live: only on change
            r.dec.setAttribute('aria-disabled', String(entry.qty <= 1));
            r.inc.setAttribute('aria-disabled', String(entry.qty >= MAX_QTY));
            r.line.textContent = euro(line);
            if (r.unit) {
                r.unit.hidden = entry.qty <= 1;
                r.unit.textContent = entry.qty > 1 ? ctx.t('favoriteUnit', { qty: entry.qty, price: euro(unit) }) : '';
            }
        }
        if (sum) sum.textContent = euro(total);
        if (empty) empty.hidden = count > 0;
        if (footer) footer.hidden = count === 0;
    }

    function showConfirm(show) {
        if (confirm) confirm.hidden = !show;
        if (actions) actions.hidden = show;
    }

    function toggle(id) {
        if (!rows.has(id)) return;
        if (list.has(id)) list.delete(id);
        else list.set(id, { variant: null, qty: 1 });
        persist();
        render();
    }

    function remove(id) {
        const order = [...rows.keys()].filter(k => list.has(k));
        const index = order.indexOf(id);
        list.delete(id);
        persist();
        render();
        // Continue at the next row (or the previous one); an empty list sends the focus to "Schließen"
        const next = order[index + 1] ?? order[index - 1];
        const target = next ? rows.get(next).row.querySelector('[data-fav-remove]') : sheet.querySelector('[data-sheet-close]');
        target?.focus();
    }

    // Hearts anywhere on the page (cards, rows, item sheet)
    root.addEventListener('click', e => {
        const target = e.target instanceof Element ? e.target : null;
        const heart = target?.closest('[data-fav-toggle]');
        if (heart) {
            e.preventDefault();
            toggle(heart.dataset.favItem);
            return;
        }
        if (!target || !sheet.contains(target)) return;

        const row = target.closest('[data-fav-row]');
        const entry = row ? list.get(row.dataset.itemId) : null;
        if (entry && target.closest('[data-fav-inc], [data-fav-dec]')) {
            const step = target.closest('[data-fav-inc]') ? 1 : -1;
            const qty = Math.min(MAX_QTY, Math.max(1, entry.qty + step));
            if (qty !== entry.qty) { entry.qty = qty; persist(); render(); }
        } else if (entry && target.closest('[data-fav-remove]')) {
            remove(row.dataset.itemId);
        } else if (target.closest('[data-fav-clear]')) {
            showConfirm(true);
            confirm?.querySelector('[data-fav-confirm-no]')?.focus();
        } else if (target.closest('[data-fav-confirm-no]')) {
            showConfirm(false);
            clearButton?.focus();
        } else if (target.closest('[data-fav-confirm-yes]')) {
            list.clear();
            persist();
            render();
            showConfirm(false);
            sheet.querySelector('[data-sheet-close]')?.focus();
        }
    });

    sheet.addEventListener('change', e => {
        const select = e.target instanceof HTMLSelectElement && e.target.matches('[data-fav-variant]') ? e.target : null;
        const entry = select ? list.get(select.closest('[data-fav-row]')?.dataset.itemId) : null;
        if (!entry) return;
        entry.variant = select.value;
        persist();
        render();
    });

    // The item sheet's heart follows the item shown (labels copied from that item's card heart)
    ctx.onItemOpen((dialog, itemId) => {
        const heart = dialog.querySelector('[data-fav-sheet]');
        if (!heart) return;
        heart.dataset.favItem = itemId;
        const source = root.querySelector(`[data-fav-toggle][data-fav-item="${cssEscape(itemId)}"]:not([data-fav-sheet])`);
        for (const name of ['labelAdd', 'labelRemove']) {
            if (source?.dataset[name]) heart.dataset[name] = source.dataset[name];
        }
        render();
    });

    ctx.registerSheet('favorites', {
        onOpen() { showConfirm(false); render(); }
    });

    // Another tab of the same menu changed the list
    window.addEventListener('storage', e => {
        if (e.key !== null && e.key !== storageKey) return;
        list = sanitize(readStorage(storageKey), rows);
        render();
    });

    render();
}

/** The option to use for an entry: its stored variant, or the first (default) one. */
function selectedOption(r, entry) {
    if (!r.select) return null;
    const options = [...r.select.options];
    return options.find(o => o.value === entry.variant) ?? options[0] ?? null;
}

/** Stored data → list; unknown items are dropped, unknown variants reset to the default, quantities clamped. */
function sanitize(data, rows) {
    const list = new Map();
    if (data?.v !== STORAGE_VERSION || !Array.isArray(data.items)) return list;
    for (const item of data.items) {
        const id = typeof item?.id === 'string' ? item.id : null;
        const r = id ? rows.get(id) : null;
        if (!r || list.has(id)) continue;
        const variant = typeof item.variant === 'string' && r.select && [...r.select.options].some(o => o.value === item.variant)
            ? item.variant : null;
        const qty = Number.isInteger(item.qty) ? Math.min(MAX_QTY, Math.max(1, item.qty)) : 1;
        list.set(id, { variant, qty });
    }
    return list;
}

function readStorage(key) {
    try {
        const raw = window.localStorage.getItem(key);
        return raw ? JSON.parse(raw) : null;
    } catch {
        return null;   // blocked or corrupt: start empty (the list then lives in memory)
    }
}

function writeStorage(key, data) {
    try {
        if (data) window.localStorage.setItem(key, JSON.stringify(data));
        else window.localStorage.removeItem(key);
    } catch {
        // storage blocked or full: the list keeps working in memory for this page
    }
}

/** "9.90" → 990 (integer cents, no floating point sums). */
function cents(value) {
    const n = Number.parseFloat(value);
    return Number.isFinite(n) ? Math.round(n * 100) : 0;
}

function cssEscape(value) {
    return window.CSS?.escape ? CSS.escape(value) : String(value).replace(/["\\]/g, '\\$&');
}
