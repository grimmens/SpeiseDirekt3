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

window.shareLink = async (url, title, text) => {
    // Check if Web Share API is supported (mainly mobile devices)
    if (navigator.share) {
        try {
            await navigator.share({
                title: title,
                text: text,
                url: url
            });
            console.log('Content shared successfully');
        } catch (error) {
            console.log('Error sharing:', error);
            // Fallback to copying to clipboard
            fallbackShare(url);
        }
    } else {
        // Fallback for desktop or unsupported browsers
        fallbackShare(url);
    }
};

window.isShareSupported = () => {
    return navigator.share !== undefined;
};

function fallbackShare(url) {
    // Copy to clipboard as fallback
    if (navigator.clipboard) {
        navigator.clipboard.writeText(url).then(() => {
            alert('Link copied to clipboard!');
        }).catch(err => {
            console.error('Could not copy text: ', err);
            // Final fallback - show URL in prompt
            prompt('Copy this link:', url);
        });
    } else {
        // Very old browser fallback
        prompt('Copy this link:', url);
    }
}
