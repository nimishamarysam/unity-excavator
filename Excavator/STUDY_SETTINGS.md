# Study settings reference

Where each adjustable setting of the excavator study lives. All paths are relative to `Excavator/Assets/`.

**Important:** almost all study objects (`StudyManager`, `ContextViews`, cue scripts, scenarios) are created **from code at Play time** (see `StudyBootstrap` in `Study/StudyManager.cs`). They are not in the scene, so changing a value in the Inspector during Play mode is lost when you stop. **To change a setting permanently, edit the default value in the `.cs` file.** Settings marked *static switch* are global `public static` flags. Settings marked *const* are compile-time constants.

---

## 1. Study design and session flow

| What | Setting | Default | File |
|---|---|---|---|
| Run the study at all (off = free driving) | `StudyBootstrap.Enabled` (static switch) | `true` | `Study/StudyManager.cs` |
| Max length of a measured trial | `trialDurationSeconds` | 240 s | `Study/StudyManager.cs` |
| Length of the FREE practice (starts after the guided walkthrough; no time limit during the walkthrough) | `practiceDurationSeconds` | 180 s (participant can end it earlier by holding A) | `Study/StudyManager.cs` |
| Guided control walkthrough at the start of the practice (phase 1: mode switch, drive fwd/back, turn left/right fwd/back, mode switch, boom/arm/bucket/swing both ways, then 3 info pages - task panel, status bar, incidents - continued with A after `InfoMinSeconds` 3 s) | `PracticeGuide.Enabled` (static switch) | `true` | `Study/Scenarios/PracticeGuide.cs` |
| Practice phase 2 (guided digging and dumping: teeth in, scoop, lift, swing anywhere and dump, one more alone); NO marked areas - dig zone = square of 2 × (reach + 3 m) around the machine where it stands when phase 2 starts, dump anywhere | `PracticeScenario.BuildDigSite`, `PracticeGuide.BuildSteps` | - | `Study/Scenarios/Scenarios.cs`, `Study/Scenarios/PracticeGuide.cs` |
| Practice phase 3: hold A to finish the practice early | `PracticeGuide.HoldAToContinue` | 1.5 s | `Study/Scenarios/PracticeGuide.cs` |
| Correct input needed per walkthrough step (cumulative, wrong mode/stick not counted); per-step overrides in `HoldPerStep` | `PracticeGuide.HoldSeconds`, `PracticeGuide.HoldPerStep` | 3 s default; boom down 1.5 s, boom up 2 s, arm out 2 s, arm in 3 s, bucket 1.5 s | `Study/Scenarios/PracticeGuide.cs` |
| Walkthrough safety cap (walkthrough ends, free practice starts) | `GuideMaxSeconds` | 1200 s | `Study/Scenarios/PracticeGuide.cs` |
| Walkthrough hint delay: no "not quite" / wrong-mode hint for the first seconds of a step, and only after continuous wrong input | `HintGraceSeconds`, `WrongBeforeHint` | 1.5 s, 0.5 s | `Study/Scenarios/PracticeGuide.cs` |
| Experimenter: skip the current walkthrough step | gamepad Select/Back or keyboard N | – | `Study/Scenarios/PracticeGuide.cs` |
| Practice trial on/off | setup screen toggle (gamepad Y) | on | `Study/StudyManager.cs` (`includePractice`) |
| Condition order (Latin square) | `latinSquare` | C1-C2-C3 / C2-C3-C1 / C3-C1-C2 | `Study/StudyTypes.cs` |
| Which scenarios are in the study | `scenarioOrder` | Driving, Digging, Dig and dump | `Study/StudyTypes.cs` |
| Scenario shuffle seed / trial seeds | `BuildTrials` (`id*7919+17`, `id*1000`) | – | `Study/StudyTypes.cs` |
| What each condition shows | `ShowContextViews` (C2+), `ShowAttentionCues` (C3) | – | `Study/StudyManager.cs` |
| Briefing texts (condition + scenario) | `ConditionBriefing`, `ScenarioBriefing` | – | `Study/StudyTypes.cs` |
| Questionnaire screen text | `OnGUI` → `State.Questionnaire` | SIM-TLX + SART | `Study/StudyManager.cs` |
| Start posture (boom up, arm vertical) | `Scenario.StartRetracted` (static switch) | `true` | `Study/Scenarios/Scenario.cs` |

## 2. Logging and incident feedback

