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
