# Compila o ControleUniversal.exe com o compilador que ja vem no Windows.
# Se android\saida\ControleUniversal.apk existir, ele vai embutido no .exe (o PC entrega o app pelo endereco /app.apk).
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "Compilador do .NET Framework nao encontrado em $csc" }

$saida = Join-Path $raiz 'saida'
New-Item -ItemType Directory -Force $saida | Out-Null
$exe = Join-Path $saida 'ControleUniversal.exe'

$recursos = @(
  "/resource:$raiz\fonte\web\app.html,app.html",
  "/resource:$raiz\fonte\web\painel.html,painel.html",
  "/resource:$raiz\fonte\web\qrcode.js,qrcode.js"
)
$apk = Join-Path $raiz 'android\saida\ControleUniversal.apk'
if (Test-Path $apk) { $recursos += "/resource:$apk,app.apk"; Write-Host "APK embutido: $apk" }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ "/out:$exe" "/win32icon:$raiz\fonte\icone.ico" `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll `
  $recursos `
  "$raiz\fonte\ControleUniversal.cs" "$raiz\fonte\Extras.cs"
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao" }
Write-Host "OK: $exe"
