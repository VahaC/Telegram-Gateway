#!/usr/bin/env bash
set -euo pipefail
dotnet test TelegramGateway.slnx -c Release --no-build --no-restore --logger "trx;LogFilePrefix=gateway"
