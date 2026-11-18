/**
 * landing.js — behaviour of the landing page (/, Components/Landing).
 *
 * Plain ES module, no framework, no build step, no network requests. The server renders the complete page and it
 * works without this module (the menu button is a link to the footer navigation, the FAQ is <details>, the theme
 * switcher is radio buttons + CSS). This module only enhances it:
 *   - header: data-scrolled on [data-lp-header] once the page is scrolled (CSS condenses the bar)
 *   - menu sheet (below 1024 px): the link [data-lp-menu-open] opens <dialog data-lp-menu> as a modal; Escape, the
 *     backdrop, [data-lp-menu-close] and a followed link [data-lp-menu-link] close it, and so does widening the window
 *     to 1024 px; focus moves into the sheet and back to the button (not after a followed in-page link: the browser
 *     moves the focus start to the target); the page behind does not scroll (data-lp-menu-open on <html>). Opened by a
 *     tap or click, the first link gets data-lp-quiet (no ring: WebKit shows one for a script focus after a tap) until
 *     the first key press
 *   - reveal: [data-reveal] blocks below the first screen fade in when they scroll into view — only when motion is
 *     allowed (prefers-reduced-motion), never before this module ran (CSS keys on data-lp-reveal), and everything is
 *     shown for printing
 * When it is done it sets data-landing-ready on <html> (tests wait for it).
 */

const root = document.documentElement;
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
// From this width the header shows the navigation itself (landing.css): the sheet is not needed
const wideHeader = window.matchMedia('(min-width: 1024px)');

function initHeader() {
    const header = document.querySelector('[data-lp-header]');
    if (!header) return;
    let scheduled = false;
    const update = () => {
        scheduled = false;
        header.toggleAttribute('data-scrolled', window.scrollY > 8);
    };
    window.addEventListener('scroll', () => {
        if (!scheduled) {
            scheduled = true;
            requestAnimationFrame(update);
        }
    }, { passive: true });
    update();
}

function initMenu() {
    const dialog = document.querySelector('dialog[data-lp-menu]');
    const opener = document.querySelector('[data-lp-menu-open]');
    if (!dialog || !opener || typeof dialog.showModal !== 'function') return;

    // The link becomes a button that opens the sheet (without JS it jumps to the footer navigation)
    opener.setAttribute('role', 'button');
    opener.setAttribute('aria-haspopup', 'dialog');
    opener.setAttribute('aria-controls', dialog.id);
    opener.setAttribute('aria-expanded', 'false');

    let restoreFocus = true;
    let pressStartedOnBackdrop = false;

    const open = byPointer => {
        if (dialog.open) return;
        restoreFocus = true;
        root.setAttribute('data-lp-menu-open', '');
        dialog.showModal();
        opener.setAttribute('aria-expanded', 'true');
        const first = dialog.querySelector('[data-lp-menu-link]');
        if (!first) return;
        if (byPointer) first.setAttribute('data-lp-quiet', '');
        first.focus();
    };

    // The first key press shows the focus ring again (keyboard users need it)
    document.addEventListener('keydown', () => {
        for (const quiet of document.querySelectorAll('[data-lp-quiet]')) quiet.removeAttribute('data-lp-quiet');
    }, true);

    const close = () => {
        if (dialog.open) dialog.close();
    };

    opener.addEventListener('click', event => {
        event.preventDefault();
        // detail = the click count: 0 when Enter activated the link
        open(event.detail > 0);
    });
    opener.addEventListener('keydown', event => {
        // role="button" on a link: Space activates it too
        if (event.key === ' ') {
            event.preventDefault();
            open(false);
        }
    });

    dialog.addEventListener('close', () => {
        root.removeAttribute('data-lp-menu-open');
        opener.setAttribute('aria-expanded', 'false');
        if (restoreFocus) opener.focus();
    });

    dialog.addEventListener('click', event => {
        if (event.target.closest('[data-lp-menu-close]')) {
            close();
            return;
        }
        const link = event.target.closest('[data-lp-menu-link]');
        if (link) {
            // Close first, then the link is followed (an in-page anchor scrolls the unlocked page)
            restoreFocus = false;
            root.removeAttribute('data-lp-menu-open');
            close();
        }
    });

    // Backdrop: the dialog element itself is hit only outside the panel; the press must have started there too
    dialog.addEventListener('pointerdown', event => { pressStartedOnBackdrop = event.target === dialog; });
    dialog.addEventListener('pointerup', event => {
        if (pressStartedOnBackdrop && event.target === dialog) close();
        pressStartedOnBackdrop = false;
    });

    wideHeader.addEventListener('change', event => {
        if (event.matches) close();
    });
}

function initReveal() {
    if (reducedMotion.matches || !('IntersectionObserver' in window)) return;
    const blocks = [...document.querySelectorAll('[data-reveal]')];
    if (blocks.length === 0) return;

    // Blocks already on the first screen are never hidden
    const fold = window.innerHeight;
    for (const block of blocks) {
        if (block.getBoundingClientRect().top < fold) block.setAttribute('data-revealed', '');
    }

    const observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            entry.target.setAttribute('data-revealed', '');
            observer.unobserve(entry.target);
        }
    }, { rootMargin: '0px 0px -8% 0px' });
    for (const block of blocks) {
        if (!block.hasAttribute('data-revealed')) observer.observe(block);
    }

    const revealAll = () => {
        for (const block of blocks) block.setAttribute('data-revealed', '');
        observer.disconnect();
    };
    window.addEventListener('beforeprint', revealAll);
    // Motion switched off while the page is open: show everything
    reducedMotion.addEventListener('change', event => { if (event.matches) revealAll(); });

    root.setAttribute('data-lp-reveal', '');
}

initHeader();
initMenu();
initReveal();
root.setAttribute('data-landing-ready', '');
