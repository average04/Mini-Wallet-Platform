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
        $existingClientId = $existing[0].id
        $clientJson | & docker compose exec -T keycloak sh -c 'exec /opt/keycloak/bin/kcadm.sh update "clients/$1" -r mini-wallet -f - --no-config --server http://localhost:8080 --realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD"' postman-client $existingClientId
    }
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the Postman client.' }
    # Verify the effective configuration instead of assuming a successful PUT applied nested data.
    $configuredJson = & docker compose exec -T keycloak sh -c 'exec /opt/keycloak/bin/kcadm.sh get clients -r mini-wallet -q clientId=mini-wallet-postman --no-config --server http://localhost:8080 --realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD"'
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify the configured Postman client.' }
    $configured = @((($configuredJson -join "`n") | ConvertFrom-Json))
    if ($configured.Count -ne 1) { throw 'Expected exactly one configured Postman client.' }
    foreach ($mapper in $client[0].protocolMappers) {
        $actual = @($configured[0].protocolMappers | Where-Object { $_.name -eq $mapper.name -and $_.protocolMapper -eq $mapper.protocolMapper })
        if ($actual.Count -ne 1) { throw "Missing or ambiguous Postman mapper: $($mapper.name). Check the Keycloak client configuration." }
        foreach ($setting in $mapper.config.PSObject.Properties) {
            # Keycloak omits empty configuration values when reading them back.
            if ([string]$actual[0].config.($setting.Name) -cne [string]$setting.Value) { throw "Postman mapper configuration mismatch: $($mapper.name)." }
        }
    }
    foreach ($scope in $client[0].defaultClientScopes) {
        if ($scope -notin $configured[0].defaultClientScopes) { throw "Missing Postman default client scope: $scope." }
    }
    Write-Host 'mini-wallet-postman is ready. Existing accounts, profiles and wallets were preserved.'
}
finally { Pop-Location }
