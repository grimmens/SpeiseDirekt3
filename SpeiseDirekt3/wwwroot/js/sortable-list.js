/**
 * sortable-list.js — reorder the rows of a back-office list by dragging their grip handle (pointer events: mouse,
 * touch and pen alike) or with the arrow keys on a focused handle. Used only by Components/Shared/Sortable/SortableList
 * (imported as ./js/sortable-list.js through the ImportMap when a page renders a sortable list; never on guest pages).
 *
 * Blazor owns the DOM, so this module never moves an element. While dragging, the lifted row and the rows it passes
 * are only translated (CSS transform; works for <tr> rows too, which keep their column widths); a placeholder outside
 * Blazor's markup (appended to <body>) marks the drop slot. On drop it reports (oldIndex, newIndex) to .NET, which
 * reorders its list and re-renders — Blazor moves the keyed rows — and a MutationObserver clears the transforms in
 * the same frame as that DOM change, so nothing jumps. A drop on the same slot or a cancel (Escape, pointercancel)
 * slides the rows back.
 *
 * Markup (rendered by SortableList): the container's rows are its children with [data-sortable-item]
 * ([data-sortable-key] identifies one), the handle of a row is its [data-sortable-handle] element. The handle has
 * `touch-action: none` (css/back-office.css), so a touch on it drags at once while the page still scrolls everywhere
 * else.
 *
 * attach(container, dotnet) → { dispose() }; dotnet.invokeMethodAsync('OnDropJs' | 'OnKeyMoveJs', oldIndex, newIndex)
 * resolves when .NET has saved (or rolled back). While a call runs, new drags are ignored and arrow keys wait their
 * turn (the moved row's handle keeps the focus, also when Blazor re-inserts the row element). Once the listeners are
 * in place the container carries [data-sortable-ready] (Blazor never sets it, so its renders leave it alone; dispose
 * removes it): before that a drag or an arrow key does nothing, which is what the E2E tests wait for.
 * focusHandle(container, key) puts the focus back on a row's handle after a keyboard move (the moved element lost it).
 * announce(text) speaks a status message through the shared live region #sd-announcer (Layout/MainLayout).
 */

const EDGE = 56;          // px from the viewport's top (below the sticky top bar) / bottom where auto-scroll starts
const MAX_SPEED = 18;     // px per frame at the very edge
const SETTLE_MS = 160;    // slide-back / drop animation

const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;

function rowsOf(container) {
    return Array.from(container.children).filter(el => el.hasAttribute('data-sortable-item'));
}

function rowOf(container, el) {
    let row = el?.closest?.('[data-sortable-item]');
    while (row && row.parentElement !== container) {
        row = row.parentElement?.closest('[data-sortable-item]');
    }
    return row ?? null;
}

/** Bottom of the sticky top bar (the auto-scroll zone starts below it). */
function topInset() {
    const bar = document.querySelector('.sd-topbar');
    const bottom = bar ? bar.getBoundingClientRect().bottom : 0;
    return Math.max(0, bottom);
}

