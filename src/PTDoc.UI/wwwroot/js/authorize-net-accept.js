const scriptUrls = {
  production: "https://js.authorize.net/v3/AcceptUI.js",
  sandbox: "https://jstest.authorize.net/v3/AcceptUI.js",
};

const activeForms = new Map();

export async function initialize(formId, dotNetRef, environment) {
  dispose(formId);
  const root = document.getElementById(formId);
  const button = root?.matches(".AcceptUI") ? root : root?.querySelector(".AcceptUI");
  if (!button?.isConnected) {
    throw new Error("The secure payment button must render before initialization.");
  }

  const scriptUrl = String(environment || "").toLowerCase() === "production"
    ? scriptUrls.production : scriptUrls.sandbox;
  const providerOrigin = new URL(scriptUrl).origin;
  // AcceptUI binds only the first button when its script executes and has no
  // public rebind/dispose API. A fresh document owns its listeners and captured
  // merchant settings; removing it also removes the old provider callbacks.
  const frame = document.createElement("iframe");
  frame.src = new URL("../authorize-net-host.html", import.meta.url).href;
  frame.title = "Authorize.Net secure payment";
  frame.style.cssText = "display:none;position:fixed;inset:0;width:100%;height:100%;border:0;z-index:1000000;background:transparent";
  const state = { frame, button, cancel: null, open: null };
  activeForms.set(formId, state);
  const current = () => activeForms.get(formId) === state && button.isConnected;
  const close = () => {
    frame.style.display = "none";
    if (current()) button.focus();
  };

  try {
    await new Promise((resolve, reject) => {
      state.cancel = () => reject(new Error("Payment form was disposed."));
      frame.onload = resolve;
      frame.onerror = () => reject(new Error("Payment host failed to load."));
      document.body.appendChild(frame);
    });
    if (!current()) throw new Error("Payment button was removed.");
    const host = frame.contentDocument;
    const hostWindow = frame.contentWindow;
    host.body.style.margin = "0";
    const trigger = button.cloneNode(true);
    trigger.removeAttribute("id");
    trigger.removeAttribute("disabled");
    trigger.hidden = true;
    host.body.appendChild(trigger);
    hostWindow.ptdocAuthorizeNetAcceptResponseHandler = response => {
      if (!current()) return;
      close();
      dotNetRef.invokeMethodAsync("HandleAuthorizeNetResponse", response);
    };
    // AcceptUI handles provider messages in its own document. Observe its close
    // notification only to hide our outer frame; this never grants paid access.
    hostWindow.addEventListener("message", event => {
      const providerWindow = host.getElementById("AcceptUIContainer")?.querySelector("iframe")?.contentWindow;
      if (event.origin === providerOrigin && event.source === providerWindow
          && event.data?.verifyOrigin === "AcceptMain" && event.data.type === "CLOSE_IFRAME") close();
    });
    await new Promise((resolve, reject) => {
      state.cancel = () => reject(new Error("Payment form was disposed."));
      const script = host.createElement("script");
      script.src = scriptUrl;
      script.async = true;
      script.onload = resolve;
      script.onerror = () => reject(new Error("Authorize.Net AcceptUI script failed to load."));
      host.head.appendChild(script);
    });
    if (!current()) throw new Error("Payment button was removed.");
    state.cancel = null;
    state.open = event => {
      if (!current() || button.disabled) return;
      event.preventDefault();
      frame.style.display = "block";
      trigger.click();
      host.getElementById("AcceptUIContainer")?.querySelector("iframe")?.focus();
    };
    button.addEventListener("click", state.open);
  } catch (error) {
    if (activeForms.get(formId) === state) dispose(formId);
    throw error;
  }
}

export function dispose(formId) {
  const state = activeForms.get(formId);
  if (!state) return;
  activeForms.delete(formId);
  state.cancel?.();
  if (state.open) state.button.removeEventListener("click", state.open);
  state.frame.remove();
}
