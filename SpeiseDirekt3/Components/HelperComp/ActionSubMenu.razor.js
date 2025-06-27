let outsideClickHandler = null;

export function registerOutsideClick(element, dotNetHelper) {
    outsideClickHandler = (event) => {
        if (!element.contains(event.target)) {
            dotNetHelper.invokeMethodAsync("CloseMenu");
        }
    };
    document.addEventListener("mousedown", outsideClickHandler);
}

export function unregisterOutsideClick() {
    if (outsideClickHandler) {
        document.removeEventListener("mousedown", outsideClickHandler);
        outsideClickHandler = null;
    }
}
