﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.DocObjects;
using Rhino;
using Rhino.FileIO;
using glTFLoader.Schema;
using Rhino.Geometry;
using Rhino.Render;
using Rhino.Display;
using System.Drawing;

namespace glTF_BinExporter
{
    class RhinoDocGltfConverter
    {
        public RhinoDocGltfConverter(glTFExportOptions options, IEnumerable<RhinoObject> objects)
        {
            this.options = options;
            this.objects = objects;
        }

        public RhinoDocGltfConverter(glTFExportOptions options, RhinoDoc doc)
        {
            this.options = options;
            this.objects = doc.Objects;
        }

        private IEnumerable<RhinoObject> objects = null;
        private glTFExportOptions options = null;

        private Dictionary<Guid, int> materialsMap = new Dictionary<Guid, int>();

        private gltfSchemaDummy dummy = new gltfSchemaDummy();

        private MemoryStream binaryBufferStream = new MemoryStream();

        public Gltf ConvertToGltf()
        {
            dummy.Scene = 0;
            dummy.Scenes.Add(new gltfSchemaSceneDummy());

            dummy.Asset = new Asset()
            {
                Version = "2.0",
            };

            dummy.Samplers.Add(new Sampler()
            {
                MinFilter = Sampler.MinFilterEnum.LINEAR,
                MagFilter = Sampler.MagFilterEnum.LINEAR,
                WrapS = Sampler.WrapSEnum.REPEAT,
                WrapT = Sampler.WrapTEnum.REPEAT,
            });

            if(options.UseDracoCompression)
            {
                dummy.ExtensionsUsed.Add(Constants.DracoMeshCompressionExtensionTag);
                dummy.ExtensionsRequired.Add(Constants.DracoMeshCompressionExtensionTag);
            }

            var sanitized = GlTFUtils.SanitizeRhinoObjects(objects);

            foreach(Tuple<Rhino.Geometry.Mesh[], Rhino.DocObjects.Material, Guid, RhinoObject> tuple in sanitized)
            {
                if(options.UseDracoCompression)
                {
                    AddRhinoObjectDraco(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);
                }
                else
                {
                    if (options.UseBinary)
                    {
                        AddRhinoObjectBinary(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);
                    }
                    else
                    {
                        AddRhinoObjectText(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);
                    }
                }
            }

            if(options.UseBinary)
            {
                //have to add the empty buffer for the binary file header
                dummy.Buffers.Add(new glTFLoader.Schema.Buffer()
                {
                    ByteLength = (int)binaryBufferStream.Length,
                    Uri = null,
                });
            }

            return dummy.ToSchemaGltf();
        }

        public byte[] GetBinaryBuffer()
        {
            return binaryBufferStream.ToArray();
        }

