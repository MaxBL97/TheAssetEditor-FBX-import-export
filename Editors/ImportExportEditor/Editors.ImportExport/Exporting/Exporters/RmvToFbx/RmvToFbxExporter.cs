using Editors.ImportExport.Common.FbxSdk;
using Editors.ImportExport.Importing.Importers.PngToDds.Helpers;
using Editors.ImportExport.Misc;
using MeshImportExport;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Types;
using TextureType = global::Shared.GameFormats.RigidModel.Types.TextureType;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Editors.ImportExport.Exporting.Exporters.RmvToFbx
{
    public class RmvToFbxExporter
    {
        private readonly AutodeskFbxService _fbxService;
        private readonly ISkeletonAnimationLookUpHelper _skeletonLookUpHelper;
        private readonly IPackFileService _packFileService;

        public RmvToFbxExporter(
            AutodeskFbxService fbxService,
            ISkeletonAnimationLookUpHelper skeletonLookUpHelper,
            IPackFileService packFileService)
        {
            _fbxService = fbxService;
            _skeletonLookUpHelper = skeletonLookUpHelper;
            _packFileService = packFileService;
        }

        internal ExportSupportEnum CanExportFile(PackFile file)
        {
            if (FileExtensionHelper.IsRmvFile(file.Name))
                return ExportSupportEnum.Supported;

            return ExportSupportEnum.NotSupported;
        }

        public void Export(RmvToFbxExporterSettings settings)
        {
            var rmv2 = new ModelFactory().Load(settings.InputModelFile.DataSource.ReadData());
            var animations = settings.InputAnimationFiles
                .Select(AnimationFile.Create)
                .ToList();

            AnimationFile? skeleton = null;
            if (settings.ExportSkeleton || settings.ExportAnimations)
            {
                if (!string.IsNullOrWhiteSpace(rmv2.Header.SkeletonName))
                    skeleton = _skeletonLookUpHelper.GetSkeletonFileFromName(rmv2.Header.SkeletonName);

                if (settings.ExportAnimations && skeleton == null)
                    throw new InvalidOperationException($"Could not find skeleton '{rmv2.Header.SkeletonName}' required to export animation clips to FBX.");
            }

            var externalTextureLinksByPackPath = settings.ExportTextures
                ? ExportReferencedTextures(rmv2, settings.OutputPath)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            _fbxService.ExportRmvToFbx(
                rmv2,
                settings.ExportSkeleton || settings.ExportAnimations ? skeleton : null,
                animations,
                settings.OutputPath,
                settings.ExportAnimations,
                exportMaterials: settings.ExportTextures,
                ascii: false,
                meshBlenderFriendlyOrientation: settings.BlenderFriendlyMeshOrientation,
                skeletonBlenderFriendlyOrientation: settings.BlenderFriendlySkeletonOrientation,
                animationBlenderFriendlyOrientation: settings.BlenderFriendlyAnimationOrientation,
                externalTextureLinksByPackPath: externalTextureLinksByPackPath);
        }


        private Dictionary<string, string> ExportReferencedTextures(RmvFile rmvFile, string outputFbxPath)
        {
            var externalTextureLinksByPackPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFbxPath));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return externalTextureLinksByPackPath;

            var textureDirectoryName = Path.GetFileNameWithoutExtension(outputFbxPath) + "_textures";
            var textureDirectory = Path.Combine(outputDirectory, textureDirectoryName);
            Directory.CreateDirectory(textureDirectory);

            foreach (var lod in rmvFile.ModelList)
            {
                foreach (var model in lod)
                {
                    foreach (var texture in model.Material.GetAllTextures().ToList())
                    {
                        if (string.IsNullOrWhiteSpace(texture.Path))
                            continue;

                        var originalPackPath = NormalizePackPath(texture.Path);
                        if (externalTextureLinksByPackPath.ContainsKey(originalPackPath))
                            continue;

                        var sourcePackFile = _packFileService.FindFile(texture.Path);
                        if (sourcePackFile == null)
                            continue;

                        var effectiveTextureType = ResolveTextureTypeFromPath(originalPackPath) ?? texture.TexureType;
                        var pngFileName = BuildBlenderTextureFileName(originalPackPath, effectiveTextureType);
                        var exportedPngPath = CreateUniqueFilePath(textureDirectory, pngFileName);
                        var pngBytes = TextureHelper.ConvertDdsToPng(sourcePackFile.DataSource.ReadData());
                        pngBytes = ConvertTexturePngForBlender(pngBytes, effectiveTextureType, originalPackPath);
                        File.WriteAllBytes(exportedPngPath, pngBytes);

                        // FBX links should be portable and Blender-friendly. The AE_Texture_* metadata
                        // still stores originalPackPath with the .dds extension for RMV2 reimport.
                        externalTextureLinksByPackPath[originalPackPath] = Path.Combine(textureDirectoryName, Path.GetFileName(exportedPngPath));
                    }
                }
            }

            return externalTextureLinksByPackPath;
        }

        private static string BuildBlenderTextureFileName(string packTexturePath, TextureType textureType)
        {
            var fileName = Path.GetFileName(packTexturePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = textureType + ".dds";

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = textureType.ToString();

            return SanitizeFileName(baseName) + ".png";
        }

        private static byte[] ConvertTexturePngForBlender(byte[] pngBytes, TextureType textureType, string packTexturePath)
        {
            // Do not blindly trust the RMV material slot when deciding whether a texture
            // needs channel conversion. Some material headers can expose legacy or ambiguous
            // slots, while the file name still tells us the real WH texture role.
            // Converting a base_colour texture as a material_map is exactly what turns
            // gold/red textures into blue/black garbage after roundtrip.
            var typeFromPath = ResolveTextureTypeFromPath(packTexturePath);
            var effectiveType = typeFromPath ?? textureType;

            return effectiveType switch
            {
                TextureType.Normal => ConvertOrangeNormalPngToBlueNormalPng(pngBytes),
                TextureType.MaterialMap => ConvertWh3MaterialMapPngToBlenderPng(pngBytes),
                TextureType.BaseColour => ConvertBaseColourPngForBlender(pngBytes),
                _ => pngBytes,
            };
        }

        private static byte[] ConvertBaseColourPngForBlender(byte[] pngBytes)
        {
            // TextureHelper.ConvertDdsToPng currently produces the channel order needed
            // by the AE DDS roundtrip, but for Blender preview it leaves normal colour
            // textures with red/blue inverted. This is NOT a TW role conversion like
            // normal/material-map handling; it only restores base colour PNGs so the
            // linked image shown by Blender matches the original diffuse/base colour.
            return SwapRedBlueChannels(pngBytes);
        }

        private static TextureType? ResolveTextureTypeFromPath(string? texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
                return null;

            var fileName = Path.GetFileNameWithoutExtension(texturePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            var lower = fileName.ToLowerInvariant();

            if (lower.EndsWith("_base_colour") || lower.EndsWith("_base_color") || lower.Contains("base_colour") || lower.Contains("base_color") || lower.Contains("diffuse") || lower.Contains("albedo"))
                return TextureType.BaseColour;

            if (lower.EndsWith("_material_map") || lower.Contains("material_map") || lower.Contains("metallic_roughness") || lower.Contains("metal_rough"))
                return TextureType.MaterialMap;

            if (lower.EndsWith("_normal") || lower.EndsWith("_n") || lower.Contains("normal"))
                return TextureType.Normal;

            if (lower.EndsWith("_mask") || lower.Contains("mask"))
                return TextureType.Mask;

            if (lower.Contains("emissive") || lower.Contains("emit"))
                return TextureType.Emissive;

            if (lower.Contains("spec"))
                return TextureType.Specular;

            if (lower.Contains("gloss"))
                return TextureType.Gloss;

            return null;
        }

        private static byte[] ConvertOrangeNormalPngToBlueNormalPng(byte[] pngBytes)
        {
            using var input = new MemoryStream(pngBytes);
            using var image = Image.FromStream(input);
            using var bitmap = new Bitmap(image);

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    var xRed = pixel.A;

                    // TextureHelper/Pfim decodes the linear DDS normal channel into a PNG
                    // value that is effectively linearized. Re-encode it for the external
                    // blue/purple normal PNG so a CA DDS -> PNG -> CA DDS roundtrip keeps
                    // the green/Y channel close to the original. Do not do this on import;
                    // import must only repack channels.
                    var yGreen = ColorChannels.GammaComponent(pixel.G, 1.0f / 2.2f);

                    bitmap.SetPixel(x, y, Color.FromArgb(255, xRed, yGreen, 255));
                }
            }

            return SavePng(bitmap);
        }

        private static byte[] ConvertWh3MaterialMapPngToBlenderPng(byte[] pngBytes)
        {
            using var input = new MemoryStream(pngBytes);
            using var image = Image.FromStream(input);
            using var bitmap = new Bitmap(image);

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(x, y, Color.FromArgb(255, pixel.B, pixel.G, pixel.R));
                }
            }

            return SavePng(bitmap);
        }

        private static byte[] SwapRedBlueChannels(byte[] pngBytes)
        {
            using var input = new MemoryStream(pngBytes);
            using var image = Image.FromStream(input);
            using var bitmap = new Bitmap(image);

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, pixel.B, pixel.G, pixel.R));
                }
            }

            return SavePng(bitmap);
        }

        private static byte[] SavePng(Bitmap bitmap)
        {
            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);
            return output.ToArray();
        }


        private static string NormalizePackPath(string path)
        {
            return path.Replace('/', '\\').Trim();
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(ch => invalid.Contains(ch) || ch == ' ' ? '_' : ch).ToArray();
            return new string(chars);
        }

        private static string CreateUniqueFilePath(string directory, string fileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var candidate = Path.Combine(directory, fileName);
            var index = 1;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, $"{baseName}_{index}{extension}");
                index++;
            }

            return candidate;
        }
    }
}
