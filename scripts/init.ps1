# Run from the repository root:  ./scripts/init.ps1
# Creates the solution, wires projects together and installs NuGet packages
# (latest stable versions compatible with the installed .NET SDK).
$ErrorActionPreference = "Stop"

if (-not (Test-Path Lighthouse.sln)) { dotnet new sln -n Lighthouse --format sln }

# Tests project is generated from the official template so that xUnit versions are always current.
if (-not (Test-Path tests/Lighthouse.Core.Tests/Lighthouse.Core.Tests.csproj)) {
    dotnet new xunit -n Lighthouse.Core.Tests -o tests/Lighthouse.Core.Tests
    Remove-Item tests/Lighthouse.Core.Tests/UnitTest1.cs -ErrorAction SilentlyContinue
    dotnet add tests/Lighthouse.Core.Tests reference src/Lighthouse.Core
}

dotnet sln Lighthouse.sln add src/Lighthouse.Shared src/Lighthouse.Core src/Lighthouse.Api tests/Lighthouse.Core.Tests

$api = "src/Lighthouse.Api"
dotnet add $api package Microsoft.EntityFrameworkCore.SqlServer
dotnet add $api package Microsoft.EntityFrameworkCore.Design
dotnet add $api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add $api package Microsoft.AspNetCore.OpenApi
dotnet add $api package Scalar.AspNetCore
dotnet add $api package BCrypt.Net-Next
dotnet add $api package FluentValidation.DependencyInjectionExtensions
dotnet add $api package Serilog.AspNetCore

dotnet tool update --global dotnet-ef 2>$null
if ($LASTEXITCODE -ne 0) { dotnet tool install --global dotnet-ef }

Write-Host "`nDone. Next: copy .env.example to .env, 'docker compose up -d db', then create the first migration (see README.md)."
