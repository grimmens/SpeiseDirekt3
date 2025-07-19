// Analytics Charts JavaScript
let userChart, menuChart, menuItemChart;

// Color palette for charts
const colors = [
    'rgba(59, 130, 246, 0.8)',   // Blue
    'rgba(34, 197, 94, 0.8)',    // Green
    'rgba(168, 85, 247, 0.8)',   // Purple
    'rgba(245, 158, 11, 0.8)',   // Amber
    'rgba(239, 68, 68, 0.8)',    // Red
    'rgba(20, 184, 166, 0.8)',   // Teal
    'rgba(251, 146, 60, 0.8)',   // Orange
    'rgba(139, 92, 246, 0.8)',   // Violet
];

const borderColors = [
    'rgba(59, 130, 246, 1)',
    'rgba(34, 197, 94, 1)',
    'rgba(168, 85, 247, 1)',
    'rgba(245, 158, 11, 1)',
    'rgba(239, 68, 68, 1)',
    'rgba(20, 184, 166, 1)',
    'rgba(251, 146, 60, 1)',
    'rgba(139, 92, 246, 1)',
];

// Common chart options
const commonOptions = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
        legend: {
            position: 'top',
            labels: {
                usePointStyle: true,
                padding: 20
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

function renderAnalyticsCharts(userTrafficData, menuTrafficData, menuItemTrafficData) {
    console.log('renderAnalyticsCharts called with data:');
    console.log('userTrafficData:', userTrafficData);
    console.log('menuTrafficData:', menuTrafficData);
    console.log('menuItemTrafficData:', menuItemTrafficData);

    renderUserTrafficChart(userTrafficData);
    renderMenuTrafficChart(menuTrafficData);
    renderMenuItemTrafficChart(menuItemTrafficData);
}

function renderUserTrafficChart(userTrafficData) {
    const ctx = document.getElementById('userTrafficChart');
    if (!ctx) return;

    // Destroy existing chart
    if (userChart) {
        userChart.destroy();
    }

    // Handle empty data
    if (!userTrafficData || userTrafficData.length === 0) {
        userChart = new Chart(ctx, {
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
                        text: 'User Activity Over Time - No Data'
                    }
                }
            }
        });
        return;
    }

    // Prepare data - show top 10 users
    const topUsers = userTrafficData.slice(0, 10);

    const datasets = topUsers.map((user, index) => ({
        label: `User ${user.sessionId.substring(0, 8)}...`,
        data: user.data.map(d => ({
            x: new Date(d.date).getTime(),
            y: d.count
        })),
        borderColor: borderColors[index % borderColors.length],
        backgroundColor: colors[index % colors.length],
        tension: 0.4,
        fill: false
    }));

    userChart = new Chart(ctx, {
        type: 'line',
        data: {
            datasets: datasets
        },
        options: {
            ...commonOptions,
            plugins: {
                ...commonOptions.plugins,
                title: {
                    display: true,
                    text: 'User Activity Over Time'
                },
                tooltip: {
                    callbacks: {
                        label: function (context) {
                            const label = context.dataset.label || '';
                            const y = context.parsed.y;
                            return `${label}: ${y}`;
                        },
                        title: function (context) {
                            const x = context[0].parsed.x;
                            return `Date: ${new Date(x).toLocaleDateString()}`;
                        }
                    }
                }
            },
            scales: {
                ...commonOptions.scales,
                x: {
                    ...commonOptions.scales.x,
                    type: 'linear',
                    title: {
                        display: true,
                        text: 'Date'
                    },
                    ticks: {
                        callback: function(value) {
                            return new Date(value).toLocaleDateString();
                        }
                    }
                }
            }
        }
    });
}

function renderMenuTrafficChart(menuTrafficData) {
    const ctx = document.getElementById('menuTrafficChart');
    if (!ctx) return;

    // Destroy existing chart
    if (menuChart) {
        menuChart.destroy();
    }

    // Handle empty data
    if (!menuTrafficData || menuTrafficData.length === 0) {
        menuChart = new Chart(ctx, {
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

    menuChart = new Chart(ctx, {
        type: 'line',
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
                        label: function (context) {
                            const label = context.dataset.label || '';
                            const y = context.parsed.y;
                            return `${label}: ${y}`;
                        },
                        title: function (context) {
                            const x = context[0].parsed.x;
                            return `Date: ${new Date(x).toLocaleDateString()}`;
                        }
                    }
                }
            },
            scales: {
                ...commonOptions.scales,
                x: {
                    ...commonOptions.scales.x,
                    type: 'linear',
                    title: {
                        display: true,
                        text: 'Date'
                    },
                    ticks: {
                        callback: function(value) {
                            return new Date(value).toLocaleDateString();
                        }
                    }
                }
            }
        }
    });
}

function renderMenuItemTrafficChart(menuItemTrafficData) {
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
                    },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                const label = context.dataset.label || '';
                                const y = context.parsed.y;
                                return `${label}: ${y}`;
                            },
                            title: function (context) {
                                const x = context[0].parsed.x;
                                return `Date: ${new Date(x).toLocaleDateString()}`;
                            }
                        }
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

    menuItemChart = new Chart(ctx, {
        type: 'line',
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
                        label: function (context) {
                            const label = context.dataset.label || '';
                            const y = context.parsed.y;
                            return `${label}: ${y}`;
                        },
                        title: function (context) {
                            const x = context[0].parsed.x;
                            return `Date: ${new Date(x).toLocaleDateString()}`;
                        }
                    }
                }
            },
            scales: {
                ...commonOptions.scales,
                x: {
                    ...commonOptions.scales.x,
                    type: 'linear',
                    title: {
                        display: true,
                        text: 'Date'
                    },
                    ticks: {
                        callback: function(value) {
                            return new Date(value).toLocaleDateString();
                        }
                    }
                }
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
