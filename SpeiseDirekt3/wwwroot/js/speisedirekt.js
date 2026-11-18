/**
 * speisedirekt.js — the back office's Bootstrap popover helpers (classic script, loaded by App.razor): InfoPopoverComp
 * calls initPopover(icon, content) after its first render and showPopover(icon) on a click.
 */
function initPopover(icon, contentElement) {
    const htmlContent = contentElement.innerHTML;

    icon.setAttribute('data-bs-content', htmlContent);
    icon.setAttribute('title', ''); // Optional: set popover title

    new bootstrap.Popover(icon, {
        html: true,
        trigger: 'focus',
        placement: 'right'
    });
}

function showPopover(icon) {
    const popover = bootstrap.Popover.getOrCreateInstance(icon);
    popover.show();
}
