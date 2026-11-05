// wwwroot/js/qr-codes.js
// "Copy link" on the QR code pages. The copy runs in the click handler itself (document-level
// delegation, no Blazor round trip), so browsers that require a user gesture for the clipboard
// (Safari) accept it. Markup: <button data-copy-url="https://…"> with children marked
// data-copy-state="idle" | "done" | "failed"; the matching child is shown for two seconds.
// Imported as a module by the QR code pages; the listener is registered once per page load.

const RESET_MS = 2000;

function copyWithTextarea(text) {
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.top = '0';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    let ok = false;
    try {
        ok = document.execCommand('copy');
    } catch {
        ok = false;
    }
    area.remove();
    return ok;
}

async function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // fall back below
        }
    }
    return copyWithTextarea(text);
}

function showState(button, state) {
    for (const el of button.querySelectorAll('[data-copy-state]')) {
        el.classList.toggle('hidden', el.dataset.copyState !== state);
    }
    button.dataset.copyResult = state;
}

document.addEventListener('click', async event => {
    const button = event.target instanceof Element ? event.target.closest('[data-copy-url]') : null;
    if (!button) return;
    event.preventDefault();

    const ok = await copyText(button.dataset.copyUrl);
    showState(button, ok ? 'done' : 'failed');
    clearTimeout(button._copyReset);
    button._copyReset = setTimeout(() => showState(button, 'idle'), RESET_MS);
});

document.documentElement.dataset.qrCopy = 'ready';
