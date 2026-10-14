using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ITKamianets.Engine.Assets.Model;
using ITKamianets.Engine.Core;
using ITKamianets.Engine.Scene;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace ITKamianets.Engine.Assets
{
    /// <summary>
    /// Loads assets by id. For each id, in order:
    /// <list type="number">
    /// <item>an <see cref="AssetCatalog"/> entry (a prefab, material, texture or clip made in Unity);</item>
    /// <item>the asset's file from a loaded library: GLB/glTF models (needs glTFast), PNG/JPG
    /// textures, WAV/OGG/MP3 sounds; materials are built from their values;</item>
    /// <item>for a model that cannot load, its placeholder, or a magenta stand-in if it has none.</item>
    /// </list>
    /// A model is shown at once: its placeholder first, replaced by the real model when it has
    /// loaded. Loaded assets are cached until <see cref="Unload"/>.
    ///
    /// Also the scene loader's <see cref="ISceneAssetResolver"/>: pass <see cref="Default"/> as
    /// <c>SceneLoadOptions.AssetResolver</c> and scene objects show their assets.
    /// </summary>
    public sealed class EngineAssets : ISceneAssetResolver
    {
        private const string LogCategory = "Assets";

        private static EngineAssets _default;

        public static EngineAssets Default => _default ??= new EngineAssets();

        /// <summary>Shader for materials and placeholders. Default: URP Lit. Set it if builds strip that shader.</summary>
        public Shader MaterialShader { get; set; }

        /// <summary>Raised when an asset cannot be loaded, with its id and the reason.</summary>
        public event Action<string, string> LoadFailed;

        private readonly Dictionary<string, AssetData> _definitions = new Dictionary<string, AssetData>();
        private readonly Dictionary<string, string> _sourceUris = new Dictionary<string, string>();
        private readonly List<AssetCatalog> _catalogs = new List<AssetCatalog>();

        private readonly Dictionary<string, Object> _loaded = new Dictionary<string, Object>();
        private readonly HashSet<string> _fromCatalog = new HashSet<string>();
        private readonly Dictionary<string, List<Action<Object>>> _pending = new Dictionary<string, List<Action<Object>>>();
        private readonly HashSet<string> _failed = new HashSet<string>();
        private readonly Dictionary<string, IDisposable> _imports = new Dictionary<string, IDisposable>();
        private readonly Dictionary<Color, Material> _placeholderMaterials = new Dictionary<Color, Material>();

        private AssetsRunner _runner;
        private Transform _prototypes;

        // ---- Libraries and catalogs ----

        /// <summary>Adds a library's assets. Relative source uris are resolved against <paramref name="baseUri"/> (a folder path or URL).</summary>
        public void AddLibrary(AssetLibraryData library, string baseUri)
        {
            foreach (var asset in library.Assets)
            {
                if (string.IsNullOrEmpty(asset.Id))
                {
                    continue;
                }

                if (_definitions.ContainsKey(asset.Id))
                {
                    EngineLog.Warning(LogCategory, $"Asset '{asset.Id}' is defined again by library '{library.LibraryId}'; the new definition wins.");
                }

                _definitions[asset.Id] = asset;
                if (asset.Source != null)
                {
                    _sourceUris[asset.Id] = ResolveUri(baseUri, asset.Source.Uri);
                }
            }

            EngineLog.Debug(LogCategory, $"Library '{library.LibraryId}': {library.Assets.Count} assets.");
        }

        /// <summary>Reads a library file from disk; its sources are relative to the file's folder.</summary>
        public AssetLibraryData LoadLibrary(string path)
        {
            var library = AssetJson.Deserialize(File.ReadAllText(path));
            AddLibrary(library, Path.GetDirectoryName(Path.GetFullPath(path)));
            return library;
        }

        /// <summary>Downloads a library (http(s), or a platform path such as StreamingAssets on Android); its sources are relative to the URL.</summary>
        public void LoadLibraryFromUrl(string url, Action<AssetLibraryData> done)
        {
            Runner.StartCoroutine(Download(UnityWebRequest.Get(url), request =>
            {
                if (request == null)
                {
                    done?.Invoke(null);
                    return;
                }

                var library = AssetJson.Deserialize(request.downloadHandler.text);
                AddLibrary(library, url.Substring(0, url.LastIndexOf('/') + 1));
                done?.Invoke(library);
            }));
        }

        public void AddCatalog(AssetCatalog catalog)
        {
            if (catalog != null && !_catalogs.Contains(catalog))
            {
                _catalogs.Add(catalog);
            }
        }

        public bool TryGetDefinition(string id, out AssetData definition)
        {
            return _definitions.TryGetValue(id, out definition);
        }

        public bool IsLoaded(string id)
        {
            return _loaded.ContainsKey(id);
        }

        // ---- Loading ----

        /// <summary>
        /// Loads an asset as <typeparamref name="T"/>: a GameObject (model prototype; use
        /// <see cref="Instantiate"/> to place it), Material, Texture2D or AudioClip. Calls
        /// <paramref name="done"/> with null when it cannot be loaded.
        /// </summary>
        public void Load<T>(string id, Action<T> done) where T : Object
        {
            LoadObject(id, loaded => done?.Invoke(loaded as T));
        }

        /// <summary>
        /// Places a model under <paramref name="parent"/> right away. The returned object stays;
        /// its content is the placeholder until the model has loaded, then the model.
        /// <paramref name="ready"/> is called once the final content is in.
        /// </summary>
        public GameObject Instantiate(string id, Transform parent, Action<GameObject> ready = null)
        {
            var root = new GameObject(id);
            root.transform.SetParent(parent, false);

            if (_loaded.TryGetValue(id, out var cached) && cached is GameObject prototype)
            {
                AddContent(root, prototype);
                ready?.Invoke(root);
                return root;
            }

            _definitions.TryGetValue(id, out var definition);
            var placeholder = BuildPlaceholder(definition, missing: definition == null && !HasCatalogEntry(id));
            if (placeholder != null)
            {
                placeholder.transform.SetParent(root.transform, false);
            }

            LoadObject(id, loaded =>
            {
                if (root == null)
                {
                    return;
                }

                if (loaded is GameObject model)
                {
                    DestroyObject(placeholder);
                    AddContent(root, model);
                }
                else if (definition?.Placeholder == null)
                {
                    // Nothing to show instead: make it obvious, not invisible.
                    DestroyObject(placeholder);
                    placeholder = BuildPlaceholder(definition, missing: true);
                    placeholder.transform.SetParent(root.transform, false);
                }

                ready?.Invoke(root);
            });

            return root;
        }

        void ISceneAssetResolver.Resolve(SceneObject target)
        {
            Instantiate(target.Asset, target.transform);
        }

        // ---- Unloading ----

        /// <summary>Frees a loaded asset. Objects already placed keep their copies; materials and meshes they share are destroyed.</summary>
        public void Unload(string id)
        {
            _failed.Remove(id);
            if (!_loaded.TryGetValue(id, out var loaded))
            {
                return;
            }

            _loaded.Remove(id);
            if (!_fromCatalog.Remove(id))
            {
                DestroyObject(loaded);
            }

            if (_imports.TryGetValue(id, out var import))
            {
                import.Dispose();
                _imports.Remove(id);
            }
        }

        public void UnloadAll()
        {
            foreach (var id in new List<string>(_loaded.Keys))
            {
                Unload(id);
            }

            _failed.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefault()
        {
            _default = null;
        }

        // ---- Internals ----

        private void LoadObject(string id, Action<Object> done)
        {
            if (string.IsNullOrEmpty(id))
            {
                done(null);
                return;
            }

            if (_loaded.TryGetValue(id, out var cached))
            {
                done(cached);
                return;
            }

            if (_failed.Contains(id))
            {
                done(null);
                return;
            }

            if (_pending.TryGetValue(id, out var waiting))
            {
                waiting.Add(done);
                return;
            }

            _pending[id] = new List<Action<Object>> { done };

            foreach (var catalog in _catalogs)
            {
                if (catalog.TryGet(id, out var fromCatalog))
                {
                    _fromCatalog.Add(id);
                    Complete(id, fromCatalog);
                    return;
                }
            }

            if (!_definitions.TryGetValue(id, out var definition))
            {
                Fail(id, "no library or catalog defines it");
                return;
            }

            _sourceUris.TryGetValue(id, out var uri);
            switch (definition.Kind)
            {
                case AssetKind.Model:
                    LoadModel(definition, uri);
                    break;
                case AssetKind.Material:
                    BuildMaterial(definition);
                    break;
                case AssetKind.Texture:
                    Runner.StartCoroutine(Download(UnityWebRequestTexture.GetTexture(uri), request =>
                    {
                        var texture = request != null ? DownloadHandlerTexture.GetContent(request) : null;
                        if (texture != null)
                        {
                            texture.name = id;
                            texture.wrapMode = TextureWrapMode.Repeat;
                        }

                        Finish(id, texture, $"could not load texture {uri}");
                    }));
                    break;
                case AssetKind.Audio:
                    Runner.StartCoroutine(Download(UnityWebRequestMultimedia.GetAudioClip(uri, AudioTypeFor(definition.Source.Format)), request =>
                    {
                        var clip = request != null ? DownloadHandlerAudioClip.GetContent(request) : null;
                        if (clip != null)
                        {
                            clip.name = id;
                        }

                        Finish(id, clip, $"could not load sound {uri}");
                    }));
                    break;
            }
        }

        private void LoadModel(AssetData definition, string uri)
        {
            if (definition.Source == null)
            {
                // Placeholder-only model (not made yet): nothing to load, no warning.
                _failed.Add(definition.Id);
                Complete(definition.Id, null);
                return;
            }

            var format = definition.Source.Format;
            if (format != "glb" && format != "gltf")
            {
                Fail(definition.Id, $"models must be glb or gltf, not '{format}'");
                return;
            }

#if GLTFAST_PRESENT
            LoadGltf(definition, uri);
#else
            Fail(definition.Id, "loading GLB/glTF needs the glTFast package (com.unity.cloud.gltfast)");
#endif
        }

#if GLTFAST_PRESENT
        private async void LoadGltf(AssetData definition, string uri)
        {
            var id = definition.Id;
            var import = new GLTFast.GltfImport();
            try
            {
                if (!await import.Load(uri))
                {
                    import.Dispose();
                    Fail(id, $"could not load model {uri}");
                    return;
                }

                var prototype = new GameObject(id);
                prototype.transform.SetParent(Prototypes, false);
                if (!await import.InstantiateMainSceneAsync(prototype.transform))
                {
                    DestroyObject(prototype);
                    import.Dispose();
                    Fail(id, $"could not build model {uri}");
                    return;
                }

                _imports[id] = import;
                ApplyMaterials(prototype, definition.Materials, () => Complete(id, prototype));
            }
            catch (Exception exception)
            {
                import.Dispose();
                Fail(id, $"{uri}: {exception.Message}");
            }
        }
#endif

        // Replaces each renderer's material slots, in order, with the listed material assets.
        private void ApplyMaterials(GameObject model, List<string> materialIds, Action done)
        {
            if (materialIds == null || materialIds.Count == 0)
            {
                done();
                return;
            }

            var materials = new Material[materialIds.Count];
            var remaining = materialIds.Count;
            for (var i = 0; i < materialIds.Count; i++)
            {
                var slot = i;
                Load<Material>(materialIds[i], material =>
                {
                    materials[slot] = material;
                    if (--remaining > 0)
                    {
                        return;
                    }

                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    {
                        var shared = renderer.sharedMaterials;
                        for (var s = 0; s < shared.Length && s < materials.Length; s++)
                        {
                            if (materials[s] != null)
                            {
                                shared[s] = materials[s];
                            }
                        }

                        renderer.sharedMaterials = shared;
                    }

                    done();
                });
            }
        }

        private void BuildMaterial(AssetData definition)
        {
            var data = definition.Material ?? new MaterialData();
            var material = new Material(Shader) { name = definition.Id };

            var baseColor = data.BaseColor?.ToColor() ?? Color.white;
            material.SetColor("_BaseColor", baseColor);
            material.color = baseColor;
            material.SetFloat("_Metallic", data.Metallic ?? 0f);
            material.SetFloat("_Smoothness", 1f - (data.Roughness ?? 0.5f));

            if (data.Emission != null)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", data.Emission.ToColor());
            }

            if (data.Transparent == true)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            if (string.IsNullOrEmpty(data.BaseColorTexture))
            {
                Complete(definition.Id, material);
                return;
            }

            Load<Texture2D>(data.BaseColorTexture, texture =>
            {
                if (texture != null)
                {
                    material.SetTexture("_BaseMap", texture);
                    material.mainTexture = texture;
                }

                Complete(definition.Id, material);
            });
        }

        private GameObject BuildPlaceholder(AssetData definition, bool missing)
        {
            var placeholder = missing ? null : definition?.Placeholder;
            if (!missing && placeholder == null)
            {
                return null;
            }

            var shape = placeholder?.Shape ?? "box";
            var primitive = shape == "sphere" ? PrimitiveType.Sphere : shape == "capsule" ? PrimitiveType.Capsule : PrimitiveType.Cube;
            var go = GameObject.CreatePrimitive(primitive);
            go.name = missing ? "Missing asset" : "Placeholder";
            Object.DestroyImmediate(go.GetComponent<Collider>());

            // Bounds are in the asset's right-handed space, front +Z (glTF's); Unity flips x.
            var size = Vector3.one;
            var center = new Vector3(0f, 0.5f, 0f);
            if (definition?.Bounds != null)
            {
                var min = definition.Bounds.Min;
                var max = definition.Bounds.Max;
                size = new Vector3(Mathf.Abs(max.X - min.X), Mathf.Abs(max.Y - min.Y), Mathf.Abs(max.Z - min.Z));
                center = new Vector3(-(min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f);
            }

            go.transform.localPosition = center;
            go.transform.localScale = primitive == PrimitiveType.Capsule ? new Vector3(size.x, size.y * 0.5f, size.z) : size;

            var color = missing ? Color.magenta : placeholder.Color?.ToColor() ?? Color.gray;
            go.GetComponent<Renderer>().sharedMaterial = PlaceholderMaterial(color);
            return go;
        }

        private Material PlaceholderMaterial(Color color)
        {
            if (!_placeholderMaterials.TryGetValue(color, out var material) || material == null)
            {
                material = new Material(Shader) { name = "Placeholder", color = color };
                material.SetColor("_BaseColor", color);
                _placeholderMaterials[color] = material;
            }

            return material;
        }

        private void AddContent(GameObject root, GameObject prototype)
        {
            var content = Object.Instantiate(prototype, root.transform, false);
            content.name = prototype.name;
        }

        private bool HasCatalogEntry(string id)
        {
            foreach (var catalog in _catalogs)
            {
                if (catalog.TryGet(id, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private void Finish(string id, Object loaded, string failure)
        {
            if (loaded != null)
            {
                Complete(id, loaded);
            }
            else
            {
                Fail(id, failure);
            }
        }

        private void Fail(string id, string reason)
        {
            _failed.Add(id);
            EngineLog.Warning(LogCategory, $"Asset '{id}' not loaded: {reason}.");
            LoadFailed?.Invoke(id, reason);
            Complete(id, null);
        }

        private void Complete(string id, Object loaded)
        {
            if (loaded != null)
            {
                _loaded[id] = loaded;
            }

            if (!_pending.TryGetValue(id, out var waiting))
            {
                return;
            }

            _pending.Remove(id);
            foreach (var callback in waiting)
            {
                try
                {
                    callback(loaded);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private Shader Shader => MaterialShader != null ? MaterialShader : MaterialShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        private AssetsRunner Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("[EngineAssets]") { hideFlags = HideFlags.HideInHierarchy };
                    if (Application.isPlaying)
                    {
                        Object.DontDestroyOnLoad(go);
                    }

                    _runner = go.AddComponent<AssetsRunner>();
                }

                return _runner;
            }
        }

        // Loaded models wait here, inactive, to be copied into the scene.
        private Transform Prototypes
        {
            get
            {
                if (_prototypes == null)
                {
                    var go = new GameObject("Prototypes") { hideFlags = HideFlags.HideInHierarchy };
                    go.SetActive(false);
                    go.transform.SetParent(Runner.transform, false);
                    _prototypes = go.transform;
                }

                return _prototypes;
            }
        }

        private static IEnumerator Download(UnityWebRequest request, Action<UnityWebRequest> done)
        {
            using (request)
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    EngineLog.Warning(LogCategory, $"{request.url}: {request.error}");
                    done(null);
                }
                else
                {
                    done(request);
                }
            }
        }

        private static string ResolveUri(string baseUri, string uri)
        {
            if (string.IsNullOrEmpty(uri) || uri.Contains("://"))
            {
                return uri;
            }

            if (string.IsNullOrEmpty(baseUri))
            {
                return uri;
            }

            if (baseUri.Contains("://"))
            {
                return new Uri(new Uri(baseUri.EndsWith("/") ? baseUri : baseUri + "/"), uri).AbsoluteUri;
            }

            return new Uri(Path.GetFullPath(Path.Combine(baseUri, uri))).AbsoluteUri;
        }

        private static AudioType AudioTypeFor(string format)
        {
            switch (format)
            {
                case "ogg": return AudioType.OGGVORBIS;
                case "mp3": return AudioType.MPEG;
                default: return AudioType.WAV;
            }
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>Runs downloads; hidden and kept across scene loads.</summary>
        private sealed class AssetsRunner : MonoBehaviour
        {
        }
    }
}
