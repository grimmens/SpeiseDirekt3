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

function renderAnalyticsCharts(sessionsPerBucket, menuTrafficData, menuItemTrafficData, timeRange) {
    console.log('renderAnalyticsCharts called with data:');
    console.log('sessionsPerBucket:', sessionsPerBucket);
    console.log('menuTrafficData:', menuTrafficData);
    console.log('menuItemTrafficData:', menuItemTrafficData);
    console.log('timeRange:', timeRange);

    renderSessionsChart(sessionsPerBucket, timeRange);
    renderMenuTrafficChart(menuTrafficData, timeRange);
    renderMenuItemTrafficChart(menuItemTrafficData, timeRange);
}

// Sessions (anonymous guest devices) active per hour/day as one series: [{ date, count }]
function renderSessionsChart(sessionsPerBucket, timeRange) {
    const ctx = document.getElementById('userTrafficChart');
    if (!ctx) return;

    // Destroy existing chart
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
                label: 'Sessions',
                data: points,
                borderColor: borderColors[0],
                backgroundColor: colors[0],
                borderWidth: 1
            }]
        },
        options: {
            ...commonOptions,
            plugins: {
                ...commonOptions.plugins,
                legend: { display: false },
                title: {
                    display: true,
                    text: points.length === 0 ? 'Sessions Over Time - No Data' : 'Sessions Over Time'
                },
                tooltip: {
                    callbacks: {
                        title: function (tooltipItems) {
                            const date = new Date(tooltipItems[0].parsed.x);

                            if (timeRange === 'Last24Hours') {
                                const startHour = new Date(date);
                                startHour.setMinutes(0, 0, 0);

                                const endHour = new Date(startHour);
                                endHour.setHours(endHour.getHours() + 1);

                                const pad = n => String(n).padStart(2, '0');

                                return `${date.toLocaleDateString()} ${pad(startHour.getHours())}:00 - ${pad(endHour.getHours())}:00`;
                            }
                            return date.toLocaleDateString(undefined, {
                                year: 'numeric',
                                month: 'short',
                                day: 'numeric'
                            });
                        },
                        label: function (tooltipItem) {
                            const n = tooltipItem.parsed.y;
                            return `${n} ${n === 1 ? 'session' : 'sessions'}`;
                        }
                    }
                }
            },
            scales: {
                ...commonOptions.scales,
                x: {
                    type: 'time',
                    time: {
                        unit: timeRange === 'Last24Hours' ? 'hour' : 'day',
                        displayFormats: {
                            hour: 'HH:00',
                            day: 'MMM dd' // e.g. "Jul 20"
                        }
                    },
                    title: {
                        display: true,
                        text: timeRange === 'Last24Hours' ? 'Hour' : 'Date'
                    },
                    grid: { display: false },
                    ticks: { source: 'auto' }
                },
                y: {
                    beginAtZero: true,
                    ticks: { precision: 0 },
                    title: {
                        display: true,
                        text: 'Sessions'
                    }
                }
            }
        }
    });
}

function renderMenuTrafficChart(menuTrafficData, timeRange) {
    const ctx = document.getElementById('menuTrafficChart');
    if (!ctx) return;

    // Destroy existing chart
    if (menuChart) {
        menuChart.destroy();
    }

    // Handle empty data
    if (!menuTrafficData || menuTrafficData.length === 0) {
        menuChart = new Chart(ctx, {
            type: 'bar',
            data: {
                datasets: []
            },
            options: {
                ...commonOptions,
                plugins: {
                    ...commonOptions.plugins,
                    title: {
                        display: true,
                        text: 'Menu Views Over Time - No Data'
                    }
                }
            }
        });
        return;
    }

    // Prepare data
    const datasets = menuTrafficData.map((menu, index) => ({
        label: menu.menuName,
        data: menu.data.map(d => ({
            x: new Date(d.date).getTime(),
            y: d.count
        })),
        borderColor: borderColors[index % borderColors.length],
        backgroundColor: colors[index % colors.length],
        tension: 0.4,
        fill: false
    }));

    // Configure X-axis based on time range
    const xAxisConfig = {
        type: 'time',
        time: {
            unit: timeRange === 'Last24Hours' ? 'hour' : 'day',
            displayFormats: {
                hour: 'HH:00',
                day: 'MMM dd' // e.g. "Jul 20"
            }
        },
        title: {
            display: true,
            text: timeRange === 'Last24Hours' ? 'Hour' : 'Date'
        },
        stacked: true,
        ticks: {
            source: 'auto'
        }
    };

    const yAxisConfig = {
        stacked: true,
        beginAtZero: true,
        title: {
            display: true,
            text: 'Count'
        }
    };

    menuChart = new Chart(ctx, {
        type: 'bar',
        data: {
            datasets: datasets
        },
        options: {
            ...commonOptions,
            plugins: {
                ...commonOptions.plugins,
                title: {
                    display: true,
                    text: 'Menu Views Over Time'
                },
                tooltip: {
                    callbacks: {
                        title: function (tooltipItems) {
                            const date = new Date(tooltipItems[0].parsed.x);

                            if (timeRange === 'Last24Hours') {
                                const startHour = new Date(date);
                                startHour.setMinutes(0, 0, 0);

                                const endHour = new Date(startHour);
                                endHour.setHours(endHour.getHours() + 1);

                                const pad = n => String(n).padStart(2, '0');

                                const startTime = `${pad(startHour.getHours())}:00`;
                                const endTime = `${pad(endHour.getHours())}:00`;

                                return `${date.toLocaleDateString()} ${startTime} - ${endTime}`;
                            } else {
                                return date.toLocaleDateString(undefined, {
                                    year: 'numeric',
                                    month: 'short',
                                    day: 'numeric'
                                });
                            }
                        },
                        label: function (tooltipItem) {
                            return `${tooltipItem.dataset.label}: ${tooltipItem.parsed.y}`;
                        }
                    }
                }
            },
            scales: {
                ...commonOptions.scales,
                x: xAxisConfig,
                y: yAxisConfig
            }
        }
    });
}

