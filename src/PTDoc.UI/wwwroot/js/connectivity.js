let dotNetReference = null;
let onlineHandler = null;
let offlineHandler = null;
let notificationTimer = null;
let pendingOnlineStatus = null;

function applyConnectivityState(isOnline) {
    document.documentElement.dataset.ptdocConnectivity = isOnline ? "online" : "offline";

    document.querySelectorAll("[data-connectivity-status]").forEach((element) => {
        element.classList.toggle("online", isOnline);
        element.classList.toggle("offline", !isOnline);
    });

    document.querySelectorAll("[data-connectivity-text]").forEach((element) => {
        element.textContent = isOnline ? "Online" : "Offline";
    });

    document.querySelectorAll("[data-sync-badge]").forEach((element) => {
        const syncing = element.dataset.syncing === "true";
        element.classList.toggle("navbar-brand-sync-badge-offline", !isOnline);
        element.classList.toggle("navbar-brand-sync-badge-syncing", isOnline && syncing);
        element.classList.toggle("navbar-brand-sync-badge-synced", isOnline && !syncing);
    });

    document.querySelectorAll("[data-sync-badge-text]").forEach((element) => {
        const syncing = element.dataset.syncing === "true";
        element.textContent = isOnline ? (syncing ? "Syncing" : "Synced") : "Offline";
    });

    document.querySelectorAll("[data-sync-now-button]").forEach((element) => {
        const syncing = element.dataset.syncing === "true";
        const blocked = !isOnline || syncing;
        element.disabled = false;
        element.dataset.syncBlocked = blocked.toString();
        element.removeAttribute("aria-disabled");
        if (!isOnline) {
            element.setAttribute("aria-label", "Sync unavailable while offline");
        } else if (syncing) {
            element.setAttribute("aria-label", "Syncing clinical data");
        } else if (!syncing) {
            element.setAttribute("aria-label", "Sync now");
        }

        const textElement = element.querySelector("[data-sync-now-text]");
        if (textElement) {
            textElement.textContent = !isOnline ? "Sync Offline" : syncing ? "Syncing..." : "Sync Now";
        }
    });
}

function scheduleDotNetNotification(isOnline) {
    pendingOnlineStatus = isOnline;

    if (notificationTimer) {
        window.clearTimeout(notificationTimer);
    }

    const delay = isOnline ? 750 : 100;
    notificationTimer = window.setTimeout(() => {
        notificationTimer = null;
        const status = pendingOnlineStatus;
        pendingOnlineStatus = null;

        if (dotNetReference && status !== null) {
            dotNetReference
                .invokeMethodAsync("OnConnectivityStatusChanged", status)
                .catch(() => {});
        }
    }, delay);
}

function notifyDotNet(isOnline) {
    applyConnectivityState(isOnline);
    scheduleDotNetNotification(isOnline);
}

export function getCurrentStatus() {
    return navigator.onLine;
}

export function register(reference) {
    unregister();
    dotNetReference = reference;
    onlineHandler = () => notifyDotNet(true);
    offlineHandler = () => notifyDotNet(false);
    window.addEventListener("online", onlineHandler);
    window.addEventListener("offline", offlineHandler);
    applyConnectivityState(navigator.onLine);
}

export function unregister() {
    if (onlineHandler) {
        window.removeEventListener("online", onlineHandler);
    }

    if (offlineHandler) {
        window.removeEventListener("offline", offlineHandler);
    }

    if (notificationTimer) {
        window.clearTimeout(notificationTimer);
    }

    notificationTimer = null;
    pendingOnlineStatus = null;
    onlineHandler = null;
    offlineHandler = null;
    dotNetReference = null;
}
