# Hugo server image.
# Build from the repository root:
#   docker build -f src/Hugo.Server/Dockerfile -t jchristn77/hugo-server:<tag> .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/ ./src/
COPY README.md LICENSE.md ./
COPY assets/icon.png ./assets/icon.png
RUN dotnet publish src/Hugo.Server/Hugo.Server.csproj -c Release -f net10.0 -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
ARG HUGO_VERSION=0.1.0-alpha
LABEL org.opencontainers.image.title="hugo-server" \
      org.opencontainers.image.version="${HUGO_VERSION}" \
      org.opencontainers.image.source="https://github.com/jchristn/AgentSmith" \
      org.opencontainers.image.licenses="MIT"
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --gid 10001 hugo \
 && useradd --uid 10001 --gid 10001 --create-home --home-dir /home/hugo hugo \
 && mkdir -p /app /app/data /app/config /app/plugins \
 && chown -R 10001:10001 /app
WORKDIR /app
COPY --from=build --chown=10001:10001 /out/ /app/
USER 10001
ENV HUGO_SETTINGS_FILE=/app/config/hugo.json \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8800
ENTRYPOINT ["dotnet", "Hugo.Server.dll"]
