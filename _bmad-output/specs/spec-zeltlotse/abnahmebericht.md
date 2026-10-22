# Abnahmebericht MVP (CAP-1 bis CAP-40)

Stand: Arbeitsstand der Story 7-2 auf Basis-Commit `5db66e4ab05ac25d237bee25a443b9c23b1e46f6` (Branch `mvp`), Datum 22.10.2026; Tests und Doku-Build liefen auf diesem Arbeitsstand. Grundlage: `SPEC.md`, die 44 Story-Specs in `stories/` und die Tests im Repo.

**Urteilsregeln:** `erfüllt` heißt: Jeder Teil des Erfolgskriteriums ist durch einen automatischen Test belegt (HTTP-Test gegen PostgreSQL, Ablauf-, bUnit-, Playwright- oder Architekturtest), Sichtbares zusätzlich durch Screenshot und Doku-Seite. `teilweise` heißt: Ein Teil ist nur von Hand prüfbar (Zeitangaben, echte Geräte, Papier) oder ein zurückgestellter Befund (`deferred`) schränkt ihn ein; `offen` heißt: Für den Kern des Kriteriums gibt es keinen Beleg.

Dieser Bericht wird von `tests/Zeltlotse.ArchitectureTests/AbnahmeTests.cs` geprüft: Das Erfolgskriterium muss wörtlich in `SPEC.md` stehen, jeder genannte Pfad muss existieren und jede genannte Testmethode in ihrer Datei stehen. Wird ein Test umbenannt oder gelöscht, wird die Prüfung rot.

## Übersicht

| CAP | Thema | Urteil |
|---|---|---|
| CAP-1 | Ein Profil, Login-Link und Passwort | erfüllt |
| CAP-2 | Familienkonto und Übergabe mit 18 | teilweise |
| CAP-3 | Organisationen, Rollen, Mitarbeit | teilweise |
| CAP-4 | Nachweise mit einmaliger Einsicht | erfüllt |
| CAP-5 | Warnung und Erinnerung bei Nachweisen | erfüllt |
| CAP-6 | Zugriffsprotokoll, Auskunft, Export, Löschen | teilweise |
| CAP-7 | Freizeitseite und Vertrauensseite | erfüllt |
| CAP-8 | Anmeldung als Gast und mit Konto | teilweise |
| CAP-9 | Anmeldungen bestätigen und absagen | erfüllt |
| CAP-10 | Elterninfo im Konto und über den Gast-Link | erfüllt |
| CAP-11 | Freizeitpass zusammenstellen | erfüllt |
| CAP-12 | Vorausfüllen und „Stimmt das noch?“ | erfüllt |
| CAP-13 | Freigabefenster und Team-Notizen | teilweise |
| CAP-14 | Leitungs-Cockpit | erfüllt |
| CAP-15 | Notfall-Button und Schnellsuche | teilweise |
| CAP-16 | Schutzkonzept bestätigen | erfüllt |
| CAP-17 | Vorfall-Protokoll und Vorfall-Notiz | erfüllt |
| CAP-18 | Notfallordner drucken | teilweise |
| CAP-19 | Wochenraster und „Ich übernehme“ | teilweise |
| CAP-20 | Material und Gesamt-Materialliste | erfüllt |
| CAP-21 | Inventar und Reservierung | erfüllt |
| CAP-22 | Schäden, Verluste, Anschaffungswünsche | erfüllt |
| CAP-23 | Aufgaben und Aufgaben-Vorlagen | erfüllt |
| CAP-24 | Programmbibliothek | erfüllt |
| CAP-25 | Dokumente und Suche | teilweise |
| CAP-26 | Module pro Freizeit schalten | erfüllt |
| CAP-27 | Phasen und Abschluss | teilweise |
| CAP-28 | Rezepte mit Hochrechnung | erfüllt |
| CAP-29 | Gerichte vormerken, tauschen, Einkaufsliste | erfüllt |
| CAP-30 | Einkauf freigeben und abhaken | erfüllt |
| CAP-31 | Küchen-Tagesansicht | erfüllt |
| CAP-32 | Kalkulation und Beitragsrechner | erfüllt |
| CAP-33 | Belege, Soll-Ist, Erstattung | erfüllt |
| CAP-34 | Beitrag bezahlt abhaken | erfüllt |
| CAP-35 | Kanäle „Team“ und „Alle“ mit Push | teilweise |
| CAP-36 | „Alle informieren?“ beim Verschieben | erfüllt |
| CAP-37 | Ansagen-Zettel und Besprechung | teilweise |
| CAP-38 | Teilnehmer-Zugang per QR-Code | teilweise |
| CAP-39 | Offline-Modus | teilweise |
| CAP-40 | Anwender-Dokumentation | teilweise |

Summe: 25 erfüllt, 15 teilweise, 0 offen.

## CAP-1 – Ein Profil, Login-Link und Passwort

**Erfolgskriterium:** Eine Person mit Rollen in zwei Organisationen hat ein Profil; Anmeldung gelingt per Mail-Link ohne Passwort und, nach Festlegen, per Passwort.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/EinladungTests.cs::Ich_Organisationen_liefert_bei_mehreren_Organisationen_je_Eintrag_nur_dessen_Rollen_nach_Name` – ein Konto mit Rollen in zwei Organisationen
- Test: `tests/Zeltlotse.Api.Tests/ProfilTests.cs::Profil_zweimal_sicherstellen_legt_eine_Person_und_einen_Schluessel_an_mit_gleicher_Antwort` – je Konto genau eine Person
- Test: `tests/Zeltlotse.E2E.Tests/LoginTests.cs::Ohne_Sitzung_fuehrt_die_App_zur_Anmeldung_und_der_Login_Link_aus_der_Mail_meldet_an` – Login-Link aus der Mail meldet im Browser an
- Test: `tests/Zeltlotse.E2E.Tests/LoginTests.cs::Vorname_per_Autosave_Passwort_festlegen_abmelden_und_mit_Passwort_wieder_anmelden` – Passwort festlegen und damit anmelden
- Test: `tests/Zeltlotse.Konten.Tests/KontenCommandTests.cs::Passwort_festlegen_erneuert_den_Stempel_und_die_Pruefung_gelingt_nur_mit_dem_richtigen_Passwort` – Passwort-Prüfung
- Screenshot: `doku/src/assets/screenshots/anmelden-handy.png`
- Screenshot: `doku/src/assets/screenshots/anmelden-laptop.png`
- Doku: `doku/src/content/docs/erste-schritte/anmelden.md`

## CAP-2 – Familienkonto und Übergabe mit 18

**Erfolgskriterium:** Nach der Übergabe besitzt das volljährige Kind Profil, Freizeitpass und Verlauf; die Eltern sehen es nicht mehr. Vor dem 18. Geburtstag gibt es keinen Übergabe-Knopf.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Ablaeufe.Tests/ProfilUebergabeAngenommenTests.cs::Annehmen_Outbox_zustellen_verbindet_das_Konto_mit_Pauls_Profil` – das Konto des Kindes hängt danach an seinem Profil, die Kinderliste der Eltern ist leer
- Test: `tests/Zeltlotse.Api.Tests/ProfilUebergabeTests.cs::Annehmen_mit_leerem_Profil_uebernimmt_das_Profil_und_die_Eltern_sehen_es_nicht_mehr` – Eltern sehen das Kind nicht mehr
- Test: `tests/Zeltlotse.Ui.Tests/FamilieSeitenTests.cs::Familie_zeigt_je_Kind_eine_Card_und_die_Uebergabe_nur_beim_volljaehrigen_Kind` – kein Übergabe-Knopf unter 18
- Test: `tests/Zeltlotse.Api.Tests/ProfilUebergabeTests.cs::Kind_unter_18_ergibt_409_fremdes_Kind_404_und_ungueltige_Mail_400` – auch der Server lehnt unter 18 ab
- Test: `tests/Zeltlotse.Api.Tests/FamilienEinladungTests.cs::Einladung_annehmen_macht_zum_zweiten_Elternteil_und_protokolliert_die_Rollenvergabe` – zweiter Elternteil
- Screenshot: `doku/src/assets/screenshots/profil-uebernehmen-handy.png`
- Screenshot: `doku/src/assets/screenshots/profil-uebernehmen-laptop.png`
- Doku: `doku/src/content/docs/eltern/familienkonto.md`
- Doku: `doku/src/content/docs/eltern/profil-uebergeben.md`

**Begründung:** Dass Freizeitpass und Verlauf nach der Übergabe beim Kind liegen, ist nur indirekt belegt (die Kennung der Person bleibt gleich, nur das Konto wird umgehängt); kein Test liest nach der Übergabe Freizeitpass oder Anmeldungen. Dazu Befund aus 1-4: Übernimmt ein Konto mit Org-Rollen ein Profil, zeigen die Org-Rollen danach auf eine gelöschte Person.

## CAP-3 – Organisationen, Rollen, Mitarbeit

**Erfolgskriterium:** Mitarbeitende sehen nur Freizeiten, in denen sie mitarbeiten, und werden mit der ersten bestätigten Mitarbeit automatisch Mitarbeitende der Organisation; eine weitere Freizeit derselben Organisation verlangt nur noch die Bestätigung der Leitung.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/FreizeitKontextTests.cs::Ich_Freizeiten_liefert_nur_eigene_ueber_Organisationen_hinweg_nach_erstem_Tag_absteigend_mit_den_Vertragsfeldern` – nur eigene Freizeiten
- Test: `tests/Zeltlotse.Api.Tests/AnmeldungEntscheidenTests.cs::Bestaetigen_und_Absagen_von_Mitarbeitenden_vergibt_und_entzieht_die_Team_Rolle_die_Mitgliedschaft_bleibt` – erst nach Bestätigung sichtbar, Mitgliedschaft bleibt
- Test: `tests/Zeltlotse.Ablaeufe.Tests/AnmeldungBestaetigtTests.cs::Bestaetigen_Outbox_zustellen_ergibt_Freigabe_Team_Mitgliedschaft_nur_fuer_Mitarbeitende_und_die_Team_Rolle` – erste Bestätigung macht zum Mitglied der Organisation
- Test: `tests/Zeltlotse.Organisationen.Tests/MitgliedschaftTests.cs::Nur_Mitarbeitende_werden_einmal_Mitglied_mit_der_Organisation_aus_dem_Umschlag` – Mitgliedschaft entsteht nur einmal
- Test: `tests/Zeltlotse.Api.Tests/OrganisationenTests.cs::Global_Admin_legt_an_201_mit_einer_Einladungs_Mail_und_die_Liste_zeigt_nicht_eingerichtet` – Global Admin legt Organisation an
- Test: `tests/Zeltlotse.Api.Tests/LeitungEinladungTests.cs::Gueltige_Einladung_setzt_die_Leitung_protokolliert_die_Rollenvergabe_und_legt_das_Ereignis_in_die_Outbox` – Leitung einsetzen
- Screenshot: `doku/src/assets/screenshots/organisation-handy.png`
- Screenshot: `doku/src/assets/screenshots/organisation-laptop.png`
- Doku: `doku/src/content/docs/org-admin/rollen-vergeben.md`
- Doku: `doku/src/content/docs/org-admin/freizeit-anlegen.md`
- Doku: `doku/src/content/docs/leitung/freizeit-oeffnen.md`

**Begründung:** Kein Test spielt eine zweite Freizeit derselben Organisation durch; belegt ist nur, dass eine zweite Zustellung keine zweite Mitgliedschaft anlegt (Story 2-3 hat diesen Test im Review bewusst abgelehnt).

## CAP-4 – Nachweise mit einmaliger Einsicht

**Erfolgskriterium:** Nach der Einsicht liegt bei der Organisation nur „welcher Nachweis, eingesehen am, gültig bis, von wem“, das Ablaufdatum ist aus Ausstellungsdatum und Org-Richtlinie berechnet, der Zugriff endet; dieselben Dateien lassen sich einer zweiten Organisation ohne neuen Upload freigeben.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Organisationen.Tests/NachweisRichtlinieTests.cs::Bestaetigung_rechnet_gueltig_bis_aus_der_Richtlinie_beim_Empfang_und_speichert_kein_Ausstellungsdatum` – nur Art, eingesehen am, gültig bis, von wem; Ablauf aus Richtlinie
- Test: `tests/Zeltlotse.Ablaeufe.Tests/NachweisEingesehenTests.cs::Bestaetigen_Outbox_zustellen_ergibt_genau_eine_Bestaetigung_in_Organisationen` – Ablauf über Module
- Test: `tests/Zeltlotse.Api.Tests/NachweisEinsichtTests.cs::Datei_ansehen_nur_gebunden_bis_zur_Entscheidung_und_bis_zum_Ablauf` – Zugriff endet
- Test: `tests/Zeltlotse.Api.Tests/NachweisEinsichtTests.cs::Zwei_Organisationen_Bestaetigung_in_B_ohne_neuen_Upload_jede_sieht_nur_ihre_Zeile` – zweite Organisation ohne neuen Upload
- Test: `tests/Zeltlotse.Api.Tests/NachweisRichtlinieTests.cs::Geaenderte_Gueltigkeit_wirkt_ab_der_naechsten_Bestaetigung` – Richtlinie wirkt
- Screenshot: `doku/src/assets/screenshots/nachweis-einsicht-handy.png`
- Screenshot: `doku/src/assets/screenshots/nachweis-einsicht-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/nachweise.md`
- Doku: `doku/src/content/docs/leitung/nachweise-pruefen.md`
- Doku: `doku/src/content/docs/org-admin/nachweis-richtlinie.md`

