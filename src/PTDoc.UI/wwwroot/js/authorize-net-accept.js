const scriptUrls = {
  production: "https://js.authorize.net/v3/AcceptUI.js",
  sandbox: "https://jstest.authorize.net/v3/AcceptUI.js",
};

const activeModals = new Map();
const scriptPromises = new Map();

function resolveScriptUrl(environment) {
  return String(environment || "").toLowerCase() === "production"
    ? scriptUrls.production
    : scriptUrls.sandbox;
}

function loadScript(environment) {
  const scriptUrl = resolveScriptUrl(environment);
  const cachedPromise = scriptPromises.get(scriptUrl);
  if (cachedPromise) {
    return cachedPromise;
  }

  const existing = document.querySelector(`script[data-ptdoc-authorize-net="true"][src="${scriptUrl}"]`);
  if (existing) {
    return Promise.resolve();
  }

  const scriptPromise = new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = scriptUrl;
    script.async = true;
    script.dataset.ptdocAuthorizeNet = "true";
    script.onload = () => resolve();
    script.onerror = () => {
      scriptPromises.delete(scriptUrl);
      reject(new Error("Authorize.net AcceptUI script failed to load."));
    };
    document.head.appendChild(script);
  });

  scriptPromises.set(scriptUrl, scriptPromise);
  return scriptPromise;
}

export async function initialize(modalId, dotNetRef, environment) {
  activeModals.set(modalId, dotNetRef);
  window.PTDocAuthorizeNetAccept = window.PTDocAuthorizeNetAccept || {};
  window.PTDocAuthorizeNetAccept.responseHandler = responseHandler;
  window.PTDocAuthorizeNetAccept.activeModalId = modalId;
  window.ptdocAuthorizeNetAcceptResponseHandler = responseHandler;
  await loadScript(environment);
}

export function dispose(modalId) {
  activeModals.delete(modalId);
  if (window.PTDocAuthorizeNetAccept?.activeModalId === modalId) {
    window.PTDocAuthorizeNetAccept.activeModalId = null;
  }
}

function responseHandler(response) {
  const modalId = window.PTDocAuthorizeNetAccept?.activeModalId;
  const dotNetRef = modalId ? activeModals.get(modalId) : null;
  if (!dotNetRef) {
    return;
  }

  dotNetRef.invokeMethodAsync("HandleAuthorizeNetResponse", response);
}
