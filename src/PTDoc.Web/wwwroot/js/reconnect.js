(() => {
    const modal = document.getElementById('components-reconnect-modal');
    if (!modal) {
        return;
    }

    modal.querySelector('[data-reconnect-retry]')?.addEventListener('click', async () => {
        try {
            const reconnect = window.Blazor?.reconnect;
            const reconnected = typeof reconnect === 'function'
                ? await reconnect.call(window.Blazor)
                : false;
            if (!reconnected) {
                modal.classList.add('components-reconnect-failed');
            }
        } catch {
            modal.classList.add('components-reconnect-failed');
        }
    });

    modal.querySelector('[data-reconnect-reload]')?.addEventListener('click', () => {
        window.location.reload();
    });
})();
