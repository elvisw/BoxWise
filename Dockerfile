FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer (cacheable)
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BoxWise.Shared/BoxWise.Shared.csproj src/BoxWise.Shared/
COPY src/BoxWise.Client/BoxWise.Client.csproj src/BoxWise.Client/
COPY src/BoxWise.Server/BoxWise.Server.csproj src/BoxWise.Server/
RUN dotnet restore src/BoxWise.Server

# Build layer
COPY . .
RUN dotnet publish src/BoxWise.Server -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "BoxWise.Server.dll"]
