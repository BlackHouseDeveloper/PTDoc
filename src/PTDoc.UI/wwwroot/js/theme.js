/**
 * PTDoc Theme Manager
 * Handles light/dark theme switching with localStorage persistence
 */
window.ptdocTheme = {
  /**
   * Initialize theme on page load
   */
  init: () => {
    const savedTheme = localStorage.getItem('ptdoc-theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const initialTheme = savedTheme || (prefersDark ? 'dark' : 'light');
    
    ptdocTheme.setTheme(initialTheme);
    
    // Listen for system theme changes
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
      if (!localStorage.getItem('ptdoc-theme')) {
        ptdocTheme.setTheme(e.matches ? 'dark' : 'light');
      }
    });
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
