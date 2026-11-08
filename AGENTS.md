# XiaomanVisitor project

- The actual project root is this directory, not DSH-Go. Stable runtime version: 0.0.2. Workshop item: 3835383562.
- Use `tools/setup-dev.ps1` to restore the development environment and `tools/verify-dev.ps1` for the full local check. Game references are read from `.dev/local.json` or an explicit `-GameRoot` argument.
- Published 0.0.2 has a renovation-only card; develop/tavern now builds **0.0.3, a stage-one test version with entry from game day 3**, independent layered actor, ordinary cup, pickup-counter project and visit ledger. Full weekly finance/contracts are not implemented. Calendar days, Monday encounter count and financial weeks must remain distinct.
- Keep the mod namespace, existing item IDs, save keys and reviewed source artwork stable. Xiaoman is appended to Monday's native queue; never replace native story customers.
- Keep player-visible text in Chinese, Japanese and English. Avoid implementation instructions in item descriptions or other gameplay UI. Use the reviewed story/translation files and existing generation tools rather than hand-editing generated bindings alone.
- Voice generation is an authoring step; runtime only plays packaged audio. Never add service credentials or network TTS to the mod. Offline verification must not make paid generation calls.
- Use Git LFS for media. Do not add personal saves, credentials, game assemblies/decompilations, downloaded toolchains, release archives or local upload state to Git. Preserve source asset hashes when changing development tooling.
- `main` and `v0.0.2` identify the stable baseline; `develop/tavern` is the upcoming tavern branch. Build/check does not install or publish. Workshop uploads are separate and follow an explicit author publishing request.
- Native scanner and existing story checks require a local game installation; do not claim those pass on a machine without its game references. The author handles gameplay testing unless asked otherwise.
