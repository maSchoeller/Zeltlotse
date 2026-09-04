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
   **Zwei Stolpersteine, die beim ersten echten Durchlauf aufgetreten sind:**
   - `az role assignment create` kann direkt nach dem Anlegen einer brandneuen
     Subscription mit `MissingSubscription` fehlschlagen (RBAC braucht
     manchmal ein paar Minuten Vorlauf — bei uns half auch Warten nicht;
     ein direkter REST-Aufruf (`az rest --method put --url
     https://management.azure.com/subscriptions/<id>/providers/Microsoft.Authorization/roleAssignments/<neue-guid>?api-version=2022-04-01
     --body '{"properties": {"roleDefinitionId": "…/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c", "principalId": "<sp-object-id>", "principalType": "ServicePrincipal"}}'`)
     funktionierte dagegen sofort.
   - Der tatsächliche OIDC-Subject-Claim hängt davon ab, ob der Deploy-Job ein
     GitHub-Environment nutzt (`environment: production` in `deploy.yml`
     führt zu `repo:<owner>/<repo>:environment:production` statt
     `ref:refs/heads/main`) und kann je nach Repo-Konfiguration
     Konto-/Repo-IDs enthalten (bei uns z.B.
     `repo:maSchoeller@56505280/Zeltlotse@1347668021:environment:production`).
     Den exakten Wert liefert die Fehlermeldung eines fehlgeschlagenen
     `azure/login`-Schritts ("Federated token details") — dort abschreiben
     und die Federated Credential per `az ad app federated-credential update`
     darauf korrigieren.
   - **Contributor reicht nicht für die Key-Vault-Rollenzuweisung im
     Bicep-Template** (Contributor darf per Definition keine
     `Microsoft.Authorization/roleAssignments` schreiben). Zusätzlich
     `User Access Administrator`, beschränkt auf die Resource Group
     `zeltlotse-produktion`, zuweisen — das geht erst, nachdem die Resource
     Group existiert (also nach dem ersten, an dieser Stelle
     fehlschlagenden Deploy-Versuch):
     ```bash
     az rest --method put \
       --url "https://management.azure.com/subscriptions/<subscription-id>/resourceGroups/zeltlotse-produktion/providers/Microsoft.Authorization/roleAssignments/<neue-guid>?api-version=2022-04-01" \
       --body '{"properties": {"roleDefinitionId": "/subscriptions/<subscription-id>/providers/Microsoft.Authorization/roleDefinitions/18d7d88d-d35e-4fb5-a5c3-7773c20a72d9", "principalId": "<sp-object-id>", "principalType": "ServicePrincipal"}}'
     ```
   - **Der SQL-Server braucht eine eigene System-Identität mit der
     Azure-AD-Rolle "Directory Readers" (Verzeichnisleser).** Ohne sie kann
     Azure SQL keine Azure-AD-Objekte nachschlagen und jedes
     `CREATE USER … FROM EXTERNAL PROVIDER` schlägt mit
     `Msg 33134: Principal '…' could not be resolved. Error message:
     'Server identity is not configured...'` fehl — auch wenn der
     Administrator und die Objekt-ID stimmen. Bicep kann die Rolle nicht
     vergeben (Verzeichnisrolle, keine Azure-RBAC-Rolle), deshalb einmalig
     per Microsoft-Graph-Aufruf nachziehen (die Rollen-ID
     `88d8e3e3-8f55-4a1e-953a-9b9898b8876b` ist die feste, tenant-weite ID
     für "Directory Readers"):
     ```bash
     PRINCIPAL_ID=$(az sql server show --name <server-name> --resource-group zeltlotse-produktion --query identity.principalId -o tsv)
     az rest --method post --url "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments" \
       --body "{\"principalId\": \"$PRINCIPAL_ID\", \"roleDefinitionId\": \"88d8e3e3-8f55-4a1e-953a-9b9898b8876b\", \"directoryScopeId\": \"/\"}"
     ```
   - **Der SQL-Administrator muss die GitHub-Actions-App selbst sein, nicht
     ein Mensch.** Ursprünglich stand hier das menschliche Konto, aber die
     Anmeldung im Deploy-Workflow läuft als die App — deren Zugriffstoken
     wurde beim `sqlcmd`-Schritt mit "Login failed for user
     '\<token-identified principal\>'" abgewiesen, weil die App selbst
     keine Datenbankrechte hatte und auch nicht Administrator war. Die App
     als `principalType: 'Application'` (nicht `'User'`) zum SQL-AAD-Admin
     zu machen, löst das; der Mensch bekommt stattdessen einen eigenen
     `db_owner`-Zugang über den `sqlcmd`-Schritt in `deploy.yml`
     (`CREATE USER … FROM EXTERNAL PROVIDER WITH OBJECT_ID = '…'` — das
     `WITH OBJECT_ID` umgeht Unsicherheiten beim genauen UPN-Format von
     Gastkonten).
3. **GitHub-Repo-Variablen** (Settings → Secrets and variables → Actions →
   Variables) setzen: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
   `AZURE_SUBSCRIPTION_ID`, `DOMAIN_NAME` (z.B. `zeltlotse.de`),
   `SQL_AAD_ADMIN_OBJECT_ID`, `SQL_AAD_ADMIN_LOGIN` (Objekt-ID und
   Anzeigename der **GitHub-Actions-App selbst**, nicht eines Menschen — der
   Deploy-Workflow muss laufend Rechte an neue Identitäten vergeben können,
   das geht nur als SQL-Administrator), `HUMAN_ADMIN_OBJECT_ID` (Objekt-ID
   des menschlichen Verwalters — bekommt einen vollwertigen
   Datenbankzugang über `db_owner`, siehe Schritt 5).
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
