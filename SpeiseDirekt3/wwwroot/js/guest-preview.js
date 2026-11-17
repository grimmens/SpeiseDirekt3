/**
 * guest-preview.js — the menu editor's live preview. A behavior of guest-menu.js (which imports and registers it; this
 * module must not import guest-menu.js, see there).
 *
 * Active ONLY on the owner preview (/menu/preview/{id}: the server renders data-preview="true" there and nowhere else)
 * and only inside a frame. The public pages /menu/{id} and /qr/{id} never listen.
 *
 * The menu editor (js/menu-preview-draft.js) posts the unsaved form values to this frame:
 *   { type: 'sd-preview-draft', v: 1, name: string, description: string, theme: string }
 * Accepted only from the parent window (event.source === window.parent) of the same origin (event.origin ===
 * location.origin) and only in exactly that shape (no other keys, strings of bounded length, a known theme key from
 * data-preview-themes). The texts are written with textContent only — never as markup:
 *   - name: every [data-menu-name] (hero h1, top bar title) and document.title ("<name> – <restaurant or Speisekarte/Menu>",
 *     left alone while an item sheet came from ?item=); an empty name shows the saved one (data-preview-saved-name);
 *     the restaurant line above the h1 hides while the name equals the restaurant's (as on the server);
 *   - description: [data-menu-description] (hidden while empty);
 *   - theme: data-theme of the root in place (guest-themes.css holds every theme), then the browser-chrome colors.
 * The language is no message: the editor reloads the frame with ?lang= (the UI texts are rendered by the server).
 * After loading, the frame announces itself to the parent ({ type: 'sd-preview-ready' }), so the editor sends its
 * current draft again — a reload (language change) never loses unsaved text.
 */

const DRAFT_TYPE = 'sd-preview-draft';
const READY_TYPE = 'sd-preview-ready';
const MAX_NAME = 300;
const MAX_DESCRIPTION = 2000;
const DRAFT_KEYS = new Set(['type', 'v', 'name', 'description', 'theme']);

export function initPreview(ctx) {
    const { root } = ctx;
    if (!ctx.isPreview || root.dataset.preview !== 'true' || window.parent === window) return;

    const savedName = root.dataset.previewSavedName ?? '';
    const themes = new Set((root.dataset.previewThemes ?? '').split(/\s+/).filter(Boolean));
    const restaurant = root.dataset.previewRestaurant ?? '';
    const menuWord = root.dataset.previewMenuWord ?? '';
    const itemFromServer = root.dataset.previewItemOpen === 'true';
    const nameTargets = [...root.querySelectorAll('[data-menu-name]')];
    const description = root.querySelector('[data-menu-description]');
    const restaurantLine = root.querySelector('[data-hero-restaurant]');
    const brand = root.querySelector('[data-hero-brand]');

    function apply(draft) {
        const name = draft.name.trim() || savedName;
        for (const el of nameTargets) {
            if (el.textContent !== name) el.textContent = name;
        }
        const showsRestaurant = restaurant !== '' && !sameName(name, restaurant);
        if (!itemFromServer) document.title = `${name} – ${showsRestaurant ? restaurant : menuWord}`;
        if (restaurantLine) restaurantLine.hidden = !showsRestaurant;
        if (brand) brand.hidden = !brand.querySelector('img') && !showsRestaurant;

        if (description) {
            if (description.textContent !== draft.description) description.textContent = draft.description;
            description.hidden = draft.description.trim() === '';
        }

        if (root.dataset.theme !== draft.theme) {
            root.dataset.theme = draft.theme;
            const scheme = getComputedStyle(document.documentElement).colorScheme;
            document.querySelector('meta[name="color-scheme"]')?.setAttribute('content', scheme.includes('dark') ? 'dark' : 'light');
            ctx.setThemeColor?.();
        }
    }

    /** The draft, or null when the message is anything but exactly the expected shape. */
    function parse(data) {
        if (!data || typeof data !== 'object' || Array.isArray(data)) return null;
        if (data.type !== DRAFT_TYPE || data.v !== 1) return null;
        if (!Object.keys(data).every(k => DRAFT_KEYS.has(k))) return null;
        const { name, description: text, theme } = data;
        if (typeof name !== 'string' || name.length > MAX_NAME) return null;
        if (typeof text !== 'string' || text.length > MAX_DESCRIPTION) return null;
        if (typeof theme !== 'string' || !themes.has(theme)) return null;
        return { name, description: text, theme };
    }

    window.addEventListener('message', e => {
        if (e.origin !== location.origin || e.source !== window.parent) return;
        const draft = parse(e.data);
        if (draft) apply(draft);
    });

    // Ask the editor for its current draft (also after a reload of the frame)
    window.parent.postMessage({ type: READY_TYPE }, location.origin);
}

/** Like the server's rule for the restaurant line: names equal ignoring case and surrounding blanks. */
function sameName(a, b) {
    return a.trim().toLocaleLowerCase() === b.trim().toLocaleLowerCase();
}
