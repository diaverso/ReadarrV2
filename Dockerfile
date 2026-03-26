# Stage 1: Build backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

COPY src/ ./src/
COPY Logo/ ./Logo/

RUN dotnet publish src/NzbDrone.Console/Readarr.Console.csproj \
    -c Debug \
    -f net10.0 \
    -p:RunAnalyzers=false \
    -p:EnforceCodeStyleInBuild=false \
    --self-contained false \
    -o /app/out

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
    libsqlite3-0 \
    mediainfo \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/out ./
COPY UI/ ./UI/

EXPOSE 8787
VOLUME ["/config", "/books"]

ENV READARR__LOG__DBENABLED=false

ENTRYPOINT ["dotnet", "Readarr.dll", "-nobrowser", "-data=/config"]
