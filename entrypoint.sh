#!/usr/bin/env bash
# Start Readarr and auto-configure download clients on first run.
set -e

mkdir -p /downloads /config

# Start Readarr in background
dotnet Readarr.dll -nobrowser -data=/config &
READARR_PID=$!

# Wait for Readarr API to be ready (up to 60s)
echo "[init] Waiting for Readarr to start..."
for i in $(seq 1 60); do
    if curl -sf http://localhost:8787/api/v1/system/status -o /dev/null 2>/dev/null; then
        echo "[init] Readarr is up."
        break
    fi
    sleep 1
done

# Read API key from config.xml
API_KEY=$(grep -oP '(?<=<ApiKey>)[^<]+' /config/config.xml 2>/dev/null || true)
if [ -z "$API_KEY" ]; then
    echo "[init] Could not read API key — skipping client setup."
    wait $READARR_PID
    exit $?
fi

# Helper
api_get()  { curl -sf -H "X-Api-Key: $API_KEY" "http://localhost:8787/api/v1$1"; }
api_post() { curl -sf -X POST -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" -d "$2" "http://localhost:8787/api/v1$1"; }

# Create download client if not already present
create_client() {
    local name="$1"
    existing=$(api_get /downloadclient | python3 -c "import json,sys; print(any(c['name']=='$name' for c in json.load(sys.stdin)))" 2>/dev/null || echo "False")
    if [ "$existing" = "True" ]; then
        echo "[init] Download client '$name' already exists — skipping."
        return
    fi
    api_post /downloadclient "{
        \"enable\": true,
        \"protocol\": \"unknown\",
        \"priority\": 1,
        \"removeCompletedDownloads\": true,
        \"removeFailedDownloads\": true,
        \"name\": \"$name\",
        \"fields\": [{\"name\": \"downloadFolder\", \"value\": \"/downloads\"}],
        \"implementation\": \"HttpBlackhole\",
        \"configContract\": \"HttpBlackholeSettings\",
        \"tags\": []
    }" > /dev/null
    echo "[init] Created download client '$name' -> /downloads"
}

create_client "Download z-Library"
create_client "Download Annas Archive"

echo "[init] Setup complete."

# Keep container alive with Readarr
wait $READARR_PID
