// Modal utilities - ESC key handler and body scroll lock
export class ModalHelper {
    constructor() {
        this.escapeHandlers = new Map();
        this.focusTrapHandlers = new Map();
        this.hiddenSiblingSets = new Map();
        this.previousFocusByModal = new Map();
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
        if (this.escapeHandlers.has(modalId)) {
            return;
        }

        this.activateModalAccessibility(modalId);

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

        this.deactivateModalAccessibility(modalId);
    }

    activateModalAccessibility(modalId) {
        const modalElement = this.findActiveModalElement(modalId);
        if (!modalElement) return;

        this.previousFocusByModal.set(modalId, document.activeElement);
        this.setupFocusTrap(modalId, modalElement);
        this.hideBackgroundSiblings(modalId, modalElement);
    }

    deactivateModalAccessibility(modalId) {
        const trapHandler = this.focusTrapHandlers.get(modalId);
        if (trapHandler) {
            document.removeEventListener('keydown', trapHandler, true);
            this.focusTrapHandlers.delete(modalId);
        }

        this.restoreBackgroundSiblings(modalId);

        const previousFocus = this.previousFocusByModal.get(modalId);
        this.previousFocusByModal.delete(modalId);
        if (previousFocus instanceof HTMLElement && document.contains(previousFocus)) {
            setTimeout(() => previousFocus.focus({ preventScroll: true }), 0);
        }
    }

    findActiveModalElement(modalId) {
        const byId = document.getElementById(modalId);
        if (byId) return byId;

        const dialogs = Array.from(document.querySelectorAll('[role="dialog"][aria-modal="true"]'))
            .filter(dialog => dialog instanceof HTMLElement && dialog.offsetParent !== null);

        return dialogs.length > 0 ? dialogs[dialogs.length - 1] : null;
    }

    // Focus trap - keep focus within modal
    setupFocusTrap(modalId, modalElement) {
        if (!modalElement) return null;

        const focusableSelector = [
            'a[href]',
            'button:not([disabled])',
            'input:not([disabled])',
            'select:not([disabled])',
            'textarea:not([disabled])',
            '[tabindex]:not([tabindex="-1"])'
        ].join(',');

        const getFocusableElements = () => Array.from(modalElement.querySelectorAll(focusableSelector))
            .filter(element =>
                element instanceof HTMLElement &&
                !element.hasAttribute('disabled') &&
                element.getAttribute('aria-hidden') !== 'true' &&
                element.offsetParent !== null);

        const focusInitialElement = () => {
            const focusableElements = getFocusableElements();
            const firstFocusable = focusableElements[0] ?? modalElement;
            if (firstFocusable instanceof HTMLElement) {
                firstFocusable.focus({ preventScroll: true });
            }
        };

        const trapHandler = (e) => {
            if (e.key !== 'Tab') return;

            const focusableElements = getFocusableElements();
            if (focusableElements.length === 0) {
                e.preventDefault();
                modalElement.focus({ preventScroll: true });
                return;
            }

            const firstFocusable = focusableElements[0];
            const lastFocusable = focusableElements[focusableElements.length - 1];

            if (e.shiftKey) {
                // Shift + Tab
                if (document.activeElement === firstFocusable) {
                    e.preventDefault();
                    lastFocusable.focus({ preventScroll: true });
                }
            } else {
                // Tab
                if (document.activeElement === lastFocusable) {
                    e.preventDefault();
                    firstFocusable.focus({ preventScroll: true });
                }
            }
        };

        document.addEventListener('keydown', trapHandler, true);
        this.focusTrapHandlers.set(modalId, trapHandler);

        // Focus first element
        setTimeout(focusInitialElement, 50);

        return {
            dispose: () => {
                document.removeEventListener('keydown', trapHandler, true);
                this.focusTrapHandlers.delete(modalId);
            }
        };
    }

    hideBackgroundSiblings(modalId, modalElement) {
        const overlayElement = modalElement.closest('.modal-overlay, .detail-backdrop, .notifications-modal-overlay, .appointment-detail-modal-overlay, .signature-consent-overlay') ?? modalElement;
        const parent = overlayElement.parentElement;
        if (!parent) return;

        const hiddenSiblings = [];
        Array.from(parent.children).forEach(child => {
            if (child === overlayElement || child.contains(overlayElement)) {
                return;
            }

            hiddenSiblings.push({
                element: child,
                ariaHidden: child.getAttribute('aria-hidden')
            });
            child.setAttribute('aria-hidden', 'true');

            if ('inert' in child) {
                child.inert = true;
            }
        });

        this.hiddenSiblingSets.set(modalId, hiddenSiblings);
    }

    restoreBackgroundSiblings(modalId) {
        const hiddenSiblings = this.hiddenSiblingSets.get(modalId) ?? [];
        hiddenSiblings.forEach(({ element, ariaHidden }) => {
            if (!element || !document.contains(element)) {
                return;
            }

            if (ariaHidden === null) {
                element.removeAttribute('aria-hidden');
            } else {
                element.setAttribute('aria-hidden', ariaHidden);
            }

            if ('inert' in element) {
                element.inert = false;
            }
        });

        this.hiddenSiblingSets.delete(modalId);
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
    return getModalHelper().setupFocusTrap(`modal-${Date.now()}`, modalElement);
}

export async function copyToClipboard(text) {
    if (navigator?.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        return;
    }

    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'absolute';
    textarea.style.left = '-9999px';
    document.body.appendChild(textarea);
    textarea.select();
    document.execCommand('copy');
    document.body.removeChild(textarea);
}