        private void AddRhinoObjectDraco(Rhino.Geometry.Mesh[] rhinoMeshes, Rhino.DocObjects.Material material, Guid materialId, RhinoObject rhinoObject)
        {
            int materialIndex = GetMaterial(material, materialId);

            var primitives = new List<MeshPrimitive>();

            var transformYUp = Transform.Identity;
            // Leave X as is
            // Change Y to Z
            transformYUp.M11 = 0;
            transformYUp.M12 = 1;

            // Change Z to -Y
            transformYUp.M21 = -1;
            transformYUp.M22 = 0;

            // For each rhino mesh, create gl-buffers, gl-meshes, etc.
            foreach (var rhinoMesh in rhinoMeshes)
            {
                rhinoMesh.Transform(transformYUp);
                rhinoMesh.TextureCoordinates.ReverseTextureCoordinates(1);

                var dracoComp = DracoCompression.Compress(
                    rhinoMesh,
                    new DracoCompressionOptions()
                    {
                        CompressionLevel = options.DracoCompressionLevel,
                        IncludeNormals = true,
                        IncludeTextureCoordinates = true,
                        IncludeVertexColors = false,
                        PositionQuantizationBits = options.DracoQuantizationBits,
                        NormalQuantizationBits = options.DracoQuantizationBits,
                        TextureCoordintateQuantizationBits = options.DracoQuantizationBits
                    }
                );

                DracoGeometryInfo dracoGeoInfo = AddDracoGeometry(dracoComp);

                var compMeshBufferView = new BufferView()
                {
                    Buffer = dracoGeoInfo.bufferIndex,
                    ByteOffset = dracoGeoInfo.byteOffset,
                    ByteLength = dracoGeoInfo.byteLength,
                };

                int compMeshBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(compMeshBufferView);

                var vtxAccessor = new Accessor
                {
                    Type = Accessor.TypeEnum.VEC3,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Count = dracoGeoInfo.verticesNum,
                    Min = dracoGeoInfo.verticesMin,
                    Max = dracoGeoInfo.verticesMax,
                    ByteOffset = 0,
                };

                int vtxAccessorIdx = dummy.Accessors.AddAndReturnIndex(vtxAccessor);

                // // Accessor Triangles Vertex IDs
                var idsAccessor = new Accessor
                {
                    Type = Accessor.TypeEnum.SCALAR,
                    ComponentType = Accessor.ComponentTypeEnum.UNSIGNED_INT,
                    Count = dracoGeoInfo.trianglesNum,
                    Min = new float[] { dracoGeoInfo.trianglesMin },
                    Max = new float[] { dracoGeoInfo.trianglesMax },
                    ByteOffset = 0,
                };
                
                int idsAccessorIdx = dummy.Accessors.AddAndReturnIndex(idsAccessor);

                // Accessor Normals
                var normalsAccessor = new Accessor
                {
                    Type = Accessor.TypeEnum.VEC3,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Count = dracoGeoInfo.normalsNum,
                    Min = dracoGeoInfo.normalsMin,
                    Max = dracoGeoInfo.normalsMax,
                    ByteOffset = 0,
                };

                int normalsAccessorIdx = dummy.Accessors.AddAndReturnIndex(normalsAccessor);

                // Accessor TexCoords
                var texCoordsAccessor = new Accessor
                {
                    Type = Accessor.TypeEnum.VEC2,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Count = dracoGeoInfo.texCoordsNum,
                    Min = dracoGeoInfo.texCoordsMin,
                    Max = dracoGeoInfo.texCoordsMax,
                    ByteOffset = 0,
                };
                
                int texCoordsAccessorIdx = dummy.Accessors.AddAndReturnIndex(texCoordsAccessor);

                var primitive = new MeshPrimitive()
                {
                    Attributes = new Dictionary<string, int>()
                    {
                        { Constants.PositionAttributeTag, vtxAccessorIdx },
                        { Constants.NormalAttributeTag, normalsAccessorIdx },
                        { Constants.TexCoord0AttributeTag, texCoordsAccessorIdx },
                    },
                    Indices = idsAccessorIdx,
                    Material = materialIndex,
                    Extensions = new Dictionary<string, object>()
                    {
                        {
                            Constants.DracoMeshCompressionExtensionTag,
                            new
                            {
                                bufferView = compMeshBufferViewIdx,
                                attributes = new
                                {
                                    POSITION = 0,
                                    NORMAL = 1,
                                    TEXCOORD_0 = 2
                                }
                            }
                        }
                    }
                };

                // Create mesh
                primitives.Add(primitive);
            }
            var mesh = new glTFLoader.Schema.Mesh()
            {
                Primitives = primitives.ToArray(),
            };
            
            int meshIndex = dummy.Meshes.AddAndReturnIndex(mesh);
            
            var node = new Node()
            {
                Mesh = meshIndex,
            };
            int nodeIndex = dummy.Nodes.AddAndReturnIndex(node);

            dummy.Scenes[dummy.Scene].Nodes.Add(nodeIndex);
        }