| What | Setting | Default | File |
|---|---|---|---|
| Sample rate of the samples CSV | `sampleRateHz` | 10 Hz | `Study/StudyLogger.cs` |
| Output folder / CSV columns | `FolderPath`, `OpenSession` | `StudyData/` | `Study/StudyLogger.cs` |
| Which events count as incidents | `IncidentType()` | contacts, impacts, spills, over-dig, outside area, bucket over worker | `Study/IncidentFeedback.cs` |
| Same incident not re-counted within | `repeatBlockSeconds` | 2 s | `Study/IncidentFeedback.cs` |
| Warning sound volume | `volume` | 0.6 | `Study/IncidentFeedback.cs` |
| Hazard check rate | `HazardTracker.rateHz` | 10 Hz | `Study/World/Hazard.cs` |
| No hazard warnings/contacts counted right after trial start or after a hazard appears (e.g. a worker) | `HazardTracker.SettleSeconds` (const) | 0.5 s | `Study/World/Hazard.cs` |
| Hazard distances use transform-based geometry (`ColliderMath`), not `Collider.ClosestPoint` | `ColliderMath.ClosestPoint` | – | `Study/World/Hazard.cs` |
| What counts as a hazard response | `Responded()` (stick release or reversal) | – | `Study/World/Hazard.cs` |
| Task-phase detection rate / hold time | `rateHz`, `holdSeconds` | 10 Hz, 0.5 s | `Study/Context/ContextMonitor.cs` |

## 3. Screen layout and measurement bar (all conditions)

