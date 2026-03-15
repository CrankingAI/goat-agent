#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo "Formatting all source files..."
cd "${REPO_ROOT}"
dotnet format GoatCheck.slnx
echo "Done."
