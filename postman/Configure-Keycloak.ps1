# Add the Postman client to an existing local Compose database without resetting it.
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$realm = Get-Content -LiteralPath (Join-Path $projectDirectory 'deploy/keycloak/mini-wallet-realm.json') -Raw | ConvertFrom-Json
$client = @($realm.clients | Where-Object clientId -EQ 'mini-wallet-postman')
if ($client.Count -ne 1) { throw 'Expected exactly one mini-wallet-postman client in the realm configuration.' }
$clientJson = $client[0] | ConvertTo-Json -Depth 30

Push-Location $projectDirectory
try {
    # The management CLI uses the existing local container bootstrap credentials.
    # --no-config avoids persisting its administrative access token.
    $existingJson = & docker compose exec -T keycloak sh -c 'exec /opt/keycloak/bin/kcadm.sh get clients -r mini-wallet -q clientId=mini-wallet-postman --fields id,clientId --no-config --server http://localhost:8080 --realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD"'
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the local Keycloak clients. Start Docker Compose first.' }
    $existing = @((($existingJson -join "`n") | ConvertFrom-Json))
    if ($existing.Count -gt 1) { throw 'Multiple Postman clients found; refusing to choose one.' }
    if ($existing.Count -eq 0) {
        $clientJson | & docker compose exec -T keycloak sh -c 'exec /opt/keycloak/bin/kcadm.sh create clients -r mini-wallet -f - --no-config --server http://localhost:8080 --realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD"'
    }
    else {
        # Pass the existing ID as a positional argument; never interpolate it into shell code.
        $clientJson | & docker compose exec -T keycloak sh -c 'exec /opt/keycloak/bin/kcadm.sh update "clients/$1" -r mini-wallet -f - --no-config --server http://localhost:8080 --realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD"' postman-client $existing[0].id
    }
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the Postman client.' }
    Write-Host 'mini-wallet-postman is ready. Existing accounts, profiles and wallets were preserved.'
}
finally { Pop-Location }
