using Rhino.DocObjects;
using Rhino.Geometry;
using System.Drawing;
using G = glTFLoader.Schema;

namespace RhinoGltf;

class RhinoPointCloudGltfConverter
{
    readonly RhinoObject _rhinoObject;
    readonly GltfSettings _options;
    readonly bool _binary;
    readonly GltfSchemaDummy _dummy;
    readonly List<byte> _binaryBuffer;

    public RhinoPointCloudGltfConverter(RhinoObject rhinoObject, GltfSettings options, bool binary, GltfSchemaDummy dummy, List<byte> binaryBuffer)
    {
        _rhinoObject = rhinoObject;
        _options = options;
        _binary = binary;
        _dummy = dummy;
        _binaryBuffer = binaryBuffer;
    }

    public int AddPointCloud()
    {
        if (_rhinoObject.Geometry.Duplicate() is not PointCloud pointCloud)
            return -1;

        if (_options.MapRhinoZToGltfY)
            pointCloud.Transform(Constants.ZtoYUp);

        Point3d[] points = pointCloud.GetPoints();
        int vertexAccessor = GetVertexAccessor(points);

        G.MeshPrimitive primitive = new()
        {
            Mode = G.MeshPrimitive.ModeEnum.POINTS,
            Attributes = new(),
        };

        primitive.Attributes.Add(Constants.PositionAttributeTag, vertexAccessor);

        if (pointCloud.ContainsColors)
        {
            System.Drawing.Color[] colors = pointCloud.GetColors();
            int colorsAccessorIdx = GetVertexColorAccessor(colors);
            primitive.Attributes.Add(Constants.VertexColorAttributeTag, colorsAccessorIdx);
        }

        if (pointCloud.ContainsNormals)
        {
            Vector3d[] normals = pointCloud.GetNormals();
            int normalsAccessorIdx = GetNormalsAccessor(normals);
            primitive.Attributes.Add(Constants.NormalAttributeTag, normalsAccessorIdx);
        }

        G.Mesh mesh = new()
        {
            Primitives = new[] { primitive },
        };

        return _dummy.Meshes.AddAndReturnIndex(mesh);
    }

    int GetVertexAccessor(Point3d[] points)
    {
        int bufferViewIndex = GetBufferView(points, out Point3d min, out Point3d max, out int count);

        G.Accessor accessor = new()
        {
            BufferView = bufferViewIndex,
            ByteOffset = 0,
            ComponentType = G.Accessor.ComponentTypeEnum.FLOAT,
            Count = count,
            Min = min.ToFloatArray(),
            Max = max.ToFloatArray(),
            Type = G.Accessor.TypeEnum.VEC3,
        };

        return _dummy.Accessors.AddAndReturnIndex(accessor);
    }

    int GetBufferView(Point3d[] points, out Point3d min, out Point3d max, out int count)
    {
        int buffer;
        int byteLength;
        int byteOffset = 0;

        if (_binary)
        {
            byte[] bytes = GetVertexBytes(points, out min, out max);
            buffer = 0;
            byteLength = bytes.Length;
            byteOffset = _binaryBuffer.Count;
            _binaryBuffer.AddRange(bytes);
        }
        else
        {
            buffer = GetVertexBuffer(points, out min, out max, out byteLength);
        }

        G.BufferView vertexBufferView = new()
        {
            Buffer = buffer,
            ByteOffset = byteOffset,
            ByteLength = byteLength,
            Target = G.BufferView.TargetEnum.ARRAY_BUFFER,
        };

        count = points.Length;

        return _dummy.BufferViews.AddAndReturnIndex(vertexBufferView);
    }

    int GetVertexBuffer(Point3d[] points, out Point3d min, out Point3d max, out int length)
    {
        byte[] bytes = GetVertexBytes(points, out min, out max);

        length = bytes.Length;

        G.Buffer buffer = new()
        {
            Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
            ByteLength = length,
        };

        return _dummy.Buffers.AddAndReturnIndex(buffer);
    }

    static byte[] GetVertexBytes(Point3d[] points, out Point3d min, out Point3d max)
    {
        min = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        max = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

        List<float> floats = new(points.Length * 3);

        foreach (Point3d vertex in points)
        {
            floats.AddRange(new float[] { (float)vertex.X, (float)vertex.Y, (float)vertex.Z });

            min.X = Math.Min(min.X, vertex.X);
            max.X = Math.Max(max.X, vertex.X);

            min.Y = Math.Min(min.Y, vertex.Y);
            max.Y = Math.Max(max.Y, vertex.Y);

            min.Z = Math.Min(min.Z, vertex.Z);
            max.Z = Math.Max(max.Z, vertex.Z);
        }

        IEnumerable<byte> bytesEnumerable = floats.SelectMany(value => BitConverter.GetBytes(value));

        return bytesEnumerable.ToArray();
    }

    int GetVertexColorAccessor(Color[] vertexColors)
    {
        int vertexColorsBufferViewIdx = GetVertexColorBufferView(vertexColors, out Rhino.Display.Color4f min, out Rhino.Display.Color4f max, out int countVertexColors);

        var type = _options.UseDracoCompression ? G.Accessor.ComponentTypeEnum.UNSIGNED_BYTE : G.Accessor.ComponentTypeEnum.FLOAT;

        G.Accessor vertexColorAccessor = new()
        {
            BufferView = vertexColorsBufferViewIdx,
            ByteOffset = 0,
            Count = countVertexColors,
            ComponentType = type,
            Min = min.ToFloatArray(),
            Max = max.ToFloatArray(),
            Type = G.Accessor.TypeEnum.VEC4,
            Normalized = _options.UseDracoCompression,
        };

        return _dummy.Accessors.AddAndReturnIndex(vertexColorAccessor);
    }

