# IT-Kamianets 3D Engine: Unity Assets

`3d-unity-assets` provides the asset layer of the **IT-Kamianets 3D Engine** for Unity.

It is responsible for discovering, loading, caching, and managing 3D assets used by engine scenes.

### Logic

Scenes should reference assets instead of embedding implementation-specific Unity objects.

An object may reference an asset such as a chair, vehicle, tree, wall module, or generated model, while this package determines how that asset is loaded into Unity.

It provides the bridge between **asset metadata** and **actual Unity runtime objects**.

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