## CAP-5 – Warnung und Erinnerung bei Nachweisen

**Erfolgskriterium:** Ein Nachweis, dessen Bestätigung abläuft, erscheint als Warnhinweis; eine Erinnerung geht erst nach Knopfdruck raus.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/NachweisWarnungTests.cs::Matrix_Warnregel_fehlt_laeuft_ab_und_abgelaufen_sortiert_nach_Nachname_ohne_die_eigene_Person` – Warngründe fehlt, läuft ab, abgelaufen
- Test: `tests/Zeltlotse.Ui.Tests/NachweisWarnungSeitenTests.cs::Pruefliste_zeigt_je_Person_die_Warnhinweise_und_Erinnern_nur_bei_Warnung` – Warnhinweis in der Oberfläche
- Test: `tests/Zeltlotse.Api.Tests/NachweisWarnungTests.cs::Miriam_erinnert_Jonas_ohne_offene_Einsicht_fragt_an_und_nach_der_Zustellung_liegt_genau_eine_Mail_vor` – Mail erst nach dem Knopf
- Test: `tests/Zeltlotse.Ui.Tests/NachweisWarnungSeitenTests.cs::Erinnern_mit_Inline_Bestaetigung_meldet_Gesendet_und_laedt_die_Liste_neu` – Knopf mit Inline-Bestätigung
- Screenshot: `doku/src/assets/screenshots/nachweis-erinnern-handy.png`
- Screenshot: `doku/src/assets/screenshots/nachweis-erinnern-laptop.png`
- Doku: `doku/src/content/docs/leitung/nachweise-erinnern.md`

## CAP-6 – Zugriffsprotokoll, Auskunft, Export, Löschen

**Erfolgskriterium:** Jede Einsicht einer Freizeit oder Organisation steht im Zugriffsprotokoll der Person; Export und Löschung funktionieren ohne Kontakt zum Betreiber.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/ZugriffsprotokollTests.cs::Einsicht_zweimal_am_selben_Tag_und_Datei_ergeben_genau_einen_Eintrag` – Einsicht einer Freizeit im Protokoll
- Test: `tests/Zeltlotse.Api.Tests/ZugriffsprotokollTests.cs::Org_Admin_auf_der_Org_Route_schreibt_den_Eintrag_mit_Organisation_und_Freizeit_der_Einsicht` – Einsicht einer Organisation im Protokoll
- Test: `tests/Zeltlotse.Api.Tests/ZugriffsprotokollTests.cs::Freizeitpass_eingesehen_steht_mit_eigenem_Zweck_neben_Nachweise_eingesehen` – auch Freizeitpass-Einsicht
- Test: `tests/Zeltlotse.Api.Tests/DatenauskunftTests.cs::Frische_Anmeldung_liefert_die_Abschnitte_aller_Module_ohne_fremde_Daten_und_protokolliert_den_Export` – Auskunft selbst abrufen
- Test: `tests/Zeltlotse.Ui.Tests/MeineDatenSeitenTests.cs::Als_PDF_speichern_oeffnet_den_Druck_und_CSV_speichert_die_Datei_mit_BOM_Anfuehrungszeichen_und_Formel_Schutz` – Export als PDF und CSV
- Test: `tests/Zeltlotse.Api.Tests/KontoLoeschenTests.cs::Jonas_loescht_sein_Konto_und_nach_der_Outbox_traegt_keine_Tabelle_mehr_seine_PersonId` – Konto selbst löschen
- Test: `tests/Zeltlotse.Ablaeufe.Tests/PersonGeloeschtTests.cs::Konto_loeschen_Outbox_zustellen_loescht_in_Konten_Organisationen_und_Freizeiten_genau_einmal` – Löschen über alle Module
- Screenshot: `doku/src/assets/screenshots/meine-daten-zugriffsprotokoll-handy.png`
- Screenshot: `doku/src/assets/screenshots/meine-daten-zugriffsprotokoll-laptop.png`
- Doku: `doku/src/content/docs/erste-schritte/meine-daten.md`

**Begründung:** Befund aus 1-7: Die Auskunft nennt keine Einladungen an die eigene Mail, keine Login-Anfragen und keine selbst gestarteten Profil-Übergaben, und Outbox/Inbox behalten die Kennung der Person in zugestellten Ereignissen. Der Druck als PDF ist nur bis zum Öffnen des Druckdialogs getestet, nicht der Inhalt des PDF.

## CAP-7 – Freizeitseite und Vertrauensseite

**Erfolgskriterium:** Ist die Höchstzahl erreicht, zeigt die Freizeitseite „ausgebucht“ und nimmt Anmeldungen ohne Aussicht auf Zusage weiter an; die Vertrauensseite ändert sich mit den Einstellungen von Freizeit und Organisation.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Freizeiten.Tests/FreizeitseiteTests.cs::Ausgebucht_steht_in_API_und_Seite_fuer_Teilnehmende_nicht_fuer_Mitarbeitende_und_nie_ohne_Hoechstzahl` – „Ausgebucht“ bei erreichter Höchstzahl
- Test: `tests/Zeltlotse.Freizeiten.Tests/FreizeitseiteTests.cs::Bei_ausgebuchter_Freizeit_legt_das_Gast_Formular_trotzdem_eine_Anmeldung_an` – Anmeldung geht weiter
- Test: `tests/Zeltlotse.Api.Tests/FreizeitseiteTests.cs::Vertrauensseite_nennt_die_gespeicherten_Einstellungen_und_folgt_ihren_Aenderungen` – folgt den Einstellungen der Organisation
- Test: `tests/Zeltlotse.Api.Tests/FreizeitseiteTests.cs::Vertrauensseite_nennt_nach_der_Auswahl_die_Fragen_des_Freizeitpasses_je_Zielgruppe` – folgt den Einstellungen der Freizeit
- Test: `tests/Zeltlotse.Api.Tests/FreizeitseiteTests.cs::Freizeitseite_liefert_Organisation_Freizeit_und_Team_nur_mit_Vorname_Initialen_Rolle_und_Herkunft` – Freizeitseite mit Team
- Screenshot: `doku/src/assets/screenshots/vertrauensseite-handy.png`
- Screenshot: `doku/src/assets/screenshots/vertrauensseite-laptop.png`
- Doku: `doku/src/content/docs/eltern/freizeitseite.md`
- Doku: `doku/src/content/docs/leitung/freizeitseite-einrichten.md`

## CAP-8 – Anmeldung als Gast und mit Konto

**Erfolgskriterium:** Eine Gast-Anmeldung am Handy gelingt ohne Konto; danach bietet Zeltlotse an, mit einem Klick ein Konto aus den Daten zu erstellen. Bleibt es beim Gast, füllt er über den Gast-Link aus der Eingangsmail den Freizeitpass aus und sieht den Anmeldestatus, bis das Freigabefenster endet. Eine Wiederanmeldung mit Konto dauert unter 3 Minuten (NFR-17).

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.E2E.Tests/GastAnmeldungTests.cs::Gast_meldet_ein_Kind_am_Handy_ohne_Konto_an_und_sieht_den_Status_ueber_den_Gast_Link` – Gast-Anmeldung in Handy-Größe, Status über den Gast-Link
- Test: `tests/Zeltlotse.Api.Tests/GastAnmeldungSeitenTests.cs::Absenden_zeigt_die_Antwortseite_direkt_im_POST_mit_Gast_Link_Angebot_und_Weiteres_Kind` – Angebot „Konto erstellen“ nach dem Absenden
- Test: `tests/Zeltlotse.Api.Tests/FreizeitAnmeldungTests.cs::Ein_leeres_Konto_uebernimmt_das_Gast_Profil_danach_zeigt_die_Familie_die_Kinder_und_der_Gast_Link_gibt_404` – Konto aus den Gast-Daten
- Test: `tests/Zeltlotse.Ui.Tests/FamilieSeitenTests.cs::Gastprofil_wird_erst_beim_Klick_uebernommen_und_Neu_anmelden_fuehrt_danach_zur_Familie` – ein Klick übernimmt
- Test: `tests/Zeltlotse.Api.Tests/GastFreizeitpassTests.cs::Der_Gast_liest_den_Pass_des_Kindes_und_speichert_das_Formular_verschluesselt` – Freizeitpass über den Gast-Link
- Test: `tests/Zeltlotse.Api.Tests/FreizeitAnmeldungTests.cs::Gast_Link_gilt_ohne_Freigabe_einen_Tag_mit_Freigabe_bis_zum_Ende_des_Freigabefensters` – Gast-Link bis zum Ende des Freigabefensters
- Test: `tests/Zeltlotse.Api.Tests/FreizeitAnmeldungTests.cs::Mit_Konto_meldet_ein_Elternteil_ein_Kind_unter_18_und_sich_selbst_an_ohne_Mail` – Anmeldung mit Konto
- Test: `tests/Zeltlotse.Api.Tests/FreizeitAnmeldungTests.cs::Ohne_Haken_ergibt_die_Anmeldung_400_und_es_entsteht_keine_Anmeldung` – Teilnahmebedingungen
- Screenshot: `doku/src/assets/screenshots/anmeldung-formular-handy.png`
- Screenshot: `doku/src/assets/screenshots/anmeldung-formular-laptop.png`
- Doku: `doku/src/content/docs/eltern/kind-anmelden.md`
- Doku: `doku/src/content/docs/mitarbeitende/anmelden.md`

