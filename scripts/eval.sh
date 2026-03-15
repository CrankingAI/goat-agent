#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

LAUNCH_SETTINGS="${REPO_ROOT}/src/GoatCheck.AppHost/Properties/launchSettings.json"

if [[ -f "${LAUNCH_SETTINGS}" ]]; then
    OTLP_URL="$(python3 -c "
import json
with open('${LAUNCH_SETTINGS}') as f:
    profiles = json.load(f)['profiles']
print(profiles.get('http', {}).get('environmentVariables', {}).get('ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL', ''))
")"
    if [[ -n "${OTLP_URL}" && "${OTLP_URL}" != "(n/a)" ]]; then
        export OTEL_EXPORTER_OTLP_ENDPOINT="${OTLP_URL}"
        export OTEL_EXPORTER_OTLP_PROTOCOL="grpc"
        echo "OTLP endpoint: ${OTLP_URL}" >&2
    fi
fi

echo "Running GoatCheck Evals..."
cd "${REPO_ROOT}"
SECONDS=0
dotnet test src/GoatCheck.Evals/GoatCheck.Evals.csproj \
  --logger "console;verbosity=normal" \
  "$@"
echo "ran for ${SECONDS} seconds" >&2
