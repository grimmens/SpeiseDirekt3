// wwwroot/js/elissar-menu.js

let dotNetRef = null;
let categoryGridRef = null;
let categoryElements = [];
let categories = [];
let scrollHandler = null;

export function initialize(dotNetReference, categoryGrid, categoryElementRefs, categoryNames) {
    dotNetRef = dotNetReference;
    categoryGridRef = categoryGrid;
    categoryElements = categoryElementRefs;
    categories = categoryNames;

    // Setup scroll handler
    scrollHandler = () => handleScroll();
    window.addEventListener('scroll', scrollHandler);
}

export function scrollToCategory(categoryName, showStickyNav) {
    const categoryIndex = categories.indexOf(categoryName);
    if (categoryIndex >= 0 && categoryElements[categoryIndex]) {
        const element = categoryElements[categoryIndex];
        const headerHeight = showStickyNav ? 60 : 0;
        const elementPosition = element.offsetTop - headerHeight;
        window.scrollTo({
            top: elementPosition,
            behavior: 'smooth'
        });
    }
}

function handleScroll() {
    if (!dotNetRef || !categoryGridRef) return;

    const scrollPosition = window.scrollY;

    // Show/hide sticky navigation based on category grid visibility
    const categoryGridBottom = categoryGridRef.offsetTop + categoryGridRef.offsetHeight;
    const shouldShowStickyNav = scrollPosition > categoryGridBottom - 100;

    // Update sticky nav state
    dotNetRef.invokeMethodAsync('UpdateStickyNav', shouldShowStickyNav);

    // Update active category based on scroll position
    const adjustedScrollPosition = scrollPosition + 150;

    for (let i = 0; i < categories.length; i++) {
        const element = categoryElements[i];
        if (element && element.offsetTop !== undefined) {
            const { offsetTop, offsetHeight } = element;
            if (adjustedScrollPosition >= offsetTop && adjustedScrollPosition < offsetTop + offsetHeight) {
                dotNetRef.invokeMethodAsync('UpdateActiveCategory', categories[i]);
                break;
            }
        }
    }
}

// Cleanup function
export function dispose() {
    if (scrollHandler) {
        window.removeEventListener('scroll', scrollHandler);
        scrollHandler = null;
    }
    dotNetRef = null;
    categoryGridRef = null;
    categoryElements = [];
    categories = [];
}