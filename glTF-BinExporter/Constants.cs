using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace glTF_BinExporter
{
  public static class Constants
  {
    public static readonly Transform ZtoYUp = new Transform()
    {
      M00 = 1,
      M01 = 0,
      M02 = 0,
      M03 = 0,

      M10 = 0,
      M11 = 0,
      M12 = 1,
      M13 = 0,

      M20 = 0,
      M21 = -1,
      M22 = 0,
      M23 = 0,

      M30 = 0,
      M31 = 0,
      M32 = 0,
      M33 = 1,
    };

    public static readonly ObjectType[] ValidObjectTypes = new ObjectType[] {
            ObjectType.Brep,
            ObjectType.InstanceReference,
            ObjectType.Mesh,
            ObjectType.Extrusion,
            ObjectType.Surface,
            ObjectType.SubD
        };

    public static readonly byte[][] Paddings = new byte[][]
    {
            new byte[] { },
            new byte[] { 0, 0, 0 },
            new byte[] { 0, 0 },
            new byte[] { 0 },
    };

    public const string TextBufferHeader = "data:application/octet-stream;base64,";
    public const string PositionAttributeTag = "POSITION";
    public const string NormalAttributeTag = "NORMAL";
    public const string TexCoord0AttributeTag = "TEXCOORD_0";
    public const string VertexColorAttributeTag = "COLOR_0";
  }

  public class DracoGeometryInfo
  {
    public bool Success;

    public int BufferIndex;
    public int ByteOffset;
    public int ByteLength;

    public int BufferViewIndex;

    public int VerticesCount;
    public float[] VerticesMin;
    public float[] VerticesMax;

    public int IndicesCount;
    public float IndicesMin;
    public float IndicesMax;

    public int NormalsCount;
    public float[] NormalsMin;
    public float[] NormalsMax;

    public int TexCoordsCount;
    public float[] TexCoordsMin;
    public float[] TexCoordsMax;

    public int VertexColorCount;
    public float[] VertexColorMin;
    public float[] VertexColorMax;

    public int VertexAttributePosition;
    public int NormalAttributePosition;
    public int TextureCoordinatesAttributePosition;
    public int VertexColorAttributePosition;
  }
}
