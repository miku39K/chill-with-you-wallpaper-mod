param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework compiler was not found.'
}

$folder = New-Item -ItemType Directory -Path $OutputDirectory -Force
$output = Join-Path $folder.FullName 'ChillWithYouWallpaper.exe'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$sources = 'Program.cs', 'DesktopWallpaper.cs', 'DesktopInteraction.cs', 'Native.cs' | ForEach-Object {
    Join-Path $PSScriptRoot $_
}
$options = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    "/win32manifest:$manifest", "/out:$output", '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll', '/reference:Accessibility.dll')
& $compiler @options @sources
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}
Get-Item -LiteralPath $output
