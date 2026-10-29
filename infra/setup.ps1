<#
.SYNOPSIS
    One-time, idempotent setup of the DataTrustHub production resources (ADR-0021).

.DESCRIPTION
    Finishes what the portal did not set on the existing production resources, with the Azure CLI only:
      - a system-assigned managed identity on the API (the WebApp holds no secret);
      - the API's app settings: SQL connection string (managed identity, no password), JWT signing key;
        existing settings, such as the Application Insights ones, are kept;
      - the admin seed (AdminSeed__Email, AdminSeed__Password), only when asked for with -SeedAdmin:
        the first admin is seeded once, then the owner removes both settings (ADR-0022). Without
        -SeedAdmin the admin seed is neither asked for nor written, and existing values are kept;
      - the WebApp's Api__BaseUrl, read from the API's default host name;
      - FTP and basic publishing credentials (SCM and FTP) disabled on both apps;
      - the health check path on both apps;
      - the SQL firewall rule that lets Azure services reach the server.
    Running it twice gives the same end state: every step reads the current state first and changes
    only what differs. It never prints a secret value.

.PARAMETER WhatIf
    Dry run: prints every az command that would change Azure, with secrets masked, and changes nothing.

.PARAMETER Force
    Skips the confirmation prompt.

.PARAMETER RotateJwtSecret
    Generates a new JWT signing key even if one exists. This signs out every user.

.PARAMETER SeedAdmin
    Asks for the admin seed e-mail address and password and writes both settings, overwriting
    existing values: for the first admin, or to recover access (ADR-0022). In a dry run both are
    planned masked and nothing is asked.

.EXAMPLE
    .\infra\setup.ps1 -WhatIf
    .\infra\setup.ps1

.EXAMPLE
    .\infra\setup.ps1 -SeedAdmin
    Seeds the first admin; after the API has seeded it, delete both AdminSeed settings (infra/README.md).
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'DataTrustHub',
    [string] $ApiAppName = 'datatrusthubapi',
    [string] $WebAppName = 'DataTrustHub',
    [string] $SqlServerName = 'datatrusthub',
    [string] $DatabaseName = 'datatrusthub',
    [switch] $WhatIf,
    [switch] $Force,
    [switch] $RotateJwtSecret,
    [switch] $SeedAdmin
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------
$ConnectionStringSetting = 'ConnectionStrings__DefaultConnection'
$JwtSecretSetting = 'Jwt__Secret'
$AdminSeedEmailSetting = 'AdminSeed__Email'
$AdminSeedPasswordSetting = 'AdminSeed__Password'
$ApiBaseUrlSetting = 'Api__BaseUrl'
$MaskedSettings = @($JwtSecretSetting, $AdminSeedEmailSetting, $AdminSeedPasswordSetting)

$ConnectionStringFormat = 'Server=tcp:{0}.database.windows.net,1433;Database={1};' +
    'Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60'
$ApiBaseUrlFormat = 'https://{0}'
$JwtSecretByteCount = 48

$SystemAssignedIdentity = '[system]'
$SystemAssignedType = 'SystemAssigned'
$FtpsStateDisabled = 'Disabled'
$HealthCheckPath = '/health'
$BasicAuthPolicies = @('scm', 'ftp')
$BasicAuthDisabled = 'false'
$FirewallRuleName = 'AllowAllWindowsAzureIps'
$AzureServicesAddress = '0.0.0.0'
$ConfirmAnswer = 'yes'
$MaxInputAttempts = 3
$EmailPattern = '^[^@\s]+@[^@\s]+$'

$MaskedValue = '***'
$ApiHostPlaceholder = '<API default host name>'
$SubscriptionPlaceholder = '<not signed in>'
$SettingsFilePlaceholder = '@<temporary settings file>'
$SqlScriptPath = 'infra/sql/grant-roles.sql'

# State of the run
$script:IsSignedIn = $false
$script:Changes = New-Object System.Collections.Generic.List[string]

# ---------------------------------------------------------------------------------------------
# Azure CLI helpers
# ---------------------------------------------------------------------------------------------

