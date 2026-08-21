const guardedRoots = new WeakMap();

const tabKeys = new Set(["ArrowLeft", "ArrowRight", "Home", "End"]);
const radioKeys = new Set(["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"]);

export function attachKeyboardGuards(root) {
  if (!root || guardedRoots.has(root)) {
    return;
  }

  const handler = (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
      return;
    }

    const isSettingsTab = target.matches(".roles-permissions__tab, .scheduling-settings__tab");
    const isPermissionLevel = target.matches(".permission-level");
    if ((isSettingsTab && tabKeys.has(event.key))
      || (isPermissionLevel && radioKeys.has(event.key))) {
      event.preventDefault();
    }
  };

  root.addEventListener("keydown", handler, true);
  guardedRoots.set(root, handler);
}

export function detachKeyboardGuards(root) {
  if (!root) {
    return;
  }

  const handler = guardedRoots.get(root);
  if (!handler) {
    return;
  }

  root.removeEventListener("keydown", handler, true);
  guardedRoots.delete(root);
}
