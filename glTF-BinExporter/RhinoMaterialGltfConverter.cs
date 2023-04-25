using System.Drawing;
using System.Drawing.Imaging;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Render;
using G = glTFLoader.Schema;

namespace RhinoGltf;

enum RgbaChannel
{
    Red = 0,
    Green = 1,
    Blue = 2,
    Alpha = 3,
}

class RhinoMaterialGltfConverter
{
    readonly bool _binary;
    readonly GltfSchemaDummy _dummy;
    readonly List<byte> _binaryBuffer;
    readonly LinearWorkflow _workflow;
    readonly Material _rhinoMaterial;
    readonly RenderMaterial _renderMaterial;

    public RhinoMaterialGltfConverter(bool binary, GltfSchemaDummy dummy, List<byte> binaryBuffer, RenderMaterial renderMaterial, LinearWorkflow workflow)
    {
        _binary = binary;
        _dummy = dummy;
        _binaryBuffer = binaryBuffer;
        _rhinoMaterial = renderMaterial.SimulatedMaterial(RenderTexture.TextureGeneration.Allow);
        _renderMaterial = renderMaterial;
        _workflow = workflow;
    }

    public int AddMaterial(bool exportTextures)
    {
        // Prep
        G.Material material = new()
        {
            Name = _renderMaterial.Name,
            PbrMetallicRoughness = new(),
            DoubleSided = true
        };

        if (!_rhinoMaterial.IsPhysicallyBased)
            _rhinoMaterial.ToPhysicallyBased();

        Rhino.DocObjects.PhysicallyBasedMaterial pbr = _rhinoMaterial.PhysicallyBased;

        // Textures
        Texture? metallicTexture = null;
        Texture? roughnessTexture = null;
        Texture? normalTexture = null;
        Texture? occlusionTexture = null;
        Texture? emissiveTexture = null;
        Texture? opacityTexture = null;
        Texture? clearcoatTexture = null;
        Texture? clearcoatRoughessTexture = null;
        Texture? clearcoatNormalTexture = null;
        Texture? specularTexture = null;

        if (exportTextures)
        {
            metallicTexture = pbr.GetTexture(TextureType.PBR_Metallic);
            roughnessTexture = pbr.GetTexture(TextureType.PBR_Roughness);
            normalTexture = pbr.GetTexture(TextureType.Bump);
            occlusionTexture = pbr.GetTexture(TextureType.PBR_AmbientOcclusion);
            emissiveTexture = pbr.GetTexture(TextureType.PBR_Emission);
            opacityTexture = pbr.GetTexture(TextureType.Opacity);
            clearcoatTexture = pbr.GetTexture(TextureType.PBR_Clearcoat);
            clearcoatRoughessTexture = pbr.GetTexture(TextureType.PBR_ClearcoatRoughness);
            clearcoatNormalTexture = pbr.GetTexture(TextureType.PBR_ClearcoatBump);
            specularTexture = pbr.GetTexture(TextureType.PBR_Specular);
        }

        HandleBaseColor(_rhinoMaterial, material, exportTextures);

        bool hasMetalTexture = metallicTexture is not null && metallicTexture.Enabled;
        bool hasRoughnessTexture = roughnessTexture is not null && roughnessTexture.Enabled;

        if (hasMetalTexture || hasRoughnessTexture)
        {
            material.PbrMetallicRoughness.MetallicRoughnessTexture = AddMetallicRoughnessTexture(_rhinoMaterial, exportTextures);

            float metallic = metallicTexture is null ? (float)pbr.Metallic : GetTextureWeight(metallicTexture);
            float roughness = roughnessTexture is null ? (float)pbr.Roughness : GetTextureWeight(roughnessTexture);

            material.PbrMetallicRoughness.MetallicFactor = metallic;
            material.PbrMetallicRoughness.RoughnessFactor = roughness;
        }
        else
        {
            material.PbrMetallicRoughness.MetallicFactor = (float)pbr.Metallic;
            material.PbrMetallicRoughness.RoughnessFactor = (float)pbr.Roughness;
        }

        if (normalTexture is not null && normalTexture.Enabled)
            material.NormalTexture = AddTextureNormal(normalTexture);

        if (occlusionTexture is not null && occlusionTexture.Enabled)
            material.OcclusionTexture = AddTextureOcclusion(occlusionTexture);

        if (emissiveTexture is not null && emissiveTexture.Enabled)
        {
            material.EmissiveTexture = AddTexture(emissiveTexture.FileReference.FullPath);
            float emissionMultiplier = 1.0f;
            var param = _rhinoMaterial.RenderMaterial.GetParameter("emission-multiplier");

            if (param is not null)
                emissionMultiplier = (float)Convert.ToDouble(param);

            material.EmissiveFactor = new float[]
            {
                emissionMultiplier,
                emissionMultiplier,
                emissionMultiplier,
            };
        }
        else
        {
            material.EmissiveFactor = new float[]
            {
                _rhinoMaterial.PhysicallyBased.Emission.R,
                _rhinoMaterial.PhysicallyBased.Emission.G,
                _rhinoMaterial.PhysicallyBased.Emission.B,
            };
        }

        //Extensions
        material.Extensions = new Dictionary<string, object>();

        //Opacity => Transmission https://github.com/KhronosGroup/glTF/blob/master/extensions/2.0/Khronos/KHR_materials_transmission/README.md

        glTFExtensions.KHR_materials_transmission transmission = new();

        if (opacityTexture != null && opacityTexture.Enabled)
        {
            //Transmission texture is stored in an images R channel
            //https://github.com/KhronosGroup/glTF/blob/master/extensions/2.0/Khronos/KHR_materials_transmission/README.md#properties
            transmission.TransmissionTexture = GetSingleChannelTexture(opacityTexture, RgbaChannel.Red, true);
            transmission.TransmissionFactor = GetTextureWeight(opacityTexture);
        }
        else
        {
            transmission.TransmissionFactor = 1.0f - (float)pbr.Opacity;
        }

        material.Extensions.Add(glTFExtensions.KHR_materials_transmission.Tag, transmission);

        //Clearcoat => Clearcoat https://github.com/KhronosGroup/glTF/blob/master/extensions/2.0/Khronos/KHR_materials_clearcoat/README.md

        glTFExtensions.KHR_materials_clearcoat clearcoat = new();

        if (clearcoatTexture is not null && clearcoatTexture.Enabled)
        {
            clearcoat.ClearcoatTexture = AddTexture(clearcoatTexture.FileReference.FullPath);
            clearcoat.ClearcoatFactor = GetTextureWeight(clearcoatTexture);
        }
        else
        {
            clearcoat.ClearcoatFactor = (float)pbr.Clearcoat;
        }

        if (clearcoatRoughessTexture is not null && clearcoatRoughessTexture.Enabled)
        {
            clearcoat.ClearcoatRoughnessTexture = AddTexture(clearcoatRoughessTexture.FileReference.FullPath);
            clearcoat.ClearcoatRoughnessFactor = GetTextureWeight(clearcoatRoughessTexture);
        }
        else
        {
            clearcoat.ClearcoatRoughnessFactor = (float)pbr.ClearcoatRoughness;
        }

        if (clearcoatNormalTexture is not null && clearcoatNormalTexture.Enabled)
        {
            clearcoat.ClearcoatNormalTexture = AddTextureNormal(clearcoatNormalTexture);
        }

        material.Extensions.Add(glTFExtensions.KHR_materials_clearcoat.Tag, clearcoat);

        //Opacity IOR -> IOR https://github.com/KhronosGroup/glTF/tree/master/extensions/2.0/Khronos/KHR_materials_ior

        glTFExtensions.KHR_materials_ior ior = new()
        {
            Ior = (float)pbr.OpacityIOR,
        };

        material.Extensions.Add(glTFExtensions.KHR_materials_ior.Tag, ior);

        //Specular -> Specular https://github.com/KhronosGroup/glTF/tree/master/extensions/2.0/Khronos/KHR_materials_specular

        glTFExtensions.KHR_materials_specular specular = new();

        if (specularTexture != null && specularTexture.Enabled)
        {
            //Specular is stored in the textures alpha channel
            specular.SpecularTexture = GetSingleChannelTexture(specularTexture, RgbaChannel.Alpha, false);
            specular.SpecularFactor = GetTextureWeight(specularTexture);
        }
        else
        {
            specular.SpecularFactor = (float)pbr.Specular;
        }

        material.Extensions.Add(glTFExtensions.KHR_materials_specular.Tag, specular);
        return _dummy.Materials.AddAndReturnIndex(material);
    }

