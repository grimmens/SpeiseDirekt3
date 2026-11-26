/**
 * legal.js — the opt-out switch of the privacy policy (/datenschutz, Components/Legal/PrivacyPage.razor, point 6),
 * AP16b. Plain ES module, no framework, no build, no request, no cookie.
 *
 * The page counter's tracker (Plausible, js/site.js + /_s/script.js, production only) counts nothing on a device whose
 * localStorage holds plausible_ignore = "true" (the tracker checks exactly "true" === localStorage.plausible_ignore).
 * This switch writes or removes exactly that one key, nothing else — in every environment, also where the counter is
 * off. Markup ([data-legal-optout], server-rendered without a state):
 *   - data-text-on / data-text-off / data-text-unavailable: the state sentence for "counting", "not counting" and
 *     "no access to the storage" (German like the page; C# owns every text, this file has none)
 *   - data-button-off / data-button-on: the button's label to switch counting off / on again
 *   - [data-legal-optout-state] (role="status", so a change is announced politely) gets the sentence,
 *     [data-legal-optout-button] (hidden until this module ran) the label; without storage the button stays hidden
 * Another tab switching it updates this one (storage event). When done it sets data-legal-ready on the switch.
 */

const KEY = 'plausible_ignore';

/** true = not counted on this device, false = counted, null = the storage cannot be used */
function readIgnored() {
    try {
        return window.localStorage.getItem(KEY) === 'true';
    } catch {
        return null;
    }
}

/** Writes the wanted state; returns what is stored afterwards (null when the storage refused). */
function writeIgnored(ignored) {
    try {
        if (ignored) window.localStorage.setItem(KEY, 'true');
        else window.localStorage.removeItem(KEY);
    } catch {
        return null;
    }
    return readIgnored();
}

function initSwitch(root) {
    const state = root.querySelector('[data-legal-optout-state]');
    const button = root.querySelector('[data-legal-optout-button]');
    if (!state || !button) return;
    const text = root.dataset;

    const show = ignored => {
        if (ignored === null) {
            state.textContent = text.textUnavailable || '';
            button.hidden = true;
            return;
        }
        state.textContent = (ignored ? text.textOff : text.textOn) || '';
        // The label says what a press does (no aria-pressed: the label itself changes)
        button.textContent = (ignored ? text.buttonOn : text.buttonOff) || '';
        button.hidden = false;
    };

    button.addEventListener('click', () => {
        const current = readIgnored();
        show(current === null ? null : writeIgnored(!current));
    });
    window.addEventListener('storage', e => {
        if (e.key === KEY || e.key === null) show(readIgnored());
    });

    show(readIgnored());
    root.setAttribute('data-legal-ready', '');
}

document.querySelectorAll('[data-legal-optout]').forEach(initSwitch);