**Begründung:** „Unter 3 Minuten“ ist nur von Hand prüfbar, „am Handy“ nur in Handy-Größe im Browser. Befund aus 2-2: Erneutes Absenden des Gast-Formulars (Neu laden, Doppelklick) legt eine zweite Gast-Familie und eine zweite Anmeldung für dasselbe Kind an.

## CAP-9 – Anmeldungen bestätigen und absagen

**Erfolgskriterium:** Nach dem Absenden steht „Anmeldung eingegangen“; nach Bestätigung kommt eine Mail und der Status ändert sich. Wer absagen will, sieht nur Mail und Telefon der Leitung. Es gibt keine Warteliste.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Ui.Tests/FreizeitAnmeldungSeitenTests.cs::Kind_mit_Haken_absenden_zeigt_Anmeldung_eingegangen_und_den_Weg_zur_Familienuebersicht` – „Anmeldung eingegangen“
- Test: `tests/Zeltlotse.Api.Tests/AnmeldungEntscheidenTests.cs::Bestaetigen_einer_Teilnehmenden_ergibt_Freigabe_Stufe_Team_und_genau_eine_Mail_an_den_Elternteil_ohne_Mitgliedschaft` – Mail und Status nach Bestätigung
- Test: `tests/Zeltlotse.Api.Tests/AnmeldungEntscheidenTests.cs::Gast_nach_Bestaetigung_bekommt_die_Mail_an_die_Gast_Adresse_und_der_Gast_Link_zeigt_Status_und_Kontakt` – dasselbe für Gäste
- Test: `tests/Zeltlotse.Ui.Tests/FamilieSeitenTests.cs::Unter_jeder_nicht_abgesagten_Anmeldung_steht_der_Kontakt_der_Leitung_fuer_Absagen` – nur Mail und Telefon der Leitung, kein Absage-Knopf
- Test: `tests/Zeltlotse.Api.Tests/AnmeldungEntscheidenTests.cs::Ausgebucht_zaehlt_bestaetigte_Teilnehmende_Mitarbeitende_nie_und_Bestaetigen_bleibt_moeglich` – keine Warteliste: auch bei voller Freizeit entscheidet die Leitung
- Test: `tests/Zeltlotse.Ui.Tests/AnmeldungenSeitenTests.cs::Abschnitte_zeigen_Name_Eingang_Status_und_nur_die_erlaubten_Aktionen` – nur Bestätigen und Absagen als Aktionen
- Screenshot: `doku/src/assets/screenshots/anmeldungen-handy.png`
- Screenshot: `doku/src/assets/screenshots/anmeldungen-laptop.png`
- Doku: `doku/src/content/docs/leitung/anmeldungen.md`

## CAP-10 – Elterninfo im Konto und über den Gast-Link

**Erfolgskriterium:** Ein Dokument mit Sichtbarkeit „Eltern und Teilnehmende“ ist im Elternkonto und über den Gast-Link lesbar.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/ElterninfoTests.cs::Katrin_sieht_zu_Emmas_Anmeldung_genau_die_Dokumente_fuer_Eltern_sortiert_und_laedt_sie` – im Elternkonto lesbar
- Test: `tests/Zeltlotse.Api.Tests/ElterninfoTests.cs::Der_Gast_sieht_die_Elterninfo_seiner_Anmeldung_ohne_Konto_nach_dem_Freigabefenster_nicht_mehr` – über den Gast-Link lesbar
- Test: `tests/Zeltlotse.Api.Tests/DokumenteSeitenTests.cs::Gast_Link_fuehrt_zur_Elterninfo_mit_Links_auf_die_Dateien_fremd_oder_ungueltig_404` – Seite für Gäste
- Test: `tests/Zeltlotse.Ui.Tests/ElterninfoSeitenTests.cs::Elterninfo_zeigt_Freizeit_als_Dachzeile_und_die_Dokumente_als_Download_Links` – Oberfläche im Konto
- Screenshot: `doku/src/assets/screenshots/elterninfo-handy.png`
- Screenshot: `doku/src/assets/screenshots/elterninfo-laptop.png`
- Doku: `doku/src/content/docs/eltern/elterninfo.md`

## CAP-11 – Freizeitpass zusammenstellen

**Erfolgskriterium:** Nur die gewählten Fragen werden erhoben und sind für die Freizeit sichtbar; fragt die Leitung keine Allergien ab, bleibt der Allergen-Abgleich der Küche leer.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/FragenAuswahlTests.cs::Eine_neue_Freizeit_fragt_nichts_ab_und_liefert_den_Katalog` – kein Pflichtkern
- Test: `tests/Zeltlotse.Api.Tests/FragenAuswahlTests.cs::Anmeldungen_tragen_genau_die_Fragen_ihrer_Zielgruppe_auch_auf_dem_Gast_Link` – nur gewählte Fragen werden erhoben
- Test: `tests/Zeltlotse.Ui.Tests/FreizeitpassSeitenTests.cs::Freizeitpass_zeigt_nur_die_Fragen_der_Freizeit_in_Katalogreihenfolge_je_Antworttyp` – nur gewählte Fragen im Formular
- Test: `tests/Zeltlotse.Api.Tests/FreizeitpassEinsichtTests.cs::Leitung_sieht_vor_der_Bestaetigung_genau_die_Fragen_der_Freigabe_und_Katrin_sieht_den_Eintrag_im_Zugriffsprotokoll` – nicht gefragte Antworten bleiben unsichtbar
- Test: `tests/Zeltlotse.Api.Tests/EinkaufslisteTests.cs::Freizeit_fragt_keine_Allergien_ab_kein_Abgleich_und_kein_Protokoll` – ohne Allergie-Frage kein Abgleich
- Screenshot: `doku/src/assets/screenshots/freizeitpass-fragen-handy.png`
- Screenshot: `doku/src/assets/screenshots/freizeitpass-fragen-laptop.png`
- Doku: `doku/src/content/docs/leitung/freizeitpass-fragen.md`

## CAP-12 – Vorausfüllen und „Stimmt das noch?“

**Erfolgskriterium:** Bei einer zweiten Freizeit sind bekannte Antworten vorausgefüllt; Angaben älter als 12 Monate tragen „Stimmt das noch?“.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/FreizeitpassTests.cs::Antworten_gehoeren_der_Person_und_stehen_bei_einer_zweiten_Freizeit_schon_da` – vorausgefüllt in der zweiten Freizeit
- Test: `tests/Zeltlotse.Api.Tests/FreizeitpassTests.cs::Nach_12_Monaten_und_einem_Tag_ist_eine_Antwort_veraltet_und_Stimmt_bestaetigt_sie_neu` – Grenze 12 Monate
- Test: `tests/Zeltlotse.Ui.Tests/FreizeitpassSeitenTests.cs::Veraltete_Angabe_traegt_Stimmt_das_noch_und_Stimmt_bestaetigt_sie` – Hinweis in der Oberfläche
- Test: `tests/Zeltlotse.Api.Tests/GastAnmeldungSeitenTests.cs::Gast_Freizeitpass_zeigt_Fehler_am_Feld_mit_Uebersicht_und_Stimmt_noch_bei_veralteten_Angaben` – auch auf der Gast-Seite
- Screenshot: `doku/src/assets/screenshots/freizeitpass-handy.png`
- Screenshot: `doku/src/assets/screenshots/freizeitpass-laptop.png`
- Doku: `doku/src/content/docs/eltern/freizeitpass.md`

## CAP-13 – Freigabefenster und Team-Notizen

**Erfolgskriterium:** Nach Ende des Freigabefensters (Org-Intervall ab Ende des letzten Freizeittages, beim Anlegen der Freizeit fixiert) sieht die Freizeit keinen Freizeitpass mehr; Eltern sehen, bis wann die Einsicht läuft; Team-Notizen erscheinen nie im Freizeitpass.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Freizeiten.Tests/PhasenTests.cs::Freigabefenster_letzter_Tag_13_08_2027_Intervall_30_bis_12_09_gueltig_bis_13_09_0000_Berlin` – Fenster ab dem letzten Tag
- Test: `tests/Zeltlotse.Api.Tests/FreizeitenTests.cs::Org_Admin_legt_an_die_Liste_zeigt_sie_mit_Phase_und_erlaubten_Aktionen_Intervall_30_gespeichert` – Intervall beim Anlegen gespeichert
- Test: `tests/Zeltlotse.Api.Tests/FreizeitpassEinsichtTests.cs::Nach_dem_Ende_des_Freigabefensters_ist_die_Einsicht_beendet_ohne_Fragen_und_ohne_Protokoll` – danach keine Einsicht
- Test: `tests/Zeltlotse.Api.Tests/FreizeitpassEinsichtTests.cs::Team_sieht_vor_der_Bestaetigung_ab_Bestaetigung_ohne_Protokoll_und_danach_die_Angaben` – Team erst ab Bestätigung
- Test: `tests/Zeltlotse.Ui.Tests/FreizeitpaesseSeitenTests.cs::Der_eigene_Freizeitpass_nennt_Einsicht_bis_und_nach_Ablauf_Einsicht_beendet_am` – Eltern sehen „Einsicht bis“
- Test: `tests/Zeltlotse.Api.Tests/AnmeldungEntscheidenTests.cs::Eigene_Anmeldungen_und_Gast_Link_nennen_Einsicht_bis_Ende_des_Freigabefensters_nach_Absage_den_Zeitpunkt_der_Absage` – „Einsicht bis“ vom Server
- Test: `tests/Zeltlotse.Api.Tests/TeamNotizenTests.cs::Die_Notiz_erscheint_nicht_im_Pass_nicht_in_den_eigenen_Anmeldungen_nur_in_der_Datenauskunft_der_Familie` – Team-Notizen nie im Freizeitpass
- Screenshot: `doku/src/assets/screenshots/freizeitpass-einsicht-handy.png`
- Screenshot: `doku/src/assets/screenshots/freizeitpass-einsicht-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/freizeitpaesse.md`

**Begründung:** Ein Intervall je Organisation gibt es nicht: Jede Freizeit bekommt beim Anlegen die feste Vorgabe von 30 Tagen (`FreizeitZeiten.FreigabeIntervallVorgabeTage`); eine Einstellung der Organisation dafür fehlt, darum ist auch nicht belegt, dass eine spätere Änderung bestehende Freizeiten nicht berührt.

## CAP-14 – Leitungs-Cockpit

**Erfolgskriterium:** Das Cockpit zeigt Anmeldungen (eingegangen, bestätigt) im Verhältnis zu den Plätzen sowie Warnhinweise zu fehlenden Freizeitpässen, fehlenden oder abgelaufenen Nachweisen, unbesetzten Aufgaben und fehlender Sicherheitskette auf einer Seite.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Ui.Tests/LeitungsCockpitSeitenTests.cs::Card_Anmeldungen_zeigt_den_Plaetze_Balken_und_je_Art_eingegangen_und_bestaetigt_ohne_Absagen` – Anmeldungen im Verhältnis zu den Plätzen
- Test: `tests/Zeltlotse.Ui.Tests/LeitungsCockpitSeitenTests.cs::Warnhinweise_beide_Zeilen_in_Mehrzahl_mit_der_Meldung_Achtung_und_dem_Link_auf_die_Tabelle` – Freizeitpässe und Nachweise
- Test: `tests/Zeltlotse.Api.Tests/FreizeitpassStandTests.cs::Matrix_Stand_Emma_offen_1_von_3_Mia_3_von_3_Tobias_ohne_Fragen_fehlt_ohne_Protokoll_und_ohne_Antworttexte` – Stand der Freizeitpässe vom Server
- Test: `tests/Zeltlotse.Api.Tests/NachweisWarnungTests.cs::Matrix_Warnregel_fehlt_laeuft_ab_und_abgelaufen_sortiert_nach_Nachname_ohne_die_eigene_Person` – fehlende und abgelaufene Nachweise vom Server
- Test: `tests/Zeltlotse.Ui.Tests/AufgabenSeitenTests.cs::Leitung_sieht_den_Warnhinweis_mit_den_unbesetzten_offenen_Aufgaben_bis_zur_Phase_der_Freizeit` – unbesetzte Aufgaben
- Test: `tests/Zeltlotse.Ui.Tests/LeitungsCockpitSeitenTests.cs::Nicht_eingerichtete_Kette_ist_eine_Zeile_mit_Link_auf_die_Card_in_den_Einstellungen_und_zaehlt_mit` – fehlende Sicherheitskette
- Test: `tests/Zeltlotse.Ui.Tests/LeitungsCockpitSeitenTests.cs::Ohne_Zeile_und_mit_allen_Abrufen_meldet_die_Card_Keine_offenen_Punkte` – alles auf der Startseite
- Screenshot: `doku/src/assets/screenshots/freizeit-start-handy.png`
- Screenshot: `doku/src/assets/screenshots/freizeit-start-laptop.png`
- Doku: `doku/src/content/docs/leitung/cockpit.md`

