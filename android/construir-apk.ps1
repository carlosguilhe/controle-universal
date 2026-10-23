# Gera android\saida\ControleUniversal.apk sem Gradle (aapt2 + javac + d8 + zipalign + apksigner).
# Uso: construir-apk.ps1 [-Instalar]   (-Instalar instala no celular ligado por adb)
param([switch]$Instalar)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
# Android SDK: ANDROID_HOME, ANDROID_SDK_ROOT ou o local padrao do Android Studio
$sdk = $env:ANDROID_HOME
if (-not $sdk) { $sdk = $env:ANDROID_SDK_ROOT }
if (-not $sdk) { $sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
$bt  = Join-Path $sdk 'build-tools\34.0.0'
$jar = Join-Path $sdk 'platforms\android-34\android.jar'
# JDK 17: JAVA_HOME, ou o primeiro encontrado nos locais comuns
$jdk = $env:JAVA_HOME
if (-not $jdk -or -not (Test-Path (Join-Path $jdk 'bin\javac.exe'))) {
    $jdk = @('C:\Program Files\Microsoft\jdk-17*', 'C:\Program Files\Eclipse Adoptium\jdk-17*', 'C:\Program Files\Java\jdk-17*', 'C:\Program Files\Android\Android Studio\jbr') |
        ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } | Where-Object { Test-Path (Join-Path $_.FullName 'bin\javac.exe') } |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $jdk) { throw 'JDK 17 nao encontrado. Instale o JDK 17 ou defina JAVA_HOME.' }
$javac = Join-Path $jdk 'bin\javac.exe'; $keytool = Join-Path $jdk 'bin\keytool.exe'
foreach ($p in $bt, $jar, $javac) { if (-not (Test-Path $p)) { throw "Nao encontrei: $p" } }

$obj = Join-Path $raiz 'obj'; $saida = Join-Path $raiz 'saida'
Remove-Item $obj -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$obj\res", "$obj\classes", "$obj\dex", $saida | Out-Null

# 1) recursos
& "$bt\aapt2.exe" compile --dir "$raiz\res" -o "$obj\res" ; if ($LASTEXITCODE) { throw 'aapt2 compile falhou' }
$base = "$obj\base.apk"
& "$bt\aapt2.exe" link -o $base -I $jar --manifest "$raiz\AndroidManifest.xml" -A "$raiz\assets" `
    --min-sdk-version 24 --target-sdk-version 34 (Get-ChildItem "$obj\res\*.flat").FullName
if ($LASTEXITCODE) { throw 'aapt2 link falhou' }

# 2) Java -> classes -> dex
$fontes = (Get-ChildItem "$raiz\src" -Recurse -Filter *.java).FullName
& $javac --release 8 -encoding UTF-8 -Xlint:-options -cp $jar -d "$obj\classes" $fontes ; if ($LASTEXITCODE) { throw 'javac falhou' }
$classes = (Get-ChildItem "$obj\classes" -Recurse -Filter *.class).FullName
& "$bt\d8.bat" --release --min-api 24 --lib $jar --output "$obj\dex" $classes ; if ($LASTEXITCODE) { throw 'd8 falhou' }

# 3) junta classes.dex no APK (sem recomprimir o resto)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($base, 'Update')
[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$obj\dex\classes.dex", 'classes.dex')
$zip.Dispose()

# 4) alinha e assina (chave local; guarde chave.jks para poder atualizar o app depois)
$chave = Join-Path $raiz 'chave.jks'
if (-not (Test-Path $chave)) {
    & $keytool -genkeypair -keystore $chave -storepass controleuniversal -keypass controleuniversal `
        -alias controle -keyalg RSA -keysize 2048 -validity 9125 -dname 'CN=Controle Universal' | Out-Null
}
$alinhado = "$obj\alinhado.apk"
& "$bt\zipalign.exe" -f -p 4 $base $alinhado ; if ($LASTEXITCODE) { throw 'zipalign falhou' }
$apk = Join-Path $saida 'ControleUniversal.apk'
& "$bt\apksigner.bat" sign --ks $chave --ks-pass pass:controleuniversal --key-pass pass:controleuniversal `
    --out $apk $alinhado ; if ($LASTEXITCODE) { throw 'apksigner falhou' }
& "$bt\apksigner.bat" verify $apk ; if ($LASTEXITCODE) { throw 'assinatura invalida' }
Write-Host ("OK: {0} ({1:N0} KB)" -f $apk, ((Get-Item $apk).Length / 1KB))

if ($Instalar) {
    $adb = Join-Path $sdk 'platform-tools\adb.exe'
    & $adb install -r $apk
}
