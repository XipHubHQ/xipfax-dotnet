#!/bin/sh
# Fetches the packages the SDK and its specs take, and every package those take, from nuget.org alone
# (nuget.config), into the repository's own .nuget/packages, and writes each project's
# packages.lock.json from what it takes. NuGet's audit reads nuget.org's advisories as it restores,
# and fails the fetch on any (Directory.Build.props). It runs in the .NET SDK's container, which
# Docker pulls the first time, and it is the one command of the SDK's that reaches the network: the
# gate and the specs run offline after it. Run it again whenever a package's version changes.
set -eu
cd "$(dirname "$0")/.."

sh scripts/docker.sh fetch sdk dotnet restore XipFax.slnx
