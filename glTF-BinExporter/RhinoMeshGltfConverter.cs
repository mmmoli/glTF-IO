using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using G = glTFLoader.Schema;

namespace RhinoGltf
{
    class RhinoMeshGltfConverter
    {
        readonly ObjectExportData _exportData;
        readonly int? _materialIndex;
        readonly GltfSettings _options;
        readonly bool _binary;
        readonly GltfSchemaDummy _dummy;
        readonly List<byte> _binaryBuffer;

        DracoGeometryInfo? _dracoGeometryInfo;

        public RhinoMeshGltfConverter(ObjectExportData exportData, int? materialIndex, GltfSettings options, bool binary, GltfSchemaDummy dummy, List<byte> binaryBuffer)
        {
            _exportData = exportData;
            _materialIndex = materialIndex;
            _options = options;
            _binary = binary;
            _dummy = dummy;
            _binaryBuffer = binaryBuffer;
        }

        public int AddMesh()
        {
            List<G.MeshPrimitive> primitives = GetPrimitives();

            G.Mesh mesh = new()
            {
                Primitives = primitives.ToArray(),
            };

            return _dummy.Meshes.AddAndReturnIndex(mesh);
        }

        private static void PreprocessMesh(Mesh rhinoMesh)
        {
            rhinoMesh.TextureCoordinates.ReverseTextureCoordinates(1);
        }

        private List<G.MeshPrimitive> GetPrimitives()
        {
            List<G.MeshPrimitive> primitives = new();

            foreach (Mesh rhinoMesh in _exportData.Meshes)
            {
                PreprocessMesh(rhinoMesh);

                if (_options.UseDracoCompression)
                {
                    if (!SetDracoGeometryInfo(rhinoMesh))
                        continue;
                }

                bool exportNormals = ExportNormals(rhinoMesh);
                bool exportTextureCoordinates = ExportTextureCoordinates(rhinoMesh);
                bool exportVertexColors = ExportVertexColors(rhinoMesh);

                G.MeshPrimitive primitive = new()
                {
                    Attributes = new(),
                };

                int vertexAccessorIdx = GetVertexAccessor(rhinoMesh.Vertices);
                primitive.Attributes.Add(Constants.PositionAttributeTag, vertexAccessorIdx);
                int indicesAccessorIdx = GetIndicesAccessor(rhinoMesh.Faces, rhinoMesh.Vertices.Count);

                primitive.Indices = indicesAccessorIdx;

                if (exportNormals)
                {
                    int normalsAccessorIdx = GetNormalsAccessor(rhinoMesh.Normals);
                    primitive.Attributes.Add(Constants.NormalAttributeTag, normalsAccessorIdx);
                }

                if (exportTextureCoordinates)
                {
                    int textureCoordinatesAccessorIdx = GetTextureCoordinatesAccessor(rhinoMesh.TextureCoordinates);
                    primitive.Attributes.Add(Constants.TexCoord0AttributeTag, textureCoordinatesAccessorIdx);
                }

                if (exportVertexColors)
                {
                    int vertexColorsAccessorIdx = GetVertexColorAccessor(rhinoMesh.VertexColors);
                    primitive.Attributes.Add(Constants.VertexColorAttributeTag, vertexColorsAccessorIdx);
                }

                if (_dracoGeometryInfo is not null)
                {
                    glTFExtensions.KHR_draco_mesh_compression dracoCompressionObject = new()
                    {
                        BufferView = _dracoGeometryInfo.BufferViewIndex
                    };

                    dracoCompressionObject.Attributes.Add(Constants.PositionAttributeTag, _dracoGeometryInfo.VertexAttributePosition);

                    if (exportNormals)
                    {
                        dracoCompressionObject.Attributes.Add(Constants.NormalAttributeTag, _dracoGeometryInfo.NormalAttributePosition);
                    }

                    if (exportTextureCoordinates)
                    {
                        dracoCompressionObject.Attributes.Add(Constants.TexCoord0AttributeTag, _dracoGeometryInfo.TextureCoordinatesAttributePosition);
                    }

                    if (exportVertexColors)
                    {
                        dracoCompressionObject.Attributes.Add(Constants.VertexColorAttributeTag, _dracoGeometryInfo.VertexColorAttributePosition);
                    }

                    primitive.Extensions = new()
                    {
                        { glTFExtensions.KHR_draco_mesh_compression.Tag, dracoCompressionObject }
                    };
                }

                primitive.Material = _materialIndex;

                primitives.Add(primitive);
            }

            return primitives;
        }

