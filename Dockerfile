# Render + Neon — Dockerfile for .NET 10 (free tier)
# Multi-stage: build with SDK, run with ASP.NET runtime
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY WebApplication1.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Render sets PORT dynamically (default 10000). ASP.NET will bind via Program.cs UseUrls.
# Expose for local testing
EXPOSE 5000
EXPOSE 10000

# Polling watcher for Render inotify limit already handled in Program.cs
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check (optional)
# HEALTHCHECK CMD curl --fail http://localhost:$PORT/ || exit 1

ENTRYPOINT ["dotnet", "WebApplication1.dll"]
