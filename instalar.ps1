# Atalho de desenvolvimento: instala o programa deste computador (o .exe e o proprio instalador).
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $raiz 'ControleUniversal.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path $raiz 'saida\ControleUniversal.exe' }
if (-not (Test-Path $exe)) { & (Join-Path $raiz 'construir.ps1'); $exe = Join-Path $raiz 'saida\ControleUniversal.exe' }
Start-Process $exe -ArgumentList '--instalar' -Wait
