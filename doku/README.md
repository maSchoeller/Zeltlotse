# Zeltlotse – Anwender-Doku

Astro Starlight, nur Deutsch.

- Voraussetzung: Node.js LTS (geprüft mit Node 26).
- Die Screenshots unter `src/assets/screenshots/` erzeugen die Playwright-Tests mit `dotnet test` (Kategorie `Doku`, braucht Docker); sie müssen vor dem ersten Build vorliegen.

```sh
npm ci          # einmalig
npm run build   # baut die Seite nach dist/
```

Ansehen: `npm run preview` (oder während des Schreibens `npm run dev`).
