#!/usr/bin/env sh
set -eu

dotnet restore CarOrama.sln
dotnet test CarOrama.sln --configuration Release --no-restore

godot_executable="${CARORAMA_GODOT:-godot}"
"$godot_executable" --headless --path src/CarOrama.Game --build-solutions --quit
"$godot_executable" --headless --path src/CarOrama.Game -- --smoke-test