    G.TextureInfo GetSingleChannelTexture(Texture texture, RgbaChannel channel, bool invert)
    {
        string path = texture.FileReference.FullPath;
        Bitmap bmp = new(path);
        Bitmap final = new(bmp.Width, bmp.Height);

        for (int i = 0; i < bmp.Width; i++)
        {
            for (int j = 0; j < bmp.Height; j++)
            {
                Color4f color = new(bmp.GetPixel(i, j));
                float value = color.L;

                if (invert)
                    value = 1.0f - value;

                Color colorFinal = GetSingleChannelColor(value, channel);

                final.SetPixel(i, j, colorFinal);
            }
        }

        int textureIndex = GetTextureFromBitmap(final);
        G.TextureInfo textureInfo = new()
        {
            Index = textureIndex,
            TexCoord = 0,
        };

        return textureInfo;
    }

    private static Color GetSingleChannelColor(float value, RgbaChannel channel)
    {
        int i = (int)(value * 255.0f);
        i = Math.Max(Math.Min(i, 255), 0);

        return channel switch
        {
            RgbaChannel.Alpha => Color.FromArgb(i, 0, 0, 0),
            RgbaChannel.Red => Color.FromArgb(0, i, 0, 0),
            RgbaChannel.Green => Color.FromArgb(0, 0, i, 0),
            RgbaChannel.Blue => Color.FromArgb(0, 0, 0, i),
            _ => Color.FromArgb(i, i, i, i),
        };
    }

