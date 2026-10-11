// Registrierung des Service Workers (AD-18), als eigene Datei wegen der CSP (keine Inline-Skripte).
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
}