## CAP-15 – Notfall-Button und Schnellsuche

**Erfolgskriterium:** Vom Notfall-Button bis zu Allergien, Medikamenten und Notfallkontakten einer Person vergehen Sekunden; alle Mitarbeitenden sehen die Notfallkontakte aller Teilnehmenden.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.E2E.Tests/NotfallTests.cs::Notfall_von_der_Startseite_ueber_die_Sicherheitskette_zum_Freizeitpass_am_Handy` – vom Notfall-Button über die Suche zum Freizeitpass im Browser
- Test: `tests/Zeltlotse.Ui.Tests/SicherheitsketteSeitenTests.cs::Schnellsuche_trifft_ab_dem_ersten_Zeichen_und_der_Treffer_oeffnet_den_Freizeitpass` – Schnellsuche ab dem ersten Zeichen
- Test: `tests/Zeltlotse.Ui.Tests/SicherheitsketteSeitenTests.cs::Eingerichtet_zeigt_Suche_oben_die_Schritte_als_Liste_und_wen_anrufen_mit_Telefon_Links` – Sicherheitskette: was tun, wen anrufen
- Test: `tests/Zeltlotse.Api.Tests/NotfallkarteTests.cs::Leitung_bekommt_bestaetigte_Personen_mit_nur_den_vier_Notfallfragen_soweit_freigegeben` – Allergien, Medikamente, Notfallkontakte
- Test: `tests/Zeltlotse.Api.Tests/NotfallkarteTests.cs::Team_laedt_die_Karte_die_Eltern_sehen_den_eigenen_Zweck_im_Zugriffsprotokoll` – auch das Team sieht die Notfallangaben
- Test: `tests/Zeltlotse.Api.Tests/FreizeitpassEinsichtTests.cs::Team_sieht_vor_der_Bestaetigung_ab_Bestaetigung_ohne_Protokoll_und_danach_die_Angaben` – Team sieht Notfallkontakte bestätigter Teilnehmender
- Screenshot: `doku/src/assets/screenshots/sicherheitskette-handy.png`
- Screenshot: `doku/src/assets/screenshots/sicherheitskette-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/notfall.md`
- Doku: `doku/src/content/docs/leitung/sicherheitskette.md`

**Begründung:** „Sekunden“ ist nicht gemessen, sondern nur als Zahl der Schritte belegt (Startseite, Notfallhilfe, Suche, Treffer); der Anruf über die Telefon-Links ist nur als Link geprüft, nicht auf einem echten Gerät.

## CAP-16 – Schutzkonzept bestätigen

**Erfolgskriterium:** Für jedes Teammitglied ist sichtbar, ob es das Schutzkonzept als gelesen bestätigt hat.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/SchutzkonzeptTests.cs::Gelesen_Tobias_bestaetigt_GET_zeigt_es_bei_ihm_und_fuer_ihn_selbst_nicht_fuer_Miriam` – Bestätigung wird je Person gespeichert
- Test: `tests/Zeltlotse.Api.Tests/SchutzkonzeptTests.cs::Leer_ohne_Schutzkonzept_mit_dem_Team_nach_Nachname_niemand_hat_gelesen` – Liste aller Teammitglieder
- Test: `tests/Zeltlotse.Ui.Tests/SchutzkonzeptSeitenTests.cs::Mit_Dokument_zeigt_Link_auf_die_Datei_Knopf_Balken_und_die_Team_Liste` – je Person „gelesen am“ oder „noch nicht gelesen“
- Test: `tests/Zeltlotse.Api.Tests/SchutzkonzeptTests.cs::Austausch_verlangt_eine_neue_Bestaetigung` – neues Dokument, neue Bestätigung
- Screenshot: `doku/src/assets/screenshots/schutzkonzept-handy.png`
- Screenshot: `doku/src/assets/screenshots/schutzkonzept-laptop.png`
- Doku: `doku/src/content/docs/leitung/schutzkonzept.md`
- Doku: `doku/src/content/docs/mitarbeitende/schutzkonzept.md`

## CAP-17 – Vorfall-Protokoll und Vorfall-Notiz

**Erfolgskriterium:** Jeder Eintrag ist für die Leitung sofort sichtbar; Entnahmen stehen automatisch auf der Schadens- und Verlustliste; die archivierte Notiz wird nach der Löschfrist der Organisation gelöscht.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/VorfaelleTests.cs::Schreiben_Tobias_legt_an_Miriam_liest_getrimmt_mit_Name_und_Verfasser_in_der_Datenbank_kein_Klartext` – Leitung liest den Eintrag direkt nach dem Schreiben
- Test: `tests/Zeltlotse.Ui.Tests/VorfallSeitenTests.cs::Startseite_der_Leitung_zeigt_die_Card_Vorfall_Notizen_neueste_oben_mit_Link_auf_das_Protokoll` – Card auf der Startseite der Leitung
- Test: `tests/Zeltlotse.Ablaeufe.Tests/ErsteHilfeEntnommenTests.cs::Entnahme_im_Vorfall_Protokoll_steht_nach_der_Zustellung_genau_einmal_auf_der_Schadens_und_Verlustliste` – Entnahme landet auf der Liste
- Test: `tests/Zeltlotse.Api.Tests/AbschlussTests.cs::Leitung_schliesst_in_der_Nachbereitung_ab_das_Ereignis_archiviert_die_Vorfall_Notiz_in_der_Organisation` – Archiv nach Abschluss
- Test: `tests/Zeltlotse.Api.Tests/VorfaelleTests.cs::Frist_der_Loeschlauf_loescht_die_Eintraege_der_abgelaufenen_Freizeit_die_anderen_bleiben` – Löschung nach Frist
- Test: `tests/Zeltlotse.Api.Tests/LoeschlaufTests.cs::Nachweis_Erinnerungen_einer_Freizeit_werden_nach_Freizeitende_plus_Frist_der_Organisation_geloescht` – Fristende je Organisation
- Screenshot: `doku/src/assets/screenshots/vorfall-protokoll-handy.png`
- Screenshot: `doku/src/assets/screenshots/vorfall-protokoll-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/vorfall-protokoll.md`
- Doku: `doku/src/content/docs/leitung/vorfall-notizen.md`
- Doku: `doku/src/content/docs/org-admin/vorfall-archiv.md`

## CAP-18 – Notfallordner drucken

**Erfolgskriterium:** Der Ausdruck ist ohne Netz nutzbar (je Kind eine Seite, A4, schwarz-weiß-tauglich) und ist bis zum Offline-Modus die Rückfallebene (NFR-11).

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Ui.Tests/NotfallordnerSeitenTests.cs::Mehrere_Personen_ergeben_je_eine_Druckseite_in_der_Reihenfolge_der_Antwort` – je Kind eine Druckseite
- Test: `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs::Notfallordner` – PDF im Format A4, zwei Kinder ergeben zwei Seiten
- Test: `tests/Zeltlotse.Api.Tests/NotfallordnerTests.cs::Leitung_druckt_eine_bestaetigte_Person_mit_genau_den_Fragen_der_Freigabe_und_einem_Protokolleintrag` – Inhalt nur aus der Freigabe
- Test: `tests/Zeltlotse.E2E.Tests/OfflineModusTests.cs::Flugmodus_Notfallkarte_Tagesansicht_und_Vorfall_Warteschlange_Ablauf_und_Abmelden` – ohne Offline-Daten verweist die App auf den ausgedruckten Ordner
- Screenshot: `doku/src/assets/screenshots/notfallordner-handy.png`
- Screenshot: `doku/src/assets/screenshots/notfallordner-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/notfallordner.md`

**Begründung:** Papier ist nicht automatisch prüfbar: „ohne Netz nutzbar“ und „schwarz-weiß-tauglich“ beruhen auf dem Druck-Stil und brauchen einen Probedruck; kein Test prüft Farben oder Kontrast im Druck.

## CAP-19 – Wochenraster und „Ich übernehme“

**Erfolgskriterium:** Eine neue Freizeit hat die drei Mahlzeiten je Tag im Raster; ein Programmpunkt lässt sich mit „Ich übernehme“ übernehmen, ohne dass die Leitung zuteilt.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/ProgrammTests.cs::Laden_legt_Fruehstueck_Mittag_und_Abendessen_an_jedem_Tag_an_ein_zweiter_Aufruf_nichts_doppelt` – drei Mahlzeiten je Tag
- Test: `tests/Zeltlotse.Api.Tests/ProgrammTests.cs::Ich_uebernehme_200_Meins_wiederholt_200_eine_andere_Person_409_Abgeben_nur_selbst` – selbst übernehmen, niemand teilt zu
- Test: `tests/Zeltlotse.Ui.Tests/ProgrammSeitenTests.cs::Ich_uebernehme_ohne_Bestaetigung_danach_steht_der_eigene_Name_und_Abgeben_am_Termin` – ein Tipp in der Oberfläche
- Test: `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs::Wochenraster` – Raster mit Mahlzeiten im Browser
- Screenshot: `doku/src/assets/screenshots/wochenraster-handy.png`
- Screenshot: `doku/src/assets/screenshots/wochenraster-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/programm.md`

**Begründung:** Befund aus 4-2: Werden die Daten einer Freizeit nachträglich verschoben, fehlen an den neuen Tagen die drei Mahlzeiten im Raster, und die Mahlzeiten der alten Tage bleiben stehen.

## CAP-20 – Material und Gesamt-Materialliste

**Erfolgskriterium:** Die Gesamt-Materialliste zeigt das Material aller Programmpunkte mit Mengen und Status, ohne zusätzliche Eingabe.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/MaterialTests.cs::Liste_zeigt_das_Material_aller_Programmpunkte_mit_Punkt_sortiert_wie_der_Raster_dann_nach_Bezeichnung` – Material aller Programmpunkte mit Menge und Status
- Test: `tests/Zeltlotse.Api.Tests/MaterialTests.cs::Anlegen_201_mit_Status_gebraucht_Version_getrimmt_und_ohne_weitere_Eingabe_in_der_Liste` – ohne zusätzliche Eingabe in der Liste
- Test: `tests/Zeltlotse.Api.Tests/MaterialTests.cs::Status_setzen_200_mit_neuem_Status_und_neuer_Version_in_jeden_Zustand_fehlt_oder_unbekannt_400_am_Feld_status` – vier Zustände
- Test: `tests/Zeltlotse.Ui.Tests/MaterialSeitenTests.cs::Materialliste_zeigt_je_Programmpunkt_eine_Card_mit_Dachzeile_Titel_Link_Menge_Status_und_den_Balken` – Gesamtliste in der Oberfläche
- Screenshot: `doku/src/assets/screenshots/materialliste-handy.png`
- Screenshot: `doku/src/assets/screenshots/materialliste-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/material.md`

