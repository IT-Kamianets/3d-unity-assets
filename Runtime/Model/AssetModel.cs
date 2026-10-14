using System.Collections.Generic;
using System.Runtime.Serialization;
using ITKamianets.Engine.Scene.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ITKamianets.Engine.Assets.Model
{
    // C# mirror of 3d-asset-schema v1. Field names match the schema exactly; Vector3, Bounds3D
    // and Metadata reuse 3d-unity-scene's types, which have the same shape in both schemas.

    /// <summary>Mirrors asset-library.schema.json.</summary>
    public class AssetLibraryData
    {
        public const string CurrentSchemaVersion = "1.0.0";

        [JsonProperty("schemaVersion")] public string SchemaVersion = CurrentSchemaVersion;
        [JsonProperty("libraryId")] public string LibraryId;
        [JsonProperty("units")] public string Units = "meters";
        [JsonProperty("assets")] public List<AssetData> Assets = new List<AssetData>();
        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)] public MetadataData Metadata;
    }

    /// <summary>Mirrors asset.schema.json#/$defs/AssetKind.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AssetKind
    {
        [EnumMember(Value = "model")] Model,
        [EnumMember(Value = "material")] Material,
        [EnumMember(Value = "texture")] Texture,
        [EnumMember(Value = "audio")] Audio,
    }

    /// <summary>Mirrors asset.schema.json#/$defs/Asset.</summary>
    public class AssetData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name;
        [JsonProperty("kind")] public AssetKind Kind;
        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)] public AssetSourceData Source;
        [JsonProperty("material", NullValueHandling = NullValueHandling.Ignore)] public MaterialData Material;
        [JsonProperty("materials", NullValueHandling = NullValueHandling.Ignore)] public List<string> Materials;
        [JsonProperty("bounds", NullValueHandling = NullValueHandling.Ignore)] public BoundsData Bounds;
        [JsonProperty("placeholder", NullValueHandling = NullValueHandling.Ignore)] public PlaceholderData Placeholder;
        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)] public MetadataData Metadata;
    }

    /// <summary>Mirrors asset.schema.json#/$defs/Source.</summary>
    public class AssetSourceData
    {
        /// <summary>As written in the library: relative to the library file, or an absolute URL.</summary>
        [JsonProperty("uri")] public string Uri;

        /// <summary>glb, gltf, png, jpg, wav, ogg or mp3.</summary>
        [JsonProperty("format")] public string Format;
    }

    /// <summary>Mirrors asset.schema.json#/$defs/Material.</summary>
    public class MaterialData
    {
        [JsonProperty("baseColor", NullValueHandling = NullValueHandling.Ignore)] public ColorData BaseColor;
        [JsonProperty("baseColorTexture", NullValueHandling = NullValueHandling.Ignore)] public string BaseColorTexture;
        [JsonProperty("metallic", NullValueHandling = NullValueHandling.Ignore)] public float? Metallic;
        [JsonProperty("roughness", NullValueHandling = NullValueHandling.Ignore)] public float? Roughness;
        [JsonProperty("emission", NullValueHandling = NullValueHandling.Ignore)] public ColorData Emission;
        [JsonProperty("transparent", NullValueHandling = NullValueHandling.Ignore)] public bool? Transparent;
    }

    /// <summary>Mirrors asset.schema.json#/$defs/Placeholder.</summary>
    public class PlaceholderData
    {
        /// <summary>box, sphere or capsule.</summary>
        [JsonProperty("shape")] public string Shape = "box";
        [JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)] public ColorData Color;
    }

    /// <summary>Mirrors common.schema.json#/$defs/Color: sRGB, 0..1.</summary>
    public class ColorData
    {
        [JsonProperty("r")] public float R;
        [JsonProperty("g")] public float G;
        [JsonProperty("b")] public float B;
        [JsonProperty("a")] public float A = 1f;

        public UnityEngine.Color ToColor()
        {
            return new UnityEngine.Color(R, G, B, A);
        }
    }

    /// <summary>Reads and writes asset libraries in the schema's JSON shape.</summary>
    public static class AssetJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };

        public static AssetLibraryData Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<AssetLibraryData>(json, Settings);
        }

        public static string Serialize(AssetLibraryData library)
        {
            return JsonConvert.SerializeObject(library, Settings);
        }
    }
}
