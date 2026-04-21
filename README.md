# Pixel Flow Clone by Furkan Tokkan

Pixel Flow Clone is a Unity case study built around three goals:

- turning imported artwork into playable puzzle boards
- building a conveyor based color matching gameplay loop
- organizing the runtime around reusable systems and mobile friendly optimizations

---

## Gameplay Preview

<p align="center">
  <video src="README_ASSETS/Movie_004.mp4" poster="README_ASSETS/Level Editor 3.jpg" controls muted playsinline width="70%">
    Your browser does not support the video tag.
  </video>
</p>

Gameplay link: [YouTube Shorts](https://youtube.com/shorts/Nvlt_VYsPLY)

---

## Project Summary

The project started as an experiment in converting 2D artwork into a readable puzzle board. A source image is sampled into a limited color palette, translated into block placements, analyzed by exposure layers, and paired with a generated pig queue that can clear the board.

At runtime the player taps pigs from the holding tray, sends them onto a spline driven conveyor, and matching pigs fire at visible blocks of the same color. Win and fail states are resolved from remaining targets, queue state, pending pig actions, and active board transitions.

---

## Tools and Tech

- Unity with URP
- VContainer for dependency injection and runtime composition
- PrimeTween for dispatch, deplete, and feedback animation flow
- Dreamteck Splines for conveyor movement
- Unity Input System for tap and pointer input
- ScriptableObjects for levels, themes, configs, and runtime settings
- Odin Inspector in editor tooling
- Custom editor window for image import, board painting, queue editing, and validation

---

## Level Creation Pipeline

- Images are imported directly into the custom Pixel Flow level editor.
- Transparent borders can be cropped before sampling.
- The importer resizes artwork into board resolution and maps pixels into the PigColor palette.
- Adaptive clustering is used during palette reduction so small image details survive better than a naive nearest color pass.
- Generated boards are stored inside `PixelFlowLevelDatabase`.
- Pig queues can be generated automatically from exposed layer analysis or edited manually.
- A guaranteed completion validator checks whether the current queue can clear the board.

---

## Runtime Architecture

- `ProjectLifetimeScope` and `GameSceneContext` bootstrap scene dependencies.
- `LevelSessionController` loads levels, tracks win and lose state, and handles saved progression.
- `GameManager` coordinates dispatch, targeting, burst flow, tray queue behavior, and renderer visibility through focused collaborators.
- `ThemeDatabase` and environment prefabs keep level presentation data driven.
- HUD flow is separated from gameplay logic through presenter style orchestration.

---

## Optimization Techniques

- Pigs, blocks, and bullets are pooled through `VisualPoolService` instead of being instantiated during play.
- Bullet warmup runs asynchronously to reduce first shot spikes.
- Dispatch runtime is prewarmed after scene start to avoid early frame hitching.
- Pig renderers are culled when they fall outside the gameplay camera viewport.
- Pig selection uses `Physics.RaycastNonAlloc` to avoid repeated runtime allocations on input.
- PrimeTween capacity is configured up front instead of growing at runtime.
- The runtime enforces a target frame rate and disables vSync where needed.
- Physics layer collisions are reduced so Pig, Bullet, and Block interactions stay focused.
- Atlas based color and tone mapping keeps block visuals reusable without multiplying material variants.

---

## Editor Workflow

<p align="center">
  <img src="README_ASSETS/Level Editor 1.jpg" width="45%" />
  <img src="README_ASSETS/Level Editor 2.jpg" width="45%" />
</p>

- Open `Tools > Pixel Flow > Level Editor`
- Select `PixelFlowLevelDatabase`
- Import an image or paint the board manually
- Tune palette, grid resolution, queue rules, and holding slot count
- Validate the generated queue
- Save and test in `Assets/Scenes/GameScene.unity`

---

## In Game View

<p align="center">
  <img src="README_ASSETS/Level Editor 3.jpg" width="28%" />
</p>

---

## Extension Points

- add new block types or obstacle rules
- introduce new pig abilities or shot behaviors
- expand the queue generation heuristics
- add more theme environments
- split progression, meta systems, or boosters into separate layers

---

## Quick Start

1. Open `Assets/Scenes/GameScene.unity`
2. Open `Tools > Pixel Flow > Level Editor`
3. Pick or create a level in `PixelFlowLevelDatabase`
4. Import artwork or place blocks manually
5. Generate or edit the pig queue
6. Press Play and iterate on readability, flow, and balance
