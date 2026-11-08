#!/bin/sh
# The SDK's specs, run once before each release, as the TypeScript SDK's are: the build as
# scripts/gates.sh makes it, from nothing, then every spec, the quickstart's walks among them, by
# dotnet run. They run in the .NET SDK's
# container with no network, on what scripts/fetch.sh fetched, and its environment holds the image's
# own variables and the two scripts/docker.sh gives it alone, so that no credential in the shell that
# runs this reaches a spec; a spec says so, by every variable shaped like one. No spec reaches
# XipFax: a stand-in in the specs' own process answers each.
set -eu
cd "$(dirname "$0")/.."

rm -rf src/*/bin src/*/obj test/*/bin test/*/obj examples/*/bin examples/*/obj
sh scripts/docker.sh offline sdk sh -c '
  dotnet restore XipFax.slnx --locked-mode -p:NuGetAudit=false &&
  dotnet build XipFax.slnx --configuration Release --no-restore &&
  dotnet run --project "$1" --configuration Release --no-build
' specs test/XipHub.XipFax.Sdk.Tests/XipHub.XipFax.Sdk.Tests.csproj
