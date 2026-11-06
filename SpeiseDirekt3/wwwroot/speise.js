// Bootstrap modals ignore show() and hide() while a fade is running, so a quick "Ok" right after opening (or a
// reopen right after closing) got lost and left the dialog open or never showed it. Every modal keeps the state
// it should be in; the next step is applied when the current fade has finished (shown/hidden events).
const modalMap = new Map();

function modalState(id) {
    let state = modalMap.get(id);
    if (!state) {
        const el = document.getElementById(id);
        if (!el) {
            return null;
        }
        state = { modal: new bootstrap.Modal(el, { keyboard: false }), visible: false, busy: false, want: null };
        const settled = (visible) => { state.visible = visible; state.busy = false; applyModalState(state); };
        el.addEventListener('shown.bs.modal', () => settled(true));
        el.addEventListener('hidden.bs.modal', () => settled(false));
        modalMap.set(id, state);
    }
    return state;
}

function applyModalState(state) {
    if (state.busy || state.want === null) {
        return;
    }
    const want = state.want;
    state.want = null;
    if (want === 'show' && !state.visible) {
        state.busy = true;
        state.modal.show();
    } else if (want === 'hide' && state.visible) {
        state.busy = true;
        state.modal.hide();
    }
}

window.openModal = (id) => {
    const state = modalState(id);
    if (state) {
        state.want = 'show';
        applyModalState(state);
    }
};

window.hideModal = (id) => {
    const state = modalMap.get(id);
    if (state) {
        state.want = 'hide';
        applyModalState(state);
    }
};