    int GetVertexColorBufferView(Color[] colors, out Rhino.Display.Color4f min, out Rhino.Display.Color4f max, out int countVertexColors)
    {
        int buffer = 0;
        int byteLength;
        int byteOffset = 0;

        if (_binary)
        {
            byte[] bytes = GetVertexColorBytes(colors, out min, out max);
            byteLength = bytes.Length;
            byteOffset = _binaryBuffer.Count;
            _binaryBuffer.AddRange(bytes);
        }
        else
        {
            buffer = GetVertexColorBuffer(colors, out min, out max, out byteLength);
        }

        G.BufferView vertexColorsBufferView = new()
        {
            Buffer = buffer,
            ByteLength = byteLength,
            ByteOffset = byteOffset,
            Target = G.BufferView.TargetEnum.ARRAY_BUFFER,
        };

        countVertexColors = colors.Length;

        return _dummy.BufferViews.AddAndReturnIndex(vertexColorsBufferView);
    }

    int GetVertexColorBuffer(Color[] colors, out Rhino.Display.Color4f min, out Rhino.Display.Color4f max, out int byteLength)
    {
        byte[] bytes = GetVertexColorBytes(colors, out min, out max);

        G.Buffer vertexColorsBuffer = new()
        {
            Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
            ByteLength = bytes.Length,
        };

        byteLength = bytes.Length;

        return _dummy.Buffers.AddAndReturnIndex(vertexColorsBuffer);
    }

    static byte[] GetVertexColorBytes(Color[] colors, out Rhino.Display.Color4f min, out Rhino.Display.Color4f max)
    {
        float[] minArr = new float[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        float[] maxArr = new float[] { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };

        List<float> colorFloats = new(colors.Length * 4);

        for (int i = 0; i < colors.Length; i++)
        {
            Rhino.Display.Color4f color = new(colors[i]);

            colorFloats.AddRange(color.ToFloatArray());

            minArr[0] = Math.Min(minArr[0], color.R);
            minArr[1] = Math.Min(minArr[1], color.G);
            minArr[2] = Math.Min(minArr[2], color.B);
            minArr[3] = Math.Min(minArr[3], color.A);

            maxArr[0] = Math.Max(maxArr[0], color.R);
            maxArr[1] = Math.Max(maxArr[1], color.G);
            maxArr[2] = Math.Max(maxArr[2], color.B);
            maxArr[3] = Math.Max(maxArr[3], color.A);
        }

        min = new(minArr[0], minArr[1], minArr[2], minArr[3]);
        max = new(maxArr[0], maxArr[1], maxArr[2], maxArr[3]);

        IEnumerable<byte> bytesEnumerable = colorFloats.SelectMany(value => BitConverter.GetBytes(value));

        return bytesEnumerable.ToArray();
    }

    int GetNormalsAccessor(Vector3d[] normals)
    {
        int normalsBufferIdx = GetNormalsBufferView(normals, out Vector3f min, out Vector3f max, out int normalsCount);

        G.Accessor normalAccessor = new()
        {
            BufferView = normalsBufferIdx,
            ByteOffset = 0,
            ComponentType = G.Accessor.ComponentTypeEnum.FLOAT,
            Count = normalsCount,
            Min = min.ToFloatArray(),
            Max = max.ToFloatArray(),
            Type = G.Accessor.TypeEnum.VEC3,
        };

        return _dummy.Accessors.AddAndReturnIndex(normalAccessor);
    }

    int GetNormalsBufferView(Rhino.Geometry.Vector3d[] normals, out Rhino.Geometry.Vector3f min, out Rhino.Geometry.Vector3f max, out int normalsCount)
    {
        int buffer = 0;
        int byteOffset = 0;
        int byteLength;

        if (_binary)
        {
            byte[] bytes = GetNormalsBytes(normals, out min, out max);
            byteLength = bytes.Length;
            byteOffset = _binaryBuffer.Count;
            _binaryBuffer.AddRange(bytes);
        }
        else
        {
            buffer = GetNormalsBuffer(normals, out min, out max, out byteLength);
        }

        G.BufferView normalsBufferView = new()
        {
            Buffer = buffer,
            ByteLength = byteLength,
            ByteOffset = byteOffset,
            Target = G.BufferView.TargetEnum.ARRAY_BUFFER,
        };

        normalsCount = normals.Length;
        return _dummy.BufferViews.AddAndReturnIndex(normalsBufferView);
    }

    int GetNormalsBuffer(Vector3d[] normals, out Vector3f min, out Vector3f max, out int byteLength)
    {
        byte[] bytes = GetNormalsBytes(normals, out min, out max);
        byteLength = bytes.Length;

        G.Buffer normalBuffer = new()
        {
            Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
            ByteLength = bytes.Length,
        };

        return _dummy.Buffers.AddAndReturnIndex(normalBuffer);
    }

    static byte[] GetNormalsBytes(Vector3d[] normals, out Vector3f min, out Vector3f max)
    {
        min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        //Preallocate
        List<float> floats = new(normals.Length * 3);

        foreach (var n in normals)
        {
            var normal = (Vector3f)n;
            floats.AddRange(new float[] { normal.X, normal.Y, normal.Z });

            min.X = Math.Min(min.X, normal.X);
            max.X = Math.Max(max.X, normal.X);

            min.Y = Math.Min(min.Y, normal.Y);
            max.Y = Math.Max(max.Y, normal.Y);

            max.Z = Math.Max(max.Z, normal.Z);
            min.Z = Math.Min(min.Z, normal.Z);
        }

        IEnumerable<byte> bytesEnumerable = floats.SelectMany(value => BitConverter.GetBytes(value));
        return bytesEnumerable.ToArray();
    }
}
