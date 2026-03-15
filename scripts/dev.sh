#!/usr/bin/env bash
set -euo pipefail

# Open dashboard after a short delay
(sleep 11 && open http://localhost:16888) &

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

LAUNCH_PROFILE="${1:-http}"
LAUNCH_SETTINGS="${REPO_ROOT}/src/GoatCheck.AppHost/Properties/launchSettings.json"

read -r OTLP_URL DASHBOARD_URL < <(python3 -c "
import json, sys
with open('${LAUNCH_SETTINGS}') as f:
    profiles = json.load(f)['profiles']
profile = profiles.get('${LAUNCH_PROFILE}')
if not profile:
    print(f'Error: launch profile \"${LAUNCH_PROFILE}\" not found. Available: {list(profiles.keys())}', file=sys.stderr)
    sys.exit(1)
otlp = profile.get('environmentVariables', {}).get('ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL', '(not set)')
app_url = profile.get('applicationUrl', '(not set)').split(';')[0]
print(otlp, app_url)
")

APPINSIGHTS_CS=""
for cfg in \
    "${REPO_ROOT}/src/GoatCheck.AppHost/appsettings.Development.json" \
    "${REPO_ROOT}/src/GoatCheck.Console/appsettings.Development.json" \
    "${REPO_ROOT}/src/GoatCheck.Api/appsettings.Development.json"; do
    if [[ -f "${cfg}" ]]; then
        APPINSIGHTS_CS=$(python3 -c "
import json, pathlib
data = json.loads(pathlib.Path('${cfg}').read_text())
print(data.get('APPLICATIONINSIGHTS_CONNECTION_STRING', ''))
" 2>/dev/null || true)
        [[ -n "${APPINSIGHTS_CS}" ]] && break
    fi
done
[[ -n "${APPINSIGHTS_CS}" ]] && export APPLICATIONINSIGHTS_CONNECTION_STRING="${APPINSIGHTS_CS}"

echo "Getting started at $(date)..."
echo "========================================"
echo " Aspire Dashboard (UI):  ${DASHBOARD_URL}"
echo " Dashboard login URL:    see AppHost output (token-bearing URL printed at startup)"
echo " OTLP Endpoint:          ${OTLP_URL}"
echo " Launch Profile:         ${LAUNCH_PROFILE}"
[[ -n "${APPINSIGHTS_CS}" ]] && echo " App Insights:           configured" || echo " App Insights:           not configured"
echo ""
echo " To send traces from CLI:"
echo "   OTEL_EXPORTER_OTLP_ENDPOINT=${OTLP_URL} dotnet run --project src/GoatCheck.Console -- <request.json>"
echo "   — or use: ./scripts/run.sh <request.json>"
echo "========================================"
echo ""

cd "${REPO_ROOT}"
SECONDS=0
dotnet run --project src/GoatCheck.AppHost/GoatCheck.AppHost.csproj --launch-profile "${LAUNCH_PROFILE}"
echo "ran for ${SECONDS} seconds"
