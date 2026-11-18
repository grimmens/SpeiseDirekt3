// Analytics Charts JavaScript
let userChart, menuChart, menuItemChart;

// Colour palette for charts: the brand first (css/brand.css, read at run time so the tokens stay the one source), then
// hues far from it and from each other. Series are coloured in this order; a single series is the brand.
function token(name, fallback) {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return (value || fallback).split(/\s+/).join(', ');
}

function palette(alpha) {
    return [
        `rgba(${token('--sd-brand-600', '34 107 122')}, ${alpha})`,   // Brand (petrol)
        `rgba(${token('--sd-accent-500', '240 138 28')}, ${alpha})`,  // Accent (saffron)
        `rgba(34, 197, 94, ${alpha})`,    // Green
        `rgba(168, 85, 247, ${alpha})`,   // Purple
        `rgba(239, 68, 68, ${alpha})`,    // Red
        `rgba(59, 130, 246, ${alpha})`,   // Blue
        `rgba(236, 72, 153, ${alpha})`,   // Pink
        `rgba(100, 116, 139, ${alpha})`,  // Slate
    ];
}

const colors = palette(0.8);
const borderColors = palette(1);

// Common chart options
const commonOptions = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
        legend: {
            position: 'top',
            labels: {
                usePointStyle: true,
                padding: 7
            }
        }
    },
    scales: {
        x: {
            grid: {
                display: false
            }
        },
        y: {
            beginAtZero: true,
            grid: {
                color: 'rgba(0, 0, 0, 0.1)'
            }
        }
    }
};

// Texts and language of the page (AP12): passed by AnalyticsPage (ChartTexts) on every render. Dates, times and
// numbers are formatted with Intl in that language ("18. Nov." / "Nov 18", "14:00" / "2:00 PM"); the date-fns adapter
// only parses. English until the first call.
let t = {
    lang: 'en',
    sessionsTitle: 'Sessions Over Time', sessionsEmpty: 'Sessions Over Time - No Data',
    menusTitle: 'Menu Views Over Time', menusEmpty: 'Menu Views Over Time - No Data',
    itemsTitle: 'Menu Item Clicks Over Time', itemsEmpty: 'Menu Item Clicks Over Time - No Data',
    axisHour: 'Hour', axisDate: 'Date', axisSessions: 'Sessions', axisCount: 'Count',
    sessionsSeries: 'Sessions', sessionOne: '1 session', sessionOther: '{0} sessions'
};

const isHourly = timeRange => timeRange === 'Last24Hours';
const dayLabel = ms => new Intl.DateTimeFormat(t.lang, { month: 'short', day: 'numeric' }).format(new Date(ms));
const hourLabel = ms => new Intl.DateTimeFormat(t.lang, { hour: 'numeric', minute: '2-digit' }).format(new Date(ms));

/** Tooltip title: the day, or the day and the hour's window ("18.11.2026 14:00–15:00"). */
function tooltipTitle(tooltipItems, timeRange) {
    const date = new Date(tooltipItems[0].parsed.x);
    if (isHourly(timeRange)) {
        const start = new Date(date);
        start.setMinutes(0, 0, 0);
        const end = new Date(start);
        end.setHours(end.getHours() + 1);
        return `${date.toLocaleDateString(t.lang)} ${hourLabel(start)}–${hourLabel(end)}`;
    }
    return date.toLocaleDateString(t.lang, { year: 'numeric', month: 'short', day: 'numeric' });
}

/** The time axis: one tick per hour or day, labelled with Intl in the page's language. */
function timeAxis(timeRange, extra) {
    return {
        type: 'time',
        time: { unit: isHourly(timeRange) ? 'hour' : 'day' },
        title: { display: true, text: isHourly(timeRange) ? t.axisHour : t.axisDate },
        ticks: { source: 'auto', callback: value => isHourly(timeRange) ? hourLabel(value) : dayLabel(value) },
        ...extra
    };
}

function emptyChart(ctx, type, title) {
    return new Chart(ctx, {
        type,
        data: { datasets: [] },
        options: {
            ...commonOptions,
            locale: t.lang,
            plugins: { ...commonOptions.plugins, title: { display: true, text: title } }
        }
    });
}

function renderAnalyticsCharts(sessionsPerBucket, menuTrafficData, menuItemTrafficData, timeRange, texts) {
    if (texts) t = { ...t, ...texts };
    renderSessionsChart(sessionsPerBucket, timeRange);
    renderMenuTrafficChart(menuTrafficData, timeRange);
    renderMenuItemTrafficChart(menuItemTrafficData, timeRange);
}

