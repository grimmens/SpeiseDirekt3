/**
 * shell.js — behaviour of the back-office chrome (Components/Layout: MainLayout, UserMenu, NavDrawer, MenuContextBar).
 * A classic script loaded once per document; everything works by event delegation on `document`, so it survives
 * Blazor's re-renders and enhanced navigation and also works on the static Identity pages (no circuit there).
 *
 * Markup hooks:
 * - Dropdowns (disclosure pattern): `[data-sd-dropdown-toggle]` button with `aria-expanded` and `aria-controls` = the id
 *   of its `[data-sd-dropdown-panel]` (rendered with `hidden`). Click toggles; a click outside, Escape (focus back to
 *   the button), Tab out of it and a followed link close it; ArrowDown/ArrowUp/Home/End move between its links and
 *   buttons. One open at a time.
 * - Drawer: `[data-sd-drawer-open]` button (`aria-controls` = the `<dialog data-sd-drawer>`). Opened with showModal()
 *   (the page behind is inert, focus moves in: the current page's link, else the close button); closed by Escape,
 *   the backdrop, `[data-sd-drawer-close]`, a followed link and widening to ≥ 1024 px. Focus returns to the button,
 *   whose `aria-expanded` follows the dialog. The page does not scroll behind it.
 * - Simple dialogs: `[data-sd-dialog-open]` button (`aria-controls` = the `<dialog data-sd-dialog>`) opens it with
 *   showModal() (focus moves to its `[data-sd-dialog-close]` button); Escape, the backdrop and `[data-sd-dialog-close]`
 *   close it and the focus returns to the button (e.g. the enlarged QR code, QrCodePreview). No Blazor state involved.
 * - `[data-sd-skip]`: the skip link focuses `<main>` (a plain #fragment would resolve against <base href> and leave
 *   the page).
 * - `sdShell.revealActiveTab(strip)`: scrolls a horizontal tab strip so its `[aria-current="page"]` tab is centred
 *   (called by MenuContextBar after render); never scrolls the page.
 * - `sdShell.revealRow(selector)`: scrolls the first visible element matching `selector` to the middle of the screen
 *   (the item list after the edit page: the row just saved); instant with reduced motion.
 */
