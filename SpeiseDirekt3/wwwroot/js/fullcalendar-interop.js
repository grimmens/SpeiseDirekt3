/**
 * fullcalendar-interop.js — browser side of the Blazor wrapper Components/Shared/Calendar/FullCalendar.razor
 * (back office only; imported as ./js/fullcalendar-interop.js through the ImportMap).
 *
 * The FullCalendar standard bundle (wwwroot/lib/fullcalendar, a classic script that defines window.FullCalendar and
 * injects its CSS) is loaded lazily on the first create() — never by pages without a calendar.
 *
 * create(element, dotnet, bundleUrl, options, events, callbacks) renders a calendar into a new child of `element`
 * (marked data-calendar-ready once rendered) and returns
 *   { update(options, events), dispose() }.
 * The calendar is controlled: it always shows exactly the events last passed in. Interactions are reported to .NET
 * (dotnet = DotNetObjectReference of the wrapper), which answers with new events:
 *   OnDateClickJs(date)                      a day was clicked/tapped, or a single day selected (never both)
 *   OnSelectJs(start, end)                   two or more days selected by dragging (end exclusive)
 *   OnEventClickJs(id, date)                 an event (occurrence) clicked or activated with Enter
 *   OnEventChangeJs(kind, id, oldStart, oldEnd, start, end)   dragged ('moved') or stretched ('resized')
 *   OnDatesSetJs(start, end, currentStart, currentEnd, viewType, title)
 * Dates are local calendar days as 'yyyy-MM-dd' (all-day only, no time zone involved).
 */

const SOURCE_ID = 'blazor';
const SAME_DAY_MS = 700;

let bundlePromise = null;

/** Loads the FullCalendar global bundle once per page. */
function loadBundle(url) {
    if (window.FullCalendar?.Calendar) return Promise.resolve(window.FullCalendar);
    if (!bundlePromise) {
        bundlePromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = url;
            script.async = true;
            script.dataset.fullcalendar = '';
            script.onload = () => window.FullCalendar?.Calendar
                ? resolve(window.FullCalendar)
                : reject(new Error('FullCalendar did not initialise'));
            script.onerror = () => {
                bundlePromise = null;
                script.remove();
                reject(new Error(`FullCalendar could not be loaded from ${url}`));
            };
            document.head.appendChild(script);
        });
    }
    return bundlePromise;
}

const pad = n => String(n).padStart(2, '0');
/** Local calendar day of a Date as yyyy-MM-dd (FullCalendar's all-day dates are local midnights). */
const iso = d => d ? `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}` : null;
const dayNumber = s => Math.round(Date.UTC(+s.slice(0, 4), +s.slice(5, 7) - 1, +s.slice(8, 10)) / 86400000);

/** Drops null/undefined, so FullCalendar falls back to its defaults. */
const compact = o => Object.fromEntries(Object.entries(o).filter(([, v]) => v !== null && v !== undefined));

function toEvent(e) {
    const recurring = Array.isArray(e.daysOfWeek) && e.daysOfWeek.length > 0;
    return compact({
        id: e.id,
        title: e.title,
        allDay: true,
        start: recurring ? null : e.start,
        end: recurring ? null : e.end,
        daysOfWeek: recurring ? e.daysOfWeek : null,
        startRecur: recurring ? e.startRecur : null,
        endRecur: recurring ? e.endRecur : null,
        backgroundColor: e.backgroundColor,
        borderColor: e.borderColor,
        textColor: e.textColor,
        classNames: e.classNames,
        editable: e.editable,
        startEditable: e.startEditable,
        durationEditable: e.durationEditable,
        extendedProps: { tooltip: e.tooltip ?? null, occurrences: e.occurrences ?? null }
    });
}

/** "$0" in a text from .NET → the count. */
const fill = (text, n) => text.replace('$0', String(n));

/**
 * Options from .NET (CalendarOptions) → FullCalendar options that may change after creation. Every text comes from
 * .NET in the page's language (o.texts); `locale` makes FullCalendar format dates with Intl in that language.
 */
function toOptions(o) {
    const t = o.texts ?? null;
    return compact({
        locale: o.locale,
        firstDay: o.firstDay,
        height: o.height,
        now: o.today,
        headerToolbar: o.headerToolbar
            ? { start: o.headerToolbar.start, center: o.headerToolbar.center, end: o.headerToolbar.end }
            : false,
        buttonText: o.buttonText,
        selectable: !!o.selectable,
        editable: !!o.editable,
        dayMaxEventRows: o.dayMaxEventRows ?? false,
        noEventsText: o.noEventsText,
        allDayText: t?.allDay,
        moreLinkText: t ? n => fill(t.moreLink, n) : null,
        moreLinkHint: t ? n => (n === 1 ? t.moreHintOne : fill(t.moreHintOther, n)) : null,
        buttonHints: t?.buttonHints,
        viewHint: t?.viewHint,
        closeHint: t?.closeHint
    });
}

