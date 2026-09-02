# Produktiv-Deployment

Jeder Push auf `main` deployt automatisch über `.github/workflows/deploy.yml`
nach Azure Container Apps (Server) und Azure Static Web Apps (Client).
Infrastruktur liegt als Bicep unter `infra/`. Details und Hintergrund der
Entscheidungen: `runs/2026-09-02-azure-produktivbetrieb/design.md`.

## Einmaliges Setup (vor dem ersten Deploy)

1. **Neue, dedizierte Azure-Subscription** für Zeltlotse anlegen.
2. **Azure-AD-App-Registrierung für GitHub Actions (OIDC)**:
   ```bash
   az ad app create --display-name zeltlotse-github-actions
   az ad sp create --id <appId>
   az role assignment create --assignee <appId> --role Contributor \
     --scope /subscriptions/<subscription-id>
   az ad app federated-credential create --id <appId> --parameters '{
     "name": "github-deploy-main",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:maSchoeller/Zeltlotse:ref:refs/heads/main",
     "audiences": ["api://AzureADTokenExchange"]
   }'
   ```
3. **GitHub-Repo-Variablen** (Settings → Secrets and variables → Actions →
   Variables) setzen: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
   `AZURE_SUBSCRIPTION_ID`, `DOMAIN_NAME` (z.B. `zeltlotse.de`),
   `SQL_AAD_ADMIN_OBJECT_ID`, `SQL_AAD_ADMIN_LOGIN` (Objekt-ID und
   Anzeigename des Azure-AD-Kontos, das die Datenbank verwalten darf).
4. **GitHub-Repo-Secrets** setzen: `JWT_SIGNING_KEY` (mindestens 32 zufällige
   Zeichen, z.B. `openssl rand -base64 32`) und
   `AZURE_STATIC_WEB_APPS_API_TOKEN` (entsteht erst nach dem ersten
   erfolgreichen Deploy, siehe Schritt 6 — bis dahin schlägt nur der
   Client-Rollout-Schritt fehl, der Rest der Pipeline läuft bereits durch).
   `JWT_SIGNING_KEY` landet beim Deploy in Azure Key Vault, nicht als
   Klartext in der Container App.
5. **Ersten Deploy anstoßen** (Push auf `main`, oder `workflow_dispatch` mit
   `bind-custom-domain: false`). Erzeugt Resource Group, Container Apps,
   SQL-Datenbank, Key Vault und die Static Web App — noch ohne eigene
   Domain.
6. **Static-Web-Apps-Deployment-Token holen** und als
   `AZURE_STATIC_WEB_APPS_API_TOKEN` nachtragen:
   ```bash
   az staticwebapp secrets list --name zeltlotse-client \
     --resource-group zeltlotse-produktion --query properties.apiKey -o tsv
   ```
7. **DNS-Einträge** bei `zeltlotse.de` setzen (Werte aus dem Deploy-Output
   bzw. Azure Portal):
   - `api` → CNAME auf die Standardadresse der Server-Container-App
   - `app` → CNAME auf die Standardadresse der Static Web App
8. **Erneut deployen** mit `bind-custom-domain: true`
   (`workflow_dispatch`), damit die Custom-Domain-Bindungen entstehen.

Ab hier läuft jeder weitere Push auf `main` ohne Sonderfall durch.

## Bekannte, bewusste Einschränkungen

Siehe `debt.md` (Wurzel und Lauf-Ordner) — insbesondere: kein automatischer
Rollback bei fehlgeschlagener Migration, keine Warnung bei aufgebrauchtem
SQL-Free-Tier-Kontingent, gespeichertes Deployment-Token für Static Web Apps
als einzige Ausnahme vom sonst durchgängigen OIDC-Prinzip.
