using System.Drawing;
using Rhino;
using Rhino.Geometry;
using Rhino.Render;
using Rhino.DocObjects;
using G = glTFLoader.Schema;

namespace RhinoGltf
{
    public class ObjectExportData
    {
        public RhinoObject Object;
        public Transform Transform = Transform.Identity;
        public RenderMaterial? RenderMaterial;
        public Mesh[] Meshes = Array.Empty<Mesh>();
        public int? MaterialIndex;

        public ObjectExportData(RhinoObject rhinoObject)
        {
            Object = rhinoObject;
        }
    }

    class RhinoDocGltfConverter
    {
        readonly RhinoDoc _doc;
        readonly bool _binary;
        readonly GltfSettings _options;
        readonly LinearWorkflow _workflow;

        readonly Dictionary<Guid, int> _materialsMap = new();
        readonly Dictionary<(int, int, int), int> _solidColorMaterialsMap = new();
        readonly GltfSchemaDummy _dummy = new();
        readonly List<byte> _binaryBuffer = new();
        readonly Dictionary<int, G.Node> _layers = new();
        RenderMaterial? _defaultMaterial;

        public RhinoDocGltfConverter(GltfSettings options, bool binary, RhinoDoc doc, LinearWorkflow workflow)
        {
            _doc = doc;
            _binary = binary;
            _options = options;
            _workflow = workflow;
        }

        RenderMaterial DefaultMaterial => _defaultMaterial ??= Material.DefaultMaterial.RenderMaterial;

        public G.Gltf ConvertToGltf()
        {
            _dummy.Scene = 0;
            _dummy.Scenes.Add(new());

            _dummy.Asset = new()
            {
                Version = "2.0",
            };

            _dummy.Samplers.Add(new()
            {
                MinFilter = G.Sampler.MinFilterEnum.LINEAR,
                MagFilter = G.Sampler.MagFilterEnum.LINEAR,
                WrapS = G.Sampler.WrapSEnum.REPEAT,
                WrapT = G.Sampler.WrapTEnum.REPEAT,
            });

            if (_options.UseDracoCompression)
            {
                _dummy.ExtensionsUsed.Add(glTFExtensions.KHR_draco_mesh_compression.Tag);
                _dummy.ExtensionsRequired.Add(glTFExtensions.KHR_draco_mesh_compression.Tag);
            }

            _dummy.ExtensionsUsed.Add(glTFExtensions.KHR_materials_transmission.Tag);
            _dummy.ExtensionsUsed.Add(glTFExtensions.KHR_materials_clearcoat.Tag);
            _dummy.ExtensionsUsed.Add(glTFExtensions.KHR_materials_ior.Tag);
            _dummy.ExtensionsUsed.Add(glTFExtensions.KHR_materials_specular.Tag);

            var objects = _options.Selected;
            IEnumerable<RhinoObject> pointClouds = objects.Where(x => x.ObjectType == ObjectType.PointSet);

            foreach (RhinoObject rhinoObject in pointClouds)
            {
                RhinoPointCloudGltfConverter converter = new(rhinoObject, _options, _binary, _dummy, _binaryBuffer);
                int meshIndex = converter.AddPointCloud();

                if (meshIndex != -1)
                {
                    G.Node node = new()
                    {
                        Mesh = meshIndex,
                        Name = GetObjectName(rhinoObject),
                    };

                    int nodeIndex = _dummy.Nodes.AddAndReturnIndex(node);
                    AddNode(nodeIndex, rhinoObject);
                }
            }

            var sanitized = SanitizeRhinoObjects(objects);

            foreach (var exportData in sanitized)
            {
                exportData.MaterialIndex = GetMaterial(exportData.RenderMaterial, exportData.Object);
            }

            var customExporter = _options.CustomExporter;
            customExporter?.MutateObjects(sanitized);

            Dictionary<Guid, int> map = new();

            foreach (ObjectExportData exportData in sanitized)
            {
                var id = exportData.Object.Id;

                if (!map.TryGetValue(id, out int meshIndex))
                {
                    int? materialIndex = exportData.MaterialIndex;
                    RhinoMeshGltfConverter meshConverter = new(exportData, materialIndex, _options, _binary, _dummy, _binaryBuffer);
                    meshIndex = meshConverter.AddMesh();
                    map.Add(id, meshIndex);
                }

                G.Node node = new()
                {
                    Mesh = meshIndex,
                    Name = GetObjectName(exportData.Object),
                    Matrix = GlTFUtils.GetMatrix(exportData.Transform, this._doc),
                };

                int nodeIndex = _dummy.Nodes.AddAndReturnIndex(node);
                AddNode(nodeIndex, exportData.Object);
            }

            customExporter?.MutateGltf(_dummy);

            if (_binary && _binaryBuffer.Count > 0)
            {
                //have to add the empty buffer for the binary file header
                _dummy.Buffers.Add(new()
                {
                    ByteLength = _binaryBuffer.Count,
                    Uri = null,
                });
            }

            return _dummy.ToSchemaGltf();
        }