        public DracoGeometryInfo AddDracoGeometry(DracoCompression dracoCompression)
        {
            var dracoGeoInfo = new DracoGeometryInfo();

            string fileName = Path.GetTempFileName();

            try
            {
                dracoCompression.Write(fileName);

                byte[] dracoBytes = GetDracoBytes(fileName);

                WriteDracoBytes(dracoBytes, out dracoGeoInfo.bufferIndex, out dracoGeoInfo.byteOffset);

                dracoGeoInfo.byteLength = dracoBytes.Length;

                var geo = DracoCompression.DecompressFile(fileName);
                if (geo.ObjectType == ObjectType.Mesh)
                {
                    var mesh = (Rhino.Geometry.Mesh)geo;
                    Point2f point2f;
                    Point3f point3f;
                    Vector3f vector3f;
                    // Vertices Stats
                    dracoGeoInfo.verticesNum = mesh.Vertices.Count;
                    point3f = mesh.Vertices.Min();
                    dracoGeoInfo.verticesMin = new float[] { point3f.X, point3f.Y, point3f.Z };
                    point3f = mesh.Vertices.Max();
                    dracoGeoInfo.verticesMax = new float[] { point3f.X, point3f.Y, point3f.Z };

                    // Triangle Stats
                    dracoGeoInfo.trianglesNum = mesh.Faces.TriangleCount;
                    dracoGeoInfo.trianglesMin = 0;
                    dracoGeoInfo.trianglesMax = dracoGeoInfo.verticesNum - 1;

                    // Normals Stats
                    dracoGeoInfo.normalsNum = mesh.Normals.Count;
                    vector3f = mesh.Normals.Min();
                    dracoGeoInfo.normalsMin = new float[] { vector3f.X, vector3f.Y, vector3f.Z };
                    vector3f = mesh.Normals.Max();
                    dracoGeoInfo.normalsMax = new float[] { vector3f.X, vector3f.Y, vector3f.Z };

                    // TexCoord Stats
                    dracoGeoInfo.texCoordsNum = mesh.TextureCoordinates.Count;
                    point2f = mesh.TextureCoordinates.Min();
                    dracoGeoInfo.texCoordsMin = new float[] { point2f.X, point2f.Y };
                    point2f = mesh.TextureCoordinates.Max();
                    dracoGeoInfo.texCoordsMax = new float[] { point2f.X, point2f.Y };

                    dracoGeoInfo.success = true;
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

        private byte[] GetDracoBytes(string fileName)
        {
            using (FileStream stream = File.Open(fileName, FileMode.Open))
            {
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, (int)stream.Length);

                return bytes;
            }
        }

        public void WriteDracoBytes(byte[] bytes, out int bufferIndex, out int byteOffset)
        {
            if(options.UseBinary)
            {
                byteOffset = (int)binaryBufferStream.Position;
                binaryBufferStream.Write(bytes, 0, bytes.Length);
                bufferIndex = 0;
            }
            else
            {
                glTFLoader.Schema.Buffer buffer = new glTFLoader.Schema.Buffer()
                {
                    Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                    ByteLength = bytes.Length,
                };
                bufferIndex = dummy.Buffers.AddAndReturnIndex(buffer);
                byteOffset = 0;
            }
        }

        private void AddRhinoObjectBinary(Rhino.Geometry.Mesh[] rhinoMeshes, Rhino.DocObjects.Material material, Guid materialId, RhinoObject rhinoObject)
        {
            int materialIndex = GetMaterial(material, materialId);

            var primitives = new List<MeshPrimitive>();

            foreach (var rhinoMesh in rhinoMeshes)
            {
                rhinoMesh.Faces.ConvertQuadsToTriangles();

                Point3d vtxMin = Point3d.Origin;
                Point3d vtxMax = Point3d.Origin;

                byte[] verticesBytes = GetVerticesBytes(rhinoMesh.Vertices, out vtxMin, out vtxMax);
                int verticesByteLength = verticesBytes.Length;
                int verticesOffset = (int)binaryBufferStream.Position;
                binaryBufferStream.Write(verticesBytes, 0, verticesByteLength);

                int indicesCount = 0;

                byte[] indicesBytes = GetIndicesBytes(rhinoMesh.Faces, out indicesCount);
                int indicesBytesLength = indicesBytes.Length;
                int indicesOffset = (int)binaryBufferStream.Position;
                binaryBufferStream.Write(indicesBytes, 0, indicesBytesLength);

                Vector3f normalsMin = Vector3f.Zero;
                Vector3f normalsMax = Vector3f.Zero;

                byte[] normalsBytes = GetNormalsBytes(rhinoMesh.Normals, out normalsMin, out normalsMax);
                int normalsBytesLength = normalsBytes.Length;
                int normalsOffset = (int)binaryBufferStream.Position;
                binaryBufferStream.Write(normalsBytes, 0, normalsBytesLength);

                Point2f texCoordsMin = new Point2f(0.0f, 0.0f);
                Point2f texCoordsMax = new Point2f(0.0f, 0.0f);

                byte[] texCoordsBytes = GetTextureCoordinatesBytes(rhinoMesh.TextureCoordinates, out texCoordsMin, out texCoordsMax);
                int texCoordsBytesLength = texCoordsBytes.Length;
                int texCoordsOffset = (int)binaryBufferStream.Position;
                binaryBufferStream.Write(texCoordsBytes, 0, texCoordsBytesLength);

                var vtxBufferView = new BufferView()
                {
                    Buffer = 0,
                    ByteOffset = verticesOffset,
                    ByteLength = verticesByteLength,
                    Target = BufferView.TargetEnum.ARRAY_BUFFER,
                };

                int vtxBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(vtxBufferView);

                var idsBufferView = new BufferView()
                {
                    Buffer = 0,
                    ByteOffset = indicesOffset,
                    ByteLength = indicesBytesLength,
                    Target = BufferView.TargetEnum.ELEMENT_ARRAY_BUFFER,
                };

                int idsBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(idsBufferView);

                BufferView normalsBufferView = new BufferView()
                {
                    Buffer = 0,
                    ByteOffset = normalsOffset,
                    ByteLength = normalsBytesLength,
                    Target = BufferView.TargetEnum.ARRAY_BUFFER,
                };

                int normalsBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(normalsBufferView);

                BufferView texCoordsBufferView = new BufferView()
                {
                    Buffer = 0,
                    ByteOffset = texCoordsOffset,
                    ByteLength = texCoordsBytesLength,
                    Target = BufferView.TargetEnum.ARRAY_BUFFER,
                };

                int texCoordsBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(texCoordsBufferView);

                // Create accessors	
                Accessor vtxAccessor = new Accessor()
                {
                    BufferView = vtxBufferViewIdx,
                    Count = rhinoMesh.Vertices.Count,
                    Min = new float[] { (float)vtxMin.X, (float)vtxMin.Y, (float)vtxMin.Z },
                    Max = new float[] { (float)vtxMax.X, (float)vtxMax.Y, (float)vtxMax.Z },
                    Type = Accessor.TypeEnum.VEC3,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    ByteOffset = 0,
                };

                int vtxAccessorIdx = dummy.Accessors.AddAndReturnIndex(vtxAccessor);

                Accessor idsAccessor = new Accessor()
                {
                    BufferView = idsBufferViewIdx,
                    Count = indicesCount,
                    Min = new float[] { 0 },
                    Max = new float[] { rhinoMesh.Vertices.Count - 1 },
                    Type = Accessor.TypeEnum.SCALAR,
                    ComponentType = Accessor.ComponentTypeEnum.UNSIGNED_INT,
                    ByteOffset = 0,
                };

                int idsAccessorIdx = dummy.Accessors.AddAndReturnIndex(idsAccessor);

                Accessor normalsAccessor = new Accessor()
                {
                    BufferView = normalsBufferViewIdx,
                    Count = rhinoMesh.Normals.Count,
                    Min = new float[] { normalsMin.X, normalsMin.Y, normalsMin.Z },
                    Max = new float[] { normalsMax.X, normalsMax.Y, normalsMax.Z },
                    Type = Accessor.TypeEnum.VEC3,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    ByteOffset = 0,
                };

                int normalsAccessorIdx = dummy.Accessors.AddAndReturnIndex(normalsAccessor);

                Accessor texCoordsAccessor = new Accessor()
                {
                    BufferView = texCoordsBufferViewIdx,
                    Count = rhinoMesh.TextureCoordinates.Count,
                    Min = new float[] { texCoordsMin.X, texCoordsMin.Y },
                    Max = new float[] { texCoordsMax.X, texCoordsMax.Y },
                    Type = Accessor.TypeEnum.VEC2,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    ByteOffset = 0,
                };

                int texCoordsAccessorIdx = dummy.Accessors.AddAndReturnIndex(texCoordsAccessor);

                var primitive = new MeshPrimitive()
                {
                    Attributes = new Dictionary<string, int>()
                    {
                        { Constants.PositionAttributeTag, vtxAccessorIdx },
                        { Constants.NormalAttributeTag, normalsAccessorIdx },
                        { Constants.TexCoord0AttributeTag, texCoordsAccessorIdx },
                    },
                    Indices = idsAccessorIdx,
                    Material = materialIndex,
                };

                // Create mesh	
                primitives.Add(primitive);
            }

            var mesh = new glTFLoader.Schema.Mesh()
            {
                Primitives = primitives.ToArray()
            };
            int idxMesh = dummy.Meshes.AddAndReturnIndex(mesh);

            var node = new Node()
            {
                Mesh = idxMesh,
                Name = string.IsNullOrEmpty(rhinoObject.Name) ? null : rhinoObject.Name,
            };

            int idxNode = dummy.Nodes.AddAndReturnIndex(node);

            dummy.Scenes[dummy.Scene].Nodes.Add(idxNode);
        }

        private void AddRhinoObjectText(Rhino.Geometry.Mesh[] rhinoMeshes, Rhino.DocObjects.Material material, Guid materialId, RhinoObject rhinoObject)
        {
            int materialIndex = GetMaterial(material, materialId);

            var primitives = new List<MeshPrimitive>();	

            foreach (var rhinoMesh in rhinoMeshes)	
            {
                rhinoMesh.Faces.ConvertQuadsToTriangles();

                Point3d vtxMin = Point3d.Origin;
                Point3d vtxMax = Point3d.Origin;

                var vtxBuffer = CreateVerticesBuffer(rhinoMesh.Vertices, out vtxMin, out vtxMax);
                int vtxBufferIdx = dummy.Buffers.AddAndReturnIndex(vtxBuffer);

                int indicesCount = 0;

                var idsBuffer = CreateIndicesBuffer(rhinoMesh.Faces, out indicesCount);
                int idsBufferIdx = dummy.Buffers.AddAndReturnIndex(idsBuffer);

                Vector3f normalsMin = Vector3f.Zero;
                Vector3f normalsMax = Vector3f.Zero;

                var normalsBuffer = CreateNormalsBuffer(rhinoMesh.Normals, out normalsMin, out normalsMax);	
                int normalsBufferIdx = dummy.Buffers.AddAndReturnIndex(normalsBuffer);

                Point2f texCoordsMin = new Point2f(0.0f, 0.0f);
                Point2f texCoordsMax = new Point2f(0.0f, 0.0f);

                var texCoordsBuffer = CreateTextureCoordinatesBuffer(rhinoMesh.TextureCoordinates, out texCoordsMin, out texCoordsMax);
                int texCoordsBufferIdx = dummy.Buffers.AddAndReturnIndex(texCoordsBuffer);	
	
                var vtxBufferView = new BufferView()
                {
                    Buffer = vtxBufferIdx,
                    ByteOffset = 0,
                    ByteLength = vtxBuffer.ByteLength,
                    Target = BufferView.TargetEnum.ARRAY_BUFFER,
                };

                int vtxBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(vtxBufferView);

                var idsBufferView = new BufferView()
                {
                    Buffer = idsBufferIdx,
                    ByteOffset = 0,
                    ByteLength = idsBuffer.ByteLength,
                    Target = BufferView.TargetEnum.ELEMENT_ARRAY_BUFFER,
                };
                
                int idsBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(idsBufferView);

                BufferView normalsBufferView = new BufferView()	
                {
                    Buffer = normalsBufferIdx,	
                    ByteOffset = 0,	
                    ByteLength = normalsBuffer.ByteLength,	
                    Target = BufferView.TargetEnum.ARRAY_BUFFER,
                };

                int normalsBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(normalsBufferView);	

                BufferView texCoordsBufferView = new BufferView()
                {
                    Buffer = texCoordsBufferIdx,	
                    ByteOffset = 0,	
                    ByteLength = texCoordsBuffer.ByteLength,	
                    Target = BufferView.TargetEnum.ARRAY_BUFFER,
                };

                int texCoordsBufferViewIdx = dummy.BufferViews.AddAndReturnIndex(texCoordsBufferView);

                // Create accessors	
                Accessor vtxAccessor = new Accessor()	
                {	
                    BufferView = vtxBufferViewIdx,	
                    Count = rhinoMesh.Vertices.Count,	
                    Min = new float[] { (float)vtxMin.X, (float)vtxMin.Y, (float)vtxMin.Z },	
                    Max = new float[] { (float)vtxMax.X, (float)vtxMax.Y, (float)vtxMax.Z },
                    Type = Accessor.TypeEnum.VEC3,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    ByteOffset = 0,
                };

                int vtxAccessorIdx = dummy.Accessors.AddAndReturnIndex(vtxAccessor);

                Accessor idsAccessor = new Accessor()	
                {
                    BufferView = idsBufferViewIdx,
                    Count = indicesCount,
                    Min = new float[] { 0 },	
                    Max = new float[] { rhinoMesh.Vertices.Count - 1 },	
                    Type = Accessor.TypeEnum.SCALAR,
                    ComponentType = Accessor.ComponentTypeEnum.UNSIGNED_INT,
                    ByteOffset = 0,
                };	

                int idsAccessorIdx = dummy.Accessors.AddAndReturnIndex(idsAccessor);

                Accessor normalsAccessor = new Accessor()
                {
                    BufferView = normalsBufferViewIdx,
                    Count = rhinoMesh.Normals.Count,
                    Min = new float[] { normalsMin.X, normalsMin.Y, normalsMin.Z },
                    Max = new float[] { normalsMax.X, normalsMax.Y, normalsMax.Z },
                    Type = Accessor.TypeEnum.VEC3,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    ByteOffset = 0,
                };

                int normalsAccessorIdx = dummy.Accessors.AddAndReturnIndex(normalsAccessor);

                Accessor texCoordsAccessor = new Accessor()	
                {	
                    BufferView = texCoordsBufferViewIdx,	
                    Count = rhinoMesh.TextureCoordinates.Count,
                    Min = new float[] { texCoordsMin.X, texCoordsMin.Y },	
                    Max = new float[] { texCoordsMax.X, texCoordsMax.Y },
                    Type = Accessor.TypeEnum.VEC2,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    ByteOffset = 0,
                };

                int texCoordsAccessorIdx = dummy.Accessors.AddAndReturnIndex(texCoordsAccessor);	

                var primitive = new MeshPrimitive()
                {
                    Attributes = new Dictionary<string, int>()
                    {
                        { Constants.PositionAttributeTag, vtxAccessorIdx },
                        { Constants.NormalAttributeTag, normalsAccessorIdx },
                        { Constants.TexCoord0AttributeTag, texCoordsAccessorIdx },
                    },
                    Indices = idsAccessorIdx,
                    Material = materialIndex,
                };

                // Create mesh	
                primitives.Add(primitive);	
            }	

            var mesh = new glTFLoader.Schema.Mesh()
            {
                Primitives = primitives.ToArray(),
            };	
            int idxMesh = dummy.Meshes.AddAndReturnIndex(mesh);	

            var node = new Node()
            {
                Mesh = idxMesh,
                Name = string.IsNullOrEmpty(rhinoObject.Name) ? null : rhinoObject.Name,
            };

            int idxNode = dummy.Nodes.AddAndReturnIndex(node);

            dummy.Scenes[dummy.Scene].Nodes.Add(idxNode);
        }

        glTFLoader.Schema.Buffer CreateVerticesBuffer(Rhino.Geometry.Collections.MeshVertexList vertices, out Point3d min, out Point3d max)
        {
            byte[] bytes = GetVerticesBytes(vertices, out min, out max);

            return new glTFLoader.Schema.Buffer()
            {
                Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                ByteLength = bytes.Length,
            };
        }

        private byte[] GetVerticesBytes(Rhino.Geometry.Collections.MeshVertexList vertices, out Point3d min, out Point3d max)
        {
            var vtxMin = new Point3d() { X = Double.PositiveInfinity, Y = Double.PositiveInfinity, Z = Double.PositiveInfinity };
            var vtxMax = new Point3d() { X = Double.NegativeInfinity, Y = Double.NegativeInfinity, Z = Double.NegativeInfinity };

            //Preallocate to reduce time spent on allocations
            List<float> floats = new List<float>(vertices.Count * 3);

            foreach (Point3d vertex in vertices)
            {
                floats.AddRange(new float[] { (float)vertex.X, (float)vertex.Z, (float)-vertex.Y });

                vtxMin.X = Math.Min(vtxMin.X, vertex.X);
                // Switch Y<=>Z for GL coords	
                vtxMin.Y = Math.Min(vtxMin.Y, vertex.Z);
                vtxMin.Z = Math.Min(vtxMin.Z, -vertex.Y);

                vtxMax.X = Math.Max(vtxMax.X, vertex.X);
                // Switch Y<=>Z for GL coords	
                vtxMax.Y = Math.Max(vtxMax.Y, vertex.Z);
                vtxMax.Z = Math.Max(vtxMax.Z, -vertex.Y);
            }

            min = vtxMin;
            max = vtxMax;

            IEnumerable<byte> bytesEnumerable = floats.SelectMany(value => BitConverter.GetBytes(value));

            return bytesEnumerable.ToArray();
        }

        glTFLoader.Schema.Buffer CreateIndicesBuffer(Rhino.Geometry.Collections.MeshFaceList faces, out int indicesCount)
        {
            byte[] bytes = GetIndicesBytes(faces, out indicesCount);

            return new glTFLoader.Schema.Buffer()
            {
                Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                ByteLength = bytes.Length,
            };
        }

        byte[] GetIndicesBytes(Rhino.Geometry.Collections.MeshFaceList faces, out int indicesCount)
        {
            //Preallocate to reduce time spent on allocations
            List<uint> faceIndices = new List<uint>(faces.Count * 3);

            foreach (Rhino.Geometry.MeshFace face in faces)
            {
                if (face.IsTriangle)
                {
                    faceIndices.AddRange(new uint[] { (uint)face.A, (uint)face.B, (uint)face.C });
                }
                else
                {
                    //Triangulate
                    faceIndices.AddRange(new uint[] { (uint)face.A, (uint)face.B, (uint)face.C, (uint)face.A, (uint)face.C, (uint)face.D });
                }
            }

            IEnumerable<byte> bytesEnumerable = faceIndices.SelectMany(value => BitConverter.GetBytes(value));

            indicesCount = faceIndices.Count;

            return bytesEnumerable.ToArray();
        }

        glTFLoader.Schema.Buffer CreateNormalsBuffer(Rhino.Geometry.Collections.MeshVertexNormalList normals, out Vector3f min, out Vector3f max)
        {
            byte[] bytes = GetNormalsBytes(normals, out min, out max);

            return new glTFLoader.Schema.Buffer()
            {
                Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                ByteLength = bytes.Length,
            };
        }

        byte[] GetNormalsBytes(Rhino.Geometry.Collections.MeshVertexNormalList normals, out Vector3f min, out Vector3f max)
        {
            Vector3f vMin = new Vector3f() { X = float.PositiveInfinity, Y = float.PositiveInfinity, Z = float.PositiveInfinity };
            Vector3f vMax = new Vector3f() { X = float.NegativeInfinity, Y = float.NegativeInfinity, Z = float.NegativeInfinity };

            //Preallocate to reduce time spent on allocations
            List<float> floats = new List<float>(normals.Count * 3);

            foreach (Vector3f normal in normals)
            {
                floats.AddRange(new float[] { normal.X, normal.Z, -normal.Y });

                vMin.X = Math.Min(vMin.X, normal.X);
                // Switch Y<=>Z for GL coords	
                vMin.Y = Math.Min(vMin.Y, normal.Z);
                vMin.Z = Math.Min(vMin.Z, -normal.Y);

                vMax.X = Math.Max(vMax.X, normal.X);
                // Switch Y<=>Z for GL coords	
                vMax.Y = Math.Max(vMax.Y, normal.Z);
                vMax.Z = Math.Max(vMax.Z, -normal.Y);
            }

            IEnumerable<byte> bytesEnumerable = floats.SelectMany(value => BitConverter.GetBytes(value));

            min = vMin;
            max = vMax;

            return bytesEnumerable.ToArray();
        }

        glTFLoader.Schema.Buffer CreateTextureCoordinatesBuffer(Rhino.Geometry.Collections.MeshTextureCoordinateList texCoords, out Point2f min, out Point2f max)
        {
            byte[] bytes = GetTextureCoordinatesBytes(texCoords, out min, out max);

            return new glTFLoader.Schema.Buffer()
            {
                Uri = Constants.TextBufferHeader + Convert.ToBase64String(bytes),
                ByteLength = bytes.Length,
            };
        }

        private byte[] GetTextureCoordinatesBytes(Rhino.Geometry.Collections.MeshTextureCoordinateList texCoords, out Point2f min, out Point2f max)
        {
            Point2f texCoordsMin = new Point2f() { X = float.PositiveInfinity, Y = float.PositiveInfinity };
            Point2f texCoordsMax = new Point2f() { X = float.NegativeInfinity, Y = float.NegativeInfinity };

            List<float> coordinates = new List<float>(texCoords.Count * 2);

            foreach (Point2f coordinate in texCoords)
            {
                coordinates.AddRange(new float[] { coordinate.X, -coordinate.Y });

                texCoordsMin.X = Math.Min(texCoordsMin.X, coordinate.X);
                // Switch Y<=>Z for GL coords	
                texCoordsMin.Y = Math.Min(texCoordsMin.Y, -coordinate.Y);

                texCoordsMax.X = Math.Max(texCoordsMax.X, coordinate.X);
                // Switch Y<=>Z for GL coords	
                texCoordsMax.Y = Math.Max(texCoordsMax.Y, -coordinate.Y);
            }

            IEnumerable<byte> bytesEnumerable = coordinates.SelectMany(value => BitConverter.GetBytes(value));

            min = texCoordsMin;
            max = texCoordsMax;

            return bytesEnumerable.ToArray();
        }

        int GetMaterial(Rhino.DocObjects.Material material, Guid materialId)
        {
            int materialIndex = -1;
            if(!materialsMap.TryGetValue(materialId, out materialIndex))
            {
                RhinoMaterialGltfConverter materialConverter = new RhinoMaterialGltfConverter(options, dummy, binaryBufferStream, material);
                materialIndex = materialConverter.AddMaterial();
                materialsMap.Add(materialId, materialIndex);
            }

            return materialIndex;
        }

    }
}
