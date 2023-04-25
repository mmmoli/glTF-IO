using RhinoGltf;

namespace Rhino.glTF;

public static class Convert
{
    public static byte[] ToGlb(GltfSettings options)
    {
        var objects = options.Selected;
        var doc = objects.FirstOrDefault()?.Document;

        if (doc is null)
            return Array.Empty<byte>();

        var workflow = doc.RenderSettings.LinearWorkflow;
        RhinoDocGltfConverter converter = new(options, true, doc, workflow);
        var gltf = converter.ConvertToGltf();

        //using MemoryStream ms = new();
        //glTFLoader.Interface.SaveModel(gltf, ms);
        //var json = Encoding.UTF8.GetString(ms.ToArray());

        byte[] bytes = converter.GetBinaryBuffer();
        using MemoryStream stream = new();
        glTFLoader.Interface.SaveBinaryModel(gltf, bytes.Length == 0 ? null : bytes, stream);
        return stream.ToArray();
    }
}