        private void AddNode(int nodeIndex, RhinoObject rhinoObject)
        {
            if (_options.ExportLayers)
            {
                AddToLayer(_doc.Layers[rhinoObject.Attributes.LayerIndex], nodeIndex);
            }
            else
            {
                _dummy.Scenes[_dummy.Scene].Nodes.Add(nodeIndex);
            }
        }

        private void AddToLayer(Layer layer, int child)
        {
            if (_layers.TryGetValue(layer.Index, out G.Node node))
            {
                if (node.Children == null)
                {
                    node.Children = new int[1] { child };
                }
                else
                {
                    node.Children = node.Children.Append(child).ToArray();
                }
            }
            else
            {
                node = new G.Node()
                {
                    Name = layer.Name,
                    Children = new int[1] { child },
                };

                _layers.Add(layer.Index, node);
                int nodeIndex = _dummy.Nodes.AddAndReturnIndex(node);
                Layer parentLayer = _doc.Layers.FindId(layer.ParentLayerId);

                if (parentLayer == null)
                {
                    _dummy.Scenes[_dummy.Scene].Nodes.Add(nodeIndex);
                }
                else
                {
                    AddToLayer(parentLayer, nodeIndex);
                }
            }
        }

        public static string? GetObjectName(RhinoObject rhinoObject)
        {
            return string.IsNullOrEmpty(rhinoObject.Name) ? null : rhinoObject.Name;
        }

        public byte[] GetBinaryBuffer()
        {
            return _binaryBuffer.ToArray();
        }

        int? GetMaterial(RenderMaterial? material, RhinoObject rhinoObject)
        {
            if (!_options.ExportMaterials)
                return null;

            if (material is null && _options.UseDisplayColorForUnsetMaterials)
            {
                var objectColor = GetObjectColor(rhinoObject);

                if (objectColor != Color.FromArgb(255, 0, 0, 0))
                {
                    var key = (objectColor.R, objectColor.G, objectColor.B);

                    if (_solidColorMaterialsMap.TryGetValue(key, out int matId))
                    {
                        return matId;
                    }

                    var colorf = new Rhino.Display.Color4f(objectColor);
                    matId = CreateSolidColorMaterial(colorf);
                    _solidColorMaterialsMap.Add(key, matId);
                    return matId;
                }
            }

            material ??= DefaultMaterial;

            Guid materialId = material.Id;

            if (!_materialsMap.TryGetValue(materialId, out int materialIndex))
            {
                RhinoMaterialGltfConverter materialConverter = new(_binary, _dummy, _binaryBuffer, material, _workflow);
                materialIndex = materialConverter.AddMaterial(_options.ExportTextures);
                _materialsMap.Add(materialId, materialIndex);
            }

            return materialIndex;
        }

        int CreateSolidColorMaterial(Rhino.Display.Color4f color)
        {
            G.Material material = new()
            {
                PbrMetallicRoughness = new()
                {
                    BaseColorFactor = color.ToFloatArray(),
                },
                DoubleSided = true
            };

            return _dummy.Materials.AddAndReturnIndex(material);
        }

        static Color GetObjectColor(RhinoObject rhinoObject)
        {
            if (rhinoObject.Attributes.ColorSource == ObjectColorSource.ColorFromLayer)
            {
                int layerIndex = rhinoObject.Attributes.LayerIndex;
                return rhinoObject.Document.Layers[layerIndex].Color;
            }
            else
            {
                return rhinoObject.Attributes.ObjectColor;
            }
        }

