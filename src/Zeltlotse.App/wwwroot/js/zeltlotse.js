// Das einzige Interop-Modul der Web-App (AD-29), nur über die C#-Schnittstelle IBrowser erreichbar.
// Inhalt: Online-Status mit Ereignissen, Zeitpunkt der letzten erfolgreichen API-Antwort (localStorage),
// Neu laden mit Aktivieren eines wartenden Service Workers, Drucken und Datei speichern (Datenauskunft).

const zuletztSchluessel = 'zeltlotse.zuletztAktualisiert';

export function istOnline() {
    return navigator.onLine;
}

// Meldet Wechsel des Online-Status an .NET (Methode „OnlineGeaendert“); liefert ein Objekt zum Beenden.
export function onlineBeobachten(empfaenger) {
    const online = () => empfaenger.invokeMethodAsync('OnlineGeaendert', true);
    const offline = () => empfaenger.invokeMethodAsync('OnlineGeaendert', false);
    window.addEventListener('online', online);
    window.addEventListener('offline', offline);
    return {
        beenden: () => {
            window.removeEventListener('online', online);
            window.removeEventListener('offline', offline);
        }
    };
}

export function zuletztAktualisiertLesen() {
    try {
        return localStorage.getItem(zuletztSchluessel);
    } catch {
        return null;
    }
}

export function zuletztAktualisiertSchreiben(wert) {
    try {
        localStorage.setItem(zuletztSchluessel, wert);
    } catch {
        // Speicher nicht verfügbar (z. B. privater Modus): dann eben ohne Zeitpunkt.
    }
}

// Lädt die App neu; wartet ein neuer Service Worker, wird er vorher aktiviert.
export async function neuLaden() {
    const registrierung = 'serviceWorker' in navigator ? await navigator.serviceWorker.getRegistration() : undefined;
    if (registrierung && registrierung.waiting) {
        const gewechselt = new Promise(fertig => {
            navigator.serviceWorker.addEventListener('controllerchange', fertig, { once: true });
            setTimeout(fertig, 3000);
        });
        registrierung.waiting.postMessage({ typ: 'aktivieren' });
        await gewechselt;
    }
    location.reload();
}

// Öffnet den Druckdialog des Browsers; dort lässt sich „Als PDF speichern“ wählen (Konvention „Druck“, keine PDF-Bibliothek).
export function drucken() {
    window.print();
}

// Bietet Bytes als Datei an: Blob, Objekt-URL und Klick auf einen Link mit download-Attribut; kein Pop-up, kein Inline-Skript.
export function dateiSpeichern(name, medientyp, inhalt) {
    const blob = new Blob([inhalt], { type: medientyp });
    const adresse = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = adresse;
    link.download = name;
    link.hidden = true;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(adresse), 10000);
}
