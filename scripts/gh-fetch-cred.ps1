#!/usr/bin/env pwsh

param(
	[Parameter(Mandatory = $true)]
	[string]$clientId,

	[string]$rawPrivateKey = $env:RAW_PRIVATE_KEY,

	[string]$Organization = "dotnet"
)


function ConvertTo-Base64Url {
	param([byte[]]$Bytes)
	return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Resolve-PrivateKeyPem {
	param([string]$Pem)

	if ([string]::IsNullOrWhiteSpace($Pem)) {
		throw "Private key value is empty."
	}

	$normalized = $Pem.Trim()

	if (($normalized.StartsWith('"') -and $normalized.EndsWith('"')) -or ($normalized.StartsWith("'") -and $normalized.EndsWith("'"))) {
		$normalized = $normalized.Substring(1, $normalized.Length - 2)
	}

	$normalized = $normalized.Replace('\\r\\n', [Environment]::NewLine)
	$normalized = $normalized.Replace('\\n', [Environment]::NewLine)
	$normalized = $normalized.Replace('\\r', [Environment]::NewLine)

	if ($normalized -notmatch '-----BEGIN[\s\S]+-----END') {
		throw "Private key does not appear to be a valid PEM payload."
	}

	return $normalized
}

if (-not $rawPrivateKey) {
	throw "Private key not provided. Ensure the pipeline variable is set and being passed correctly."
}

$resolvedPrivateKeyPem = Resolve-PrivateKeyPem -Pem $rawPrivateKey

$header = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @{
	alg = "RS256"
	typ = "JWT"
} -Compress)))

$payload = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @{
	iat = [System.DateTimeOffset]::UtcNow.AddSeconds(-10).ToUnixTimeSeconds()
	exp = [System.DateTimeOffset]::UtcNow.AddMinutes(10).ToUnixTimeSeconds()
	iss = $ClientId
} -Compress)))


try {
	
	$rsa = [System.Security.Cryptography.RSA]::Create()
	$rsa.ImportFromPem($resolvedPrivateKeyPem)

	$unsignedJwt = "$header.$payload"
	$signature = ConvertTo-Base64Url ($rsa.SignData(
		[System.Text.Encoding]::UTF8.GetBytes($unsignedJwt),
		[System.Security.Cryptography.HashAlgorithmName]::SHA256,
		[System.Security.Cryptography.RSASignaturePadding]::Pkcs1
	))
	$jwt = "$unsignedJwt.$signature"

	$headers = @{
		Authorization = "Bearer $jwt"
		Accept = "application/vnd.github+json"
		"X-GitHub-Api-Version" = "2022-11-28"
		"User-Agent" = "dotnet-website-gh-fetch-cred"
	}

	$orgInstallation = Invoke-RestMethod -Uri "https://api.github.com/orgs/$Organization/installation" -Headers $headers -Method Get -ErrorAction Stop

	if (-not $orgInstallation) {
		throw "No installation found for org '$Organization'. Ensure the app is installed for that org."
	}

	$resolvedInstallationId = [string]$orgInstallation.id

	Write-Host "Resolved installation id for org '$Organization': $resolvedInstallationId"

	$tokenResponse = $null
	$tokenEndpoint = "https://api.github.com/app/installations/$resolvedInstallationId/access_tokens"
	$tokenResponse = Invoke-RestMethod -Uri $tokenEndpoint -Headers $headers -Method Post -ContentType "application/json" -Body "{}" -ErrorAction Stop

	if (-not $tokenResponse.token) {
		throw "Access token endpoint returned no token."
	}

	Write-Host "Installation token check passed."
	Write-Host "Token expires at: $($tokenResponse.expires_at)"
	Write-Host "##vso[task.setVariable variable=hasToken]true"
	Write-Host "##vso[task.setVariable variable=GITHUB_TOKEN;issecret=true]$($tokenResponse.token)"
	
} catch {
	$statusCode = $null
	if ($_.Exception.Response) {
		$statusCode = [int]$_.Exception.Response.StatusCode
	}

	if ($statusCode) {
		Write-Host "GitHub API call failed with HTTP $statusCode."
	} else {
		Write-Host "GitHub API call failed: $($_.Exception.Message)"
	}

	Write-Host "##vso[task.setVariable variable=hasToken]false"
}
finally {
	$rsa.Dispose()
}
