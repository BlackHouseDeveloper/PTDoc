(() => {
    const modal = document.getElementById('components-reconnect-modal');
    if (!modal) {
        return;
    }

    modal.querySelector('[data-reconnect-retry]')?.addEventListener('click', async () => {
        const reconnected = await window.Blazor?.reconnect?.();
        if (!reconnected) {
            modal.classList.add('components-reconnect-failed');
        }
    });

    modal.querySelector('[data-reconnect-reload]')?.addEventListener('click', () => {
        window.location.reload();
    });
})();