export function attach(container, dotnet) {
    let busy = false;
    let drag = null;          // the running drag (see begin)
    let pending = null;       // a drop reported to .NET, waiting for Blazor to re-render
    let pendingTimer = 0;

    let keyFocus = null;      // key of the row moved with the keyboard: its handle keeps the focus across re-renders
    const keyQueue = [];      // arrow keys pressed while a move was still being saved

    const observer = new MutationObserver(() => {
        if (pending) clearVisuals();
        // Blazor moved the focused row: the element lost the focus; give it back before the next key arrives
        if (keyFocus) refocus(keyFocus);
    });
    observer.observe(container, { childList: true });

    function clearVisuals() {
        clearTimeout(pendingTimer);
        pending = null;
        for (const row of rowsOf(container)) {
            row.style.transform = '';
            row.style.transition = '';
            row.classList.remove('is-sort-lifted', 'is-sort-dropping');
        }
        container.classList.remove('is-sorting');
        document.documentElement.classList.remove('sd-sorting');
        document.querySelectorAll('.sd-sort-placeholder').forEach(el => el.remove());
    }

    function call(method, from, to) {
        busy = true;
        container.setAttribute('aria-busy', 'true');
        return dotnet.invokeMethodAsync(method, from, to)
            .catch(() => false)
            .finally(() => {
                busy = false;
                container.removeAttribute('aria-busy');
            });
    }

    // --- Pointer dragging ---

    function begin(e, row, handle) {
        const rows = rowsOf(container);
        const index = rows.indexOf(row);
        if (index < 0 || rows.length < 2) return;

        const sy = window.scrollY;
        const boxes = rows.map(r => {
            const rect = r.getBoundingClientRect();
            return { top: rect.top + sy, height: rect.height, left: rect.left + window.scrollX, width: rect.width };
        });
        const me = boxes[index];
        const gap = index + 1 < boxes.length
            ? boxes[index + 1].top - (me.top + me.height)
            : index > 0 ? me.top - (boxes[index - 1].top + boxes[index - 1].height) : 0;

        const placeholder = document.createElement('div');
        placeholder.className = 'sd-sort-placeholder';
        placeholder.setAttribute('aria-hidden', 'true');
        placeholder.dataset.testid = 'sort-placeholder';
        Object.assign(placeholder.style, {
            top: `${me.top}px`, left: `${me.left}px`, width: `${me.width}px`, height: `${me.height}px`
        });
        document.body.appendChild(placeholder);

        drag = {
            pointerId: e.pointerId, handle, row, rows, boxes, index, target: index,
            slot: me.height + Math.max(0, gap), startY: e.clientY + sy, clientY: e.clientY, dy: 0,
            placeholder, frame: 0
        };

        container.classList.add('is-sorting');
        document.documentElement.classList.add('sd-sorting');
        row.classList.add('is-sort-lifted');
        try { handle.setPointerCapture(e.pointerId); } catch { /* pointer already gone */ }
        handle.addEventListener('pointermove', onPointerMove);
        handle.addEventListener('pointerup', onPointerUp);
        handle.addEventListener('pointercancel', onPointerCancel);
        handle.addEventListener('lostpointercapture', onPointerCancel);
        document.addEventListener('keydown', onDragKeyDown, true);
        drag.frame = requestAnimationFrame(autoScroll);
    }

    function update() {
        const d = drag;
        const { boxes, index } = d;
        const me = boxes[index];
        const first = boxes[0];
        const last = boxes[boxes.length - 1];
        const docY = d.clientY + window.scrollY;
        // Keep the lifted row within the list, but let its centre get past the first/last row's centre (rows may differ
        // in height), so both ends are reachable
        const centre = box => box.top + box.height / 2;
        const minDy = Math.min(first.top - me.top, centre(first) - centre(me) - 1);
        const maxDy = Math.max(last.top + last.height - me.height - me.top, centre(last) - centre(me) + 1);
        d.dy = Math.min(maxDy, Math.max(minDy, docY - d.startY));

        const center = me.top + d.dy + me.height / 2;
        let target = index;
        for (let j = index + 1; j < boxes.length && center > boxes[j].top + boxes[j].height / 2; j++) target = j;
        for (let j = index - 1; j >= 0 && center < boxes[j].top + boxes[j].height / 2; j--) target = j;
        d.target = target;

        d.row.style.transform = `translate3d(0, ${d.dy}px, 0)`;
        d.rows.forEach((row, j) => {
            if (j === index) return;
            let shift = 0;
            if (target > index && j > index && j <= target) shift = -d.slot;
            else if (target < index && j >= target && j < index) shift = d.slot;
            row.style.transform = shift ? `translate3d(0, ${shift}px, 0)` : '';
        });

        const landing = landingTop(d);
        d.placeholder.style.top = `${landing}px`;
    }

    function landingTop(d) {
        const { boxes, index, target } = d;
        if (target > index) return boxes[target].top + boxes[target].height - boxes[index].height;
        return boxes[target].top;
    }

    function autoScroll() {
        if (!drag) return;
        const y = drag.clientY;
        const top = topInset() + EDGE;
        const bottom = window.innerHeight - EDGE;
        let speed = 0;
        if (y < top) speed = -Math.ceil(Math.min(1, (top - y) / EDGE) * MAX_SPEED);
        else if (y > bottom) speed = Math.ceil(Math.min(1, (y - bottom) / EDGE) * MAX_SPEED);
        if (speed) {
            const before = window.scrollY;
            window.scrollBy(0, speed);
            if (window.scrollY !== before) update();
        }
        drag.frame = requestAnimationFrame(autoScroll);
    }

    function onPointerMove(e) {
        if (!drag || e.pointerId !== drag.pointerId) return;
        e.preventDefault();
        drag.clientY = e.clientY;
        update();
    }

    function onPointerUp(e) {
        if (!drag || e.pointerId !== drag.pointerId) return;
        drag.clientY = e.clientY;
        update();
        end(true);
    }

    function onPointerCancel(e) {
        if (!drag || (e.pointerId !== undefined && e.pointerId !== drag.pointerId)) return;
        end(false);
    }

    function onDragKeyDown(e) {
        if (drag && e.key === 'Escape') {
            e.preventDefault();
            e.stopPropagation();
            end(false);
        }
    }

    function end(commit) {
        const d = drag;
        drag = null;
        cancelAnimationFrame(d.frame);
        d.handle.removeEventListener('pointermove', onPointerMove);
        d.handle.removeEventListener('pointerup', onPointerUp);
        d.handle.removeEventListener('pointercancel', onPointerCancel);
        d.handle.removeEventListener('lostpointercapture', onPointerCancel);
        document.removeEventListener('keydown', onDragKeyDown, true);
        try { d.handle.releasePointerCapture(d.pointerId); } catch { /* already released */ }

        const animate = !reducedMotion();
        if (commit && d.target !== d.index) {
            // Drop: let the row settle into its slot, then wait for Blazor to re-render the new order
            const offset = landingTop(d) - d.boxes[d.index].top;
            d.row.classList.add('is-sort-dropping');
            d.row.style.transition = animate ? `transform ${SETTLE_MS}ms ease-out` : '';
            d.row.style.transform = `translate3d(0, ${offset}px, 0)`;
            pending = { key: d.row.dataset.sortableKey };
            pendingTimer = setTimeout(clearVisuals, 3000);
            call('OnDropJs', d.index, d.target).then(() => {
                // Nothing changed in the DOM (e.g. the save failed and the order was the same again)
                if (pending) requestAnimationFrame(() => { if (pending) clearVisuals(); });
            });
            return;
        }

        // Same slot or cancelled: slide everything back
        for (const row of d.rows) {
            row.style.transition = animate ? `transform ${SETTLE_MS}ms ease-out` : '';
            row.style.transform = '';
        }
        d.placeholder.remove();
        setTimeout(clearVisuals, animate ? SETTLE_MS : 0);
    }

    function onPointerDown(e) {
        if (busy || drag || pending) return;
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        const handle = e.target.closest?.('[data-sortable-handle]');
        if (!handle || !container.contains(handle) || handle.disabled) return;
        const row = rowOf(container, handle);
        if (!row) return;
        e.preventDefault(); // no text selection, no native drag, no focus jump
        begin(e, row, handle);
    }

    // --- Keyboard: ↑/↓ on a focused handle ---

    function refocus(key) {
        const row = rowsOf(container).find(r => r.dataset.sortableKey === key);
        const handle = row?.querySelector('[data-sortable-handle]');
        if (handle && document.activeElement !== handle) handle.focus({ preventScroll: true });
    }

    function keyMove(key, delta) {
        const index = rowsOf(container).findIndex(r => r.dataset.sortableKey === key);
        if (index < 0) return;
        keyFocus = key;
        call('OnKeyMoveJs', index, index + delta).then(() => {
            const next = keyQueue.shift();
            if (next) keyMove(next.key, next.delta);
            else keyFocus = null;
        });
    }

    function onKeyDown(e) {
        if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
        if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return;
        const handle = e.target.closest?.('[data-sortable-handle]');
        if (!handle || !container.contains(handle)) return;
        e.preventDefault();
        const row = rowOf(container, handle);
        const key = row?.dataset.sortableKey;
        if (!key || drag || pending) return;
        const delta = e.key === 'ArrowUp' ? -1 : 1;
        // A key pressed while the previous move is still being saved runs right after it (a few at most)
        if (busy) {
            if (keyQueue.length < 5) keyQueue.push({ key, delta });
            return;
        }
        keyMove(key, delta);
    }

    // Some mobile browsers start a native drag of the handle's parent row on a long press
    function onDragStart(e) {
        if (e.target.closest?.('[data-sortable-handle]')) e.preventDefault();
    }

    container.addEventListener('pointerdown', onPointerDown);
    container.addEventListener('keydown', onKeyDown);
    container.addEventListener('dragstart', onDragStart);
    container.setAttribute('data-sortable-ready', '');

    return {
        dispose() {
            if (drag) end(false);
            container.removeAttribute('data-sortable-ready');
            container.removeEventListener('pointerdown', onPointerDown);
            container.removeEventListener('keydown', onKeyDown);
            container.removeEventListener('dragstart', onDragStart);
            observer.disconnect();
            clearVisuals();
        }
    };
}

export function focusHandle(container, key) {
    const row = rowsOf(container).find(r => r.dataset.sortableKey === key);
    const handle = row?.querySelector('[data-sortable-handle]');
    if (!handle) return;
    handle.focus({ preventScroll: true });
    row.scrollIntoView?.({ block: 'nearest' });
}

let announceTimer = 0;

export function announce(text) {
    const region = document.getElementById('sd-announcer');
    if (!region) return;
    // Clear first, so the same sentence twice in a row is spoken twice
    region.textContent = '';
    clearTimeout(announceTimer);
    announceTimer = setTimeout(() => { region.textContent = text; }, 50);
}
