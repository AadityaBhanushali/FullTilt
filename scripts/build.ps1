. "$PSScriptRoot/env.ps1"
dotnet restore FullTilt.slnx --configfile NuGet.Config -m:1 -nr:false
Assert-NativeSuccess 'Restore'
dotnet build FullTilt.slnx -c Release --no-restore -m:1 -nr:false
Assert-NativeSuccess 'Release build'
dotnet run --project tests/FullTilt.Tests/FullTilt.Tests.csproj -c Release --no-build -- artifacts/test-results.xml
Assert-NativeSuccess 'Automated tests'
& "$PSScriptRoot/test-release.ps1"
