using Newtonsoft.Json;
using G = glTFLoader.Schema;

namespace glTFExtensions;

public class KHR_materials_clearcoat
{
    public const string Tag = "KHR_materials_clearcoat";

    [JsonProperty("clearcoatFactor")]
    public float ClearcoatFactor = 0.0f;

    [JsonProperty("clearcoatTexture")]
    public G.TextureInfo? ClearcoatTexture;

    [JsonProperty("clearcoatRoughnessFactor")]
    public float ClearcoatRoughnessFactor = 0.0f;

    [JsonProperty("clearcoatRoughnessTexture")]
    public G.TextureInfo? ClearcoatRoughnessTexture;

    [JsonProperty("clearcoatNormalTexture")]
    public G.MaterialNormalTextureInfo? ClearcoatNormalTexture;

    public bool ShouldSerializeClearcoatFactor()
    {
        return ClearcoatFactor != 0.0f;
    }

    public bool ShouldSerializeClearcoatTexture()
    {
        return ClearcoatTexture != null;
    }

    public bool ShouldSerializeClearcoatRoughnessFactor()
    {
        return ClearcoatRoughnessFactor != 0.0f;
    }

    public bool ShouldSerializeClearcoatRoughnessTexture()
    {
        return ClearcoatRoughnessTexture != null;
    }

    public bool ShouldSerializeClearcoatNormalTexture()
    {
        return ClearcoatNormalTexture != null;
    }
}
