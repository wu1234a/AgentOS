param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
try {
    $response = Invoke-WebRequest -Uri "$($BaseUrl.TrimEnd('/'))/api/v1/version" -TimeoutSec $TimeoutSeconds -UseBasicParsing
    if ($response.StatusCode -ne 200) {
        Write-Error "Unexpected HTTP status: $($response.StatusCode)"
        exit 1
    }

    $info = $response.Content | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($info.version) -or
        [string]::IsNullOrWhiteSpace($info.gitCommit) -or
        [string]::IsNullOrWhiteSpace($info.buildTimeUtc) -or
        [string]::IsNullOrWhiteSpace($info.environment)) {
        Write-Error "Version response is missing required fields."
        exit 1
    }

    $actual = $info.gitCommit.Trim()
    $expected = $ExpectedCommit.Trim()
    "Version:      $($info.version)"
    "Commit:       $actual"
    "Expected:     $expected"
    "BuildTimeUtc: $($info.buildTimeUtc)"
    "Environment:  $($info.environment)"

    if ([string]::Equals($actual, 'Unknown', [StringComparison]::OrdinalIgnoreCase)) {
        Write-Error "Deployment did not report a Git commit."
        exit 1
    }

    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Error "Commit mismatch."
        exit 1
    }

    "Deployment verified."
    exit 0
}
catch {
    Write-Error "Deployment verification failed: $($_.Exception.Message)"
    exit 1
}
