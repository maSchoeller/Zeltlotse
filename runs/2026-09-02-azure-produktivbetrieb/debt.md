# Debt — Lauf 2026-09-02-azure-produktivbetrieb

- 2026-09-02 — SQL-Firewall erlaubt `AllowAllWindowsAzureIps` statt einer
  festen IP-Einschränkung, weil Container Apps ohne NAT Gateway keine feste
  ausgehende IP hat. Mitigiert durch AAD-only-Auth (kein SQL-Passwort, das
  erraten werden könnte).
- 2026-09-02 — **Im Retro behoben:** Secrets liegen jetzt in Azure Key Vault
  statt in Container-Apps-eigenen Secrets (JWT-Signierschlüssel, ghcr-Token),
  per Key-Vault-Referenz und Rollenzuweisung an die Server-Identität. Kostet
  eine zusätzliche Ressource, dafür zentrale Verwaltung/Rotationsfähigkeit.
- 2026-09-02 — **Im Retro geprüft und bewusst beibehalten:** Rechtevergabe
  der Managed Identity auf die Azure-SQL-Datenbank läuft weiterhin als
  `sqlcmd`-Schritt im Deploy-Workflow, nicht als deklarative
  Bicep-`deploymentScript`-Ressource. Eine Managed Identity könnte laut
  Microsoft-Dokumentation zwar als Azure-AD-Verwalter der SQL-Datenbank
  eingesetzt werden, aber `deploymentScript` müsste `sqlcmd` zur Laufzeit in
  einem isolierten Azure-Container nachinstallieren — genauso ungetestet und
  fragil wie der jetzige Schritt, nur an schwerer kontrollierbarer Stelle.
  Kein Sicherheitsgewinn, nur verlagertes Risiko.
- 2026-09-02 — Alte Container-Images in ghcr.io werden nach fester Anzahl
  (letzte 10 je Image) aufgeräumt, unabhängig davon, ob eine ältere Version
  theoretisch noch für einen manuellen Rollback gebraucht würde.
- 2026-09-02 — Der allererste Produktiv-Deploy braucht zwei
  Bicep-Deployment-Durchläufe mit einem manuellen DNS-Schritt dazwischen
  (Custom-Domain-Verifizierung braucht die von Azure vergebene
  Standardadresse). Jeder weitere Deploy ist ein normaler, ungeteilter Lauf.
- 2026-09-02 — Kein automatischer Rollback bei fehlgeschlagener Migration
  während des Deploys; Abbruch, alte Version bleibt aktiv, manuelle Behebung.
  Bewusst so entschieden, siehe requirements.md (Nicht-Ziele).
- 2026-09-04 — **Beim echten Erstdeploy behoben:** `deploy.yml`s Schritt
  "Datenbankrechte für die Server-Identität setzen" scheiterte zweifach: das
  `azure-cli`-Image bringt kein `sqlcmd` mit (jetzt per Direct-Download des
  offiziellen go-sqlcmd-Release behoben), und
  `--authentication-method="ActiveDirectoryAccessToken"` existiert als Wert
  gar nicht — ein fertiges Zugriffstoken übergibt man stattdessen schlicht
  über `-G -P "<token>"` (ab sqlcmd v17.8). Gegen die echte Azure-SQL-Instanz
  bestätigt.
- 2026-09-02 — Kein Warnmechanismus, wenn das kostenlose SQL-Kontingent
  (100.000 vCore-Sekunden/Monat) aufgebraucht ist; die Datenbank pausiert
  dann automatisch bis zum nächsten Monat. Bewusst in Kauf genommenes Risiko
  bei erwartetem geringem Traffic.
- 2026-09-02 — Während der Implementierung entdeckt: Der Wartungsmodus
  (Migrationen, Aufräumdienst) brauchte in SQL Server eine explizite
  `system_bypass`-Sitzungsvariable statt, wie in PostgreSQL, einfach keine
  Rolle umzustellen — SQL Server unter Azure-AD-Auth hat keine privilegierte
  Basisrolle, die automatisch von der Richtlinie ausgenommen wäre. In
  `MandantInterceptor` und `SystemDatenbank` entsprechend nachgezogen; durch
  die Integrationstests abgesichert (`Wartungszugang_sieht_alle_Organisationen`
  u.a. liefen zunächst rot).
- 2026-09-02 — Der Client läuft auf Azure Static Web Apps statt als
  Container App; das Deployment dorthin braucht ein gespeichertes
  Deployment-Token als GitHub-Secret statt echtem OIDC (Static Web Apps
  bietet dafür kein Workload-Identity-Verfahren). Das Token ist auf genau
  diese eine Ressource beschränkt, kein Subscription-weiter Zugriff — einzige
  Ausnahme vom sonst durchgängigen "kein gespeichertes Azure-Geheimnis"-Prinzip
  dieser Infrastruktur.
