using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ITKamianets.Engine.Assets
{
    /// <summary>
    /// Asset ids mapped to things made in Unity: a prefab for a model, a Material, a Texture, an
    /// AudioClip. Checked before any file is loaded, so an id can point at a hand-made prefab while
    /// other engines read the same id's GLB. Create with Assets → Create → IT-Kamianets → Asset Catalog
    /// and add it with <see cref="EngineAssets.AddCatalog"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "IT-Kamianets/Asset Catalog", fileName = "AssetCatalog")]
    public class AssetCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Asset id, e.g. props/crate-wood")]
            public string Id;

            [Tooltip("Prefab (model), Material, Texture or AudioClip")]
            public Object Asset;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public List<Entry> Entries => entries;

        public bool TryGet(string id, out Object asset)
        {
            foreach (var entry in entries)
            {
                if (entry.Id == id && entry.Asset != null)
                {
                    asset = entry.Asset;
                    return true;
                }
            }

            asset = null;
            return false;
        }
    }
}
