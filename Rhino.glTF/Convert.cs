using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino.DocObjects;
using glTF_BinExporter;

namespace Rhino.glTF;

public static class Convert
{
    public static byte[] ToGlb(glTFExportOptions options, IEnumerable<RhinoObject> objects)
    {
        var doc = objects.FirstOrDefault()?.Document;

        if (doc is null)
            return new byte[0];

        var workflow = doc.RenderSettings.LinearWorkflow;
        RhinoDocGltfConverter converter = new(options, true, doc, objects, workflow);
        glTFLoader.Schema.Gltf gltf = converter.ConvertToGltf();

        byte[] bytes = converter.GetBinaryBuffer();
        using MemoryStream stream = new();
        glTFLoader.Interface.SaveBinaryModel(gltf, bytes.Length == 0 ? null : bytes, stream);
        return stream.ToArray();
    }
}
