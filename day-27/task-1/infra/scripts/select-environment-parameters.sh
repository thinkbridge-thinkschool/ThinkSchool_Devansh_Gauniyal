#!/bin/sh
# azd preprovision hook: selects the existing Day 23 .bicepparam file matching
# the active azd environment ($AZURE_ENV_NAME, set by azd itself) and copies
# it to infra/main.bicepparam - the filename azd's Bicep provider looks for by
# convention alongside a module named "main" (confirmed empirically: without
# this file, `azd provision` falls back to prompting for every individual
# Bicep parameter instead, one "infra.parameters.<name>" azd-env-config key at
# a time - see ../../VERIFICATION-LOG.md).
#
# This is a selection mechanism, not a duplication: no parameter value is
# authored here or in azure.yaml. infra/main.bicepparam is a generated file
# (gitignored - see infra/.gitignore) that just points at whichever of
# parameters/dev.bicepparam / parameters/prod.bicepparam matches
# $AZURE_ENV_NAME, both of which are Day 23's original, unmodified files.
set -eu

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
INFRA_DIR="$(dirname "$SCRIPT_DIR")"

if [ -z "${AZURE_ENV_NAME:-}" ]; then
  echo "select-environment-parameters.sh: AZURE_ENV_NAME is not set - azd should set this before running hooks." >&2
  exit 1
fi

SOURCE_PARAMS="$INFRA_DIR/parameters/$AZURE_ENV_NAME.bicepparam"
if [ ! -f "$SOURCE_PARAMS" ]; then
  echo "select-environment-parameters.sh: no parameter file at $SOURCE_PARAMS for azd environment '$AZURE_ENV_NAME'." >&2
  exit 1
fi

cp "$SOURCE_PARAMS" "$INFRA_DIR/main.bicepparam"
echo "select-environment-parameters.sh: wired azd environment '$AZURE_ENV_NAME' to $SOURCE_PARAMS"
