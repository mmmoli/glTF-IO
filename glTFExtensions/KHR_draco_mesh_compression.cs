using Newtonsoft.Json;

namespace glTFExtensions;

public class KHR_draco_mesh_compression
{
    public const string Tag = "KHR_draco_mesh_compression";

    [JsonProperty("bufferView")]
    public int BufferView;

    [JsonProperty("attributes")]
    public Dictionary<string, int> Attributes = new();
}
