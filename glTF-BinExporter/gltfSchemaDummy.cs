using glTFLoader.Schema;

namespace RhinoGltf;

/// <summary>
/// Helper class to convert to the serializeable class.
/// Primarily just makes things lists so appending is easier.
/// </summary>
public class GltfSchemaDummy
{
    public List<string> ExtensionsUsed = new();
    public List<string> ExtensionsRequired = new();
    public List<Accessor> Accessors = new();
    public List<Animation> Animations = new();
    public Asset? Asset;
    public List<glTFLoader.Schema.Buffer> Buffers = new();
    public List<BufferView> BufferViews = new();
    public List<Camera> Cameras = new();
    public List<Image> Images = new();
    public List<Material> Materials = new();
    public List<Mesh> Meshes = new();
    public List<Node> Nodes = new();
    public List<Sampler> Samplers = new();
    public int Scene;
    public List<GltfSchemaSceneDummy> Scenes = new();
    public List<Skin> Skins = new();
    public List<Texture> Textures = new();
    public Dictionary<string, object> Extensions = new();
    public Extras? Extras;

    public Gltf ToSchemaGltf()
    {
        return new()
        {
            ExtensionsUsed = ExtensionsUsed.Count == 0 ? null : ExtensionsUsed.ToArray(),
            ExtensionsRequired = ExtensionsRequired.Count == 0 ? null : ExtensionsRequired.ToArray(),
            Accessors = Accessors.Count == 0 ? null : Accessors.ToArray(),
            Animations = Animations.Count == 0 ? null : Animations.ToArray(),

            Asset = Asset,

            Buffers = Buffers.Count == 0 ? null : Buffers.ToArray(),
            BufferViews = BufferViews.Count == 0 ? null : BufferViews.ToArray(),
            Cameras = Cameras.Count == 0 ? null : Cameras.ToArray(),
            Images = Images.Count == 0 ? null : Images.ToArray(),
            Materials = Materials.Count == 0 ? null : Materials.ToArray(),
            Meshes = Meshes.Count == 0 ? null : Meshes.ToArray(),
            Nodes = Nodes.Count == 0 ? null : Nodes.ToArray(),
            Samplers = Samplers.Count == 0 ? null : Samplers.ToArray(),

            Scene = Scene,

            Scenes = Scenes.Count == 0 ? null : ConvertScenes(),

            Skins = Skins.Count == 0 ? null : Skins.ToArray(),
            Textures = Textures.Count == 0 ? null : Textures.ToArray(),
            Extensions = Extensions,
            Extras = Extras
        };
    }

    private Scene[] ConvertScenes()
    {
        List<Scene> scenes = new();

        foreach (GltfSchemaSceneDummy dummy in Scenes)
            scenes.Add(dummy.ToSchemaGltf());

        return scenes.ToArray();
    }

}