// Sessions (anonymous guest devices) active per hour/day as one series: [{ date, count }]
function renderSessionsChart(sessionsPerBucket, timeRange) {
    const ctx = document.getElementById('userTrafficChart');
    if (!ctx) return;

    if (userChart) {
        userChart.destroy();
    }

    const points = (sessionsPerBucket || []).map(d => ({
        x: new Date(d.date).getTime(),
        y: d.count
    }));

    userChart = new Chart(ctx, {
        type: 'bar',
        data: {
            datasets: points.length === 0 ? [] : [{
                label: t.sessionsSeries,
                data: points,
                borderColor: borderColors[0],
                backgroundColor: colors[0],
                borderWidth: 1
            }]
        },
        options: {
            ...commonOptions,
            locale: t.lang,
            plugins: {
                ...commonOptions.plugins,
                legend: { display: false },
                title: {
                    display: true,
                    text: points.length === 0 ? t.sessionsEmpty : t.sessionsTitle
                },
                tooltip: {
                    callbacks: {
                        title: items => tooltipTitle(items, timeRange),
                        label: item => {
                            const n = item.parsed.y;
                            return n === 1 ? t.sessionOne : t.sessionOther.replace('{0}', n.toLocaleString(t.lang));
                        }
                    }
                }
            },
            scales: {
                ...commonOptions.scales,
                x: timeAxis(timeRange, { grid: { display: false } }),
                y: {
                    beginAtZero: true,
                    ticks: { precision: 0 },
                    title: { display: true, text: t.axisSessions }
                }
            }
        }
    });
}

/** A stacked bar chart of one series per menu or item: [{ <label>, data: [{ date, count }] }]. */
function renderStackedChart(ctx, series, label, title, timeRange) {
    const datasets = series.map((entry, index) => ({
        label: entry[label],
        data: entry.data.map(d => ({
            x: new Date(d.date).getTime(),
            y: d.count
        })),
        borderColor: borderColors[index % borderColors.length],
        backgroundColor: colors[index % colors.length],
        tension: 0.4,
        fill: false
    }));

    return new Chart(ctx, {
        type: 'bar',
        data: { datasets },
        options: {
            ...commonOptions,
            locale: t.lang,
            plugins: {
                ...commonOptions.plugins,
                title: { display: true, text: title },
                tooltip: {
                    callbacks: {
                        title: items => tooltipTitle(items, timeRange),
                        label: item => `${item.dataset.label}: ${item.parsed.y.toLocaleString(t.lang)}`
                    }
                }
            },
            scales: {
                ...commonOptions.scales,
                x: timeAxis(timeRange, { stacked: true }),
                y: {
                    stacked: true,
                    beginAtZero: true,
                    title: { display: true, text: t.axisCount }
                }
            }
        }
    });
}

function renderMenuTrafficChart(menuTrafficData, timeRange) {
    const ctx = document.getElementById('menuTrafficChart');
    if (!ctx) return;

    if (menuChart) {
        menuChart.destroy();
    }

    menuChart = !menuTrafficData || menuTrafficData.length === 0
        ? emptyChart(ctx, 'bar', t.menusEmpty)
        : renderStackedChart(ctx, menuTrafficData, 'menuName', t.menusTitle, timeRange);
}

function renderMenuItemTrafficChart(menuItemTrafficData, timeRange) {
    const ctx = document.getElementById('menuItemTrafficChart');
    if (!ctx) return;

    if (menuItemChart) {
        menuItemChart.destroy();
    }

    // Show the top 15 menu items
    menuItemChart = !menuItemTrafficData || menuItemTrafficData.length === 0
        ? emptyChart(ctx, 'line', t.itemsEmpty)
        : renderStackedChart(ctx, menuItemTrafficData.slice(0, 15), 'menuItemName', t.itemsTitle, timeRange);
}

// Cleanup function
function destroyAnalyticsCharts() {
    if (userChart) {
        userChart.destroy();
        userChart = null;
    }
    if (menuChart) {
        menuChart.destroy();
        menuChart = null;
    }
    if (menuItemChart) {
        menuItemChart.destroy();
        menuItemChart = null;
    }
}

// Make functions globally available
window.renderAnalyticsCharts = renderAnalyticsCharts;
window.destroyAnalyticsCharts = destroyAnalyticsCharts;
