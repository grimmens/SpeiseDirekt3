/**
 * modal-dialog.js — a native <dialog> driven by Blazor (back office; imported as ./js/modal-dialog.js through the
 * ImportMap). Used by the QR code calendar's entry panel (Components/QrCodes/QrCodeCalendarEntries.razor), the shared
 * confirmation (Components/Shared/ConfirmationModal.razor) and the combo editor (MenuCombos/Components/MenuComboEditor).
 *
 * attach(dialog, dotnet, fallbackFocus, options) returns { open(), close(), dispose() }.
 * - open(): showModal() — the page behind is inert, focus moves to the [autofocus] control (else the first one).
 * - Escape, a click on the backdrop (press and release outside the panel) and close() close it; the browser's close
 *   event is reported as dotnet.invokeMethodAsync('OnDialogClosedJs') so Blazor drops its draft. Focus returns to the
 *   element that had it before open() — or to `fallbackFocus` when that element is gone (e.g. a re-rendered event).
 * - Enter in a text/date field or select presses the button marked [data-dialog-default] instead of submitting the
 *   surrounding form (the panel sits inside the editor's EditForm). `options.enterApplies === false` leaves Enter alone
 *   (a dialog with its own form, e.g. the combo editor, whose item search uses Enter itself).
 */

export function attach(dialog, dotnet, fallbackFocus, options) {
    const enterApplies = options?.enterApplies !== false;
    let opener = null;
    let pressedOnBackdrop = false;

    const onClose = () => {
        const target = opener?.isConnected ? opener : fallbackFocus;
        opener = null;
        target?.focus?.({ preventScroll: true });
        dotnet.invokeMethodAsync('OnDialogClosedJs').catch(() => { /* circuit gone */ });
    };
    // The panel fills the dialog box, so only the backdrop hits the <dialog> element itself
    const onPointerDown = e => { pressedOnBackdrop = e.target === dialog; };
    const onClick = e => {
        if (e.target === dialog && pressedOnBackdrop) dialog.close();
        pressedOnBackdrop = false;
    };
    const onKeyDown = e => {
        if (!enterApplies || e.key !== 'Enter' || e.isComposing) return;
        const field = e.target;
        if (!(field instanceof HTMLElement) || !field.matches('input:not([type=button]):not([type=submit]), select')) return;
        e.preventDefault();
        dialog.querySelector('[data-dialog-default]')?.click();
    };

    dialog.addEventListener('close', onClose);
    dialog.addEventListener('pointerdown', onPointerDown);
    dialog.addEventListener('click', onClick);
    dialog.addEventListener('keydown', onKeyDown);

    return {
        open() {
            if (dialog.open) return;
            opener = document.activeElement instanceof HTMLElement && document.activeElement !== document.body
                ? document.activeElement
                : null;
            dialog.showModal();
            (dialog.querySelector('[autofocus]') ?? dialog.querySelector('select, input, button'))?.focus();
        },
        close() {
            if (dialog.open) dialog.close();
        },
        dispose() {
            dialog.removeEventListener('close', onClose);
            dialog.removeEventListener('pointerdown', onPointerDown);
            dialog.removeEventListener('click', onClick);
            dialog.removeEventListener('keydown', onKeyDown);
            if (dialog.open) dialog.close();
        }
    };
}
