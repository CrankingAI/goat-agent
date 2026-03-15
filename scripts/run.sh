#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

RUN_STARTED_AT="$(date '+%Y-%m-%dT%H:%M:%S%z')"
echo "Run started at: ${RUN_STARTED_AT}" >&2

usage() {
    echo "Usage: $(basename "$0") <request.json> [output.json]" >&2
    echo "" >&2
    echo "Runs the GoatCheck CLI with OTLP tracing auto-configured from" >&2
    echo "the Aspire AppHost launchSettings.json." >&2
    echo "" >&2
    echo "Arguments:" >&2
    echo "  request.json   Input request file (required)" >&2
    echo "  output.json    Write JSON result to file (default: stdout)" >&2
    echo "" >&2
    echo "Examples:" >&2
    echo "  $(basename "$0") request.json" >&2
    echo "  $(basename "$0") request.json result.json" >&2
    exit 1
}

REQUEST_FILE=""
OUTPUT_FILE=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        -h|--help)
            usage
            ;;
        *)
            if [[ -z "${REQUEST_FILE}" ]]; then
                REQUEST_FILE="$1"
            elif [[ -z "${OUTPUT_FILE}" ]]; then
                OUTPUT_FILE="$1"
            else
                echo "Error: unexpected argument '$1'" >&2
                usage
            fi
            shift
            ;;
    esac
done

if [[ -z "${REQUEST_FILE}" ]]; then
    echo "Error: no request JSON file specified" >&2
    usage
fi

if [[ ! -f "${REQUEST_FILE}" ]]; then
    echo "Error: file not found: ${REQUEST_FILE}" >&2
    exit 1
fi

LAUNCH_SETTINGS="${REPO_ROOT}/src/GoatCheck.AppHost/Properties/launchSettings.json"

if [[ ! -f "${LAUNCH_SETTINGS}" ]]; then
    echo "Warning: launchSettings.json not found at ${LAUNCH_SETTINGS}" >&2
else
    read -r HTTP_OTLP HTTPS_OTLP HTTP_DASH HTTPS_DASH < <(python3 -c "
import json
with open('${LAUNCH_SETTINGS}') as f:
    profiles = json.load(f)['profiles']
def get(p, key):
    return profiles.get(p, {}).get('environmentVariables', {}).get(key, '(n/a)')
def app(p):
    return profiles.get(p, {}).get('applicationUrl', '(n/a)').split(';')[0]
print(get('http','ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL'), get('https','ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL'), app('http'), app('https'))
")

    echo "========================================" >&2
    echo " Aspire OTLP Endpoints (from launchSettings.json):" >&2
    echo "   http  → OTLP: ${HTTP_OTLP}  Dashboard: ${HTTP_DASH}" >&2
    echo "   https → OTLP: ${HTTPS_OTLP}  Dashboard: ${HTTPS_DASH}" >&2
    echo "========================================" >&2

    OTLP_URL="${HTTP_OTLP}"
    if [[ -n "${OTLP_URL}" && "${OTLP_URL}" != "(n/a)" ]]; then
        export OTEL_EXPORTER_OTLP_ENDPOINT="${OTLP_URL}"
        export OTEL_EXPORTER_OTLP_PROTOCOL="grpc"
        export OTEL_SERVICE_NAME="goatcheck-console"
        echo "Using OTLP endpoint (http, grpc+h2c): ${OTLP_URL}" >&2
    fi
fi

cd "${REPO_ROOT}"

echo "Running: dotnet run --project src/GoatCheck.Console --no-launch-profile -- ${REQUEST_FILE}" >&2

SECONDS=0
if [[ -n "${OUTPUT_FILE}" ]]; then
    echo "Output file: ${OUTPUT_FILE}" >&2
    DOTNET_ENVIRONMENT="Development" \
    OTEL_EXPORTER_OTLP_ENDPOINT="${OTEL_EXPORTER_OTLP_ENDPOINT:-}" \
    OTEL_EXPORTER_OTLP_PROTOCOL="${OTEL_EXPORTER_OTLP_PROTOCOL:-}" \
    OTEL_SERVICE_NAME="${OTEL_SERVICE_NAME:-goatcheck-console}" \
        dotnet run --project src/GoatCheck.Console --no-launch-profile -- "${REQUEST_FILE}" > "${OUTPUT_FILE}"
    echo "Done. Result written to ${OUTPUT_FILE}" >&2
else
    DOTNET_ENVIRONMENT="Development" \
    OTEL_EXPORTER_OTLP_ENDPOINT="${OTEL_EXPORTER_OTLP_ENDPOINT:-}" \
    OTEL_EXPORTER_OTLP_PROTOCOL="${OTEL_EXPORTER_OTLP_PROTOCOL:-}" \
    OTEL_SERVICE_NAME="${OTEL_SERVICE_NAME:-goatcheck-console}" \
        dotnet run --project src/GoatCheck.Console --no-launch-profile -- "${REQUEST_FILE}"
fi

echo "ran for ${SECONDS} seconds" >&2
