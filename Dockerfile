# ==============================
# Build stage
# ==============================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore first (better layer caching)
COPY LocationIngestor/LocationIngestor.csproj LocationIngestor/
RUN dotnet restore LocationIngestor/LocationIngestor.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish LocationIngestor/LocationIngestor.csproj -c Release -o /app/out

# ==============================
# Runtime stage
# ==============================
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out ./

# Render provides PORT at runtime; default to 8080 locally
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet","LocationIngestor.dll"]
