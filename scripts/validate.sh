#!/usr/bin/env sh
set -eu

dotnet restore CarOrama.sln
dotnet test CarOrama.sln --configuration Release --no-restore

godot_executable="${CARORAMA_GODOT:-godot}"
"$godot_executable" --headless --path src/CarOrama.Game --build-solutions --quit
"$godot_executable" --headless --path src/CarOrama.Game -- --smoke-test
"$godot_executable" --headless --path src/CarOrama.Game -- --episode-smoke-test
if [ "${CARORAMA_SKIP_RENDER_VALIDATION:-0}" != "1" ] && \
   { [ -n "${DISPLAY:-}" ] || [ -n "${WAYLAND_DISPLAY:-}" ]; }; then
    "$godot_executable" --path src/CarOrama.Game --rendering-method gl_compatibility -- --dataset-smoke-test
else
    echo "Skipping renderer-backed dataset smoke test (no display or explicitly disabled)."
fi
