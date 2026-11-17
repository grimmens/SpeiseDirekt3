/**
 * menu-preview-draft.js — menu editor (back office, MenuEditForm.razor): shows the unsaved name, description and theme
 * in the phone-frame preview right away.
 *
 * attach(form, frame) listens to input/change events inside `form` (#menuName, #menuDesc, #themeSelect) and posts the
 * draft to the preview iframe inside `frame` (debounced, 150 ms):
 *   { type: 'sd-preview-draft', v: 1, name, description, theme }   targetOrigin = location.origin
 * The frame (js/guest-preview.js, owner preview only) applies it with textContent and switches the theme in place.
 * When the frame (re)loads it posts { type: 'sd-preview-ready' }; the draft is then sent again, so a reload (the editor
 * reloads the frame with ?lang= when the language changes) keeps the unsaved text.
 * Returns { refresh(), dispose() }: refresh() after Blazor changed a field itself (theme arrows), dispose() on leave.
 */

const DRAFT_TYPE = 'sd-preview-draft';
const READY_TYPE = 'sd-preview-ready';
const DELAY_MS = 150;

export function attach(form, frame) {
    let timer = 0;
    const iframe = () => frame?.querySelector('iframe');
    const value = selector => form?.querySelector(selector)?.value ?? '';

    const draft = () => ({
        type: DRAFT_TYPE,
        v: 1,
        name: value('#menuName'),
        description: value('#menuDesc'),
        theme: value('#themeSelect').toLowerCase()
    });

    const send = () => {
        clearTimeout(timer);
        const target = iframe()?.contentWindow;
        if (target) target.postMessage(draft(), location.origin);
    };
    const schedule = () => {
        clearTimeout(timer);
        timer = setTimeout(send, DELAY_MS);
    };
    const onMessage = e => {
        if (e.origin !== location.origin || e.source !== iframe()?.contentWindow) return;
        if (e.data && typeof e.data === 'object' && e.data.type === READY_TYPE) send();
    };

    form?.addEventListener('input', schedule);
    form?.addEventListener('change', schedule);
    window.addEventListener('message', onMessage);
    send();

    return {
        refresh: schedule,
        dispose() {
            clearTimeout(timer);
            form?.removeEventListener('input', schedule);
            form?.removeEventListener('change', schedule);
            window.removeEventListener('message', onMessage);
        }
    };
}
