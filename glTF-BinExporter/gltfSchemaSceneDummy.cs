using glTFLoader.Schema;

namespace RhinoGltf;

public class GltfSchemaSceneDummy
{
    public List<int> Nodes = new();
    public string? Name;
    public Dictionary<string, object> Extensions = new();

    public Scene ToSchemaGltf()
    {
        return new()
        {
            Nodes = Nodes.Count == 0 ? null : Nodes.ToArray(),
            Name = Name,
            Extensions = Extensions
        };
    }
}
