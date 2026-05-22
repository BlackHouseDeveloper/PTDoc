export function replaceUrl(url) {
    window.history.replaceState(window.history.state, document.title, url);
}