## CAP-21 – Inventar und Reservierung

**Erfolgskriterium:** Überschneiden sich Reservierungen paralleler Freizeiten, erscheint ein Konflikthinweis; Zeltlotse löst den Konflikt nicht.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/InventarTests.cs::Konflikt_zwischen_zwei_Freizeiten_nennt_den_Namen_der_anderen_in_Antwort_und_beiden_Inventaren_Grenztag_zaehlt` – Konflikthinweis; beide Reservierungen bleiben bestehen
- Test: `tests/Zeltlotse.Inventar.Tests/InventarTests.cs::Ein_Konflikt_braucht_denselben_Artikel_und_eine_andere_Freizeit` – Regel für einen Konflikt
- Test: `tests/Zeltlotse.Ui.Tests/InventarSeitenTests.cs::Freizeit_zeigt_Dachzeile_Konflikthinweis_eigene_Reservierungen_und_das_vorbelegte_Formular` – Hinweis in der Oberfläche
- Screenshot: `doku/src/assets/screenshots/inventar-handy.png`
- Screenshot: `doku/src/assets/screenshots/inventar-laptop.png`
- Doku: `doku/src/content/docs/org-admin/inventar.md`
- Doku: `doku/src/content/docs/mitarbeitende/inventar.md`

## CAP-22 – Schäden, Verluste, Anschaffungswünsche

**Erfolgskriterium:** Anschaffungswünsche sind für Org-Admins, Leitungen und Org-Überblick sichtbar, auch nach Abschluss der Freizeit.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/SchaedenTests.cs::Anschaffungswuensche_nur_Freizeiten_in_Phase_gelaufen_mit_Namen_neueste_Gruppe_zuerst` – Org-Admin sieht die Wünsche abgeschlossener Freizeiten
- Test: `tests/Zeltlotse.Api.Tests/SchaedenTests.cs::Rechte_Org_Admin_alles_Org_Ueberblick_liest_aber_erledigt_403_Org_Admin_von_B_ohne_Org_Rolle_404_ohne_Sitzung_401` – Org-Überblick liest mit
- Test: `tests/Zeltlotse.Api.Tests/SchaedenTests.cs::Abgeschlossene_Freizeit_Eintragen_Aendern_und_Entfernen_gehen_weiter` – Leitung und Team nach Abschluss
- Test: `tests/Zeltlotse.Ui.Tests/SchaedenSeitenTests.cs::Abgeschlossen_zeigt_den_Hinweis_Anschaffungswuensche_und_bleibt_beschreibbar_nicht_verfuegbar_bei_404` – Leitung sieht die Liste als Anschaffungswünsche
- Test: `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs::Org_Anschaffungswuensche` – gefüllte Ansicht im Browser
- Screenshot: `doku/src/assets/screenshots/anschaffungswuensche-handy.png`
- Screenshot: `doku/src/assets/screenshots/anschaffungswuensche-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/schaeden-verluste.md`
- Doku: `doku/src/content/docs/org-admin/anschaffungswuensche.md`

## CAP-23 – Aufgaben und Aufgaben-Vorlagen

**Erfolgskriterium:** Eine neue Freizeit enthält die Vorlagen-Aufgaben; übernommene Aufgaben erscheinen unter „Meine Aufgaben“; eine Aufgabe lässt sich in eine Vorlage übernehmen.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/AufgabenTests.cs::Vorlagen_der_Organisation_stehen_beim_ersten_Laden_als_Aufgaben_da_ein_zweites_Laden_kopiert_nichts_spaetere_Vorlagen_nicht` – Vorlagen in der neuen Freizeit
- Test: `tests/Zeltlotse.Ui.Tests/AufgabenSeitenTests.cs::Reiter_Meine_Aufgaben_zeigt_nur_eigene_ohne_eigene_Keine_Aufgaben_uebernommen` – „Meine Aufgaben“
- Test: `tests/Zeltlotse.Api.Tests/AufgabenTests.cs::Als_Vorlage_zweimal_eine_neue_Vorlage_in_A_nach_dem_Loeschen_der_Vorlage_vorlage_false_erneut_eine_neue` – Aufgabe wird Vorlage
- Test: `tests/Zeltlotse.Ui.Tests/AufgabenSeitenTests.cs::Als_Vorlage_speichern_danach_steht_der_Satz_und_der_Knopf_fehlt` – in der Oberfläche
- Screenshot: `doku/src/assets/screenshots/meine-aufgaben-handy.png`
- Screenshot: `doku/src/assets/screenshots/meine-aufgaben-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/aufgaben.md`
- Doku: `doku/src/content/docs/org-admin/aufgaben-vorlagen.md`

## CAP-24 – Programmbibliothek

**Erfolgskriterium:** Ohne bewusste Ablage landet nichts in der Bibliothek; eine Übernahme bringt Material und Aufgaben mit.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/ProgrammbibliothekTests.cs::Bibliothek_ist_leer_obwohl_die_Freizeit_Programmpunkte_hat_und_Z_in_Organisation_B_sieht_die_Eintraege_von_A_nicht` – ohne Ablage leer
- Test: `tests/Zeltlotse.Api.Tests/ProgrammbibliothekTests.cs::Uebernehmen_legt_einen_neuen_freien_Punkt_mit_Material_gebraucht_und_offenen_Aufgaben_an_ohne_Verknuepfung` – Übernahme mit Material und Aufgaben
- Test: `tests/Zeltlotse.Ui.Tests/ProgrammbibliothekSeitenTests.cs::Programmpunkt_In_Bibliothek_ablegen_mit_einem_Tipp_danach_steht_der_Satz_und_der_Knopf_fehlt` – Ablage ist ein bewusster Knopf
- Screenshot: `doku/src/assets/screenshots/programmbibliothek-handy.png`
- Screenshot: `doku/src/assets/screenshots/programmbibliothek-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/programmbibliothek.md`
- Doku: `doku/src/content/docs/org-admin/programmbibliothek.md`

## CAP-25 – Dokumente und Suche

**Erfolgskriterium:** Ein Dokument erscheint genau für die gewählte Sichtbarkeit; eine Suche findet Dokumente über alle Kontexte.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/DokumenteTests.cs::Tobias_laedt_den_Infobrief_fuer_Eltern_hoch_Liste_und_Datei_als_Anhang` – Team sieht die Dokumente der Freizeit
- Test: `tests/Zeltlotse.Api.Tests/ElterninfoTests.cs::Katrin_sieht_zu_Emmas_Anmeldung_genau_die_Dokumente_fuer_Eltern_sortiert_und_laedt_sie` – Eltern sehen nur „Eltern und Teilnehmende“
- Test: `tests/Zeltlotse.Api.Tests/DokumenteTests.cs::Die_Freizeitseite_zeigt_nur_oeffentliche_Dokumente_und_liefert_nur_deren_Datei` – öffentlich nur „öffentlich“
- Test: `tests/Zeltlotse.Api.Tests/DokumenteTests.cs::Miriam_bindet_die_Hausordnung_ein_ohne_Kopie_Auswahl_danach_ohne_zweites_Einbinden_409` – Org-Dokumente eingebunden statt kopiert
- Test: `tests/Zeltlotse.Ui.Tests/DokumenteSeitenTests.cs::Suche_filtert_beide_Listen_beim_Tippen_ohne_Treffer_steht_Keine_Dokumente_gefunden` – Suche
- Screenshot: `doku/src/assets/screenshots/dokumente-suche-handy.png`
- Screenshot: `doku/src/assets/screenshots/dokumente-suche-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/dokumente.md`
- Doku: `doku/src/content/docs/org-admin/org-dokumente.md`

**Begründung:** Dokumente gibt es nur am Kontext Freizeit, nicht am Programmpunkt (Story 2-7 hat das bewusst ausgelassen); die Suche filtert im Browser die Dokumente der Freizeit und die einbindbaren Org-Dokumente, eine Suche über alle Kontexte gibt es damit nicht.

## CAP-26 – Module pro Freizeit schalten

**Erfolgskriterium:** Ein abgeschaltetes Modul erscheint nirgends und ist auch serverseitig nicht erreichbar; beim Abschalten bleiben seine Daten erhalten.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/RezepteTests.cs::Schalter_aus_jede_Route_404_fuer_Leitung_und_Team_ohne_Sitzung_401_nach_dem_Einschalten_sind_die_Daten_wieder_da` – Verpflegung: serverseitig weg, Daten bleiben
- Test: `tests/Zeltlotse.Api.Tests/KalkulationTests.cs::Schalter_aus_jede_Route_404_fuer_Leitung_und_Team_ohne_Sitzung_401_nach_dem_Einschalten_sind_die_Daten_wieder_da` – Finanzen: serverseitig weg, Daten bleiben
- Test: `tests/Zeltlotse.Api.Tests/TeilnehmerZugangTests.cs::Schalter_aus_404_beim_Einloesen_und_auf_allen_Gast_Routen_wieder_an_wirkt_das_Cookie_wieder` – Teilnehmer-Zugang: serverseitig weg
- Test: `tests/Zeltlotse.SharedKernel.Tests/PipelineTests.cs::Schalter_aus_lehnt_den_Benutzer_Command_ab_ohne_den_Handler_aufzurufen` – auch Befehle werden abgelehnt
- Test: `tests/Zeltlotse.Ui.Tests/RezeptSeitenTests.cs::Startseite_zeigt_den_Knopf_Kueche_nur_mit_Verpflegung` – Küche erscheint nicht
- Test: `tests/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs::Startseite_zeigt_den_Knopf_Finanzen_nur_mit_dem_Modul_Finanzen` – Finanzen erscheint nicht
- Test: `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs::Ohne_Modul_Teilnehmer_Zugang_keine_Card_und_keine_Anfrage_nach_dem_QR_Link` – Teilnehmer-Zugang erscheint nicht
- Screenshot: `doku/src/assets/screenshots/module-schalten-handy.png`
- Screenshot: `doku/src/assets/screenshots/module-schalten-laptop.png`
- Doku: `doku/src/content/docs/leitung/module-schalten.md`

## CAP-27 – Phasen und Abschluss

