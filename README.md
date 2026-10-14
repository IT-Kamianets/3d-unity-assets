# IT-Kamianets 3D Engine: Unity Assets

`3d-unity-assets` provides the asset layer of the **IT-Kamianets 3D Engine** for Unity.

It is responsible for discovering, loading, caching, and managing 3D assets used by engine scenes.

### Logic

Scenes should reference assets instead of embedding implementation-specific Unity objects.

An object may reference an asset such as a chair, vehicle, tree, wall module, or generated model, while this package determines how that asset is loaded into Unity.

It provides the bridge between **asset metadata** (`3d-asset-schema` libraries) and **actual Unity runtime objects**. For each asset id it tries, in order:

1. an `AssetCatalog` entry: a prefab, material, texture or AudioClip made in Unity, so an id can point at a hand-built prefab while other engines read the same id's GLB;
2. the asset's file from a loaded library: GLB/glTF models (through glTFast), PNG/JPG textures, WAV/OGG/MP3 sounds; materials are built from their values (URP Lit);
3. for a model, its placeholder (a shape and color sized to its bounds); with no placeholder, a magenta stand-in and a warning, so a missing asset is obvious, not invisible.

A model is placed at once: the placeholder shows until the real model has loaded and replaces it. Loaded assets are cached and shared until unloaded.

It is the scene loader's `ISceneAssetResolver`: `3d-unity-scene` knows only asset ids, this package knows how to load them.

### Status

Version 0.1.

* C# model of `3d-asset-schema` v1 (`AssetLibraryData`, `AssetData`, ... and `AssetJson`)
* `EngineAssets.Default`: `LoadLibrary(path)`, `LoadLibraryFromUrl(url)` (sources resolve relative to the library), `AddCatalog`, `Load<T>(id, done)` for models, materials, textures and sounds, `Instantiate(id, parent)` with placeholder → model replacement, `Unload(id)`, `UnloadAll()`, `LoadFailed` event
* GLB/glTF needs `com.unity.cloud.gltfast` in the project (optional dependency, `GLTFAST_PRESENT`); without it models fall back to their placeholders with a warning
* Model `materials` from the library replace the model's own, in order

Checked in Play mode with `3d-asset-schema/examples`: the crate shows its placeholder, then the GLB with the wood material and texture; the placeholder-only barrel stays a 0.7 × 1.1 m capsule; an unknown id shows the magenta stand-in; the texture (16×16) and sound (0.12 s) load; the arena scene's crates get their models through the resolver, standing on the floor, rotated and scaled as in the scene.

```csharp
var assets = EngineAssets.Default;
assets.LoadLibrary(Path.Combine(Application.streamingAssetsPath, "props.library.json"));
assets.AddCatalog(myCatalog);

var level = SceneLoader.Load(json, null, new SceneLoadOptions { AssetResolver = assets });
assets.Instantiate("props/crate-wood", parent);
assets.Load<AudioClip>("sounds/footstep-stone-1", clip => source.PlayOneShot(clip));
```

### Roadmap

* Asset definition format
* Local asset loading
* Remote asset loading
* GLB/glTF support
* Unity prefab support
* Materials
* Textures
* Asset metadata
* Asset IDs
* Asset caching
* Async loading
* Asset unloading
* Placeholder assets
* Missing-asset handling
* Runtime asset replacement
* Asset library API
* Performance optimization
* Stable v1 asset API
