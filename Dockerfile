# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, in its own layer: it only reruns when a project or package version changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/FiapX.Notification.Domain/FiapX.Notification.Domain.csproj src/FiapX.Notification.Domain/
COPY src/FiapX.Notification.Application/FiapX.Notification.Application.csproj src/FiapX.Notification.Application/
COPY src/FiapX.Notification.Infrastructure/FiapX.Notification.Infrastructure.csproj src/FiapX.Notification.Infrastructure/
COPY src/FiapX.Notification.Api/FiapX.Notification.Api.csproj src/FiapX.Notification.Api/
RUN dotnet restore src/FiapX.Notification.Api/FiapX.Notification.Api.csproj

COPY src/ src/
RUN dotnet publish src/FiapX.Notification.Api/FiapX.Notification.Api.csproj -c Release --no-restore -o /app

# ---- runtime: chiseled image, no shell, non-root by default ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    OTEL_SERVICE_NAME=notification-service
EXPOSE 8080
ENTRYPOINT ["dotnet", "FiapX.Notification.Api.dll"]
