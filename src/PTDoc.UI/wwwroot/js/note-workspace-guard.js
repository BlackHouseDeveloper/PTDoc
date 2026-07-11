const beforeUnloadHandlers = new Map();

export function setIncompleteNoteBeforeUnload(key, enabled) {
    const guardKey = key || "default";
    const existing = beforeUnloadHandlers.get(guardKey);

    if (!enabled) {
        if (existing) {
            window.removeEventListener("beforeunload", existing);
            beforeUnloadHandlers.delete(guardKey);
        }

        return;
    }

    if (existing) {
        return;
    }

    const handler = (event) => {
        event.preventDefault();
        event.returnValue = "";
        return "";
    };

    beforeUnloadHandlers.set(guardKey, handler);
    window.addEventListener("beforeunload", handler);
}

export function focusMissingRequiredField(selector) {
    if (!selector) {
        return false;
    }

    const target = document.querySelector(selector);
    if (!target) {
        return false;
    }

    const focusTarget = target.matches("input, select, textarea, button, [tabindex]")
        ? target
        : target.querySelector("input, select, textarea, button, [tabindex]") || target;

    const highlightTarget = target.closest("[data-note-field-key], .pt-card__field, .pt-card, [data-testid]") || target;
    highlightTarget.classList.remove("note-workspace__missing-field-highlight");
    void highlightTarget.offsetWidth;
    highlightTarget.classList.add("note-workspace__missing-field-highlight");

    const prefersReducedMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches === true;
    target.scrollIntoView({ block: "center", behavior: prefersReducedMotion ? "auto" : "smooth" });

    if (focusTarget instanceof HTMLElement) {
        focusTarget.focus({ preventScroll: true });
    }

    return true;
}
