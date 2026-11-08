#!/bin/sh
# The repository's gate, run before every pull request: the SDK as openapi.json generates it, its
# project where the solution, the specs' project and the quickstart's name it, and the build, from
# nothing, which compiles the SDK for .NET Standard 2.0, and its specs and the quickstart for .NET 10,
# and packs the SDK's package. The specs run once before each release, by scripts/test.sh, not here. The build runs in the .NET SDK's
# container with no network, on what scripts/fetch.sh fetched, and restores in locked mode: a package
# the repository's .nuget/packages lacks, or one a project's lock doesn't name, stops the gate, named.
set -eu
cd "$(dirname "$0")/.."

sh scripts/generate.sh

# What the generator writes, by the list it wrote now and the one the commit holds, and ApiVersion.cs
# in whichever project: a generated file that differs from the commit, is new to it or is gone from it
# fails the gate.
generated=$({
  cat .openapi-generator/FILES
  git show HEAD:.openapi-generator/FILES 2>/dev/null || true
} | grep -E '^src/.+' | sort -u)
# $generated is split into its paths on purpose, one pathspec each, none of which holds a space.
# shellcheck disable=SC2086
changed=$(git status --porcelain --untracked-files=all -- 'src/*/ApiVersion.cs' .openapi-generator $generated)
if [ -n "$changed" ]; then
  echo "The SDK differs from what openapi.json generates: run scripts/generate.sh and commit it." >&2
  echo "$changed" >&2
  exit 1
fi

# The project the generator writes from openapi-generator.yaml's packageName, held to where the
# solution names it, and to the specs' project and the quickstart's, which take it.
package=$(sed -n 's/^packageName: //p' openapi-generator.yaml)
project="src/$package/$package.csproj"
if ! grep -q -F "Path=\"$project\"" XipFax.slnx; then
  echo "XipFax.slnx doesn't name $project, the project openapi-generator.yaml's packageName makes: name it there." >&2
  exit 1
fi
for taker in test/XipHub.XipFax.Sdk.Tests/XipHub.XipFax.Sdk.Tests.csproj examples/Quickstart/Quickstart.csproj; do
  if ! grep -q -F "Include=\"../../$project\"" "$taker"; then
    echo "$taker doesn't take $project, the project openapi-generator.yaml's packageName makes: name it there." >&2
    exit 1
  fi
done

# From nothing, so that nothing an earlier build wrote is built on or packed.
rm -rf src/*/bin src/*/obj test/*/bin test/*/obj examples/*/bin examples/*/obj artifacts
sh scripts/docker.sh offline sdk sh -c '
  dotnet restore XipFax.slnx --locked-mode -p:NuGetAudit=false &&
  dotnet build XipFax.slnx --configuration Release --no-restore &&
  dotnet pack "$1" --configuration Release --no-build --output artifacts
' gates "$project"
echo "Gates: GREEN"
