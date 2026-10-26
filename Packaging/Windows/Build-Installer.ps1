[CmdletBinding()]
param(
    [string]$PlayerDirectory = (Join-Path $PSScriptRoot '..\..\Builds\Windows'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\Builds'),
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$playerRoot = (Resolve-Path -LiteralPath $PlayerDirectory).Path
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
foreach ($required in @('ScenarioBuilder.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe',
    'ScenarioBuilder_Data', 'MonoBleedingEdge', 'D3D12', 'dstorage.dll', 'dstoragecore.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $playerRoot $required))) {
        throw "Missing Windows player component: $required"
    }
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$playerPrefix = $playerRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($outputRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar).Equals(
        $playerRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar), [System.StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($playerPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be outside PlayerDirectory to avoid copying the payload into itself.'
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $outputRoot).Path
$payloadRoot = Join-Path $outputRoot ('InstallerPayload-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payloadRoot | Out-Null
Get-ChildItem -LiteralPath $playerRoot -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/][^\\/]*DoNotShip[^\\/]*(?:[\\/]|$)' } |
    ForEach-Object {
        $relativePath = $_.FullName.Substring($playerRoot.Length + 1)
        $targetPath = Join-Path $payloadRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $targetPath
    }
New-Item -ItemType Directory -Path (Join-Path $payloadRoot 'Licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ScenarioBuilder\Assets\TextMesh Pro\Fonts\LiberationSans - OFL.txt') `
    -Destination (Join-Path $payloadRoot 'Licenses\LiberationSans-OFL.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'START_HERE.txt') -Destination (Join-Path $payloadRoot 'START_HERE.txt')

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $outputRoot 'ScenarioBuilder-Windows.zip'
$temporaryZip = Join-Path $outputRoot ('ScenarioBuilder-Windows-' + [guid]::NewGuid().ToString('N') + '.zip')
[System.IO.Compression.ZipFile]::CreateFromDirectory($payloadRoot, $temporaryZip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $temporaryZip -Destination $zipPath -Force

& $compiler /Q "/DPayloadDir=$payloadRoot" "/DOutputDir=$outputRoot" "/DMyAppVersion=$Version" `
    (Join-Path $PSScriptRoot 'ScenarioBuilder.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE" }

$setupPath = Join-Path $outputRoot 'ScenarioBuilder-Setup.exe'
$checksums = foreach ($artifact in @($setupPath, $zipPath)) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant(),
        (Split-Path -Leaf $artifact)
}
$checksumPath = Join-Path $outputRoot 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksumPath, [string[]]$checksums, [System.Text.Encoding]::ASCII)
[pscustomobject]@{
    Version = $Version
    Installer = $setupPath
    PortableZip = $zipPath
    Checksums = $checksumPath
    Payload = $payloadRoot
    PayloadFiles = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File).Count
}
