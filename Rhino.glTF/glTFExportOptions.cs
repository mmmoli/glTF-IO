using Rhino.DocObjects;

namespace RhinoGltf;

public enum SubDMode : int
{
    ControlNet = 0,
    Surface = 1,
}

public class GltfSettings
{
    public bool MapRhinoZToGltfY { get; set; } = true;
    public bool ExportMaterials { get; set; } = true;
    public bool UseDisplayColorForUnsetMaterials { get; set; } = true;

    public SubDMode SubDExportMode { get; set; } = SubDMode.Surface;

    public bool ExportTextures { get; set; } = true;
    public bool ExportTextureCoordinates { get; set; } = true;
    public bool ExportVertexNormals { get; set; } = true;
    public bool ExportOpenMeshes { get; set; } = true;
    public bool ExportVertexColors { get; set; }
    public bool UseDracoCompression { get; set; }
    public int DracoCompressionLevel { get; set; } = 10;
    public int DracoQuantizationBitsPosition { get; set; } = 11;
    public int DracoQuantizationBitsNormal { get; set; } = 8;
    public int DracoQuantizationBitsTexture { get; set; } = 10;

    public bool ExportLayers { get; set; }
    public List<RhinoObject> Selected { get; set; } = new();
    public ICustomExporter? CustomExporter { get; set; }
}

public interface ICustomExporter
{
    void MutateObjects(List<ObjectExportData> objects);
    void MutateGltf(GltfSchemaDummy gltf);
}
