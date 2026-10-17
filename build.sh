#!/usr/bin/env sh
# builds everything
set -eu

cd "$(dirname "$0")"

CONFIG="${1:-Release}"

dotnet build ScpSlWhitelist/ScpSlWhitelist.csproj -c "$CONFIG"

PLUGIN_DLL="ScpSlWhitelist/bin/$CONFIG/net48/ScpSlWhitelist.dll"

echo
echo "Build succeeded."
echo "Plugin: $PLUGIN_DLL"
echo "Copy this file into the server's LabAPI/plugins directory."