**Erfolgskriterium:** Die Phasen bis zur Nachbereitung ergeben sich aus Datum; erst der Abschluss setzt „gelaufen“, danach ist die Freizeit nur lesbar (Ausnahmen: Erstattungsvermerke, Anschaffungswünsche), einsehbar und per Filter ausblendbar; Abschluss trotz offener Punkte ist nach Inline-Warnung möglich.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Freizeiten.Tests/PhasenTests.cs::Sommer_Vortag_2359_ist_in_Vorbereitung_erster_Tag_0000_laeuft_letzter_Tag_2359_laeuft_Tag_danach_0000_Nachbereitung` – Phasen aus dem Datum
- Test: `tests/Zeltlotse.Freizeiten.Tests/PhasenTests.cs::Mit_Abschluss_ist_die_Freizeit_gelaufen_in_jeder_Lage_der_Uhr` – erst der Abschluss setzt „gelaufen“
- Test: `tests/Zeltlotse.Api.Tests/AbschlussTests.cs::Vor_dem_Freizeitende_400_am_Feld_phase_und_nichts_gespeichert` – Abschluss erst nach dem Ende
- Test: `tests/Zeltlotse.SharedKernel.Tests/PipelineTests.cs::Gelaufen_lehnt_den_Benutzer_Command_der_Freizeit_ab_ohne_den_Handler_aufzurufen` – danach nur lesbar
- Test: `tests/Zeltlotse.Api.Tests/DokumenteTests.cs::Nach_dem_Abschluss_ergeben_Hochladen_Aendern_Entfernen_und_Einbinden_409_Lesen_bleibt` – Schreiben 409, Lesen bleibt
- Test: `tests/Zeltlotse.Api.Tests/BelegeTests.cs::Gelaufen_Lesen_und_Erstattet_200_jede_andere_Schreibaktion_409_freizeit_abgeschlossen` – Ausnahme Erstattungsvermerke
- Test: `tests/Zeltlotse.Api.Tests/AbschlussTests.cs::Ausnahme_Schaden_eintragen_bleibt_nach_dem_Abschluss_erlaubt` – Ausnahme Anschaffungswünsche
- Test: `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs::Org_Uebersicht_filtert_die_Freizeiten_nach_Phase` – per Filter ausblendbar
- Test: `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs::Leitung_schliesst_in_der_Nachbereitung_mit_offenen_Aufgaben_ab_und_sieht_danach_den_Hinweis_ohne_Knopf` – Abschluss trotz offener Punkte nach Inline-Warnung
- Screenshot: `doku/src/assets/screenshots/freizeit-abschliessen-handy.png`
- Screenshot: `doku/src/assets/screenshots/freizeit-abschliessen-laptop.png`
- Doku: `doku/src/content/docs/leitung/freizeit-abschliessen.md`

**Begründung:** Befunde aus 3-8 und 4-1: Zwischen Prüfung des Schreibschutzes und dem Schreiben gibt es ein Zeitfenster ohne Sperre, ein gleichzeitiger Abschluss kann einen letzten Schreibvorgang oder eine Anmeldung durchlassen. Die Inline-Warnung nennt nur offene Aufgaben (keine offenen Erstattungen oder Beiträge) und ist im Browser nicht bis „Ja, abschließen“ durchgespielt.

## CAP-28 – Rezepte mit Hochrechnung

**Erfolgskriterium:** Ein Rezept für 3 Personen erscheint für eine Freizeit mit 60 Personen mit zwanzigfachen Mengen; jede Person zählt gleich.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/RezepteTests.cs::Rezept_rechnet_jede_Menge_auf_die_Personenzahl_hoch_Zutat_ohne_Menge_bleibt_ohne` – 3 Personen auf 60 Personen
- Test: `tests/Zeltlotse.Kueche.Tests/RezeptTests.cs::Hochrechnen_rechnet_Menge_mal_Personen_durch_Basis_auf_zwei_Stellen_kaufmaennisch_gerundet` – Rechenregel
- Test: `tests/Zeltlotse.Api.Tests/RezepteTests.cs::Personenzahl_bestaetigte_Teilnehmende_und_Rollen_jede_Person_einmal_eingegangen_abgesagt_und_Mitarbeitende_ohne_Rolle_zaehlen_nicht` – jede Person zählt gleich und einmal
- Test: `tests/Zeltlotse.Ui.Tests/RezeptSeitenTests.cs::Rezept_zeigt_Tabelle_mit_hochgerechneter_Spalte_Zubereitung_und_Felder` – hochgerechnete Spalte in der Oberfläche
- Screenshot: `doku/src/assets/screenshots/rezept-handy.png`
- Screenshot: `doku/src/assets/screenshots/rezept-laptop.png`
- Doku: `doku/src/content/docs/kueche/rezepte.md`

## CAP-29 – Gerichte vormerken, tauschen, Einkaufsliste

**Erfolgskriterium:** Ein Gerichttausch zeigt die Änderung der noch nicht gekauften Posten als Vorschlag; ohne Bestätigung der Küche ändert sich die Liste nicht; von Hand überschriebene Mengen bleiben erhalten.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/GerichteTests.cs::Tausch_Vorschau_zeigt_Vorher_und_Nachher_und_schreibt_nichts_gleiches_Rezept_ohne_Aenderung` – Vorschlag, ohne Bestätigung keine Änderung
- Test: `tests/Zeltlotse.Kueche.Tests/EinkaufFreigabeTests.cs::Tausch_Vorschau_zeigt_vorher_nur_nicht_gekaufte_Posten_und_zaehlt_die_gekauften` – nur noch nicht gekaufte Posten
- Test: `tests/Zeltlotse.Ui.Tests/EssensplanSeitenTests.cs::Anpassen_tauscht_und_oeffnet_die_Einkaufsliste_am_Tag_Abbrechen_tauscht_nicht` – Abbrechen tauscht nicht
- Test: `tests/Zeltlotse.Api.Tests/GerichteTests.cs::Tauschen_uebernimmt_genau_die_Vorschau_Hand_Menge_wandert_auf_die_gleiche_Zutat_die_uebrigen_entfallen` – Hand-Menge bleibt beim Tausch
- Test: `tests/Zeltlotse.Api.Tests/EinkaufslisteTests.cs::Hand_Menge_ersetzt_die_berechnete_Menge_und_bleibt_bei_geaenderter_Personenzahl_anderer_Altwert_409_leer_zurueck` – Hand-Menge bleibt bei neuer Personenzahl
- Test: `tests/Zeltlotse.Api.Tests/EinkaufslisteTests.cs::Abgleich_Mia_bestaetigt_mit_Gluten_ergibt_eine_Ersatzportion_Emma_nur_eingegangen_zaehlt_nicht_Protokoll_einmal_am_Tag` – Allergen-Abgleich und Ersatzportionen
- Screenshot: `doku/src/assets/screenshots/gericht-tauschen-handy.png`
- Screenshot: `doku/src/assets/screenshots/gericht-tauschen-laptop.png`
- Doku: `doku/src/content/docs/kueche/essensplan.md`
- Doku: `doku/src/content/docs/kueche/einkaufsliste.md`

## CAP-30 – Einkauf freigeben und abhaken

**Erfolgskriterium:** Nur freigegebene Posten erscheinen zum Einkaufen; Abhaken ist ein Tipp.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/EinkaufFreigabeTests.cs::Posten_freigeben_200_Liste_und_Einkauf_zeigen_ihn_freigegeben_anderer_Altwert_409` – erst nach Freigabe im Einkauf
- Test: `tests/Zeltlotse.Api.Tests/EinkaufFreigabeTests.cs::Einkauf_ohne_Freigabe_hat_keine_Tage_und_liest_keine_Allergien_kein_Eintrag_im_Zugriffsprotokoll` – ohne Freigabe leer
- Test: `tests/Zeltlotse.Ui.Tests/EinkaufSeitenTests.cs::Einkauf_zeigt_den_Kueche_Reiter_und_je_Tag_eine_Card_mit_einem_Ankreuzfeld_je_freigegebenem_Posten` – nur freigegebene Posten in der Oberfläche
- Test: `tests/Zeltlotse.Ui.Tests/EinkaufSeitenTests.cs::Abhaken_mit_einem_Tipp_schickt_PATCH_gekauft_und_zeigt_Gespeichert_Rueckgaengig` – ein Tipp hakt ab
- Screenshot: `doku/src/assets/screenshots/einkauf-handy.png`
- Screenshot: `doku/src/assets/screenshots/einkauf-laptop.png`
- Doku: `doku/src/content/docs/kueche/einkauf.md`

## CAP-31 – Küchen-Tagesansicht

**Erfolgskriterium:** Die Tagesansicht zeigt die Gerichte des Tages mit den betroffenen Allergenen auf einen Blick.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/TagesansichtTests.cs::Tagesansicht_Dienstag_Pfannkuchen_mit_Ersatzportion_Allergene_und_Sonderkost_nur_bestaetigt_Protokoll_einmal_am_Tag` – Gerichte des Tages mit Allergenen
- Test: `tests/Zeltlotse.Ui.Tests/TagesansichtSeitenTests.cs::Tagesansicht_zeigt_fuenf_Kueche_Reiter_Tag_Reiter_vom_Server_und_je_Mahlzeit_eine_Card_mit_Gericht_und_Ersatzportion` – je Mahlzeit eine Card mit Gericht und Allergen
- Test: `tests/Zeltlotse.Ui.Tests/TagesansichtSeitenTests.cs::Uebersicht_zeigt_Allergien_und_Sonderkost_nur_als_Anzahlen` – Allergien auf einen Blick
- Test: `tests/Zeltlotse.Api.Tests/TagesansichtTests.cs::Restetag_setzen_200_Tagesansicht_und_Essensplan_zeigen_ihn_Einkaufsliste_unveraendert_anderer_Altwert_409` – Restetag
- Screenshot: `doku/src/assets/screenshots/tagesansicht-handy.png`
- Screenshot: `doku/src/assets/screenshots/tagesansicht-laptop.png`
- Doku: `doku/src/content/docs/kueche/tagesansicht.md`

## CAP-32 – Kalkulation und Beitragsrechner

**Erfolgskriterium:** Kosten der Mitarbeitenden werden auf die Teilnehmerbeiträge umgelegt; Verpflegung = Tagessatz × alle Personen × Tage ohne Kopplung an den Essensplan; Regler verändern den Beitrag sichtbar als Balken.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Finanzen.Tests/KalkulationTests.cs::Mitarbeitende_zaehlen_als_Personen_und_ihre_Kosten_tragen_die_Teilnehmenden` – Kosten der Mitarbeitenden auf die Beiträge umgelegt
- Test: `tests/Zeltlotse.Api.Tests/KalkulationTests.cs::Beitrag_rechnen_40_TN_10_MA_12_Tage_Verpflegung_7_Unterkunft_2000_plus_10_Einnahme_1200_ergibt_275` – Verpflegung 7 € × 50 Personen × 12 Tage
- Test: `tests/Zeltlotse.ArchitectureTests/AbhaengigkeitsTests.cs::Contracts_Aufrufe_nur_entlang_der_Pfeile` – Finanzen darf die Küche nicht lesen: keine Kopplung an den Essensplan
- Test: `tests/Zeltlotse.ArchitectureTests/ProjektDateiTests.cs::Modul_referenziert_fremde_Module_nur_ueber_Contracts` – keine Projekt-Abhängigkeit von Finanzen zur Küche
- Test: `tests/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs::Regler_aendern_Zahl_Differenz_und_Balken_sofort_ohne_Serveraufruf_und_Zuruecksetzen_stellt_die_Kalkulation_her` – Regler verändern den Balken
- Screenshot: `doku/src/assets/screenshots/beitragsrechner-handy.png`
- Screenshot: `doku/src/assets/screenshots/beitragsrechner-laptop.png`
- Doku: `doku/src/content/docs/finanzen/kalkulation.md`
- Doku: `doku/src/content/docs/finanzen/beitragsrechner.md`

