# Dataset recording and format

CarOrama records one immutable directory per dataset. The recorder refuses to overwrite an existing dataset or frame. Training, validation, and test scenarios use disjoint procedural seeds from `config/scenario-splits.json`.

## Record

Camera-backed Godot/Jolt recording requires a render-capable process:

```powershell
./scripts/record-dataset.ps1 -Split Training -MaxEpisodes 1 -DatasetId rgb-example
```

State-only Jolt regression avoids rendering and can run headlessly:

```powershell
./scripts/record-dataset.ps1 -NoCameras -DatasetId state-regression
```

Useful options include `-Scenario <id>`, `-CameraWidth`, `-CameraHeight`, and `-CameraHz`. The script records the source revision, appending `-dirty` when the working tree has uncommitted changes. Use a unique dataset identifier for every run.

The fast deterministic reference environment can also create structured episodes without Jolt or pixels:

```powershell
dotnet run --project tools/CarOrama.Dataset -- record-reference `
  --config config/scenario-splits.json `
  --output artifacts/datasets `
  --dataset-id reference-example `
  --build-id local
```

## Layout

```text
<dataset-id>/
  dataset.json
  scenario-manifest.json
  _COMPLETE
  training|validation|test/
    <scenario-id>/
      episode.json
      steps.jsonl
      summary.json
      _COMPLETE
      frames/
        windshieldmain/
          0000000004.png
        ...
```

`dataset.json` records schema and simulation-contract versions, dataset/build identifiers, creation time, coordinate convention, and units. `scenario-manifest.json` contains only the scenarios selected for that dataset. `episode.json` records seed, route, spawn, cadence, and camera calibration.

The first JSON Lines record is the tick-zero reset observation. Every later record contains the action, resulting observation, reward, cumulative metrics, termination state, and zero or more sensor-frame references. Actions and results must be contiguous: action tick `n` produces observation tick `n + 1`.

Camera files are scheduled from integer physics ticks, not wall time. The Godot adapter pauses on a due sensor tick, renders while vehicle and traffic state are fixed, saves each PNG through a temporary path, and then resumes physics. Every frame reference includes camera ID, exact physics tick, simulation time, and episode-relative path.

## Completion and quality

While an episode is being written its stream is named `steps.jsonl.tmp`. Successful terminal finalization renames it to `steps.jsonl`, writes `summary.json`, and creates the episode `_COMPLETE` marker. The dataset marker is created only after every manifest episode has its own marker. Consumers must ignore directories without the relevant completion marker.

Completeness and driving quality are separate. The Godot recorder returns a non-zero status if any selected episode fails to complete its route or reports a collision, lane departure, red-light violation, stop-sign violation, or speeding duration. Metrics remain in the completed episode so failures can be reproduced and investigated.
