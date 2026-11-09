param(
    [Parameter(Mandatory)][string]$Game
)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $Game 'Chill With You_Data\Managed'
$loader = Join-Path $PSScriptRoot 'loader\BepInEx\core'
$sdk = Get-ChildItem -Directory (Join-Path (Split-Path (Get-Command dotnet).Source) 'sdk') |
    Where-Object { Test-Path (Join-Path $_.FullName 'Roslyn\bincore\csc.dll') } |
    Sort-Object Name -Descending | Select-Object -First 1
$csc = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$references = @('mscorlib', 'netstandard', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'UnityEngine', 'UnityEngine.CoreModule',
    'UnityEngine.UIModule', 'UnityEngine.InputLegacyModule', 'UnityEngine.UI', 'UnityEngine.AnimationModule', 'UnityEngine.AudioModule', 'UnityEngine.TextRenderingModule',
    'Unity.TextMeshPro', 'Cinemachine') | ForEach-Object { '/reference:' + (Join-Path $managed "$_.dll") }
$references += '/reference:' + (Join-Path $loader 'BepInEx.dll')
$outDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$source = Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.cs' | ForEach-Object FullName
& dotnet $csc /nologo /noconfig /nostdlib /target:library /optimize+ /warnaserror+ "/out:$outDir\ChillPortrait.dll" @references @source
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed ($LASTEXITCODE)." }
