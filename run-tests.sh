#!/usr/bin/env bash
set -euo pipefail
dotnet test AgentGateway.slnx -c Release --no-build --no-restore --logger "trx;LogFilePrefix=gateway"