        private bool ExportNormals(Mesh rhinoMesh)
        {
            return rhinoMesh.Normals.Count > 0 && _options.ExportVertexNormals;
        }

        private bool ExportTextureCoordinates(Mesh rhinoMesh)
        {
            return rhinoMesh.TextureCoordinates.Count > 0 && _options.ExportTextureCoordinates;
        }

        private bool ExportVertexColors(Mesh rhinoMesh)
        {
            return rhinoMesh.VertexColors.Count > 0 && _options.ExportVertexColors;
        }

        private bool SetDracoGeometryInfo(Mesh rhinoMesh)
        {
            var dracoComp = DracoCompression.Compress(
                rhinoMesh,
                new()
                {
                    VertexColorFormat = DracoColorFormat.RGBA,
                    CompressionLevel = _options.DracoCompressionLevel,
                    IncludeNormals = ExportNormals(rhinoMesh),
                    IncludeTextureCoordinates = ExportTextureCoordinates(rhinoMesh),
                    IncludeVertexColors = ExportVertexColors(rhinoMesh),
                    PositionQuantizationBits = _options.DracoQuantizationBitsPosition,
                    NormalQuantizationBits = _options.DracoQuantizationBitsNormal,
                    TextureCoordintateQuantizationBits = _options.DracoQuantizationBitsTexture
                }
            );

            _dracoGeometryInfo = AddDracoGeometry(dracoComp);
            return _dracoGeometryInfo.Success;
        }

        private int GetVertexAccessor(MeshVertexList vertices)
        {
            int? vertexBufferViewIdx = GetVertexBufferView(vertices, out var min, out var max, out int countVertices);

            G.Accessor vertexAccessor = new()
            {
                BufferView = vertexBufferViewIdx,
                ComponentType = G.Accessor.ComponentTypeEnum.FLOAT,
                Count = countVertices,
                Min = min,
                Max = max,
                Type = G.Accessor.TypeEnum.VEC3,
                ByteOffset = 0,
            };

            return _dummy.Accessors.AddAndReturnIndex(vertexAccessor);
        }

        private int? GetVertexBufferView(MeshVertexList vertices, out float[] min, out float[] max, out int countVertices)
        {
            if (_dracoGeometryInfo is not null)
            {
                min = _dracoGeometryInfo.VerticesMin!;
                max = _dracoGeometryInfo.VerticesMax!;
                countVertices = _dracoGeometryInfo.VerticesCount;
                return null;
            }

            int buffer;
            int byteLength;
            int byteOffset = 0;

            if (_binary)
            {
                byte[] bytes = GetVertexBytes(vertices, out min, out max);
                buffer = 0;
                byteLength = bytes.Length;
                byteOffset = _binaryBuffer.Count;
                _binaryBuffer.AddRange(bytes);
            }
            else
            {
                buffer = GetVertexBuffer(vertices, out min, out max, out byteLength);
            }

            G.BufferView vertexBufferView = new()
            {
                Buffer = buffer,
                ByteOffset = byteOffset,
                ByteLength = byteLength,
                Target = G.BufferView.TargetEnum.ARRAY_BUFFER,
            };

            countVertices = vertices.Count;
            return _dummy.BufferViews.AddAndReturnIndex(vertexBufferView);
        }