## CAP-33 – Belege, Soll-Ist, Erstattung

**Erfolgskriterium:** Soll-Ist und „wer bekommt noch Geld“ zeigen immer den aktuellen Stand, ohne Abrechnungsabschluss und ohne Zusammenführen von Tabellen.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/BelegeTests.cs::Soll_Ist_Verpflegung_knapp_Fahrt_ueberschritten_leere_Kategorie_im_Plan_Erstattungen_nach_Offen` – Soll-Ist und Erstattungen; ein neuer Beleg steht beim nächsten Aufruf drin
- Test: `tests/Zeltlotse.Finanzen.Tests/BelegeTests.cs::Erstattungen_je_Person_summiert_nach_Offen_absteigend_bei_Gleichstand_nach_Erstattet` – wer noch Geld bekommt
- Test: `tests/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs::Soll_Ist_zeigt_Balken_mit_Ist_von_Soll_den_drei_Statuswoertern_und_die_Erstattungen` – beides auf einer Seite
- Test: `tests/Zeltlotse.Api.Tests/BelegeTests.cs::Beleg_von_Hand_201_bezahlt_von_der_angemeldeten_Person_nicht_erstattet_ohne_Datei` – Beleg von Hand
- Test: `tests/Zeltlotse.Api.Tests/BelegeTests.cs::Foto_hochladen_inline_mit_nosniff_durch_ein_PDF_ersetzen_als_Anhang_entfernen_der_Beleg_bleibt` – Beleg als Foto
- Screenshot: `doku/src/assets/screenshots/soll-ist-handy.png`
- Screenshot: `doku/src/assets/screenshots/soll-ist-laptop.png`
- Doku: `doku/src/content/docs/finanzen/belege.md`
- Doku: `doku/src/content/docs/finanzen/soll-ist.md`

## CAP-34 – Beitrag bezahlt abhaken

**Erfolgskriterium:** Zu jedem Haken ist vermerkt, wer ihn gesetzt hat.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/BeitraegeTests.cs::Abhaken_vermerkt_wer_und_wann_ein_zweiter_Haken_aendert_nichts_der_erste_zaehlt` – wer und wann
- Test: `tests/Zeltlotse.Api.Tests/BeitraegeTests.cs::Wieder_offen_loescht_den_Vermerk_auch_zweimal_200` – neuer Haken, neuer Vermerk
- Test: `tests/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs::Beitraege_zeigen_Reiter_Balken_x_von_y_bezahlt_Namen_und_wer_abgehakt_hat` – „Abgehakt von …“ in der Oberfläche
- Screenshot: `doku/src/assets/screenshots/beitraege-handy.png`
- Screenshot: `doku/src/assets/screenshots/beitraege-laptop.png`
- Doku: `doku/src/content/docs/finanzen/beitraege.md`

## CAP-35 – Kanäle „Team“ und „Alle“ mit Push

**Erfolgskriterium:** Push erreicht das Team verlässlich, Teilnehmende nur, wo das Gerät es erlaubt; eine Nachricht in „Alle“ geht erst nach Inline-Bestätigung raus.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/KanaeleTests.cs::Team_Nachricht_201_und_nach_der_Zustellung_bekommt_genau_Miriams_Abo_den_Push_mit_Freizeit_Kanal_Text_und_Adresse` – Push an das Team (nachgebildeter Push-Dienst)
- Test: `tests/Zeltlotse.Api.Tests/KanaeleTests.cs::Abgelaufenes_Abo_wird_abgeraeumt_die_uebrigen_Empfaenger_bekommen_den_Push_ein_kaputtes_haelt_nichts_auf` – ein kaputtes Abo hält die anderen nicht auf
- Test: `tests/Zeltlotse.Api.Tests/WebPushVersandTests.cs::Zugestellt_mit_VAPID_Authorization_und_TTL_an_den_Endpunkt` – Versand nach Web-Push-Standard
- Test: `tests/Zeltlotse.Api.Tests/TeilnehmerZugangTests.cs::Gast_Abo_bekommt_den_Push_aus_Alle_mit_der_Adresse_des_Teilnehmer_Zugangs_aus_Team_keinen` – Teilnehmende bekommen Push aus „Alle“
- Test: `tests/Zeltlotse.Ui.Tests/KanaeleSeitenTests.cs::Ohne_Push_erscheint_der_passende_Hinweis_und_der_Kanal_bleibt_les_und_beschreibbar` – ohne Erlaubnis Hinweis, Kanal bleibt nutzbar
- Test: `tests/Zeltlotse.Api.Tests/KanaeleTests.cs::Alle_ohne_Bestaetigung_400_am_Feld_bestaetigt_nichts_gespeichert_kein_Push` – „Alle“ ohne Bestätigung geht nicht raus
- Test: `tests/Zeltlotse.Ui.Tests/KanaeleSeitenTests.cs::Senden_in_Alle_klappt_erst_die_Inline_Bestaetigung_auf_Nein_sendet_nichts_Ja_senden_schickt_mit_Bestaetigung` – Inline-Bestätigung in der Oberfläche
- Screenshot: `doku/src/assets/screenshots/kanaele-alle-handy.png`
- Screenshot: `doku/src/assets/screenshots/kanaele-alle-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/kanaele.md`

**Begründung:** Push ist nur mit einem nachgebildeten Push-Dienst getestet; ob er das Team auf echten Handys über die Push-Dienste von Google, Mozilla und Apple verlässlich erreicht, ist nur von Hand prüfbar. Befunde aus 6-1: Die Kette Push → Service Worker → Neuladen und die Nutzlast zwischen Server und Service Worker sind durch keinen echten Test gebunden; nach einem Wechsel des Push-Schlüssels bleiben alte Abos stumm.

## CAP-36 – „Alle informieren?“ beim Verschieben

**Erfolgskriterium:** Ohne Bestätigung geht nichts raus; nach „Nein“ bleibt am Programmpunkt der Hinweis „Geändert, noch nicht mitgeteilt“ mit „Jetzt informieren“.

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/ProgrammTests.cs::Verschieben_verschickt_nichts_keine_Nachricht_kein_Push_kein_Auftrag_in_den_Outboxen_erst_Ja_informieren_schickt_einen_Push` – Verschieben allein schickt nichts
- Test: `tests/Zeltlotse.Ui.Tests/ProgrammSeitenTests.cs::Detailseite_nach_dem_Verschieben_klappt_Alle_informieren_mit_vorbereitetem_Text_auf_Fokus_auf_Nein` – Frage „Alle informieren?“
- Test: `tests/Zeltlotse.Ui.Tests/ProgrammSeitenTests.cs::Detailseite_Nein_schickt_nichts_der_Hinweis_bleibt_mit_Jetzt_informieren_und_Ausblenden` – nach „Nein“ bleibt der Hinweis mit „Jetzt informieren“
- Test: `tests/Zeltlotse.Api.Tests/ProgrammTests.cs::Verschieben_merkt_die_alte_Uhrzeit_erneut_verschieben_behaelt_sie_zurueckschieben_leert_sie` – Hinweis ist am Programmpunkt gespeichert
- Screenshot: `doku/src/assets/screenshots/programm-mitteilen-handy.png`
- Screenshot: `doku/src/assets/screenshots/programm-mitteilen-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/programm.md`

## CAP-37 – Ansagen-Zettel und Besprechung

**Erfolgskriterium:** Besprechungspunkte des Tages lassen sich in der Besprechung abhaken.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/KanalNotizenTests.cs::Abhaken_200_mit_erledigtAm_steht_unter_abgehakt_wiederholt_bleibt_der_erste_Zeitpunkt` – Punkt abhaken
- Test: `tests/Zeltlotse.Api.Tests/KanalNotizenTests.cs::Anlegen_201_mit_offenem_Punkt_er_steht_auf_dem_Ansagen_Zettel_nicht_in_der_Besprechung` – Ansagen-Zettel getrennt von der Besprechung
- Test: `tests/Zeltlotse.Ui.Tests/KanalNotizenSeitenTests.cs::Ein_Tipp_auf_einen_offenen_Punkt_hakt_ihn_ab_und_laedt_neu` – ein Tipp hakt ab
- Test: `tests/Zeltlotse.Ui.Tests/KanalNotizenSeitenTests.cs::Besprechung_zeigt_offene_Punkte_als_Kaestchen_und_darunter_Abgehakt_mit_gesetzten_Kaestchen` – offene oben, abgehakte unten
- Screenshot: `doku/src/assets/screenshots/kanaele-besprechung-handy.png`
- Screenshot: `doku/src/assets/screenshots/kanaele-besprechung-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/kanaele.md`

**Begründung:** „Des Tages“ ist bewusst ohne Datum umgesetzt (Story 6-3): Es stehen alle offenen Punkte oben, eine Zuordnung zu einem Tag gibt es nicht. Befund aus 6-3: Dass das Kästchen nach einem gescheiterten Tipp wieder den Stand des Servers zeigt, prüft kein Test.

## CAP-38 – Teilnehmer-Zugang per QR-Code

**Erfolgskriterium:** Ein Scan genügt; Teilnehmende sehen nichts darüber hinaus; mit dem Ende der Freizeit endet der Zugang.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.Api.Tests/TeilnehmerZugangTests.cs::Einloesen_204_setzt_das_Gast_Cookie_bis_Freizeitende_ohne_Token_danach_lesen_alle_Gast_Routen_200` – ein Einlösen genügt
- Test: `tests/Zeltlotse.Ui.Tests/TeilnehmerSeitenTests.cs::Der_Aufruf_zeigt_nur_die_Seite_erst_Freizeit_oeffnen_loest_ein_und_fuehrt_zur_Startseite` – Aufruf aus dem QR-Code
- Test: `tests/Zeltlotse.Api.Tests/TeilnehmerZugangTests.cs::Datenumfang_Woche_nur_Tag_Beginn_Titel_Kanal_nur_Alle_ohne_Autor_Infos_nur_Eltern_und_Teilnehmende` – nur Wochenübersicht, Infos und „Alle“
- Test: `tests/Zeltlotse.Api.Tests/TeilnehmerZugangTests.cs::Ohne_Gast_Cookie_404_auch_mit_Team_Sitzung_ein_Gast_an_Team_Routen_401` – nichts darüber hinaus
- Test: `tests/Zeltlotse.Api.Tests/TeilnehmerZugangTests.cs::Ab_dem_Freizeitende_404_beim_Einloesen_und_auf_allen_Gast_Routen` – Zugang endet mit der Freizeit
- Test: `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs::Teilnehmer_Zugang_Qr` – QR-Code in den Einstellungen
- Screenshot: `doku/src/assets/screenshots/teilnehmer-zugang-qr-handy.png`
- Screenshot: `doku/src/assets/screenshots/teilnehmer-zugang-qr-laptop.png`
- Doku: `doku/src/content/docs/leitung/teilnehmer-zugang.md`

