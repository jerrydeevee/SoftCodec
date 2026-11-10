param([switch]$Installer)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 dotnet restore SoftCodec.sln
 if ($LASTEXITCODE) { throw 'Restore mislukt.' }
 dotnet test tests/SoftCodec.Tests/SoftCodec.Tests.csproj -c Release --no-restore
 if ($LASTEXITCODE) { throw 'Tests mislukt.' }
 dotnet publish src/SoftCodec.App/SoftCodec.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish -p:PublishSingleFile=false
 if ($LASTEXITCODE) { throw 'Publiceren mislukt.' }
 Copy-Item THIRD-PARTY-NOTICES.md artifacts/publish
 Copy-Item README.md artifacts/publish
 Copy-Item licenses artifacts/publish/licenses -Recurse -Force
 Copy-Item docs artifacts/publish/docs -Recurse -Force
 if ($Installer) { & ISCC.exe installer/SoftCodec.iss; if ($LASTEXITCODE) { throw 'Installerbuild mislukt.' } }
} finally { Pop-Location }