        public bool MeshIsValidForExport(Mesh mesh)
        {
            if (mesh is null)
                return false;

            if (mesh.Vertices.Count == 0)
                return false;

            if (mesh.Faces.Count == 0)
                return false;

            if (!_options.ExportOpenMeshes && !mesh.IsClosed)
                return false;

            return true;
        }

        public List<ObjectExportData> SanitizeRhinoObjects(IEnumerable<RhinoObject> rhinoObjects)
        {
            List<ObjectExportData> explodedObjects = new();

            foreach (var rhinoObject in rhinoObjects)
            {
                if (rhinoObject.ObjectType == ObjectType.InstanceReference && rhinoObject is InstanceObject instanceObject)
                {
                    List<RhinoObject> pieces = new();
                    List<Transform> transforms = new();

                    ExplodeRecursive(instanceObject, instanceObject.InstanceXform, pieces, transforms);

                    foreach (var item in pieces.Zip(transforms, (rObj, trans) => (rhinoObject: rObj, trans)))
                    {
                        explodedObjects.Add(new(item.rhinoObject)
                        {
                            Transform = item.trans,
                            RenderMaterial = GetObjectMaterial(item.rhinoObject)
                        });
                    }
                }
                else
                {
                    explodedObjects.Add(new(rhinoObject)
                    {
                        RenderMaterial = GetObjectMaterial(rhinoObject),
                    });
                }
            }

            //Remove Unmeshable
            explodedObjects.RemoveAll(obj => !obj.Object.IsMeshable(MeshType.Any));

            foreach (var item in explodedObjects)
            {
                //Mesh

                if (item.Object.ObjectType == ObjectType.SubD
                    && item.Object.Geometry is SubD subd
                    && _options.SubDExportMode == SubDMode.ControlNet
                    )
                {
                    var mesh = Mesh.CreateFromSubDControlNet(subd);
                    //mesh.Transform(item.Transform);
                    item.Meshes = new[] { mesh };
                }
                else
                {
                    MeshingParameters parameters = item.Object.GetRenderMeshParameters();

                    if (item.Object.MeshCount(MeshType.Render, parameters) == 0)
                        item.Object.CreateMeshes(MeshType.Render, parameters, false);

                    List<Mesh> meshes = new(item.Object.GetMeshes(MeshType.Render));

                    //Remove bad meshes
                    meshes.RemoveAll(mesh => mesh is null || !MeshIsValidForExport(mesh));
                    item.Meshes = meshes.ToArray();
                }
            }

            //Remove meshless objects
            explodedObjects.RemoveAll(x => x.Meshes.Length == 0);

            return explodedObjects;
        }

        private RenderMaterial? GetObjectMaterial(RhinoObject rhinoObject)
        {
            ObjectMaterialSource source = rhinoObject.Attributes.MaterialSource;

            RenderMaterial? renderMaterial = null;

            if (source == ObjectMaterialSource.MaterialFromObject)
            {
                renderMaterial = rhinoObject.RenderMaterial;
            }
            else if (source == ObjectMaterialSource.MaterialFromLayer)
            {
                int layerIndex = rhinoObject.Attributes.LayerIndex;
                renderMaterial = GetLayerMaterial(layerIndex);
            }

            return renderMaterial;
        }

        private RenderMaterial? GetLayerMaterial(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _doc.Layers.Count)
                return null;

            return _doc.Layers[layerIndex].RenderMaterial;
        }

        private void ExplodeRecursive(InstanceObject instanceObject, Transform instanceTransform, List<RhinoObject> pieces, List<Transform> transforms)
        {
            for (int i = 0; i < instanceObject.InstanceDefinition.ObjectCount; i++)
            {
                RhinoObject rhinoObject = instanceObject.InstanceDefinition.Object(i);

                if (rhinoObject is InstanceObject nestedObject)
                {
                    Transform nestedTransform = instanceTransform * nestedObject.InstanceXform;
                    ExplodeRecursive(nestedObject, nestedTransform, pieces, transforms);
                }
                else
                {
                    pieces.Add(rhinoObject);
                    transforms.Add(instanceTransform);
                }
            }
        }
    }
}
