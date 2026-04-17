<#
.SYNOPSIS
    Acquires a bearer token for the Copilot Tracker API with caching.
.DESCRIPTION
    Supports two auth modes:
    - "user": Uses Azure CLI (az account get-access-token)
    - "certificate": Uses app + certificate with JWT client assertion
    
    Tokens are cached to disk and reused until near expiry.
#>

function Get-TrackerToken {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Config
    )
    
    $cacheDir = Join-Path $env:USERPROFILE ".copilot\copilot-tracker"
    $cachePath = Join-Path $cacheDir ".token-cache.json"
    
    # Check cache (with validation)
    if (Test-Path $cachePath) {
        try {
            $cache = Get-Content $cachePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $configAuthMode = if ($Config.authMode) { $Config.authMode } else { "user" }
            $isValid = $cache.tenantId -eq $Config.tenantId -and
                       $cache.resourceId -eq $Config.resourceId -and
                       $cache.authMode -eq $configAuthMode -and
                       ([datetime]$cache.expiresOn) -gt (Get-Date).ToUniversalTime().AddMinutes(5)
            
            if ($Config.authMode -eq "certificate") {
                $isValid = $isValid -and $cache.clientId -eq $Config.clientId
            }
            
            if ($isValid) {
                return $cache.accessToken
            }
        } catch {
            # Cache corrupt, treat as miss
        }
    }
    
    $token = $null
    $expiresOn = $null
    
    if ($Config.authMode -eq "certificate") {
        $result = Get-CertificateToken -Config $Config
        $token = $result.AccessToken
        $expiresOn = $result.ExpiresOn
    } else {
        # Azure CLI token acquisition
        $scope = $Config.resourceId
        $tokenJson = az account get-access-token --resource $scope --query "{accessToken:accessToken,expiresOn:expiresOn}" -o json 2>$null
        if (-not $tokenJson) {
            throw "Failed to acquire token via Azure CLI. Run 'az login' first."
        }
        $tokenResponse = $tokenJson | ConvertFrom-Json
        $token = $tokenResponse.accessToken
        $expiresOn = $tokenResponse.expiresOn
    }
    
    # Cache token atomically
    $cacheData = @{
        accessToken = $token
        expiresOn   = $expiresOn
        tenantId    = $Config.tenantId
        resourceId  = $Config.resourceId
        authMode    = if ($Config.authMode) { $Config.authMode } else { "user" }
        clientId    = $Config.clientId
    }
    
    if (-not (Test-Path $cacheDir)) {
        New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    }
    
    $tempPath = "$cachePath.tmp.$PID"
    try {
        $cacheData | ConvertTo-Json | Set-Content -Path $tempPath -Encoding UTF8 -Force
        Move-Item -Path $tempPath -Destination $cachePath -Force
    } catch {
        Remove-Item -Path $tempPath -ErrorAction SilentlyContinue
    }
    
    return $token
}

function Get-CertificateToken {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Config
    )
    
    # Find certificate
    $certSubject = $Config.certificateSubject
    $certThumbprint = $Config.certificateThumbprint
    
    $cert = $null
    if ($certThumbprint) {
        $cert = Get-ChildItem Cert:\CurrentUser\My\$certThumbprint -ErrorAction SilentlyContinue
    }
    if (-not $cert -and $certSubject) {
        $certs = Get-ChildItem Cert:\CurrentUser\My | Where-Object { 
            $_.Subject -eq $certSubject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) 
        }
        if ($certs.Count -gt 1) {
            throw "Multiple valid certificates found for subject '$certSubject'. Specify certificateThumbprint in config."
        }
        $cert = $certs | Select-Object -First 1
    }
    
    if (-not $cert) {
        throw "Certificate not found. Subject='$certSubject', Thumbprint='$certThumbprint'. Ensure cert is in CurrentUser\My store with a private key."
    }
    
    if (-not $cert.HasPrivateKey) {
        throw "Certificate found but has no private key. Thumbprint: $($cert.Thumbprint)"
    }
    
    # Build JWT client assertion
    $clientId = $Config.clientId
    $tenantId = $Config.tenantId
    $tokenEndpoint = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token"
    
    # JWT Header
    $certHash = $cert.GetCertHash()
    $x5t = [Convert]::ToBase64String($certHash).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $header = @{ alg = "RS256"; typ = "JWT"; x5t = $x5t } | ConvertTo-Json -Compress
    
    # JWT Payload
    $now = [DateTimeOffset]::UtcNow
    $payload = @{
        iss = $clientId
        sub = $clientId
        aud = $tokenEndpoint
        exp = ($now.AddMinutes(10)).ToUnixTimeSeconds()
        iat = $now.ToUnixTimeSeconds()
        nbf = $now.ToUnixTimeSeconds()
        jti = [Guid]::NewGuid().ToString()
    } | ConvertTo-Json -Compress
    
    # Base64Url encode
    $headerB64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($header)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $payloadB64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    
    # Sign
    $dataToSign = [System.Text.Encoding]::UTF8.GetBytes("$headerB64.$payloadB64")
    $privateKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
    $signature = $privateKey.SignData($dataToSign, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $signatureB64 = [Convert]::ToBase64String($signature).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    
    $jwt = "$headerB64.$payloadB64.$signatureB64"
    
    # Exchange for token
    $scope = "$($Config.resourceId)/.default"
    $body = @{
        grant_type            = "client_credentials"
        client_id             = $clientId
        client_assertion_type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
        client_assertion      = $jwt
        scope                 = $scope
    }
    
    try {
        $response = Invoke-RestMethod -Uri $tokenEndpoint -Method POST -Body $body -ContentType "application/x-www-form-urlencoded" -TimeoutSec 10
    } catch {
        throw "Failed to acquire certificate token: $($_.Exception.Message)"
    }
    
    # Calculate expiry
    $expiresOn = (Get-Date).ToUniversalTime().AddSeconds($response.expires_in).ToString("o")
    
    return @{
        AccessToken = $response.access_token
        ExpiresOn   = $expiresOn
    }
}
