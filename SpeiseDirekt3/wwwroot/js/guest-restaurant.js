/**
 * guest-restaurant.js — restaurant profile and item sharing on the static guest menu.
 *
 * Imported and registered by guest-menu.js (registerBehavior('restaurant', initRestaurant)). Does NOT import
 * guest-menu.js itself: that module is loaded by a fingerprinted URL, an import from here would be a second
 * instance with its own state. Everything it needs comes in ctx (see the header of guest-menu.js).
 *
 * The info sheet needs no code here: the info button is <a href="#gm-info" data-sheet-open="info"> (without JS a
 * jump to the footer, with JS the generic sheet behaviour of guest-menu.js).
 *
 * Share ("Teilen" in the item sheet, [data-share], markup in GuestItemDetail.razor):
 *   1. navigator.share({ title, text, url }) — a cancelled share (AbortError) is not an error;
 *   2. otherwise the clipboard: "Link kopiert" / "Link copied" (ctx.t('shareCopied')) in [data-share-status] (role="status");
 *   3. otherwise the link in the read-only field [data-share-fallback], selected for copying by hand.
 * The url is always the public page /menu/{menuId}?item={itemId} — also from /qr/… and from the owner preview.
 * No tracking request for sharing.
 */

const STATUS_CLEAR_MS = 5000;

/** @param {{ root: HTMLElement, menuId: string|null, t: (key: string, values?: object) => string }} ctx */
export function initRestaurant(ctx) {
    ctx.root?.addEventListener('click', e => {
        const button = e.target instanceof Element ? e.target.closest('[data-share]') : null;
        if (!button) return;
        e.preventDefault();
        shareItem(ctx, button);
    });
}

/** Public, canonical link of an item: /menu/{menuId}?item={itemId} on this origin. */
export function publicItemUrl(menuId, itemId) {
    const url = new URL(`/menu/${encodeURIComponent(menuId)}`, location.origin);
    url.searchParams.set('item', itemId);
    return url.href;
}

async function shareItem(ctx, button) {
    const detail = button.closest('[data-item-id]');
    const itemId = detail?.dataset.itemId;
    if (!itemId || !ctx.menuId) return;

    const url = publicItemUrl(ctx.menuId, itemId);
    const title = button.dataset.shareTitle || document.title;
    const site = document.querySelector('meta[property="og:site_name"]')?.content;
    const text = site && site !== title ? `${title} – ${site}` : title;
    const status = detail.querySelector('[data-share-status]');
    const fallback = detail.querySelector('[data-share-fallback]');
    setStatus(status, '');
    if (fallback) fallback.hidden = true;

    if (typeof navigator.share === 'function') {
        const data = { title, text, url };
        let supported = true;
        try { supported = typeof navigator.canShare !== 'function' || navigator.canShare(data); } catch { supported = false; }
        if (supported) {
            try {
                await navigator.share(data);
                return;
            } catch (e) {
                if (e?.name === 'AbortError') return;      // the guest closed the share dialog
                // NotAllowedError, a share target that failed, …: offer the link another way
            }
        }
    }

    if (await copyToClipboard(url)) {
        setStatus(status, ctx.t('shareCopied'));
        return;
    }

    // Last resort: the link in a read-only field, selected (its label "Link zum Kopieren" / "Link to copy" is read on focus)
    if (fallback) {
        const field = fallback.querySelector('[data-share-url]');
        fallback.hidden = false;
        if (field) {
            field.value = url;
            field.focus();
            field.select();
        }
    }
}

async function copyToClipboard(text) {
    try {
        if (!navigator.clipboard?.writeText) return false;
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

const clearTimers = new WeakMap();

/** Shows a short message in the role="status" element; it disappears after a few seconds. */
function setStatus(element, message) {
    if (!element) return;
    clearTimeout(clearTimers.get(element));
    element.textContent = message;
    if (message) clearTimers.set(element, setTimeout(() => { element.textContent = ''; }, STATUS_CLEAR_MS));
}