    void HandleBaseColor(Material rhinoMaterial, G.Material gltfMaterial, bool exportTextures)
    {
        Texture? baseColorDoc = null;
        Texture? alphaTextureDoc = null;
        RenderTexture? baseColorTexture = null;
        RenderTexture? alphaTexture = null;

        if (exportTextures)
        {
            baseColorDoc = rhinoMaterial.GetTexture(TextureType.PBR_BaseColor);
            alphaTextureDoc = rhinoMaterial.GetTexture(TextureType.PBR_Alpha);
            baseColorTexture = rhinoMaterial.RenderMaterial.GetTextureFromUsage(RenderMaterial.StandardChildSlots.PbrBaseColor);
            alphaTexture = rhinoMaterial.RenderMaterial.GetTextureFromUsage(RenderMaterial.StandardChildSlots.PbrAlpha);
        }

        bool baseColorLinear = baseColorTexture is not null && IsLinear(baseColorTexture);
        bool hasBaseColorTexture = baseColorDoc is not null && baseColorDoc.Enabled;
        bool hasAlphaTexture = alphaTextureDoc is not null && alphaTextureDoc.Enabled;
        bool baseColorDiffuseAlphaForTransparency = rhinoMaterial.PhysicallyBased.UseBaseColorTextureAlphaForObjectAlphaTransparencyTexture;

        Color4f baseColor = rhinoMaterial.PhysicallyBased.BaseColor;

        if (_workflow.PreProcessColors)
            baseColor = Color4f.ApplyGamma(baseColor, _workflow.PreProcessGamma);

        if (!hasBaseColorTexture && !hasAlphaTexture)
        {
            gltfMaterial.PbrMetallicRoughness.BaseColorFactor = new float[]
            {
                baseColor.R,
                baseColor.G,
                baseColor.B,
                (float)rhinoMaterial.PhysicallyBased.Alpha,
            };

            if (rhinoMaterial.PhysicallyBased.Alpha == 1.0)
            {
                gltfMaterial.AlphaMode = G.Material.AlphaModeEnum.OPAQUE;
            }
            else
            {
                gltfMaterial.AlphaMode = G.Material.AlphaModeEnum.BLEND;
            }
        }
        else
        {
            gltfMaterial.PbrMetallicRoughness.BaseColorTexture = CombineBaseColorAndAlphaTexture(baseColorTexture, alphaTexture, baseColorDiffuseAlphaForTransparency, baseColor, baseColorLinear, (float)rhinoMaterial.PhysicallyBased.Alpha, out bool hasAlpha);

            if (hasAlpha)
            {
                gltfMaterial.AlphaMode = G.Material.AlphaModeEnum.BLEND;
            }
            else
            {
                gltfMaterial.AlphaMode = G.Material.AlphaModeEnum.OPAQUE;
            }
        }
    }

