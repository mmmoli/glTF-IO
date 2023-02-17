using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino.DocObjects;
using glTF_BinExporter;
using System.Text;

namespace Rhino.glTF;

public static class Convert
{
  public static byte[] ToGlb(glTFExportOptions options, IEnumerable<RhinoObject> objects, Action<List<ObjectExportData>> transform, Action<gltfSchemaDummy> gltfAdd)
  {
    var doc = objects.FirstOrDefault()?.Document;

    if (doc is null)
      return new byte[0];

    var workflow = doc.RenderSettings.LinearWorkflow;
    RhinoDocGltfConverter converter = new(options, true, doc, objects, workflow, transform, gltfAdd);
    glTFLoader.Schema.Gltf gltf = converter.ConvertToGltf();

    //using MemoryStream ms = new();
    //glTFLoader.Interface.SaveModel(gltf, ms);
    //var json = Encoding.UTF8.GetString(ms.ToArray());

    byte[] bytes = converter.GetBinaryBuffer();
    using MemoryStream stream = new();
    glTFLoader.Interface.SaveBinaryModel(gltf, bytes.Length == 0 ? null : bytes, stream);
    return stream.ToArray();
  }
}
