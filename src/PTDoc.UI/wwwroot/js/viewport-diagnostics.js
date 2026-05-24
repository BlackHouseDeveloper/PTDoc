const DRAWER_BREAKPOINT = 1200;
const STORAGE_KEY = 'ptdoc.viewportDiagnostics';

let dotNetReference = null;
let serverEnabled = false;
let resizeHandler = null;
let themeHandler = null;
let mutationObserver = null;
let updateTimer = null;

export function initializeViewportDiagnostics(reference, isServerEnabled) {
  dotNetReference = reference;
  serverEnabled = Boolean(isServerEnabled);

  applyQueryOverride();
  if (!isDiagnosticsEnabled()) {
    dotNetReference = null;
    return;
  }

  notify();

  resizeHandler = debounce(notify);
  themeHandler = debounce(notify);
  window.addEventListener('resize', resizeHandler);
  window.addEventListener('ptdoc-theme-changed', themeHandler);

  mutationObserver = new MutationObserver(debounce(notify));
  mutationObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  mutationObserver.observe(document.body, { attributes: true, attributeFilter: ['class'] });
}

export function disposeViewportDiagnostics() {
  if (resizeHandler) {
    window.removeEventListener('resize', resizeHandler);
  }

  if (themeHandler) {
    window.removeEventListener('ptdoc-theme-changed', themeHandler);
  }

  if (mutationObserver) {
    mutationObserver.disconnect();
  }

  if (updateTimer) {
    window.clearTimeout(updateTimer);
  }

  dotNetReference = null;
  resizeHandler = null;
  themeHandler = null;
  mutationObserver = null;
  updateTimer = null;
}

function debounce(callback) {
  return () => {
    if (updateTimer) {
      window.clearTimeout(updateTimer);
    }

    updateTimer = window.setTimeout(callback, 80);
  };
}

function notify() {
  if (!dotNetReference) {
    return;
  }

  dotNetReference.invokeMethodAsync('OnViewportDiagnosticsChanged', captureViewportDiagnostics())
    .catch(() => {
      // Ignore disconnected Blazor circuits.
    });
}

function captureViewportDiagnostics() {
  const width = Math.round(window.innerWidth || document.documentElement.clientWidth || 0);
  const height = Math.round(window.innerHeight || document.documentElement.clientHeight || 0);
  const devicePixelRatio = Number(window.devicePixelRatio || 1);

  return {
    isVisible: isDiagnosticsEnabled(),
    width,
    height,
    devicePixelRatio,
    zoomEstimate: estimateZoom(width),
    theme: getTheme(),
    layoutMode: getLayoutMode(width)
  };
}

function isDiagnosticsEnabled() {
  return serverEnabled || getStoredOverride();
}

function estimateZoom(width) {
  const outerWidth = Number(window.outerWidth || 0);
  if (!outerWidth || !width) {
    return 1;
  }

  const estimate = outerWidth / width;
  return Number.isFinite(estimate) ? Number(estimate.toFixed(2)) : 1;
}

function getTheme() {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}

function getLayoutMode(width) {
  if (width < DRAWER_BREAKPOINT) {
    return 'drawer';
  }

  const sidebar = document.querySelector('.sidebar');
  if (sidebar?.classList.contains('closed')) {
    return 'desktop-icon-rail';
  }

  return 'desktop-full';
}

function applyQueryOverride() {
  const params = new URLSearchParams(window.location.search);
  if (!params.has('ptdocViewportDiagnostics')) {
    return;
  }

  const value = params.get('ptdocViewportDiagnostics')?.toLowerCase();
  if (value === '1' || value === 'true' || value === 'on') {
    localStorage.setItem(STORAGE_KEY, 'true');
    return;
  }

  if (value === '0' || value === 'false' || value === 'off') {
    localStorage.setItem(STORAGE_KEY, 'false');
  }
}

function getStoredOverride() {
  return localStorage.getItem(STORAGE_KEY) === 'true';
}
