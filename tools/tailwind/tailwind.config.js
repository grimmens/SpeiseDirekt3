// Tailwind CSS 3.4.17 for the back office. Built by tools/tailwind/build.sh (Linux/macOS) or build.ps1 (Windows) into
// SpeiseDirekt3/wwwroot/css/tailwind.css, which is committed; CI refuses a stale one (build.sh --check).
// Until AP15b the same config was inline in Components/App.razor for the Play CDN (cdn.tailwindcss.com, 3.4.17).
//
// Only complete class names in these files end up in the CSS: never build one from fragments
// (`bg-${tone}-600`, "text-" + color) — write the full names, e.g. in a switch.

// Brand palettes from css/brand.css (RGB channels), so opacity modifiers work: bg-brand-600/20. `gray` is the back
// office's cool, brand-tinted gray (AP11, --sd-gray-*), `shadow-sm` the card shadow (--sd-shadow-card).
const scale = name => Object.fromEntries([50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950]
    .map(step => [step, `rgb(var(--sd-${name}-${step}) / <alpha-value>)`]));

/** @type {import('tailwindcss').Config} */
module.exports = {
    content: {
        // Paths relative to this file, not to the working directory
        relative: true,
        files: [
            // Everything the back office renders: pages, layout, shared components, account settings, C# helpers that
            // return class names. The guest pages, the live demo, the landing page and the imprint have their own CSS
            // and are never rendered in the back-office document, so they are left out.
            '../../SpeiseDirekt3/Components/**/*.{razor,cs}',
            '!../../SpeiseDirekt3/Components/{Guest,Demo,Landing,Legal}/**',
            // Back-office scripts that set class names (qr-codes.js toggles `hidden`)
            '../../SpeiseDirekt3/wwwroot/js/{analytics,fullcalendar-interop,menu-preview-draft,modal-dialog,qr-codes,shell,sortable-list,speisedirekt}.js'
        ]
    },
    theme: {
        extend: {
            colors: {
                brand: scale('brand'),
                primary: scale('brand'),
                accent: scale('accent'),
                gray: scale('gray')
            },
            boxShadow: {
                sm: 'var(--sd-shadow-card)'
            },
            fontFamily: {
                'sans': ['Inter', 'system-ui', 'sans-serif'],
                'display': ['Manrope', 'system-ui', 'sans-serif'],
            }
        }
    }
};
