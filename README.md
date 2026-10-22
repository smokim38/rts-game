# rts-game

A real-time strategy game in the spirit of **Command & Conquer: Red Alert 2**, built for today's hardware.

## Vision

The goal is to keep what made classic RTS games fun, like fast base building, distinct factions and big battles, and improve on the parts that have aged:

- **Smarter AI:** opponents that scout, adapt and coordinate, and that don't just cheat to win.
- **Better movement:** good pathfinding, formations and crowd flow, so units don't jam in chokepoints or wander off alone.
- **Massive scale:** thousands of units on screen at once, running smoothly on modest PCs as well as high-end machines.

## Who builds it

This game is made by an **AI game studio**. Most of the design, code and docs are written by AI coding agents, **Claude Code** and **Codex**, working with a human director who sets the direction and reviews the work.

## Folder layout

```
rts-game/
├── Game/    Unity 6.3 LTS project (URP): Assets, Packages, ProjectSettings
├── Art/     Source art (.blend, .psd, ...) that gets exported into Game/Assets
├── docs/    Design docs; docs/adr/ holds Architecture Decision Records
└── tools/   Helper scripts and utilities that aren't part of the game build
```

- **Game/** is the only folder Unity opens. Commit every asset together with its `.meta` file.
- **Art/** holds the editable originals. Only the exported copies go in `Game/Assets/`.
- **docs/adr/** records important technical decisions and the reasons behind them.
- **tools/** is for build scripts, data pipelines and other developer helpers.

## Getting started

1. Install [Git LFS](https://git-lfs.com/) and run `git lfs install` (models, textures and audio are stored with LFS).
2. Clone the repo.
3. Open the `Game/` folder in Unity Hub with **Unity 6000.3.23f1** (Unity 6.3 LTS).

## License

Copyright (C) 2026 smokim38. All rights reserved, except for the `.py` scripts in `Art/` that carry `# SPDX-License-Identifier: GPL-3.0-or-later`: only those scripts are covered by [Art/LICENSE](Art/LICENSE) under `GPL-3.0-or-later`. `.blend` files and other owner-made content in `Art/`, game code, exported art, docs and tools stay all rights reserved, with no license beyond what GitHub's Terms of Service and applicable law allow. Third-party content keeps its own license, unchanged.

See [LICENSE](LICENSE) for the scope and [ADR 0003](docs/adr/0003-repository-licensing.md) for the decision. GitHub may not correctly detect or display the repository's licenses.
