using Newtonsoft.Json;
using G = glTFLoader.Schema;

namespace glTFExtensions;

public class KHR_materials_transmission
{
    public const string Tag = "KHR_materials_transmission";

    [JsonProperty("transmissionFactor")]
    public float TransmissionFactor = 0.0f;

    [JsonProperty("transmissionTexture")]
    public G.TextureInfo? TransmissionTexture;

    public bool ShouldSerializeTransmissionFactor()
    {
        return TransmissionFactor != 0.0f;
    }

    public bool ShouldSerializeTransmissionTexture()
    {
        return TransmissionTexture is not null;
    }
}
