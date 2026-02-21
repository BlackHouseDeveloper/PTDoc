/**
 * PTDoc Theme Manager
 * Handles light/dark theme switching with localStorage persistence
 */
window.ptdocTheme = {
  _isInitialized: false,
  _mediaQueryList: null,
  _isDocumentEnhancedNavListenerAttached: false,
  _isBlazorEnhancedNavListenerAttached: false,

  /**
   * Resolve persisted/system theme preference
   * @returns {string} 'light' or 'dark'
   */
  resolvePreferredTheme: () => {
    const savedTheme = localStorage.getItem('ptdoc-theme');
    if (savedTheme === 'dark' || savedTheme === 'light') {
      return savedTheme;
    }

    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    return prefersDark ? 'dark' : 'light';
  },

  /**
   * Re-apply the preferred theme to DOM (used after enhanced navigation)
   */
  applyPreferredTheme: () => {
    const preferredTheme = ptdocTheme.resolvePreferredTheme();
    ptdocTheme.setTheme(preferredTheme);
  },

  /**
   * Attach listener for Blazor enhanced navigation to preserve theme class
   */
  registerEnhancedNavigationSync: () => {
    if (!ptdocTheme._isDocumentEnhancedNavListenerAttached) {
      document.addEventListener('enhancedload', ptdocTheme.applyPreferredTheme);
      ptdocTheme._isDocumentEnhancedNavListenerAttached = true;
    }

    if (!ptdocTheme._isBlazorEnhancedNavListenerAttached && window.Blazor && typeof window.Blazor.addEventListener === 'function') {
      window.Blazor.addEventListener('enhancedload', ptdocTheme.applyPreferredTheme);
      ptdocTheme._isBlazorEnhancedNavListenerAttached = true;
    }
  },

  /**
   * Initialize theme on page load
   */
  init: () => {
    ptdocTheme.applyPreferredTheme();

    if (ptdocTheme._isInitialized) {
      return;
    }

    ptdocTheme._isInitialized = true;

    // Listen for system theme changes
    ptdocTheme._mediaQueryList = window.matchMedia('(prefers-color-scheme: dark)');
    ptdocTheme._mediaQueryList.addEventListener('change', (e) => {
      if (!localStorage.getItem('ptdoc-theme')) {
        ptdocTheme.setTheme(e.matches ? 'dark' : 'light');
      }
    });

    // Blazor enhanced navigation can replace DOM and drop runtime classes.
    // Re-apply persisted theme whenever enhanced navigation completes.
    ptdocTheme.registerEnhancedNavigationSync();

    // If Blazor wasn't available yet, try to bind to its event API shortly after startup.
    setTimeout(() => {
      ptdocTheme.registerEnhancedNavigationSync();
    }, 0);
  },

  /**
   * Toggle between light and dark theme
   */
  toggle: () => {
    const currentTheme = document.documentElement.classList.contains('dark') ? 'dark' : 'light';
    const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
    ptdocTheme.setTheme(newTheme);
    return newTheme;
  },

  /**
   * Set theme to specific value
   * @param {string} theme - 'light' or 'dark'
   */
  setTheme: (theme) => {
    const root = document.documentElement;
    
    if (theme === 'dark') {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }
    
    localStorage.setItem('ptdoc-theme', theme);
    
    // Dispatch custom event for Blazor components to listen
    window.dispatchEvent(new CustomEvent('ptdoc-theme-changed', { 
      detail: { theme } 
    }));
  },

  /**
   * Get current theme
   * @returns {string} 'light' or 'dark'
   */
  getTheme: () => {
    return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
  }
};

// Auto-initialize on load
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', ptdocTheme.init);
} else {
  ptdocTheme.init();
}
