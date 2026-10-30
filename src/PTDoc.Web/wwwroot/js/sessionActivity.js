let stopCurrent = null;

// No bearer credential enters JavaScript. This identifier binds the current
// circuit to its existing HttpOnly cookie and cannot authenticate a request.
export function start(sessionId, receiver) {
  stop();
  let lastAttempt = -Infinity;
  let pending = false;
  let stopped = false;
  const controller = new AbortController();
  const events = ["input", "keydown", "pointerdown", "wheel", "touchstart"];

  const activity = async (event) => {
    if (!event.isTrusted || document.visibilityState !== "visible" || stopped || pending) return;
    const now = performance.now();
    if (now - lastAttempt < 60000) return;
    lastAttempt = now;
    pending = true;
    try {
      const response = await fetch("/auth/activity", {
        method: "POST",
        credentials: "same-origin",
        cache: "no-store",
        headers: { "X-PTDoc-Web-Session": sessionId },
        signal: controller.signal
      });
      if (!stopped && (response.status === 401 || response.status === 409)) {
        await receiver.invokeMethodAsync("SessionEnded", response.status === 409 ? "SessionChanged" : "SessionEnded");
        stopped = true;
      }
    } catch {
      // A disconnected network/circuit does not prove the session expired.
      // Retry on subsequent real activity without clearing any draft.
    } finally {
      pending = false;
    }
  };
  for (const name of events) document.addEventListener(name, activity, { passive: true, capture: true });
  stopCurrent = () => {
    stopped = true;
    controller.abort();
    for (const name of events) document.removeEventListener(name, activity, { capture: true });
  };
}

export function stop() {
  stopCurrent?.();
  stopCurrent = null;
}
