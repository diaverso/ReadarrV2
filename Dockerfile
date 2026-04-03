# Stage 1: Build frontend
FROM node:20-slim AS frontend
WORKDIR /frontend

COPY package.json yarn.lock tsconfig.json ./
RUN yarn install --frozen-lockfile --network-timeout 120000

COPY frontend/ ./frontend/
COPY Logo/ ./Logo/
RUN yarn build

# Stage 2: Build backend
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

# Readarr.Mono is loaded dynamically on Linux and is not a direct reference,
# so it is not included in the publish output — publish it separately and copy
# all output files (including native libs like libMonoPosixHelper.so).
RUN dotnet publish src/NzbDrone.Mono/Readarr.Mono.csproj \
    -c Debug \
    -f net10.0 \
    -p:RunAnalyzers=false \
    -p:EnforceCodeStyleInBuild=false \
    --self-contained false \
    -o /app/mono-out \
    && cp -n /app/mono-out/*.dll /app/out/ \
    && find /app/mono-out -name "*.so" -exec cp -n {} /app/out/ \;

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
    libsqlite3-0 \
    mediainfo \
    curl \
    python3 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/out ./
# In Debug builds, Readarr resolves UiFolder as "../UI" relative to /app → /UI/
COPY --from=frontend /frontend/_output/UI/ /UI/
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 8787
VOLUME ["/config", "/books"]

ENV READARR__LOG__DBENABLED=false

ENTRYPOINT ["/entrypoint.sh"]
