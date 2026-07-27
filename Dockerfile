# ============================================================================
# Users Service — Docker Image
# ============================================================================
# Multi-stage build optimized for size, security, and layer caching.
#
# Build:
#   docker build -t users-service:latest .
#
# Run:
#   docker run -p 7203:7203 -e ConnectionStrings__UsersDb="..." users-service:latest
# ============================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/UsersService/UsersService.csproj ./src/UsersService/
RUN dotnet restore src/UsersService/UsersService.csproj

COPY src/ ./
RUN dotnet publish src/UsersService/UsersService.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --output /app \
    /p:DebugType=None \
    /p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN adduser --disabled-password --gecos "" appuser && \
    chown -R appuser:appuser /app

COPY --from=build /app ./

USER appuser
EXPOSE 7203

HEALTHCHECK --interval=15s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:7201/api/health/live || exit 1

ENTRYPOINT ["dotnet", "Platform.UsersService.dll"]
