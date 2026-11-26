/**
 * site.js — AP16: queue, options and URL masking for the page counter (Plausible CE).
 *
 * Rendered only while the counter is on (production; Components/Plausible/PlausibleScripts.razor), in every document,
 * as a classic script right before the tracker (<script async src="/_s/script.js">). Both the tracker and its endpoint
 * are paths on this site: the vhost on server2 proxies them, so nothing here names another host. No framework, no
 * build, no cookie, no storage.
 *
 *   - the queue stub of Plausible's snippet: window.plausible(…) calls made before the tracker has loaded are kept in
 *     plausible.q, the options of plausible.init in plausible.o; the tracker replays both when it arrives
 *   - plausible.init({ endpoint, transformRequest }): the endpoint from data-endpoint on this script element
 *   - transformRequest masks every request the tracker makes (pageviews and its own events — outbound links, file
 *     downloads, form submissions — as well as ours; its engagement events reuse the masked URL of their pageview):
 *       u  ids in the path become ":id" (GUIDs, numbers, and the parameter of the routes without a :guid constraint),
 *          the query keeps only the campaign and source parameters (KEEP), the hash is dropped
 *       r  dropped when it is this site; another site's is kept without its query and hash
 *       p.url (outbound link, file download) masked like u on this site, without query and hash elsewhere
 *     and it drops the event (returns null) on the editor's phone preview /menu/preview/… (the server renders no tag
 *     there either) and for a page that is not on this site
 *   - data-account-created (one-shot, set by the server right after a self-registration): sends "Konto angelegt"
 */
(() => {
    'use strict';

    const script = document.currentScript;
    const endpoint = script && script.dataset.endpoint;
    if (!endpoint) return;

    // Query parameters that may be sent: the campaign tags and the source parameters the counter reads. Everything else
    // (ReturnUrl, code, userId, email, item, theme, …) is dropped: the account pages carry tokens in the query
    const KEEP = new Set(['utm_source', 'utm_medium', 'utm_campaign', 'utm_content', 'utm_term', 'ref', 'source']);

    // An id in a path segment: a GUID (with or without dashes, any case) or a number
    const ID = /^(?:[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}|\d+)$/i;

    // Routes whose parameter has no :guid constraint (the app links them with GUIDs, but any value reaches the page):
    // first segment → position of the parameter. /MenuItems/{MenuId}, but /MenuItems/Category/… and /MenuItems/item/…
    const UNCONSTRAINED = { allergens: 2, categories: 2, menuitems: 2 };
    const MENU_ITEMS_LITERALS = new Set(['category', 'item']);

    const isParameter = (segments, index) => {
        const first = (segments[1] || '').toLowerCase();
        if (first === 'menu' && (segments[2] || '').toLowerCase() === 'preview') return index === 3;
        if (UNCONSTRAINED[first] !== index) return false;
        return first !== 'menuitems' || !MENU_ITEMS_LITERALS.has(segments[index].toLowerCase());
    };

    const maskPath = path => {
        const segments = path.split('/');
        for (let i = 1; i < segments.length; i++) {
            if (segments[i] !== '' && (ID.test(segments[i]) || isParameter(segments, i))) segments[i] = ':id';
        }
        return segments.join('/');
    };

    const parse = value => {
        if (typeof value !== 'string' || value === '') return null;
        try {
            return new URL(value, location.href);
        } catch {
            return null;
        }
    };

    const onThisSite = url => url.host === location.host && (url.protocol === 'https:' || url.protocol === 'http:');

    /** A URL of this site, masked: ids → ":id", only KEEP parameters, no hash. */
    const maskOwn = url => {
        url.pathname = maskPath(url.pathname);
        const kept = new URLSearchParams();
        for (const [key, value] of url.searchParams) {
            if (KEEP.has(key)) kept.append(key, value);
        }
        url.search = kept.toString();
        url.hash = '';
        return url.href;
    };

    /** Another site's URL: without its query and hash. */
    const stripForeign = url => {
        url.search = '';
        url.hash = '';
        return url.href;
    };

    const maskAny = value => {
        const url = parse(value);
        return url === null ? null : onThisSite(url) ? maskOwn(url) : stripForeign(url);
    };

    const transformRequest = payload => {
        const page = parse(payload.u);
        if (page === null || !onThisSite(page)) return null;
        const u = maskOwn(page);
        if (/^\/menu\/preview(\/|$)/i.test(new URL(u).pathname)) return null;
        payload.u = u;

        if (payload.r) {
            const referrer = parse(payload.r);
            payload.r = referrer === null || onThisSite(referrer) ? null : stripForeign(referrer);
        }

        if (payload.p && typeof payload.p.url === 'string') payload.p.url = maskAny(payload.p.url) || '';
        return payload;
    };

    const w = window;
    w.plausible = w.plausible || function () { (w.plausible.q = w.plausible.q || []).push(arguments); };
    w.plausible.init = w.plausible.init || function (options) { w.plausible.o = options || {}; };
    w.plausible.init({ endpoint, transformRequest });

    if (script.hasAttribute('data-account-created')) w.plausible('Konto angelegt');
})();
