# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial Unity package structure (`Runtime`, `Editor`, `Tests` assemblies).
- C# model of 3d-asset-schema v1, `AssetJson`.
- `EngineAssets`: libraries, `AssetCatalog`, loading models (glTFast, optional), materials, textures, sounds; placeholders, missing-asset stand-in, caching, unloading; `ISceneAssetResolver` for 3d-unity-scene.

### Fixed

- The hidden loader object no longer survives the end of Play mode in the Editor (was `HideAndDontSave`, piling up one per session).
