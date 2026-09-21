# Build context — корінь репозиторію:  docker build -f src/Lighthouse.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Lighthouse.Shared/Lighthouse.Shared.csproj src/Lighthouse.Shared/
COPY src/Lighthouse.Core/Lighthouse.Core.csproj src/Lighthouse.Core/
COPY src/Lighthouse.Api/Lighthouse.Api.csproj src/Lighthouse.Api/
RUN dotnet restore src/Lighthouse.Api/Lighthouse.Api.csproj

COPY src/ src/
RUN dotnet publish src/Lighthouse.Api/Lighthouse.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lighthouse.Api.dll"]
