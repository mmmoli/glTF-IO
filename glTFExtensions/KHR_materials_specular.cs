using Newtonsoft.Json;
using G = glTFLoader.Schema;

namespace glTFExtensions;

public class KHR_materials_specular
{
    public const string Tag = "KHR_materials_specular";

    [JsonProperty("specularFactor")]
    public float SpecularFactor = 1.0f;

    [JsonProperty("specularTexture")]
    public G.TextureInfo? SpecularTexture;

    [JsonProperty("specularColorFactor")]
    public float[] SpecularColorFactor = new float[3]
    {
        1.0f,
        1.0f,
        1.0f,
    };

    [JsonProperty("specularColorTexture")]
    public G.TextureInfo? SpecularColorTexture;

    public bool ShouldSerializeSpecularTexture()
    {
        return SpecularTexture is not null;
    }

    public bool ShouldSerializeSpecularColorFactor()
    {
        return SpecularColorFactor is not null;
    }

    public bool ShouldSerializeSpecularColorTexture()
    {
        return SpecularColorTexture is not null;
    }

}