(() => {
    if (window.sdShell) return;

    const WIDE = window.matchMedia('(min-width: 1024px)');
    const focusables = panel => [...panel.querySelectorAll('a[href], button:not([disabled])')]
        .filter(el => el.getClientRects().length > 0);

    // --- Dropdowns ---

    const panelOf = toggle => document.getElementById(toggle.getAttribute('aria-controls') ?? '');
    const toggleOf = panel => panel.id ? document.querySelector(`[data-sd-dropdown-toggle][aria-controls="${CSS.escape(panel.id)}"]`) : null;
    const openToggles = () => [...document.querySelectorAll('[data-sd-dropdown-toggle][aria-expanded="true"]')];

    function closeDropdown(toggle, { focusToggle = false } = {}) {
        toggle.setAttribute('aria-expanded', 'false');
        const panel = panelOf(toggle);
        if (panel) panel.hidden = true;
        if (focusToggle) toggle.focus();
    }

    function openDropdown(toggle) {
        openToggles().forEach(t => t !== toggle && closeDropdown(t));
        toggle.setAttribute('aria-expanded', 'true');
        const panel = panelOf(toggle);
        if (panel) panel.hidden = false;
        return panel;
    }

    function closeAllDropdowns() {
        openToggles().forEach(t => closeDropdown(t));
    }

    // --- Drawer ---

    let drawerOpener = null;

    function openDrawer(button) {
        const dialog = document.getElementById(button.getAttribute('aria-controls') ?? '');
        if (!(dialog instanceof HTMLDialogElement) || dialog.open) return;
        closeAllDropdowns();
        drawerOpener = button;
        dialog.showModal();
        button.setAttribute('aria-expanded', 'true');
        document.documentElement.classList.add('sd-drawer-open');
        const target = dialog.querySelector('[aria-current="page"]') ?? dialog.querySelector('[data-sd-drawer-close]');
        target?.focus({ preventScroll: true });
    }

    function closeDrawer(dialog) {
        if (dialog.open) dialog.close();
    }

    // The dialog's own close event (Escape, close(), backdrop): tidy up and give the focus back
    document.addEventListener('close', e => {
        const dialog = e.target;
        if (!(dialog instanceof HTMLDialogElement) || !dialog.matches('[data-sd-drawer]')) return;
        document.documentElement.classList.remove('sd-drawer-open');
        document.querySelectorAll(`[data-sd-drawer-open][aria-controls="${CSS.escape(dialog.id)}"]`)
            .forEach(b => b.setAttribute('aria-expanded', 'false'));
        const opener = drawerOpener;
        drawerOpener = null;
        if (opener?.isConnected && opener.getClientRects().length > 0) opener.focus({ preventScroll: true });
    }, true);

    // --- Simple dialogs (no Blazor state: the enlarged QR code) ---

    let dialogOpener = null;

    function openSimpleDialog(button) {
        const dialog = document.getElementById(button.getAttribute('aria-controls') ?? '');
        if (!(dialog instanceof HTMLDialogElement) || dialog.open) return;
        closeAllDropdowns();
        dialogOpener = button;
        dialog.showModal();
        dialog.querySelector('[data-sd-dialog-close]')?.focus({ preventScroll: true });
    }

    document.addEventListener('close', e => {
        if (!(e.target instanceof HTMLDialogElement) || !e.target.matches('[data-sd-dialog]')) return;
        const opener = dialogOpener;
        dialogOpener = null;
        if (opener?.isConnected && opener.getClientRects().length > 0) opener.focus({ preventScroll: true });
    }, true);

    WIDE.addEventListener('change', () => {
        if (WIDE.matches) document.querySelectorAll('dialog[data-sd-drawer][open]').forEach(closeDrawer);
    });

    // --- Pointer ---

    let pressedOnBackdrop = false;
    document.addEventListener('pointerdown', e => {
        const dialog = e.target instanceof HTMLDialogElement && e.target.matches('[data-sd-drawer], [data-sd-dialog]') ? e.target : null;
        pressedOnBackdrop = !!dialog && isOutside(dialog, e);
    }, true);

    const isOutside = (dialog, e) => {
        const r = dialog.getBoundingClientRect();
        return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
    };

    // Capture phase: runs before Blazor's own link interception (which must not see the skip link)
    document.addEventListener('click', e => {
        const target = e.target instanceof Element ? e.target : null;
        if (!target) return;

        const skip = target.closest('[data-sd-skip]');
        if (skip) {
            e.preventDefault();
            e.stopPropagation();
            const main = document.getElementById('sd-main');
            main?.focus({ preventScroll: true });
            main?.scrollIntoView({ block: 'start' });
            return;
        }

        // Drawer
        const drawerButton = target.closest('[data-sd-drawer-open]');
        if (drawerButton) {
            openDrawer(drawerButton);
            return;
        }
        const drawer = target.closest('dialog[data-sd-drawer]');
        if (drawer) {
            if (target === drawer && pressedOnBackdrop && isOutside(drawer, e)) closeDrawer(drawer);
            else if (target.closest('[data-sd-drawer-close]')) closeDrawer(drawer);
            else if (target.closest('a[href]')) closeDrawer(drawer); // navigation: the link still navigates
            pressedOnBackdrop = false;
            return;
        }

        // Simple dialogs
        const dialogButton = target.closest('[data-sd-dialog-open]');
        if (dialogButton) {
            openSimpleDialog(dialogButton);
            return;
        }
        const simple = target.closest('dialog[data-sd-dialog]');
        if (simple) {
            if (target === simple && pressedOnBackdrop && isOutside(simple, e)) simple.close();
            else if (target.closest('[data-sd-dialog-close]')) simple.close();
            pressedOnBackdrop = false;
            return;
        }

        // Dropdowns
        const toggle = target.closest('[data-sd-dropdown-toggle]');
        if (toggle) {
            if (toggle.getAttribute('aria-expanded') === 'true') closeDropdown(toggle);
            else openDropdown(toggle);
            return;
        }
        const panel = target.closest('[data-sd-dropdown-panel]');
        if (panel) {
            if (target.closest('a[href]')) {
                const owner = toggleOf(panel);
                if (owner) closeDropdown(owner); // the link still navigates
            }
            return;
        }
        closeAllDropdowns();
    }, true);

    // --- Keyboard ---

    document.addEventListener('keydown', e => {
        if (e.defaultPrevented || e.isComposing) return;
        const target = e.target instanceof Element ? e.target : null;

        if (e.key === 'Escape') {
            const open = openToggles();
            if (open.length === 0) return; // the drawer closes itself (native dialog cancel)
            e.preventDefault();
            const focusWasInside = open.some(t => t.contains(target) || panelOf(t)?.contains(target));
            open.forEach(t => closeDropdown(t, { focusToggle: focusWasInside }));
            return;
        }

        if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(e.key) || !target) return;
        const toggle = target.closest('[data-sd-dropdown-toggle]');
        const panel = toggle ? panelOf(toggle) : target.closest('[data-sd-dropdown-panel]');
        if (!panel) return;
        if (toggle && toggle.getAttribute('aria-expanded') !== 'true') {
            if (e.key !== 'ArrowDown') return;
            openDropdown(toggle);
        }
        const items = focusables(panel);
        if (items.length === 0) return;
        e.preventDefault();
        const index = items.indexOf(document.activeElement);
        let next;
        if (e.key === 'Home') next = 0;
        else if (e.key === 'End') next = items.length - 1;
        else if (index < 0) next = e.key === 'ArrowUp' ? items.length - 1 : 0;
        else next = (index + (e.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length;
        items[next].focus();
    });

    // Tab (or a click) moving the focus out of an open dropdown closes it
    document.addEventListener('focusin', e => {
        const target = e.target instanceof Element ? e.target : null;
        openToggles().forEach(t => {
            if (!target || (!t.contains(target) && !panelOf(t)?.contains(target))) closeDropdown(t);
        });
    });

    // --- Tab strip ---

    function revealActiveTab(strip) {
        if (!(strip instanceof Element)) return;
        const tab = strip.querySelector('[aria-current="page"]');
        if (!tab || strip.scrollWidth <= strip.clientWidth) return;
        const stripBox = strip.getBoundingClientRect();
        const tabBox = tab.getBoundingClientRect();
        const centre = tabBox.left - stripBox.left + strip.scrollLeft - (strip.clientWidth - tabBox.width) / 2;
        strip.scrollLeft = Math.max(0, Math.min(centre, strip.scrollWidth - strip.clientWidth));
    }

    // Static rendering (prerender, Identity pages): centre the strip once the document is ready
    const revealAll = () => document.querySelectorAll('[data-sd-tab-strip]').forEach(revealActiveTab);
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', revealAll);
    else revealAll();

    function revealRow(selector) {
        const row = [...document.querySelectorAll(selector)].find(el => el.getClientRects().length > 0);
        if (!row) return false;
        const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        row.scrollIntoView({ block: 'center', behavior: reduce ? 'instant' : 'smooth' });
        return true;
    }

    window.sdShell = { revealActiveTab, revealRow, closeAllDropdowns };
})();
