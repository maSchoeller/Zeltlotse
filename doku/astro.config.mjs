// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Anwender-Doku von Zeltlotse (NFR-20): nur Deutsch, gegliedert nach Rolle, ELKW-Farben, keine Ressourcen von
// Drittanbietern. Build: `npm run build` nach `dist/`.
export default defineConfig({
	telemetry: false,
	integrations: [
		starlight({
			title: 'Zeltlotse – Anleitung',
			description: 'Anleitung für Zeltlotse: Freizeiten planen und durchführen, nach Rolle und Aufgabe.',
			defaultLocale: 'root',
			locales: {
				root: { label: 'Deutsch', lang: 'de' },
			},
			favicon: '/favicon.png',
			customCss: ['./src/styles/elkw.css'],
			lastUpdated: false,
			sidebar: [
				{ label: 'Startseite', link: '/' },
				{ label: 'Erste Schritte', items: [{ autogenerate: { directory: 'erste-schritte' } }] },
				{ label: 'Eltern', items: [{ autogenerate: { directory: 'eltern' } }] },
				{ label: 'Mitarbeitende', items: [{ autogenerate: { directory: 'mitarbeitende' } }] },
				{ label: 'Leitung', items: [{ autogenerate: { directory: 'leitung' } }] },
				{ label: 'Küche', items: [{ autogenerate: { directory: 'kueche' } }] },
				{ label: 'Org-Admin', items: [{ autogenerate: { directory: 'org-admin' } }] },
				{ label: 'Betreiber', items: [{ autogenerate: { directory: 'betreiber' } }] },
				{ label: 'Was passiert mit meinen Daten', slug: 'daten' },
			],
		}),
	],
});