function renderMenuItemTrafficChart(menuItemTrafficData, timeRange) {
    const ctx = document.getElementById('menuItemTrafficChart');
    if (!ctx) return;

    // Destroy existing chart
    if (menuItemChart) {
        menuItemChart.destroy();
    }

    // Handle empty data
    if (!menuItemTrafficData || menuItemTrafficData.length === 0) {
        menuItemChart = new Chart(ctx, {
            type: 'line',
            data: {
                datasets: []
            },
            options: {
                ...commonOptions,
                plugins: {
                    ...commonOptions.plugins,
                    title: {
                        display: true,
                        text: 'Menu Item Clicks Over Time - No Data'
                    }
                }
            }
        });
        return;
    }

    // Show top 15 menu items
    const topItems = menuItemTrafficData.slice(0, 15);

    const datasets = topItems.map((item, index) => ({
        label: item.menuItemName,
        data: item.data.map(d => ({
            x: new Date(d.date).getTime(),
            y: d.count
        })),
        borderColor: borderColors[index % borderColors.length],
        backgroundColor: colors[index % colors.length],
        tension: 0.4,
        fill: false
    }));

    // Configure X-axis based on time range

    const xAxisConfig = {
        type: 'time',
        time: {
            unit: timeRange === 'Last24Hours' ? 'hour' : 'day',
            displayFormats: {
                hour: 'HH:00',
                day: 'MMM dd' // e.g. "Jul 20"
            }
        },
        title: {
            display: true,
            text: timeRange === 'Last24Hours' ? 'Hour' : 'Date'
        },
        stacked: true,
        ticks: {
            source: 'auto'
        }
    };

    const yAxisConfig = {
        stacked: true,
        beginAtZero: true,
        title: {
            display: true,
            text: 'Count'
        }
    };

    menuItemChart = new Chart(ctx, {
        type: 'bar',
        data: {
            datasets: datasets
        },
        options: {
            ...commonOptions,
            plugins: {
                ...commonOptions.plugins,
                title: {
                    display: true,
                    text: 'Menu Item Clicks Over Time'
                },
                tooltip: {
                    callbacks: {
                        title: function (tooltipItems) {
                            const date = new Date(tooltipItems[0].parsed.x);

                            if (timeRange === 'Last24Hours') {
                                const startHour = new Date(date);
                                startHour.setMinutes(0, 0, 0);

                                const endHour = new Date(startHour);
                                endHour.setHours(endHour.getHours() + 1);

                                const pad = n => String(n).padStart(2, '0');

                                const startTime = `${pad(startHour.getHours())}:00`;
                                const endTime = `${pad(endHour.getHours())}:00`;

                                return `${date.toLocaleDateString()} ${startTime} - ${endTime}`;
                            } else {
                                return date.toLocaleDateString(undefined, {
                                    year: 'numeric',
                                    month: 'short',
                                    day: 'numeric'
                                });
                            }
                        },
                        label: function (tooltipItem) {
                            return `${tooltipItem.dataset.label}: ${tooltipItem.parsed.y}`;
                        }
                    }
                }
            },
            scales: {
                ...commonOptions.scales,
                x: xAxisConfig,
                y: yAxisConfig
            }
        }
    });
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