        private int GetVertexBuffer(MeshVertexList vertices, out float[] min, out float[] max, out int length)
        {
            byte[] bytes = GetVertexBytes(vertices, out min, out max);
            length = bytes.Length;

            G.Buffer buffer = new()
            {
                Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                ByteLength = length,
            };

            return _dummy.Buffers.AddAndReturnIndex(buffer);
        }

        static void GetBounds(float[] floats, int dimensions, out float[] min, out float[] max)
        {
            min = new float[dimensions];
            max = new float[dimensions];

            for (int i = 0; i < dimensions; i++)
            {
                min[i] = float.MaxValue;
                max[i] = float.MinValue;
            }

            for (int i = 0; i < floats.Length; i += dimensions)
            {
                for (int j = 0; j < dimensions; j++)
                {
                    float value = floats[i + j];
                    min[j] = Math.Min(min[j], value);
                    max[j] = Math.Max(max[j], value);
                }
            }
        }

        private byte[] GetVertexBytes(MeshVertexList vertices, out float[] min, out float[] max)
        {
            var floats = vertices.ToFloatArray();
            var scale = (float)RhinoMath.MetersPerUnit(_exportData.Object.Document.ModelUnitSystem);

            for (int i = 0; i < floats.Length; i += 3)
            {
                floats[i + 0] *= scale;
                (floats[i + 1], floats[i + 2]) = (floats[i + 2] * scale, -floats[i + 1] * scale);
            }

            GetBounds(floats, 3, out min, out max);
            var bytes = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private int GetIndicesAccessor(MeshFaceList faces, int verticesCount)
        {
            int? indicesBufferViewIdx = GetIndicesBufferView(faces, verticesCount, out float min, out float max, out int indicesCount);

            G.Accessor indicesAccessor = new()
            {
                BufferView = indicesBufferViewIdx,
                Count = indicesCount,
                Min = new float[] { min },
                Max = new float[] { max },
                Type = G.Accessor.TypeEnum.SCALAR,
                ComponentType = G.Accessor.ComponentTypeEnum.UNSIGNED_INT,
                ByteOffset = 0,
            };

            return _dummy.Accessors.AddAndReturnIndex(indicesAccessor);
        }

        private int? GetIndicesBufferView(MeshFaceList faces, int verticesCount, out float min, out float max, out int indicesCount)
        {
            if (_dracoGeometryInfo is not null)
            {
                min = _dracoGeometryInfo.IndicesMin;
                max = _dracoGeometryInfo.IndicesMax;
                indicesCount = _dracoGeometryInfo.IndicesCount;
                return null;
            }

            int bufferIndex = 0;
            int byteOffset = 0;
            int byteLength;

            if (_binary)
            {
                byte[] bytes = GetIndicesBytes(faces, out indicesCount);
                byteLength = bytes.Length;
                byteOffset = _binaryBuffer.Count;
                _binaryBuffer.AddRange(bytes);
            }
            else
            {
                bufferIndex = GetIndicesBuffer(faces, out indicesCount, out byteLength);
            }

            G.BufferView indicesBufferView = new()
            {
                Buffer = bufferIndex,
                ByteOffset = byteOffset,
                ByteLength = byteLength,
                Target = G.BufferView.TargetEnum.ELEMENT_ARRAY_BUFFER,
            };

            min = 0;
            max = verticesCount - 1;

            return _dummy.BufferViews.AddAndReturnIndex(indicesBufferView);
        }

        private int GetIndicesBuffer(MeshFaceList faces, out int indicesCount, out int byteLength)
        {
            byte[] bytes = GetIndicesBytes(faces, out indicesCount);
            byteLength = bytes.Length;

            G.Buffer indicesBuffer = new()
            {
                Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                ByteLength = bytes.Length,
            };

            return _dummy.Buffers.AddAndReturnIndex(indicesBuffer);
        }

        private static byte[] GetIndicesBytes(MeshFaceList faces, out int indicesCount)
        {
            var indices = faces.ToIntArray(true);

            var bytes = new byte[indices.Length * 4];
            Buffer.BlockCopy(indices, 0, bytes, 0, bytes.Length);

            indicesCount = indices.Length;

            return bytes;
        }

        private int GetNormalsAccessor(MeshVertexNormalList normals)
        {
            int? normalsBufferIdx = GetNormalsBufferView(normals, out float[] min, out float[] max, out int normalsCount);

            G.Accessor normalAccessor = new()
            {
                BufferView = normalsBufferIdx,
                ByteOffset = 0,
                ComponentType = G.Accessor.ComponentTypeEnum.FLOAT,
                Count = normalsCount,
                Min = min,
                Max = max,
                Type = G.Accessor.TypeEnum.VEC3,
            };

            return _dummy.Accessors.AddAndReturnIndex(normalAccessor);
        }

        int? GetNormalsBufferView(MeshVertexNormalList normals, out float[] min, out float[] max, out int normalsCount)
        {
            if (_dracoGeometryInfo is not null)
            {
                min = _dracoGeometryInfo.NormalsMin!;
                max = _dracoGeometryInfo.NormalsMax!;
                normalsCount = _dracoGeometryInfo.NormalsCount;
                return null;
            }

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

            normalsCount = normals.Count;

            return _dummy.BufferViews.AddAndReturnIndex(normalsBufferView);
        }

        int GetNormalsBuffer(MeshVertexNormalList normals, out float[] min, out float[] max, out int byteLength)
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

        static byte[] GetNormalsBytes(MeshVertexNormalList normals, out float[] min, out float[] max)
        {
            var floats = normals.ToFloatArray();

            for (int i = 0; i < floats.Length; i += 3)
                (floats[i + 1], floats[i + 2]) = (floats[i + 2], -floats[i + 1]);

            GetBounds(floats, 3, out min, out max);
            var bytes = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        int GetTextureCoordinatesAccessor(MeshTextureCoordinateList textureCoordinates)
        {
            int? textureCoordinatesBufferViewIdx = GetTextureCoordinatesBufferView(textureCoordinates, out float[] min, out float[] max, out int countCoordinates);

            G.Accessor textureCoordinatesAccessor = new()
            {
                BufferView = textureCoordinatesBufferViewIdx,
                ByteOffset = 0,
                ComponentType = G.Accessor.ComponentTypeEnum.FLOAT,
                Count = countCoordinates,
                Min = min,
                Max = max,
                Type = G.Accessor.TypeEnum.VEC2,
            };

            return _dummy.Accessors.AddAndReturnIndex(textureCoordinatesAccessor);
        }

        int? GetTextureCoordinatesBufferView(MeshTextureCoordinateList textureCoordinates, out float[] min, out float[] max, out int countCoordinates)
        {
            if (_dracoGeometryInfo is not null)
            {
                min = _dracoGeometryInfo.TexCoordsMin!;
                max = _dracoGeometryInfo.TexCoordsMax!;
                countCoordinates = _dracoGeometryInfo.TexCoordsCount;
                return null;
            }

            int buffer = 0;
            int byteLength;
            int byteOffset = 0;

            if (_binary)
            {
                byte[] bytes = GetTextureCoordinatesBytes(textureCoordinates, out min, out max);
                byteLength = bytes.Length;
                byteOffset = _binaryBuffer.Count;
                _binaryBuffer.AddRange(bytes);
            }
            else
            {
                buffer = GetTextureCoordinatesBuffer(textureCoordinates, out min, out max, out byteLength);
            }

            G.BufferView textureCoordinatesBufferView = new()
            {
                Buffer = buffer,
                ByteLength = byteLength,
                ByteOffset = byteOffset,
                Target = G.BufferView.TargetEnum.ARRAY_BUFFER,
            };

            countCoordinates = textureCoordinates.Count;
            return _dummy.BufferViews.AddAndReturnIndex(textureCoordinatesBufferView);
        }

        int GetTextureCoordinatesBuffer(MeshTextureCoordinateList textureCoordinates, out float[] min, out float[] max, out int byteLength)
        {
            byte[] bytes = GetTextureCoordinatesBytes(textureCoordinates, out min, out max);

            G.Buffer textureCoordinatesBuffer = new()
            {
                Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                ByteLength = bytes.Length,
            };

            byteLength = bytes.Length;
            return _dummy.Buffers.AddAndReturnIndex(textureCoordinatesBuffer);
        }

        private static byte[] GetTextureCoordinatesBytes(MeshTextureCoordinateList textureCoordinates, out float[] min, out float[] max)
        {
            var floats = textureCoordinates.ToFloatArray();

            GetBounds(floats, 2, out min, out max);
            var bytes = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private int GetVertexColorAccessor(MeshVertexColorList vertexColors)
        {
            int? vertexColorsBufferViewIdx = GetVertexColorBufferView(vertexColors, out float[] min, out float[] max, out int countVertexColors);
            var type = _options.UseDracoCompression ? G.Accessor.ComponentTypeEnum.UNSIGNED_BYTE : G.Accessor.ComponentTypeEnum.FLOAT;

            G.Accessor vertexColorAccessor = new()
            {
                BufferView = vertexColorsBufferViewIdx,
                ByteOffset = 0,
                Count = countVertexColors,
                ComponentType = type,
                Min = min,
                Max = max,
                Type = G.Accessor.TypeEnum.VEC4,
                Normalized = _options.UseDracoCompression,
            };

            return _dummy.Accessors.AddAndReturnIndex(vertexColorAccessor);
        }

        int? GetVertexColorBufferView(MeshVertexColorList vertexColors, out float[] min, out float[] max, out int countVertexColors)
        {
            if (_dracoGeometryInfo is not null)
            {
                min = _dracoGeometryInfo.VertexColorMin!;
                max = _dracoGeometryInfo.VertexColorMax!;
                countVertexColors = _dracoGeometryInfo.VertexColorCount;
                return null;
            }

            int buffer = 0;
            int byteLength;
            int byteOffset = 0;

            if (_binary)
            {
                byte[] bytes = GetVertexColorBytes(vertexColors, out min, out max);
                byteLength = bytes.Length;
                byteOffset = _binaryBuffer.Count;
                _binaryBuffer.AddRange(bytes);
            }
            else
            {
                buffer = GetVertexColorBuffer(vertexColors, out min, out max, out byteLength);
            }

            G.BufferView vertexColorsBufferView = new()
            {
                Buffer = buffer,
                ByteLength = byteLength,
                ByteOffset = byteOffset,
                Target = G.BufferView.TargetEnum.ARRAY_BUFFER,
            };

            countVertexColors = vertexColors.Count;

            return _dummy.BufferViews.AddAndReturnIndex(vertexColorsBufferView);
        }

        int GetVertexColorBuffer(MeshVertexColorList vertexColors, out float[] min, out float[] max, out int byteLength)
        {
            byte[] bytes = GetVertexColorBytes(vertexColors, out min, out max);

            G.Buffer vertexColorsBuffer = new()
            {
                Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                ByteLength = bytes.Length,
            };

            byteLength = bytes.Length;
            return _dummy.Buffers.AddAndReturnIndex(vertexColorsBuffer);
        }

        static byte[] GetVertexColorBytes(MeshVertexColorList vertexColors, out float[] min, out float[] max)
        {
            var ints = vertexColors.ToARGBArray();
            var floats = new float[ints.Length];

            //TODO:(maybe?) reorder ARGB to RGBA
            for (int i = 0; i < ints.Length; i++)
            {
                floats[i] = ints[i] / 255.0f;
            }

            GetBounds(floats, 4, out min, out max);
            var bytes = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public DracoGeometryInfo AddDracoGeometry(DracoCompression dracoCompression)
        {
            DracoGeometryInfo dracoGeoInfo = new();
            string fileName = Path.GetTempFileName();

            try
            {
                dracoCompression.Write(fileName);

                dracoGeoInfo.VertexAttributePosition = dracoCompression.VertexAttributePosition;
                dracoGeoInfo.NormalAttributePosition = dracoCompression.NormalAttributePosition;
                dracoGeoInfo.TextureCoordinatesAttributePosition = dracoCompression.TextureCoordinatesAttributePosition;
                dracoGeoInfo.VertexColorAttributePosition = dracoCompression.VertexColorAttributePosition;

                byte[] dracoBytes = GetDracoBytes(fileName);

                WriteDracoBytes(dracoBytes, out dracoGeoInfo.BufferIndex, out dracoGeoInfo.ByteOffset, out dracoGeoInfo.ByteLength);

                G.BufferView compMeshBufferView = new()
                {
                    Buffer = dracoGeoInfo.BufferIndex,
                    ByteOffset = dracoGeoInfo.ByteOffset,
                    ByteLength = dracoGeoInfo.ByteLength,
                };

                dracoGeoInfo.BufferViewIndex = _dummy.BufferViews.AddAndReturnIndex(compMeshBufferView);
                dracoGeoInfo.ByteLength = dracoBytes.Length;

                var geo = DracoCompression.DecompressFile(fileName);
                if (geo.ObjectType == Rhino.DocObjects.ObjectType.Mesh)
                {
                    var mesh = (Mesh)geo;

                    // Vertices Stats
                    dracoGeoInfo.VerticesCount = mesh.Vertices.Count;
                    dracoGeoInfo.VerticesMin = new Point3d(mesh.Vertices.Min()).ToFloatArray();
                    dracoGeoInfo.VerticesMax = new Point3d(mesh.Vertices.Max()).ToFloatArray();

                    dracoGeoInfo.IndicesCount = mesh.Faces.TriangleCount;
                    dracoGeoInfo.IndicesMin = 0;
                    dracoGeoInfo.IndicesMax = dracoGeoInfo.VerticesCount - 1;

                    dracoGeoInfo.NormalsCount = mesh.Normals.Count;
                    if (dracoGeoInfo.NormalsCount > 0)
                    {
                        dracoGeoInfo.NormalsMin = mesh.Normals.Min().ToFloatArray();
                        dracoGeoInfo.NormalsMax = mesh.Normals.Max().ToFloatArray();
                    }

                    // TexCoord Stats
                    dracoGeoInfo.TexCoordsCount = mesh.TextureCoordinates.Count;

                    if (dracoGeoInfo.TexCoordsCount > 0)
                    {
                        dracoGeoInfo.TexCoordsMin = mesh.TextureCoordinates.Min().ToFloatArray();
                        dracoGeoInfo.TexCoordsMax = mesh.TextureCoordinates.Max().ToFloatArray();
                    }

                    dracoGeoInfo.VertexColorCount = mesh.VertexColors.Count;
                    dracoGeoInfo.VertexColorMin = new float[4] { 0, 0, 0, 0 };
                    dracoGeoInfo.VertexColorMax = new float[4] { 1, 1, 1, 1 };

                    dracoGeoInfo.Success = true;
                }
                geo.Dispose();
                dracoCompression.Dispose();
            }
            finally
            {
                File.Delete(fileName);
            }

            return dracoGeoInfo;
        }

        private static byte[] GetDracoBytes(string fileName)
        {
            using FileStream stream = File.Open(fileName, FileMode.Open);
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, (int)stream.Length);
            return bytes;
        }

        public void WriteDracoBytes(byte[] bytes, out int bufferIndex, out int byteOffset, out int byteLength)
        {
            byteLength = bytes.Length;

            if (_binary)
            {
                byteOffset = (int)_binaryBuffer.Count;
                _binaryBuffer.AddRange(bytes);
                bufferIndex = 0;
            }
            else
            {
                G.Buffer buffer = new()
                {
                    Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                    ByteLength = bytes.Length,
                };
                bufferIndex = _dummy.Buffers.AddAndReturnIndex(buffer);
                byteOffset = 0;
            }
        }
    }
}
