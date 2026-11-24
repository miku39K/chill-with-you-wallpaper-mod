param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [Parameter(Mandatory = $true)][string]$BepInExRoot,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'mod-build' }
$folder = New-Item -ItemType Directory -Path $OutputDirectory -Force
$managed = Join-Path $GameRoot 'Chill With You_Data\Managed'
$bep = Join-Path $BepInExRoot 'BepInEx\core\BepInEx.dll'
$references = @($bep) + @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll',
    'UnityEngine.CoreModule.dll', 'UnityEngine.dll' | ForEach-Object { Join-Path $managed $_ })
foreach ($reference in $references) {
    if (!(Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Missing reference: $reference" }
}

& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $folder.FullName
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$plugin = Join-Path $folder.FullName 'ChillWithYouWallpaper.Plugin.dll'
$source = Join-Path $PSScriptRoot 'Mod\WallpaperPlugin.cs'
$options = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/optimize+',
    '/codepage:65001', "/out:$plugin")
$referenceOptions = $references | ForEach-Object { "/reference:$_" }
& $compiler @options @referenceOptions $source
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed with exit code $LASTEXITCODE." }
Get-Item -LiteralPath $plugin
