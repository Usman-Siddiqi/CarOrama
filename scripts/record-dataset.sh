#!/usr/bin/env sh
set -eu

repository="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
godot_executable="${CARORAMA_GODOT:-godot}"
config="${CARORAMA_CONFIG:-$repository/config/scenario-splits.json}"
output="${CARORAMA_DATASET_OUTPUT:-$repository/artifacts/datasets}"
dataset_id="${CARORAMA_DATASET_ID:-$(date -u +%Y%m%d-%H%M%S)}"
build_id="${CARORAMA_BUILD_ID:-$(git -C "$repository" rev-parse HEAD)}"

if [ -n "$(git -C "$repository" status --porcelain)" ]; then
    build_id="$build_id-dirty"
fi

"$godot_executable" --headless --path "$repository/src/CarOrama.Game" --build-solutions --quit

if [ "${CARORAMA_NO_CAMERAS:-0}" = "1" ]; then
    "$godot_executable" --headless --fixed-fps 120 --path "$repository/src/CarOrama.Game" -- \
        --record-dataset --no-cameras --config "$config" --output "$output" \
        --dataset-id "$dataset_id" --build-id "$build_id" "$@"
else
    "$godot_executable" --fixed-fps 120 --path "$repository/src/CarOrama.Game" \
        --rendering-method gl_compatibility --disable-vsync -- \
        --record-dataset --config "$config" --output "$output" \
        --dataset-id "$dataset_id" --build-id "$build_id" "$@"
fi
