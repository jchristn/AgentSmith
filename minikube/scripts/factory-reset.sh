#!/usr/bin/env bash
# DESTRUCTIVE: deletes namespace hugo in the minikube profile (all data volumes and Secret hugo-secrets), creates new
# secrets, and redeploys with the same components. Requires typing RESET. The profile and its images stay.
# Usage: minikube/scripts/factory-reset.sh [--with postgres,observability]
source "$(dirname "$0")/common.sh"
require_running
exec "${K8S_SCRIPTS}/factory-reset.sh" minikube "$@"