**Begründung:** Den Scan mit einer echten Handy-Kamera prüft kein Test. Befunde aus 6-4: Die Zugriffsbegrenzung ist ein gemeinsames Budget je IP-Adresse, eine Scan-Welle im Lager-WLAN kann Teilnehmende und auch die Anmeldung der Leitung bis zu einer Minute sperren; auf dem iPhone fehlt der Web-App vom Home-Bildschirm vielleicht das Gast-Cookie (nur am echten Gerät klärbar).

## CAP-39 – Offline-Modus

**Erfolgskriterium:** Im Flugmodus öffnen sich Notfallkarte und Küchen-Tagesansicht; Offline-Daten werden nach Freizeitende auf dem Gerät gelöscht, auch ohne Netz.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.E2E.Tests/OfflineModusTests.cs::Flugmodus_Notfallkarte_Tagesansicht_und_Vorfall_Warteschlange_Ablauf_und_Abmelden` – offline im Browser: Notfallkarte, Tagesansicht, verschlüsselte Ablage, Löschen nach Freizeitende ohne Netz
- Test: `tests/Zeltlotse.E2E.Tests/OfflineStartTests.cs::App_startet_offline_aus_dem_Service_Worker_mit_Offline_Leiste_und_Notfall_Box` – App startet ohne Netz
- Test: `tests/Zeltlotse.Ui.Tests/OfflineModusTests.cs::Tagesansicht_offline_zeigt_heute_aus_dem_Speicher_mit_ausgegrautem_Restetag_und_Tag_Reitern` – Tagesansicht aus dem Speicher
- Test: `tests/Zeltlotse.Ui.Tests/OfflineModusTests.cs::Nach_dem_Freizeitende_sind_die_Daten_beim_Start_auch_offline_weg` – Löschen ohne Netz
- Test: `tests/Zeltlotse.Ui.Tests/OfflineModusTests.cs::Vorfall_offline_Feld_verlassen_reiht_ein_leert_das_Feld_und_zeigt_wartet_auf_Verbindung` – Vorfall-Protokoll offline beschreibbar
- Screenshot: `doku/src/assets/screenshots/offline-notfallkarte-handy.png`
- Screenshot: `doku/src/assets/screenshots/offline-notfallkarte-laptop.png`
- Doku: `doku/src/content/docs/mitarbeitende/offline.md`

**Begründung:** Der Flugmodus ist im Browser nachgestellt (Playwright offline in Chromium), nicht auf einem echten Handy; das Löschen geschieht beim nächsten Öffnen der Web-App nach Freizeitende, wird sie nie mehr geöffnet, bleiben die verschlüsselten Daten auf dem Gerät liegen.

## CAP-40 – Anwender-Dokumentation

**Erfolgskriterium:** Jede Capability mit Oberfläche hat mindestens eine Doku-Seite mit Screenshots aus dem aktuellen Stand; die Screenshots erzeugt Playwright automatisch aus erfundenen Demo-Daten in Handy- und Laptop-Größe; der Doku-Build läuft fehlerfrei; die Seite „Was passiert mit meinen Daten“ erklärt Freigabefenster, Einsicht und Löschen in Klartext.

**Urteil:** teilweise

**Belege:**
- Test: `tests/Zeltlotse.ArchitectureTests/DokuTests.cs::Jede_Capability_mit_Oberflaeche_hat_eine_Doku_Seite_mit_Handy_und_Laptop_Screenshot` – jede CAP 1–39 hat eine Seite mit Bildpaar
- Test: `tests/Zeltlotse.ArchitectureTests/DokuTests.cs::Jedes_Doku_Bild_wird_von_einem_Playwright_Test_erzeugt` – jedes Bild hat einen Erzeuger
- Test: `tests/Zeltlotse.ArchitectureTests/DokuTests.cs::Screenshot_Links_der_Doku_haben_eine_Datei_und_jedes_Bild_ist_verlinkt` – keine fehlenden oder verwaisten Bilder
- Test: `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs::Meine_Daten` – Beispiel eines Screenshot-Tests für Handy und Laptop
- Test: `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs::Demo_Katalog_hat_erfundene_Stammdaten_mit_Mail_Adressen_unter_example_org` – erfundene Demo-Daten
- Test: `tests/Zeltlotse.ArchitectureTests/DokuTests.cs::Doku_hat_die_fuenf_Rollen_Ordner_mit_index_und_die_Seite_daten` – die Seite „daten“ gibt es
- Doku: `doku/src/content/docs/daten.md`
- Doku: `doku/src/content/docs/erste-schritte/meine-daten.md`
- Screenshot: `doku/src/assets/screenshots/meine-daten-zugriffsprotokoll-handy.png`
- Screenshot: `doku/src/assets/screenshots/meine-daten-zugriffsprotokoll-laptop.png`

**Begründung:** Der Doku-Build (`npm run build` in `doku/`) läuft nicht in `dotnet test` und ist durch keinen automatischen Test belegt; er lief auf dem Arbeitsstand der Story 7-2 (Basis-Commit `5db66e4…`) fehlerfrei (Exit-Code 0, nur der bekannte Sitemap-Hinweis). Ob die Seite „Was passiert mit meinen Daten“ Freigabefenster, Einsicht und Löschen verständlich erklärt, muss ein Mensch lesen; geprüft ist nur, dass es sie gibt. „Aktueller Stand“ heißt: Jedes Bild hat einen Erzeuger, es gibt keinen Bildvergleich.

## Stories mit Status blocked

Keine.

Gelesen aus den `status:`-Zeilen in `stories/`: Die übrigen 43 Stories sind `done`; Story 7-2 ist dieser Bericht selbst und wird mit ihm abgeschlossen.

## Offene Punkte aus den Stories

Zurückgestellte Befunde (`deferred`), die ein Urteil oben einschränken oder vor dem Start als Dienst eine Entscheidung brauchen:

Zur gemeinsamen Zugriffsbegrenzung je IP-Adresse (`0-2`, `1-1`, `2-1`, `6-4`): Nur bei CAP-38 senkt sie das Urteil, weil die Scan-Welle im Lager-WLAN ein konkreter Ablauf genau dieser Funktion ist; bei CAP-1, CAP-7 und CAP-8 ist sie eine ungeprüfte Annahme über Last, die das Erfolgskriterium selbst nicht berührt.

- `1-1` – Login-Link-Anfragen sind nur je IP begrenzt, nicht je Mail-Adresse; vor dem Start als Dienst eine Begrenzung je Adresse entscheiden.
- `1-1` – Die gemeinsame Zugriffsbegrenzung (20 Anfragen je Minute und IP) kann viele Menschen hinter einem Hotspot beim gleichzeitigen Anmelden ausbremsen.
- `1-4` – Übernimmt ein Konto mit Org-Rollen ein Profil, zeigen seine Org-Rollen danach auf eine gelöschte Person (schränkt CAP-2 ein).
- `1-7` – Die Datenauskunft nennt keine Einladungen an die eigene Mail, keine Login-Anfragen und keine selbst gestarteten Profil-Übergaben (schränkt CAP-6 ein).
- `1-7` – Outbox- und Inbox-Tabellen behalten die Kennung einer gelöschten Person in zugestellten Ereignissen; vor dem Start entscheiden, ob diese aufgeräumt werden.
- `1-7` – Einladungen an die Mail-Adresse einer gelöschten Person behalten die Adresse bis 30 Tage nach ihrem Ablauf.
- `0-2` – Crypto-Shredding (Löschen durch Vernichten des Schlüssels) wirkt in Backups nur, wenn der Personen-Schlüssel nicht mit dem alten Hauptschlüssel zurückkommt; mit der Wahl des Hostings entscheiden.
- `0-2` – Der Topf der Zugriffsbegrenzung kann von vielen Besuchern hinter derselben IP geteilt werden (NAT im Lager-WLAN); ungeprüft.
- `2-1` – Die Zugriffsbegrenzung der öffentlichen Freizeit- und Vertrauensseite könnte bei vielen Eltern hinter einer gemeinsamen IP greifen; ungeprüft.
- `2-2` – Erneutes Absenden des Gast-Formulars legt eine zweite Gast-Familie und eine zweite Anmeldung für dasselbe Kind an (schränkt CAP-8 ein).
- `3-8` – Zwischen Schreibschutz-Prüfung und Schreiben kann ein gleichzeitiger Abschluss einen letzten Schreibvorgang durchlassen (schränkt CAP-27 ein).
- `3-8` – Die Anmeldung über den Anmeldelink kann gleichzeitig zum Abschluss noch eine Anmeldung anlegen (schränkt CAP-27 ein).
- `4-1` – Dasselbe Zeitfenster beim Abschluss gilt für das Anlegen von Rezepten; ungeprüft (schränkt CAP-27 ein).
- `4-2` – Nach dem Verschieben der Freizeit-Daten fehlen Mahlzeiten an neuen Tagen, alte Mahlzeiten samt Gerichten erscheinen weiter in Essensplan und Einkaufsliste (schränkt CAP-19 ein).
- `4-2` – Jeder Aufruf von Essensplan oder Einkaufsliste entschlüsselt je Person die Allergie-Antwort; bei großen Freizeiten mit einer Messung prüfen.
- `6-1` – Die Kette Push → Service Worker → Neuladen und die Nutzlast zwischen Server und Service Worker sind nur mit Nachbildungen getestet (schränkt CAP-35 ein).
- `6-1` – Nach einem Wechsel des Push-Schlüssels (VAPID) bleiben bestehende Abos stumm und werden nie abgeräumt.
- `6-1` – Push-Abos bleiben nach Verlassen des Teams bis zur Org-Frist gespeichert (nach Abschluss räumt 6-4 sie inzwischen ab).
- `6-3` – Dass das Kästchen nach einem gescheiterten Tipp wieder den Stand des Servers zeigt, prüft kein Test (schränkt CAP-37 ein).
- `6-4` – Die Zugriffsbegrenzung ist ein gemeinsames Budget je IP für alle begrenzten Routen; eine Scan-Welle im Lager-WLAN kann die Anmeldung der Leitung bis zu einer Minute sperren (schränkt CAP-38 ein; nötig ist eine Entscheidung zur Architekturregel AD-25 „Web-Sicherheit“, die die Zugriffsbegrenzung festlegt).
- `6-4` – Auf dem iPhone fehlt der Web-App vom Home-Bildschirm vielleicht das Gast-Cookie aus Safari; nur am echten Gerät klärbar (schränkt CAP-38 ein).

Erledigt, aber in der Story noch als zurückgestellt eingetragen: `2-10` (das Archiv bleibt leer, bis eine Freizeit abgeschlossen wird – seit 3-8 belegt durch den Abschluss-Test bei CAP-17), `3-4` (Anschaffungswünsche nur im Leer-Zustand – seit 3-8 zeigt der Screenshot-Test eine gefüllte Liste), `1-1`/`1-2`/`1-3` (abgelaufene Login-Anfragen und Einladungen werden seit 1-7 vom Löschlauf entfernt). Die übrigen zurückgestellten Befunde (`0-1`, `0-3`, `3-3`, `3-8` Demo-Team, `6-4` QR-Fehlerzweige) betreffen Testabdeckung oder Demo-Daten, nicht das Verhalten für Anwender.