# Runs an az command and returns its exit code and output lines. Its stderr (warnings) is dropped.
function Invoke-Az {
    param([string[]] $Arguments)
    $ErrorActionPreference = 'Continue'
    $output = & az @Arguments 2>$null
    return [pscustomobject] @{ ExitCode = $LASTEXITCODE; Output = $output }
}

# Runs a read-only az command for the named step and returns its output lines; a successful read
# with no output means "not set". Only in a signed-out dry run is nothing read ($null is returned).
# A read that fails stops the script, so that a failed read is never taken for a missing setting.
function Invoke-AzRead {
    param([string] $Step, [string[]] $Arguments)
    if (-not $script:IsSignedIn) { return $null }
    $result = Invoke-Az $Arguments
    if ($result.ExitCode -ne 0) {
        throw ("Cannot read $Step (az exited with code $($result.ExitCode)). The script stopped here; " +
            'fix the cause and run it again.')
    }
    return $result.Output
}

# Runs an az command that changes Azure, or only prints it in dry-run mode.
# DisplayArguments replaces Arguments in the printed command, so that no secret is shown.
function Invoke-AzChange {
    param(
        [string] $Description,
        [string[]] $Arguments,
        [string[]] $DisplayArguments = $Arguments
    )
    $script:Changes.Add($Description)
    if ($WhatIf) {
        Write-Host ('  az ' + (Format-Arguments $DisplayArguments)) -ForegroundColor Yellow
        return
    }
    Write-Host "  $Description"
    $ErrorActionPreference = 'Continue'
    & az @Arguments --output none
    if ($LASTEXITCODE -ne 0) { throw "az failed: $Description" }
}

# Joins arguments for display, quoting the ones that contain spaces.
function Format-Arguments {
    param([string[]] $Arguments)
    $quoted = foreach ($argument in $Arguments) {
        if ($argument -match '\s') { '"' + $argument + '"' } else { $argument }
    }
    return ($quoted -join ' ')
}

# ---------------------------------------------------------------------------------------------
# Sign-in and confirmation
# ---------------------------------------------------------------------------------------------

# Checks that az is signed in and returns the subscription name.
# In dry-run mode a missing sign-in is allowed: the defaults and placeholders are used.
function Get-SignedInSubscription {
    $result = Invoke-Az @('account', 'show', '--query', 'name', '--output', 'tsv')
    if (($result.ExitCode -eq 0) -and $result.Output) {
        $script:IsSignedIn = $true
        return [string] $result.Output
    }
    if (-not $WhatIf) { throw 'The Azure CLI is not signed in. Run az login, then run this script again.' }
    Write-Warning 'The Azure CLI is not signed in: the dry run assumes nothing is set yet.'
    return $SubscriptionPlaceholder
}

# Asks the owner to confirm before anything changes, unless -Force or -WhatIf is given.
function Confirm-Run {
    param([string] $SubscriptionName)
    Write-Host "Subscription:   $SubscriptionName"
    Write-Host "Resource group: $ResourceGroup"
    if ($WhatIf) { Write-Host 'Dry run: nothing is changed. Commands that would change Azure:'; return }
    if ($Force) { return }
    $answer = Read-Host "Type '$ConfirmAnswer' to change these resources"
    if ($answer -ne $ConfirmAnswer) { throw 'Cancelled by the owner. Nothing was changed.' }
}

# ---------------------------------------------------------------------------------------------
# Managed identity
# ---------------------------------------------------------------------------------------------

# Enables the system-assigned managed identity on the API, if it is not enabled yet.
function Enable-ApiManagedIdentity {
    $identityType = Invoke-AzRead "the managed identity of $ApiAppName" @(
        'webapp', 'show', '--resource-group', $ResourceGroup,
        '--name', $ApiAppName, '--query', 'identity.type', '--output', 'tsv')
    if ($identityType -and ([string] $identityType).Contains($SystemAssignedType)) { return }
    Invoke-AzChange "Enable the system-assigned managed identity on $ApiAppName" @(
        'webapp', 'identity', 'assign', '--resource-group', $ResourceGroup,
        '--name', $ApiAppName, '--identities', $SystemAssignedIdentity)
}

