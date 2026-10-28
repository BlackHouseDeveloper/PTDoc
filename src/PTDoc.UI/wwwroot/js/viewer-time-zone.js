export function getViewerTimeZone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}
