// Service Worker der Web-App (AD-18), nach der PWA-Vorlage von Blazor WebAssembly.
// Er legt nur die Dateien der App aus dem Asset-Manifest in den Cache, nie API-Antworten, und beantwortet
// /api/** nie (auch nicht aus dem Cache): solche Anfragen gehen unverändert ans Netz.
// Hinweise der Vorlage: https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => {
    if (istApiOderFremd(event.request)) {
        return;
    }
    event.respondWith(onFetch(event));
});
// „Neu laden“ der App aktiviert einen wartenden Service Worker sofort.
self.addEventListener('message', event => {
    if (event.data && event.data.typ === 'aktivieren') {
        self.skipWaiting();
    }
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.ttf$/, /\.svg$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /(^|\/)service-worker\.js$/ ];

// Die Adressen im Asset-Manifest beginnen mit dem Basispfad „app/“ und gelten relativ zur Wurzel der Seite.
const baseUrl = new URL('/', self.origin);
const indexUrl = new URL('app/index.html', baseUrl).href;
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

function istApiOderFremd(request) {
    const url = new URL(request.url);
    return url.origin !== self.origin || url.pathname === '/api' || url.pathname.startsWith('/api/');
}

async function onInstall(event) {
    // Alle passenden Dateien aus dem Asset-Manifest laden und ablegen.
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(new URL(asset.url, baseUrl), { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    // Caches älterer Stände löschen.
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // Navigationen innerhalb der App bekommen index.html aus dem Cache, außer sie zielen auf eine Datei der App.
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? indexUrl : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}