# ---------------------------------------------------------------------------------------------
# App settings
# ---------------------------------------------------------------------------------------------

# Returns the names of an app's settings (never their values).
function Get-AppSettingNames {
    param([string] $AppName)
    return @(Invoke-AzRead "the app setting names of $AppName" @(
        'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup,
        '--name', $AppName, '--query', '[].name', '--output', 'tsv'))
}

# Returns the value of one non-secret app setting, or $null when it does not exist.
function Get-AppSettingValue {
    param([string] $AppName, [string] $SettingName)
    $query = "[?name=='$SettingName'].value"
    $value = Invoke-AzRead "app setting $SettingName of $AppName" @(
        'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup,
        '--name', $AppName, '--query', $query, '--output', 'tsv')
    if ($value) { return [string] $value }
    return $null
}

# Generates a random JWT signing key, base64-encoded.
function New-JwtSecret {
    $bytes = New-Object byte[] $JwtSecretByteCount
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

# Converts a secure string to plain text, for the settings file only.
function ConvertTo-PlainText {
    param([System.Security.SecureString] $SecureValue)
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

# Stops the script when a secret setting would be written without the app's setting names having
# been read. Only a signed-out dry run gets past without a read; it assumes nothing is set yet.
function Assert-SettingNamesRead {
    param([string] $SettingName)
    if ($script:IsSignedIn -or $WhatIf) { return }
    throw "Cannot decide whether $SettingName exists: the app settings were not read."
}

# Adds the JWT signing key to the settings if it is missing, or if a rotation is asked for.
# A new key signs out every user, so it is generated only after the setting names were read.
function Add-JwtSecretSetting {
    param([hashtable] $Settings, [string[]] $ExistingNames)
    if (($ExistingNames -contains $JwtSecretSetting) -and -not $RotateJwtSecret) { return }
    if (-not $RotateJwtSecret) { Assert-SettingNamesRead $JwtSecretSetting }
    if ($WhatIf) { $Settings[$JwtSecretSetting] = $MaskedValue; return }
    $Settings[$JwtSecretSetting] = New-JwtSecret
}

# Adds the admin seed to the settings, only when -SeedAdmin asks for it (ADR-0022): both values are
# read from the owner and overwrite existing ones. Without -SeedAdmin the admin seed is left as it is.
# The password is read as a secure string and never echoed.
function Add-AdminSeedSettings {
    param([hashtable] $Settings)
    if (-not $SeedAdmin) { return }
    if ($WhatIf) {
        $Settings[$AdminSeedEmailSetting] = $MaskedValue
        $Settings[$AdminSeedPasswordSetting] = $MaskedValue
        return
    }
    $Settings[$AdminSeedEmailSetting] = Read-AdminSeedEmail
    $Settings[$AdminSeedPasswordSetting] = Read-AdminSeedPassword
}

# Reads the admin seed e-mail address from the owner, asking again while it is empty or malformed.
function Read-AdminSeedEmail {
    for ($attempt = 1; $attempt -le $MaxInputAttempts; $attempt++) {
        $email = ([string] (Read-Host 'Admin seed e-mail address')).Trim()
        if ($email -match $EmailPattern) { return $email }
        Write-Warning 'The e-mail address is empty or malformed. Try again.'
    }
    throw "No valid admin seed e-mail address after $MaxInputAttempts attempts. The admin seed was not set."
}

# Reads the admin seed password twice as a secure string, asking again while it is empty or the
# two entries differ. The password is never echoed.
function Read-AdminSeedPassword {
    for ($attempt = 1; $attempt -le $MaxInputAttempts; $attempt++) {
        $password = Read-Host 'Admin seed password' -AsSecureString
        $confirmation = Read-Host 'Admin seed password (again)' -AsSecureString
        if ($password.Length -eq 0) { Write-Warning 'The password is empty. Try again.'; continue }
        if (Test-SecureStringsEqual $password $confirmation) { return $password }
        Write-Warning 'The two passwords differ. Try again.'
    }
    throw "No valid admin seed password after $MaxInputAttempts attempts. The admin seed was not set."
}

# Compares two secure strings without keeping their plain text longer than the comparison.
function Test-SecureStringsEqual {
    param([System.Security.SecureString] $First, [System.Security.SecureString] $Second)
    if ($First.Length -ne $Second.Length) { return $false }
    return [string]::Equals((ConvertTo-PlainText $First), (ConvertTo-PlainText $Second), [StringComparison]::Ordinal)
}

# Writes the settings to a temporary JSON file in the format az reads with --settings @file.
# A file avoids cmd.exe mangling special characters (az.cmd) and keeps secrets off the command line.
function Write-SettingsFile {
    param([hashtable] $Settings)
    $entries = foreach ($name in $Settings.Keys) {
        $value = $Settings[$name]
        if ($value -is [System.Security.SecureString]) { $value = ConvertTo-PlainText $value }
        [pscustomobject] @{ name = $name; value = [string] $value; slotSetting = $false }
    }
    $path = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName() + '.json')
    $json = ConvertTo-Json -InputObject @($entries)
    [IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding $false))
    return $path
}

