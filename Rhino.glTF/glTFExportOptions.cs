
namespace glTF_BinExporter;

public enum SubDMode : int
{
    ControlNet = 0,
    Surface = 1,
}

public class glTFExportOptions
{
    public bool MapRhinoZToGltfY { get; set; } = true;
    public bool ExportMaterials { get; set; } = true;
    public bool UseDisplayColorForUnsetMaterials { get; set; } = true;

    public SubDMode SubDExportMode { get; set; } = SubDMode.Surface;
    public int SubDLevel { get; set; } = 4;

    public bool ExportTextureCoordinates { get; set; } = true;
    public bool ExportVertexNormals { get; set; } = true;
    public bool ExportOpenMeshes { get; set; } = true;
    public bool ExportVertexColors { get; set; } = false;

    public bool UseDracoCompression { get; set; } = false;
    public int DracoCompressionLevel { get; set; } = 10;
    public int DracoQuantizationBitsPosition { get; set; } = 11;
    public int DracoQuantizationBitsNormal { get; set; } = 8;
    public int DracoQuantizationBitsTexture { get; set; } = 10;

    public bool ExportLayers { get; set; }
}
