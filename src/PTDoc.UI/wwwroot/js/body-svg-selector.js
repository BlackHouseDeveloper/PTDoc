const guardedRoots = new WeakMap();

export function attachKeyboardGuards(root) {
  if (!root || guardedRoots.has(root)) {
    return;
  }

  const handler = (event) => {
    const target = event.target;
    if (!(target instanceof Element) || !target.closest(".body-svg-selector__region")) {
      return;
    }

    if (event.key === " " || event.key === "Enter") {
      event.preventDefault();
    }
  };

  root.addEventListener("keydown", handler, true);
  guardedRoots.set(root, handler);
}

export function detachKeyboardGuards(root) {
  const handler = guardedRoots.get(root);
  if (!root || !handler) {
    return;
  }

  root.removeEventListener("keydown", handler, true);
  guardedRoots.delete(root);
}