# Adds or updates the given settings of an app, keeping every other setting.
function Set-AppSettings {
    param([string] $AppName, [hashtable] $Settings)
    if ($Settings.Count -eq 0) { return }
    $names = @($Settings.Keys | Sort-Object)
    $description = "Set app settings on ${AppName}: " + ($names -join ', ')
    $baseArguments = @('webapp', 'config', 'appsettings', 'set', '--resource-group', $ResourceGroup,
        '--name', $AppName, '--settings')
    if ($WhatIf) {
        Invoke-AzChange $description @() ($baseArguments + $SettingsFilePlaceholder)
        Write-SettingsPreview $Settings $names
        return
    }
    $path = Write-SettingsFile $Settings
    try { Invoke-AzChange $description ($baseArguments + "@$path") }
    finally { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
}

# Prints the settings a dry run would write, with secrets and personal data masked.
function Write-SettingsPreview {
    param([hashtable] $Settings, [string[]] $Names)
    foreach ($name in $Names) {
        $value = $Settings[$name]
        if ($MaskedSettings -contains $name) { $value = $MaskedValue }
        Write-Host "      $name=$value" -ForegroundColor DarkYellow
    }
}

# Sets the API's connection string, JWT signing key and, with -SeedAdmin, the admin seed;
# keeps every other setting.
function Set-ApiAppSettings {
    $existingNames = Get-AppSettingNames $ApiAppName
    $settings = @{}
    $connectionString = $ConnectionStringFormat -f $SqlServerName, $DatabaseName
    if ((Get-AppSettingValue $ApiAppName $ConnectionStringSetting) -ne $connectionString) {
        $settings[$ConnectionStringSetting] = $connectionString
    }
    Add-JwtSecretSetting $settings $existingNames
    Add-AdminSeedSettings $settings
    Set-AppSettings $ApiAppName $settings
}

# Sets the WebApp's Api__BaseUrl to the API's default host name, read from Azure.
function Set-WebAppApiBaseUrl {
    $apiHostName = Invoke-AzRead "the default host name of $ApiAppName" @(
        'webapp', 'show', '--resource-group', $ResourceGroup,
        '--name', $ApiAppName, '--query', 'defaultHostName', '--output', 'tsv')
    if (-not $apiHostName) {
        if (-not $WhatIf) { throw "Cannot read the default host name of $ApiAppName." }
        $apiHostName = $ApiHostPlaceholder
    }
    $baseUrl = $ApiBaseUrlFormat -f ([string] $apiHostName)
    if ((Get-AppSettingValue $WebAppName $ApiBaseUrlSetting) -eq $baseUrl) { return }
    Set-AppSettings $WebAppName @{ $ApiBaseUrlSetting = $baseUrl }
}

# ---------------------------------------------------------------------------------------------
# Site configuration and publishing credentials
# ---------------------------------------------------------------------------------------------

# Reads one property of an app's site configuration.
function Get-SiteConfigValue {
    param([string] $AppName, [string] $Property)
    $value = Invoke-AzRead "site configuration $Property of $AppName" @(
        'webapp', 'config', 'show', '--resource-group', $ResourceGroup,
        '--name', $AppName, '--query', $Property, '--output', 'tsv')
    if ($value) { return [string] $value }
    return $null
}

# Disables FTP and FTPS on an app, if not disabled yet.
function Disable-Ftp {
    param([string] $AppName)
    if ((Get-SiteConfigValue $AppName 'ftpsState') -eq $FtpsStateDisabled) { return }
    Invoke-AzChange "Disable FTP on $AppName" @('webapp', 'config', 'set', '--resource-group', $ResourceGroup,
        '--name', $AppName, '--ftps-state', $FtpsStateDisabled)
}

# Sets the App Service health check path of an app, if it differs.
function Set-HealthCheckPath {
    param([string] $AppName)
    if ((Get-SiteConfigValue $AppName 'healthCheckPath') -eq $HealthCheckPath) { return }
    Invoke-AzChange "Set the health check path of $AppName to $HealthCheckPath" @(
        'webapp', 'config', 'set', '--resource-group', $ResourceGroup, '--name', $AppName,
        '--generic-configurations', "healthCheckPath=$HealthCheckPath")
}

# Disables basic publishing credentials (SCM and FTP) on an app, for each policy still allowing them.
function Disable-BasicPublishingCredentials {
    param([string] $AppName)
    foreach ($policy in $BasicAuthPolicies) {
        $policyArguments = @('--resource-group', $ResourceGroup, '--namespace', 'Microsoft.Web',
            '--resource-type', 'basicPublishingCredentialsPolicies', '--parent', "sites/$AppName", '--name', $policy)
        $allowed = Invoke-AzRead "the $policy basic publishing credentials policy of $AppName" (
            @('resource', 'show') + $policyArguments + @('--query', 'properties.allow', '--output', 'tsv'))
        if ($allowed -eq $BasicAuthDisabled) { continue }
        Invoke-AzChange "Disable $policy basic publishing credentials on $AppName" (
            @('resource', 'update') + $policyArguments + @('--set', "properties.allow=$BasicAuthDisabled"))
    }
}

# ---------------------------------------------------------------------------------------------
# SQL firewall
# ---------------------------------------------------------------------------------------------

# Adds the firewall rule that lets Azure services reach the SQL server, if it is missing.
function Add-AzureServicesFirewallRule {
    $ruleNames = @(Invoke-AzRead "the firewall rules of SQL server $SqlServerName" @(
        'sql', 'server', 'firewall-rule', 'list', '--resource-group', $ResourceGroup,
        '--server', $SqlServerName, '--query', '[].name', '--output', 'tsv'))
    if ($ruleNames -contains $FirewallRuleName) { return }
    Invoke-AzChange "Allow Azure services through the firewall of SQL server $SqlServerName" @(
        'sql', 'server', 'firewall-rule', 'create', '--resource-group', $ResourceGroup,
        '--server', $SqlServerName, '--name', $FirewallRuleName,
        '--start-ip-address', $AzureServicesAddress, '--end-ip-address', $AzureServicesAddress)
}

# ---------------------------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------------------------

# Prints what changed (or would change) and the next step, without any secret value.
function Write-Summary {
    Write-Host ''
    $verb = if ($WhatIf) { 'Would change' } else { 'Changed' }
    if ($script:Changes.Count -eq 0) { Write-Host 'Nothing to change: the setup is already complete.' }
    else {
        Write-Host "${verb}:"
        foreach ($change in $script:Changes) { Write-Host "  - $change" }
    }
    Write-Host ''
    Write-Host "Next step: as the Entra admin of SQL server $SqlServerName, run $SqlScriptPath"
    Write-Host "on database $DatabaseName (for example in the portal query editor) for the API user;"
    Write-Host 'run it again after #102 for the deploy user. See infra/README.md.'
}

# ---------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------
$subscriptionName = Get-SignedInSubscription
Confirm-Run $subscriptionName
Enable-ApiManagedIdentity
Set-ApiAppSettings
Set-WebAppApiBaseUrl
foreach ($appName in @($ApiAppName, $WebAppName)) {
    Disable-Ftp $appName
    Disable-BasicPublishingCredentials $appName
    Set-HealthCheckPath $appName
}
Add-AzureServicesFirewallRule
Write-Summary
