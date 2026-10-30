<#
.SYNOPSIS
    Publica uma versão nova do Sharelink no GitHub.

.DESCRIPTION
    1. Confere pré-requisitos (branch main limpa, Inno Setup, GitHub CLI, chave de assinatura).
    2. Atualiza a versão no csproj e roda os testes.
    3. Gera o app (já com o .NET embutido) e o instalador.
    4. Assina o instalador com a chave privada deste PC (Gerenciador de Credenciais).
    5. Faz commit da versão, cria a tag vX.Y.Z, envia e cria a release com o instalador + .sig.

    Os apps instalados encontram a release sozinhos, baixam, conferem a assinatura e oferecem a atualização.

.EXAMPLE
    .\scripts\release.ps1 -Version 1.1.0 -Notes "Correções no editor"
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Check([string]$what) { if ($LASTEXITCODE -ne 0) { throw "$what falhou (código $LASTEXITCODE)." } }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versão inválida '$Version': use X.Y.Z (ex.: 1.2.0)." }
$tag = "v$Version"
$installer = Join-Path $root "artifacts\Sharelink-Setup-$Version.exe"

Step "Conferindo pré-requisitos"
if ((git rev-parse --abbrev-ref HEAD) -ne 'main') { throw "Publique a partir da branch main." }
if (git status --porcelain) { throw "Há alterações não commitadas. Faça commit (ou descarte) antes de publicar." }
if (git tag -l $tag) { throw "A tag $tag já existe." }
cmd /c "gh auth status >nul 2>&1"; Check "gh auth status (rode 'gh auth login')"
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup não encontrado. Instale com: winget install JRSoftware.InnoSetup" }

Step "Versão $Version no csproj"
$csproj = Join-Path $root "src\Sharelink\Sharelink.csproj"
$content = [IO.File]::ReadAllText($csproj)
$content = [Text.RegularExpressions.Regex]::Replace($content, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($csproj, $content, (New-Object Text.UTF8Encoding $false))

try {
    Step "Testes"
    dotnet test Sharelink.sln -c Release --nologo -v q; Check "dotnet test"

    Step "Gerando o app (com .NET embutido)"
    Remove-Item artifacts -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish src/Sharelink/Sharelink.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o artifacts/app --nologo -v q
    Check "dotnet publish"

    Step "Gerando o instalador"
    & $iscc /Qp "/DAppVersion=$Version" installer\Sharelink.iss; Check "Inno Setup"

    Step "Assinando o instalador"
    dotnet run --project tools/ReleaseTool -c Release -- sign $installer $Version; Check "assinatura"
}
catch {
    git checkout -- $csproj
    throw
}

Step "Commit, tag e envio"
git diff --quiet -- $csproj
if ($LASTEXITCODE -ne 0) {
    git add $csproj
    git commit -m "Versão $Version"; Check "git commit"
}
git tag -a $tag -m "Sharelink $Version"; Check "git tag"
git push origin main $tag; Check "git push"

Step "Criando a release no GitHub"
$assets = @($installer, "$installer.sig")
if ($Notes) {
    gh release create $tag @assets --title "Sharelink $Version" --notes $Notes
} else {
    gh release create $tag @assets --title "Sharelink $Version" --generate-notes
}
Check "gh release create"

Write-Host "`nPronto! Sharelink $Version publicado. Os apps instalados vão encontrar a atualização sozinhos." -ForegroundColor Green
