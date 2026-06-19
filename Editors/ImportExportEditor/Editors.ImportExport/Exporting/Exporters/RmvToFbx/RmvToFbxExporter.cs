using Editors.ImportExport.Common.FbxSdk;
using Editors.ImportExport.Misc;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
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

            if (settings.ExportTextures)
                ExportReferencedTextures(rmv2, settings.OutputPath);

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
                animationBlenderFriendlyOrientation: settings.BlenderFriendlyAnimationOrientation);
        }


        private void ExportReferencedTextures(RmvFile rmvFile, string outputFbxPath)
        {
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFbxPath));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return;

            var textureDirectory = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(outputFbxPath) + "_textures");
            Directory.CreateDirectory(textureDirectory);

            var exportedPathsByPackPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var lod in rmvFile.ModelList)
            {
                foreach (var model in lod)
                {
                    foreach (var texture in model.Material.GetAllTextures().ToList())
                    {
                        if (string.IsNullOrWhiteSpace(texture.Path))
                            continue;

                        if (!exportedPathsByPackPath.TryGetValue(texture.Path, out var exportedPath))
                        {
                            var sourcePackFile = _packFileService.FindFile(texture.Path);
                            if (sourcePackFile == null)
                                continue;

                            var fileName = Path.GetFileName(texture.Path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                            if (string.IsNullOrWhiteSpace(fileName))
                                fileName = $"{texture.TexureType}.dds";

                            exportedPath = CreateUniqueFilePath(textureDirectory, fileName);
                            File.WriteAllBytes(exportedPath, sourcePackFile.DataSource.ReadData());
                            exportedPathsByPackPath[texture.Path] = exportedPath;
                        }

                        model.Material.SetTexture(texture.TexureType, exportedPath);
                    }
                }
            }
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