    static bool IsLinear(RenderTexture texture)
    {
        var attribs = (CustomRenderContentAttribute[])texture.GetType().GetCustomAttributes(typeof(CustomRenderContentAttribute), false);

        if (attribs is not null && attribs.Length > 0)
            return attribs[0].IsLinear;

        return texture.IsLinear();
    }

    G.TextureInfo CombineBaseColorAndAlphaTexture(RenderTexture? baseColorTexture, RenderTexture? alphaTexture, bool baseColorDiffuseAlphaForTransparency, Color4f baseColor, bool baseColorLinear, float alpha, out bool hasAlpha)
    {
        hasAlpha = false;
        int baseColorWidth = 0, baseColorHeight = 0;
        int alphaWidth = 0, alphaHeight = 0;

        baseColorTexture?.PixelSize(out baseColorWidth, out baseColorHeight, out _);
        alphaTexture?.PixelSize(out alphaWidth, out alphaHeight, out _);

        int width = Math.Max(baseColorWidth, alphaWidth);
        int height = Math.Max(baseColorHeight, alphaHeight);

        if (width <= 0)
        {
            width = 1024;
        }

        if (height <= 0)
        {
            height = 1024;
        }

        var baseColorEvaluator = baseColorTexture?.CreateEvaluator(RenderTexture.TextureEvaluatorFlags.Normal);
        var alphaTextureEvaluator = alphaTexture?.CreateEvaluator(RenderTexture.TextureEvaluatorFlags.Normal);

        Bitmap bitmap = new(width, height);

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                double x = i / ((double)(width - 1));
                double y = j / ((double)(height - 1));

                y = 1.0 - y;

                Point3d uvw = new(x, y, 0.0);
                Color4f baseColorOut = baseColor;

                if (baseColorEvaluator is not null)
                {
                    baseColorOut = baseColorEvaluator.GetColor(uvw, Vector3d.Zero, Vector3d.Zero);

                    if (baseColorLinear)
                        baseColorOut = Color4f.ApplyGamma(baseColorOut, _workflow.PreProcessGamma);
                }

                if (!baseColorDiffuseAlphaForTransparency)
                    baseColorOut = new(baseColorOut.R, baseColorOut.G, baseColorOut.B, 1.0f);

                float evaluatedAlpha = (float)alpha;

                if (alphaTextureEvaluator is not null)
                {
                    Color4f alphaColor = alphaTextureEvaluator.GetColor(uvw, Vector3d.Zero, Vector3d.Zero);
                    evaluatedAlpha = alphaColor.L;
                }

                float alphaFinal = baseColor.A * evaluatedAlpha;
                hasAlpha = hasAlpha || alpha != 1.0f;
                Color4f colorFinal = new(baseColorOut.R, baseColorOut.G, baseColorOut.B, alphaFinal);
                bitmap.SetPixel(i, j, colorFinal.AsSystemColor());
            }
        }

        return GetTextureInfoFromBitmap(bitmap);
    }

    int AddTextureToBuffers(string texturePath)
    {
        var image = GetImageFromFile(texturePath);
        int imageIdx = _dummy.Images.AddAndReturnIndex(image);

        G.Texture texture = new()
        {
            Source = imageIdx,
            Sampler = 0
        };

        return _dummy.Textures.AddAndReturnIndex(texture);
    }

    G.Image GetImageFromFileText(string fileName)
    {
        byte[] imageBytes = GetImageBytesFromFile(fileName);

        G.Buffer textureBuffer = new()
        {
            Uri = Constants.TextBufferHeader + Convert.ToBase64String(imageBytes),
            ByteLength = imageBytes.Length,
        };

        int textureBufferIdx = _dummy.Buffers.AddAndReturnIndex(textureBuffer);

        G.BufferView textureBufferView = new()
        {
            Buffer = textureBufferIdx,
            ByteOffset = 0,
            ByteLength = textureBuffer.ByteLength,
        };
        int textureBufferViewIdx = _dummy.BufferViews.AddAndReturnIndex(textureBufferView);

        return new()
        {
            BufferView = textureBufferViewIdx,
            MimeType = G.Image.MimeTypeEnum.image_png,
        };
    }

    G.Image GetImageFromFile(string fileName)
    {
        if (_binary)
        {
            return GetImageFromFileBinary(fileName);
        }
        else
        {
            return GetImageFromFileText(fileName);
        }
    }

    G.Image GetImageFromFileBinary(string fileName)
    {
        byte[] imageBytes = GetImageBytesFromFile(fileName);
        int imageBytesOffset = (int)_binaryBuffer.Count;
        _binaryBuffer.AddRange(imageBytes);

        G.BufferView textureBufferView = new()
        {
            Buffer = 0,
            ByteOffset = imageBytesOffset,
            ByteLength = imageBytes.Length,
        };
        int textureBufferViewIdx = _dummy.BufferViews.AddAndReturnIndex(textureBufferView);

        return new()
        {
            BufferView = textureBufferViewIdx,
            MimeType = G.Image.MimeTypeEnum.image_png,
        };
    }

    static byte[] GetImageBytesFromFile(string fileName)
    {
        Bitmap bmp = new(fileName);
        return GetImageBytes(bmp);
    }

    G.TextureInfo AddTexture(string texturePath)
    {
        int textureIdx = AddTextureToBuffers(texturePath);
        return new() { Index = textureIdx, TexCoord = 0 };
    }

    G.MaterialNormalTextureInfo AddTextureNormal(Texture normalTexture)
    {
        int textureIdx = AddNormalTexture(normalTexture);
        float weight = GetTextureWeight(normalTexture);

        return new()
        {
            Index = textureIdx,
            TexCoord = 0,
            Scale = weight,
        };
    }

    int AddNormalTexture(Texture normalTexture)
    {
        Bitmap bmp = new(normalTexture.FileReference.FullPath);

        if (!Rhino.BitmapExtensions.IsNormalMap(bmp, true, out _))
            bmp = Rhino.BitmapExtensions.ConvertToNormalMap(bmp, true, out _);

        return GetTextureFromBitmap(bmp);
    }

    G.MaterialOcclusionTextureInfo AddTextureOcclusion(Texture texture)
    {
        int textureIdx = AddTextureToBuffers(texture.FileReference.FullPath);

        return new()
        {
            Index = textureIdx,
            TexCoord = 0,
            Strength = GetTextureWeight(texture),
        };
    }

    public G.TextureInfo AddMetallicRoughnessTexture(Material rhinoMaterial, bool exportTextures)
    {
        Texture? metalTexture = null;
        Texture? roughnessTexture = null;

        if (exportTextures)
        {
            metalTexture = rhinoMaterial.PhysicallyBased.GetTexture(TextureType.PBR_Metallic);
            roughnessTexture = rhinoMaterial.PhysicallyBased.GetTexture(TextureType.PBR_Roughness);
        }

        bool hasMetalTexture = metalTexture is not null && metalTexture.Enabled;
        bool hasRoughnessTexture = roughnessTexture is not null && roughnessTexture.Enabled;

        RenderTexture? renderTextureMetal = null;
        RenderTexture? renderTextureRoughness = null;

        int mWidth = 0;
        int mHeight = 0;
        int rWidth = 0;
        int rHeight = 0;

        // Get the textures
        if (hasMetalTexture)
        {
            renderTextureMetal = rhinoMaterial.RenderMaterial.GetTextureFromUsage(Rhino.Render.RenderMaterial.StandardChildSlots.PbrMetallic);
            renderTextureMetal.PixelSize(out mWidth, out mHeight, out _);
        }

        if (hasRoughnessTexture)
        {
            renderTextureRoughness = rhinoMaterial.RenderMaterial.GetTextureFromUsage(RenderMaterial.StandardChildSlots.PbrRoughness);
            renderTextureRoughness.PixelSize(out rWidth, out rHeight, out _);
        }

        int width = Math.Max(mWidth, rWidth);
        int height = Math.Max(mHeight, rHeight);

        // Metal
        var evalMetal = renderTextureMetal?.CreateEvaluator(RenderTexture.TextureEvaluatorFlags.Normal);

        // Roughness
        var evalRoughness = renderTextureRoughness?.CreateEvaluator(RenderTexture.TextureEvaluatorFlags.Normal);

        // Copy Metal to the blue channel, roughness to the green
        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);

        for (var j = 0; j < height - 1; j += 1)
        {
            for (var i = 0; i < width - 1; i += 1)
            {
                double x = i / (double)(width - 1);
                double y = j / (double)(height - 1);

                Point3d uvw = new(x, y, 0.0);

                float g = 1.0f;
                float b = 1.0f;

                if (evalMetal is not null)
                {
                    Color4f metal = evalMetal.GetColor(uvw, Vector3d.Zero, Vector3d.Zero);
                    b = metal.L; //grayscale maps, so we want lumonosity
                }

                if (evalRoughness is not null)
                {
                    Color4f roughnessColor = evalRoughness.GetColor(uvw, Vector3d.ZAxis, Vector3d.Zero);
                    g = roughnessColor.L; //grayscale maps, so we want lumonosity
                }

                Color4f color = new(0.0f, g, b, 1.0f);
                bitmap.SetPixel(i, height - j - 1, color.AsSystemColor());
            }
        }

        return GetTextureInfoFromBitmap(bitmap);
    }

    int GetTextureFromBitmap(Bitmap bitmap)
    {
        var image = GetImageFromBitmap(bitmap);
        int imageIdx = _dummy.Images.AddAndReturnIndex(image);

        G.Texture texture = new()
        {
            Source = imageIdx,
            Sampler = 0
        };

        return _dummy.Textures.AddAndReturnIndex(texture);
    }

    G.TextureInfo GetTextureInfoFromBitmap(Bitmap bitmap)
    {
        int textureIdx = GetTextureFromBitmap(bitmap);

        return new()
        {
            Index = textureIdx,
            TexCoord = 0
        };
    }

    G.Image GetImageFromBitmap(Bitmap bitmap)
    {
        if (_binary)
        {
            return GetImageFromBitmapBinary(bitmap);
        }
        else
        {
            return GetImageFromBitmapText(bitmap);
        }
    }

    G.Image GetImageFromBitmapText(Bitmap bitmap)
    {
        byte[] imageBytes = GetImageBytes(bitmap);

        G.Buffer textureBuffer = new()
        {
            Uri = Constants.TextBufferHeader + Convert.ToBase64String(imageBytes),
            ByteLength = imageBytes.Length
        };

        int textureBufferIdx = _dummy.Buffers.AddAndReturnIndex(textureBuffer);

        // Create bufferviews
        var textureBufferView = new glTFLoader.Schema.BufferView()
        {
            Buffer = textureBufferIdx,
            ByteOffset = 0,
            ByteLength = textureBuffer.ByteLength,
        };
        int textureBufferViewIdx = _dummy.BufferViews.AddAndReturnIndex(textureBufferView);

        return new()
        {
            BufferView = textureBufferViewIdx,
            MimeType = G.Image.MimeTypeEnum.image_png,
        };
    }

    G.Image GetImageFromBitmapBinary(Bitmap bitmap)
    {
        byte[] imageBytes = GetImageBytes(bitmap);
        int imageBytesOffset = _binaryBuffer.Count;
        _binaryBuffer.AddRange(imageBytes);

        // Create bufferviews
        G.BufferView textureBufferView = new()
        {
            Buffer = 0,
            ByteOffset = imageBytesOffset,
            ByteLength = imageBytes.Length,
        };
        int textureBufferViewIdx = _dummy.BufferViews.AddAndReturnIndex(textureBufferView);

        return new()
        {
            BufferView = textureBufferViewIdx,
            MimeType = G.Image.MimeTypeEnum.image_png,
        };
    }

    static byte[] GetImageBytes(Bitmap bitmap)
    {
        using MemoryStream imageStream = new(4096);
        bitmap.Save(imageStream, ImageFormat.Png);

        //Zero pad so its 4 byte aligned
        long mod = imageStream.Position % 4;
        imageStream.Write(Constants.Paddings[mod], 0, Constants.Paddings[mod].Length);

        return imageStream.ToArray();
    }

    static float GetTextureWeight(Texture texture)
    {
        texture.GetAlphaBlendValues(out double constant, out _, out _, out _, out _);
        return (float)constant;
    }
}