| What | Setting | Default | File |
|---|---|---|---|
| Right camera panel width | `PanelWidth` (const) | 0.28 of screen | `Study/StudyLayout.cs` |
| Measurement bar height | `BarHeight` (const) **and** `MeasurementBar.barHeight` (keep equal) | 72 px @1080p | `Study/StudyLayout.cs`, `Study/MeasurementBar.cs` |
| Gap between panel windows | `Gap` (const) | 0.006 | `Study/StudyLayout.cs` |
| Measurement bar refresh rate | `refreshRateHz` | 10 Hz | `Study/MeasurementBar.cs` |
| Which readouts are on the bar | `BuildUI()` (`Cell(...)` calls) | 9 readouts | `Study/MeasurementBar.cs` |
| Task panel size (text, incidents, mini-map) | `textH`, `incH`, `mapW` (const) | 118, 26, 130 | `Study/Scenarios/Scenario.cs` |
| Instructions and guidance (practice guide, task instruction screens, practice/task complete, pause) on their own display | `StudyLayout.InstructionsOnSeparateDisplay` (static switch) | `true` | `Study/StudyLayout.cs` |
| **Which display shows the instructions** (0-based: Display 3 = 2) | `StudyLayout.InstructionDisplayIndex` (const) | `2` (Display 3) | `Study/StudyLayout.cs` |
| Instruction display size in the Editor (a build uses the monitor's resolution) | `InstructionEditorWidth`, `InstructionEditorHeight` | 1920 × 1080 | `Study/StudyLayout.cs` |
| Instructions upside down on the instruction display | `InstructionDisplay.FlipY` (static switch) | `false` | `Study/InstructionDisplay.cs` |

## 4. Context-aware views (C2 and C3)

| What | Setting | Default | File |
|---|---|---|---|
| Travel mode: bird's-eye + obstacle camera (true) or bird's-eye + left/right side (false) | `ContextViews.PopupStyleForDriving` (static switch) | `true` | `Study/Context/ContextViews.cs` |
| Views per mode | `TravelSet`, `OperationSet` | see file | `Study/Context/ContextViews.cs` |
| Window labels | `LabelFor()` | – | `Study/Context/ContextViews.cs` |
| Camera follow smoothing | `followSpeed` | 6 | `Study/Context/ContextViews.cs` |
| FOV close-up / wide views | `fieldOfView`, `wideFieldOfView` | 42°, 55° | `Study/Context/ContextViews.cs` |
| Close-up distance multiplier | `zoom` | 1.0 | `Study/Context/ContextViews.cs` |
| Bird's-eye height | `birdsEyeHeight` | 20 m | `Study/Context/ContextViews.cs` |
| Bucket side view distance | `bucketSideDistance` | 5 m | `Study/Context/ContextViews.cs` |
| View into bucket: distance / side shift | `intoBucketDistance`, `intoBucketSideShift` | 3.2 m, 1.3 m | `Study/Context/ContextViews.cs` |
| View into bucket shown above bucket angle (hidden again at or below min − hysteresis) | `intoBucketMinAngle`, `intoBucketHysteresis` | 1°, 0.5° (i.e. shown when the bar reads above 0°) | `Study/Context/ContextViews.cs` |
| While the view into bucket is hidden, the bucket side view fills the middle + bottom slots (one tall window) | `sideTall` window in `LateUpdate()` | – | `Study/Context/ContextViews.cs` |
| Camera poses (all views) | `Pose()` | – | `Study/Context/ContextViews.cs` |
| Obstacle camera docked in panel (true) or floating in driver view (false) | `PopupViews.DockInPanel` (static switch) | `true` | `Study/Context/PopupViews.cs` |
| Obstacle camera opens at | `openDistanceObstacle`, `openDistanceWorker` | 4 m, 8 m | `Study/Context/PopupViews.cs` |
| Closing hysteresis / delay / min. open time | `closeExtra`, `closeDelay`, `minOpenSeconds` | 1.5 m, 2 s, 4 s | `Study/Context/PopupViews.cs` |
| Max pop-ups, size, switch margin | `maxPopups`, `size`, `switchMargin` | 2 (1 docked), 0.20×0.24, 0.5 m | `Study/Context/PopupViews.cs` |

### Underground cut-away in the bucket side view (C2 and C3)

| What | Setting | Default | File |
|---|---|---|---|
| Enabled per scenario | `CueProfile.bucketSideUnderground` | on in Digging and Dig and dump | `Study/Cues/AttentionCues.cs` (`CueProfile.For`) |
| Section length each side of bucket | `halfSpan` | 5 m | `Study/Cues/UndergroundSideView.cs` |
| Section depth below original ground | `sectionDepth` | 3.5 m | `Study/Cues/UndergroundSideView.cs` |
| Section plane behind the bucket | `behindBucket` | 0.9 m | `Study/Cues/UndergroundSideView.cs` |
| C3 "PIPE x m" tag only when teeth closer than | `labelDistance` | 0.6 m (or at warning level) | `Study/Cues/UndergroundSideView.cs` |
| Cut-away: only objects this far in front of the bucket are drawn (hides workers/stakes between camera and bucket) | `cutAwaySlab` | 1.2 m | `Study/Context/ContextViews.cs` |
| Obstacle/worker 3D highlights in the bucket side view | removed (culling mask in `Place()`); still in bird's-eye, view into bucket, driver view | – | `Study/Context/ContextViews.cs` |
| Curl advice (CURL OK / MORE / OPEN MORE) | `CueProfile.bucketSideAngleLabel`; shown in the VIEW INTO BUCKET, only near the suggested path | – | `Study/Cues/DiggingCues.cs` (`DrawAngleLabel`) |
| Switch-on / switch-off thresholds | `LateUpdate()` (on: teeth < ground + 0.3 m, off: > ground + 0.8 m) | – | `Study/Cues/UndergroundSideView.cs` |
| Which services are drawn, colours, label | `Draw()` (BuriedPipe, BuriedCable; C2 grey circle, C3 warning colours + "PIPE x m") | – | `Study/Cues/UndergroundSideView.cs` |

### Dig pit top view + buried services x-ray (Digging, C2 and C3)

| What | Setting | Default | File |
|---|---|---|---|
| Middle window = DIG PIT (TOP VIEW) instead of VIEW INTO BUCKET in Digging | `ContextViews.PitTopViewInDigging` (static switch) | `false` (Digging uses bird's-eye / view into bucket / bucket side view) | `Study/Context/ContextViews.cs` |
| Pit top view field of view / margin around the trench | `pitTopFieldOfView`, `pitTopMargin` | 40°, 0.4 m | `Study/Context/ContextViews.cs` |
| Pit top view draws only the bucket (cab, boom, arm hidden for that camera) | `hideMachineInPitView` | `true` | `Study/Context/ContextViews.cs` |
| X-ray of pipe and cable (drawn in the view into bucket; in the pit top view when that is switched on) | `CueProfile.intoBucketXray` | on in Digging | `Study/Cues/AttentionCues.cs` (`CueProfile.For`) |
| Line width / colour | `pipeWidth`, `cableWidth`, `PipeColor`, `CableColor` | 0.3 m blue, 0.1 m orange | `Study/Cues/BuriedServicesXray.cs` |

## 5. Attention cues (C3)

### Global cue switches (static switches, all true = full C3)

`Study/Cues/AttentionCues.cs`: `StaticObstacleCues`, `DynamicObstacleCues`, `DigAreaCues`, `DrivingPathCues`, `BucketTrajectoryCues`. Each one is combined with the scenario profile (both must be on).

### Per-scenario cue profile

`Study/Cues/AttentionCues.cs` → class `CueProfile`. The field defaults are the full set, and `CueProfile.For(scenario)` overrides them per scenario (`Easy_Driving`, `Moderate_Digging`, `Hard_DigAndDump`).

| Group | Fields |
|---|---|
| Cue types | `staticObstacles`, `dynamicObstacles`, `drivingPath`, `digArea`, `trajectory` |
| Obstacle detail | `obstaclesOnlyNearPath`, `gatePostsOnlyAtWarning`, `workerMotionText`, `workerMotionArrow`, `groundRing`, `edgeMarkersForAll`, `edgeMarkersWorkersOnly`, `staticOnlyAtWarning`, `truckOnlyAtContact`, `obstaclesHiddenInDigMode`, `workerMotionOnlyApproaching`, `maxObstacleCues`, `thinHighlight`, `truckWallProximity` |
| Driving detail | `steeringBanner`, `pathEndRing`, `bannerWithTarget`, `steerThreshold` (15°), `passAdviceInBanner` |
| Digging detail | `trajectoryInBucketSideViewOnly`, `bucketSideHeightArrow`, `bucketSideAngleLabel`, `heightArrowInDriverView`, `bucketSideUnderground` |

### Cue tuning values

| Cue | Settings (default) | File |
|---|---|---|
| Obstacle / worker | `workerRange` 12 m, `obstacleExtra` 3 m (cue distance = warn + this), `powerLineExtra` 2 m, `cableExtra` 0.8 m, `hysteresis` 0.5 m, `motionPreview` 2 s | `Study/Cues/ObstacleCues.cs` |
| Driving path | `planRateHz` 10, `maxPathLength` 28 m, `obstacleMargin` 0.6 m, `workerMargin` 1.2 m, `chevronSpacing` 2 m, `chevronSpeed` 1.5 m/s, `nearPathDistance` 2.5 m | `Study/Cues/DrivingCues.cs` |
| Dig area / trajectory | `cutDepthPerPass` 0.45 m, `dragLength` 1.6 m, `replanSeconds` 0.6 s, `reachFraction` 0.85, `markerMinBucketAngle` 20°, `serviceMargin` 0.05 m, `curlMatchDegrees` 15°; trajectory progress: `onPathDistance` 0.5 m (teeth following the path), `offPathDistance` 0.3 m (path ahead turns orange during a scoop) | `Study/Cues/DiggingCues.cs` |
| Bucket trajectory shown only when the bucket is in position to dig (teeth over dig area, < 1.5 m above ground) | `LateUpdate()` (`BucketInDigArea`) | `Study/Cues/DiggingCues.cs` |
| Truck wall proximity | `showGap` 1.2 m, `warnGap` 0.6 m, `dangerGap` 0.25 m, `rimLength` 1.6 m, field size/bulge | `Study/Cues/TruckProximityCue.cs` |
| Cue colours (yellow/orange/red/cyan/green) | `Look`, `Warn`, `Danger`, `Guide`, `Good` | `Study/Cues/CueKit.cs` |
| Render layers (context-only, bucket-side-only, driver-only) | `ContextOnlyLayer` 31, `BucketSideOnlyLayer` 30, `DriverOnlyLayer` 29 (const) | `Study/Cues/CueKit.cs` |

## 6. Scenarios

All in `Study/Scenarios/Scenarios.cs` unless noted. Per-trial randomness uses `Rand(...)` / `Rng`, seeded from the trial seed.

| Scenario | Settings (default) |
|---|---|
| Practice | phase 1 solid bucket (`EnableCuttingEdges(false)`); from phase 2: invisible dig zone all around the machine (max depth 1.5 m), dump anywhere, nothing marked (in `PracticeScenario.BuildDigSite`) |
| Easy – Driving | `GateWidth` 10 m; `GatePassBefore` 1.5 m (gate counts when the machine centre is within 1.5 m before the gate line or up to 4 m past it); `gates[]` positions; `start`; `stopLine`; barrel positions + jitter; worker timings (`crosserAt` 0.5–3.5 s after gate 1, `behindAt` 1.5–3.5 s after gate 4, stops 4 s); finish = on line, < 1 km/h for 1 s |
| Moderate – Digging | `FixedLayout` (static switch, `true` = same trench/cable/pipe/spoil layout in every trial from `LayoutSeed` 20261018 → cable right, spoil square left; worker timing still per trial); `TargetDepth` 1.2 m; Trench done % = smooth progress `TerrainDigger.ProgressTo` (each 0.5 m cell partial credit, full at target − `ProgressTolerance` 0.2 = 1.0 m), trial completed at `CompleteAt` 0.85 of the same number; `SpoilSize` 2.5 m; `SpoilMinGap` 5 m; `SpoilSwingDeg` 120°; `PipeBelowTarget` 0.25 m; `WorkerStandSeconds` 6 s; `workPos`; `StartBehind` 9 m; trench length 0.43 × reach; cable 0.9 m deep, 0.5–0.8 m outside the edge; worker at 45–110 s; bucket over worker = `OverWorkerRadius` 1.0 m (hinge→teeth segment, horizontal) and whole bucket above the worker's ground; over-dig = target + 0.35 m; (old whole-cell share `FractionDugTo` still logged as dug= in the summary) |
| Hard – Dig and dump | `Goal` 4 loads; `Limit` 360 s; lane waypoints `W1`, `W2`, `WP`; `TruckOffset` 0.75 × reach; `PowerLineHeight` 5.8 m; `PowerLineBeyondTruck` 1.6 m; `PitPipeEnabled` true; `PipeDepth` 1.2 m; pit 4.5 m; workers at 35–90 s (pit) and 150–230 s (truck); dig target for cues = `PipeDepth − 0.3` m |
| Task panel text | `TaskText`, `ProgressText` in each scenario class |
| Time limit per scenario | `TimeLimitSeconds` override (else `trialDurationSeconds`) — `Study/Scenarios/Scenario.cs` |

### Hazard distances (warning / contact)

| Hazard | Warn / contact | Where set |
|---|---|---|
| Gate posts, barrels | 1.5 m / 0.15 m | `Study/World/WorldBuilder.cs` (`Gate`, `Barrel`) |
| Worker | 5 m / 0.4 m | `Study/World/WorldBuilder.cs` (`Worker`) |
| Truck | 0.8 m / 0.1 m | `Study/World/WorldBuilder.cs` (`Truck`) |
| Concrete blocks, lane fence | 1.2 m / 0.15 m | `DigDumpScenario.Build` |
| Power line (measured from boom/arm/bucket) | 1.5 m / 0.3 m | `DigDumpScenario.Build` |
| Buried cable (teeth) | 0.7 m / 0.3 m | `DiggingScenario.Build` |
| Crossing pipe, Digging (teeth) | 0.2 m / 0.08 m | `DiggingScenario.Build` |
| Pit pipe, Dig and dump (to axis) | 0.4 m / 0.17 m | `DigDumpScenario.Build` |
| Trench edge (tracks) | 1.5 m / 0.3 m | `DiggingScenario.Build` |
| Default for a new hazard | 3 m / 0.3 m | `Study/World/Hazard.cs` |

## 7. Soil, bucket and machine

| What | Settings (default) | File |
|---|---|---|
| Bucket filling and dumping | `dragMetersToFill` 1.5 m, `curlDegreesToFill` 70°, `minScoop` 0.25, `dumpAngle` 140°, `dumpMinHeight` 0.5 m, `cutTolerance` 0.15 m, `zoneMargin` 0.5 m | `Study/World/BucketLoad.cs` |
| Bucket/arm stopped by truck walls | `bucketProbeRadius` 0.25, `armProbeRadius` 0.25, `bucketHalfWidth` 0.45 m, `impactSpeed` 0.5 m/s (incident threshold) | `Study/World/BucketBlocker.cs` |
| Terrain digging | `Dig(tip, radius = 0.9)`, `FractionDugTo(depth, tolerance = 0.15)`, `EndlessSoil`, `riverBankHeight` | `Study/World/TerrainDigger.cs` |
| Truck bed size | `TruckBedLength` 6 m, `TruckBedWidth` 3.2 m (const) | `Study/World/WorldBuilder.cs` |
| Worker walking speed / stride | `speed` 1.3 m/s, `stride` 28° | `Study/World/WorldBuilder.cs` (`WorkerWalker`) |
| Gamepad dead zone | `deadZone` 0.1 | `Excavator/ExcavatorInput.cs` |
| Stick → joint mapping, mode button | `ReadGamepad()`, `ModeTogglePressedThisFrame()` | `Excavator/ExcavatorInput.cs` |
| Machine speed / acceleration | `maxSpeed`, `creepSpeed`, `initialAccel`, `maxAccel`, … | `Excavator/Excavator.cs` (Inspector on the scene object; these *are* saved in the scene) |
