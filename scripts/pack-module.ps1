param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory,
    [string]$NyautomatorReferencePath,
    [string]$UseNyautomatorProjectReferences = "true"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleManifestPath = Join-Path $repoRoot "module/module.json"
$moduleProject = Join-Path $repoRoot "src/Nyautomator.Module.VTubeStudio/Nyautomator.Module.VTubeStudio.csproj"
$publishRoot = Join-Path $repoRoot "artifacts/publish/$Configuration"
$packageRoot = Join-Path $repoRoot "artifacts/package/vtubestudio"

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot "artifacts/modules"
}

if (-not (Test-Path -LiteralPath $moduleManifestPath)) {
    throw "Module manifest not found: $moduleManifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $moduleManifestPath | ConvertFrom-Json
$version = if ($manifest.version) { [string]$manifest.version } else { "0.0.0" }
$zipPath = Join-Path $OutputDirectory "vtubestudio-$version.zip"

$allowedNyautomatorDlls = @(
    "Nyautomator.Module.VTubeStudio.dll",
    "Nyautomator.Automation.VTubeStudio.dll",
    "Nyautomator.Runtime.Integrations.VTubeStudio.dll",
    "Nyautomator.VTubeStudio.dll"
)

$allowedThirdPartyDlls = @(
    "Microsoft.Extensions.DependencyInjection.Abstractions.dll"
)

New-Item -ItemType Directory -Force -Path $publishRoot, $packageRoot, $OutputDirectory | Out-Null
Remove-Item -Recurse -Force -LiteralPath $publishRoot -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force -LiteralPath $packageRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishRoot, $packageRoot | Out-Null

$publishArgs = @(
    "publish",
    $moduleProject,
    "--configuration",
    $Configuration,
    "--output",
    $publishRoot,
    "-p:UseNyautomatorProjectReferences=$UseNyautomatorProjectReferences"
)

if ($NyautomatorReferencePath) {
    $publishArgs += "-p:NyautomatorReferencePath=$NyautomatorReferencePath"
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $moduleManifestPath -Destination (Join-Path $packageRoot "module.json")
Copy-Item -Recurse -LiteralPath (Join-Path $repoRoot "module/assets") -Destination (Join-Path $packageRoot "assets")

foreach ($file in Get-ChildItem -File -LiteralPath $publishRoot) {
    $name = $file.Name
    $copy = $false

    if ($file.Extension -ieq ".dll") {
        if ($name.StartsWith("Nyautomator.", [StringComparison]::OrdinalIgnoreCase)) {
            $copy = $allowedNyautomatorDlls -contains $name
        } else {
            $copy = $allowedThirdPartyDlls -contains $name
        }
    } elseif ($name -ieq "Nyautomator.Module.VTubeStudio.deps.json") {
        $copy = $true
    }

    if ($copy) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $packageRoot $name)
    }
}

$forbidden = Get-ChildItem -Recurse -File -LiteralPath $packageRoot -Filter "Nyautomator*.dll" |
    Where-Object { $allowedNyautomatorDlls -notcontains $_.Name }

if ($forbidden) {
    $names = ($forbidden | Select-Object -ExpandProperty Name) -join ", "
    throw "Package contains host-provided Nyautomator DLLs: $names"
}

$unexpected = Get-ChildItem -Recurse -File -LiteralPath $packageRoot -Filter "*.dll" |
    Where-Object { ($allowedNyautomatorDlls + $allowedThirdPartyDlls) -notcontains $_.Name }

if ($unexpected) {
    $names = ($unexpected | Select-Object -ExpandProperty Name) -join ", "
    throw "Package contains unexpected DLLs: $names"
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -Force -LiteralPath $zipPath
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath
Write-Host "Packed VTube Studio module: $zipPath"
