// PTDoc JavaScript Utilities

window.PTDoc = {
    // Modal utilities
    lockBodyScroll: function() {
        document.body.style.overflow = 'hidden';
    },
    
    unlockBodyScroll: function() {
        document.body.style.overflow = '';
    },
    
    // Theme utilities
    getTheme: function() {
        return localStorage.getItem('theme') || 'light';
    },
    
    setTheme: function(theme) {
        localStorage.setItem('theme', theme);
    }
};