const occurrenceOf = event => event.extendedProps?.occurrences?.[iso(event.start)] ?? null;

export async function create(element, dotnet, bundleUrl, options, events, callbacks) {
    const FullCalendar = await loadBundle(bundleUrl);
    const invoke = (method, ...args) => dotnet.invokeMethodAsync(method, ...args).catch(err => {
        // The circuit is gone (page left) or .NET threw: nothing to do in the browser
        console.debug('FullCalendar interop:', err?.message ?? err);
    });

    // A click on a day may fire both dateClick and a one-day select: report the day once
    let lastDay = { date: null, at: 0 };
    const raiseDay = date => {
        const now = Date.now();
        if (lastDay.date === date && now - lastDay.at < SAME_DAY_MS) return;
        lastDay = { date, at: now };
        invoke('OnDateClickJs', date);
    };

    // Render into an own child: `element` belongs to Blazor
    const mount = document.createElement('div');
    mount.className = 'sd-fc-mount';
    element.appendChild(mount);

    let current = toOptions(options);
    const calendar = new FullCalendar.Calendar(mount, {
        ...current,
        initialView: options.initialView,
        initialDate: options.initialDate ?? options.today ?? undefined,
        timeZone: 'local',
        fixedWeekCount: false,
        displayEventTime: false,
        eventDisplay: 'block',
        eventInteractive: !!callbacks.eventClick,
        selectMirror: false,
        unselectAuto: true,
        eventResizableFromStart: false,
        events: [],
        eventClassNames: arg => occurrenceOf(arg.event)?.classNames ?? [],
        eventDidMount: arg => {
            const text = occurrenceOf(arg.event)?.tooltip ?? arg.event.extendedProps?.tooltip;
            if (text) {
                arg.el.title = text;
                arg.el.setAttribute('aria-label', text);
            }
        },
        dateClick: callbacks.dateClick ? info => raiseDay(iso(info.date)) : undefined,
        select: callbacks.select || callbacks.dateClick
            ? info => {
                calendar.unselect();
                const start = iso(info.start), end = iso(info.end);
                if (dayNumber(end) - dayNumber(start) <= 1) {
                    if (callbacks.dateClick) raiseDay(start);
                } else if (callbacks.select) {
                    invoke('OnSelectJs', start, end);
                }
            }
            : undefined,
        eventClick: callbacks.eventClick
            ? info => {
                info.jsEvent?.preventDefault();
                invoke('OnEventClickJs', info.event.id, iso(info.event.start));
            }
            : undefined,
        eventDrop: info => change('moved', info),
        eventResize: info => change('resized', info),
        datesSet: info => {
            if (!callbacks.datesSet) return;
            invoke('OnDatesSetJs', iso(info.start), iso(info.end), iso(info.view.currentStart), iso(info.view.currentEnd),
                info.view.type, info.view.title);
        }
    });

    // Controlled: .NET decides and sends the events again (or the old ones, which puts the event back)
    function change(kind, info) {
        if (!callbacks.eventChange) {
            info.revert();
            return;
        }
        invoke('OnEventChangeJs', kind, info.event.id, iso(info.oldEvent.start), iso(info.oldEvent.end),
            iso(info.event.start), iso(info.event.end));
    }

    let events$ = events.map(toEvent);
    calendar.addEventSource({ id: SOURCE_ID, events: events$ });
    calendar.render();
    mount.dataset.calendarReady = '';

    return {
        /** New options and/or events (null = unchanged); the visible month stays. */
        update(newOptions, newEvents) {
            calendar.batchRendering(() => {
                if (newOptions) {
                    const next = toOptions(newOptions);
                    for (const [key, value] of Object.entries(next)) {
                        if (JSON.stringify(current[key]) !== JSON.stringify(value)) calendar.setOption(key, value);
                    }
                    current = next;
                }
                if (newEvents) {
                    events$ = newEvents.map(toEvent);
                    calendar.getEventSourceById(SOURCE_ID)?.remove();
                    calendar.addEventSource({ id: SOURCE_ID, events: events$ });
                }
            });
        },
        dispose() {
            calendar.destroy();
            mount.remove();
        }
    };
}
