#!/usr/bin/env bash
# Polls an Azure Container App's latest revision until it is Running + Healthy,
# or exits non-zero on a definitive failure or timeout.
#
# Usage: wait-for-containerapp-revision.sh <container-app-name> <resource-group>
#
# Why: `az containerapp update` returns as soon as the update is submitted;
# if the new revision fails to start (image pull error, migration crash,
# container exits at boot, ...) the previous revision keeps serving traffic
# silently, while the deploy action exits 0. Without this poll the workflow
# badge goes green while production stays stale.

set -e

APP=${1:?usage: $0 <container-app-name> <resource-group>}
RG=${2:?usage: $0 <container-app-name> <resource-group>}

ATTEMPTS=30
SLEEP_SECONDS=10

echo "Polling latest revision of $APP (up to $((ATTEMPTS * SLEEP_SECONDS))s)..."
for i in $(seq 1 "$ATTEMPTS"); do
  REV=$(az containerapp show --name "$APP" --resource-group "$RG" \
    --query properties.latestRevisionName -o tsv)
  STATE=$(az containerapp revision show --name "$APP" --resource-group "$RG" \
    --revision "$REV" --query properties.runningState -o tsv 2>/dev/null || echo "Unknown")
  HEALTH=$(az containerapp revision show --name "$APP" --resource-group "$RG" \
    --revision "$REV" --query properties.healthState -o tsv 2>/dev/null || echo "Unknown")
  echo "attempt $i: revision=$REV state=$STATE health=$HEALTH"

  if [ "$STATE" = "ActivationFailed" ]; then
    echo "::error::$APP revision $REV failed to activate."
    echo "Last 100 lines of container console logs:"
    az containerapp logs show --name "$APP" --resource-group "$RG" \
      --revision "$REV" --type console --tail 100 --format text || true
    exit 1
  fi

  if { [ "$STATE" = "Running" ] || [ "$STATE" = "RunningAtMaxScale" ]; } \
     && [ "$HEALTH" = "Healthy" ]; then
    echo "::notice::$APP revision $REV is $STATE / $HEALTH"
    exit 0
  fi

  sleep "$SLEEP_SECONDS"
done

echo "::error::Timed out waiting for $APP revision $REV to become Running+Healthy."
exit 1
