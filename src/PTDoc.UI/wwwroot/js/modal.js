// Modal utilities - ESC key handler and body scroll lock
export class ModalHelper {
    constructor() {
        this.escapeHandlers = new Map();
        this.originalBodyOverflow = null;
        this.scrollbarWidth = 0;
    }

    // Get scrollbar width to prevent layout shift
    getScrollbarWidth() {
        if (this.scrollbarWidth > 0) return this.scrollbarWidth;
        
        const outer = document.createElement('div');
        outer.style.visibility = 'hidden';
        outer.style.overflow = 'scroll';
        document.body.appendChild(outer);
        
        const inner = document.createElement('div');
        outer.appendChild(inner);
        
        this.scrollbarWidth = outer.offsetWidth - inner.offsetWidth;
        document.body.removeChild(outer);
        
        return this.scrollbarWidth;
    }

    // Lock body scroll when modal opens
    lockBodyScroll() {
        if (document.body.classList.contains('modal-open')) {
            return; // Already locked
        }

        this.originalBodyOverflow = document.body.style.overflow;
        const scrollbarWidth = this.getScrollbarWidth();
        
        // Prevent body scroll
        document.body.style.overflow = 'hidden';
        
        // Prevent layout shift by adding padding equal to scrollbar width
        if (scrollbarWidth > 0) {
            document.body.style.paddingRight = `${scrollbarWidth}px`;
        }
        
        document.body.classList.add('modal-open');
    }

    // Unlock body scroll when modal closes
    unlockBodyScroll() {
        if (!document.body.classList.contains('modal-open')) {
            return; // Already unlocked
        }

        document.body.style.overflow = this.originalBodyOverflow || '';
        document.body.style.paddingRight = '';
        document.body.classList.remove('modal-open');
    }

    // Register ESC key handler for a modal
    registerEscapeHandler(modalId, dotNetRef) {
        const handler = (e) => {
            if (e.key === 'Escape' || e.key === 'Esc') {
                e.preventDefault();
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('CloseFromJs');
            }
        };

        this.escapeHandlers.set(modalId, handler);
        document.addEventListener('keydown', handler, true);
    }

    // Unregister ESC key handler
    unregisterEscapeHandler(modalId) {
        const handler = this.escapeHandlers.get(modalId);
        if (handler) {
            document.removeEventListener('keydown', handler, true);
            this.escapeHandlers.delete(modalId);
        }
    }

    // Focus trap - keep focus within modal
    setupFocusTrap(modalElement) {
        if (!modalElement) return null;

        const focusableSelector = 'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
        const focusableElements = modalElement.querySelectorAll(focusableSelector);
        
        if (focusableElements.length === 0) return null;

        const firstFocusable = focusableElements[0];
        const lastFocusable = focusableElements[focusableElements.length - 1];

        const trapHandler = (e) => {
            if (e.key !== 'Tab') return;

            if (e.shiftKey) {
                // Shift + Tab
                if (document.activeElement === firstFocusable) {
                    e.preventDefault();
                    lastFocusable.focus();
                }
            } else {
                // Tab
                if (document.activeElement === lastFocusable) {
                    e.preventDefault();
                    firstFocusable.focus();
                }
            }
        };

        modalElement.addEventListener('keydown', trapHandler);

        // Focus first element
        setTimeout(() => firstFocusable.focus(), 50);

        return {
            dispose: () => modalElement.removeEventListener('keydown', trapHandler)
        };
    }

    // Clean up all handlers (for disposal)
    dispose() {
        this.escapeHandlers.forEach((handler, modalId) => {
            this.unregisterEscapeHandler(modalId);
        });
        this.unlockBodyScroll();
    }
}

// Global helper instance
let modalHelperInstance = null;

export function getModalHelper() {
    if (!modalHelperInstance) {
        modalHelperInstance = new ModalHelper();
    }
    return modalHelperInstance;
}

// Public API for Blazor
export function lockBodyScroll() {
    getModalHelper().lockBodyScroll();
}

export function unlockBodyScroll() {
    getModalHelper().unlockBodyScroll();
}

export function registerEscapeHandler(modalId, dotNetRef) {
    getModalHelper().registerEscapeHandler(modalId, dotNetRef);
}

export function unregisterEscapeHandler(modalId) {
    getModalHelper().unregisterEscapeHandler(modalId);
}

export function setupFocusTrap(modalElement) {
    return getModalHelper().setupFocusTrap(modalElement);
}
