using Editors.ImportExport.Common.FbxSdk;
using Editors.ImportExport.Misc;
using GameWorld.Core.Services;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;

namespace Editors.ImportExport.Exporting.Exporters.AnimToFbx
{
    public sealed class AnimToFbxExporter
    {
        private readonly AutodeskFbxService _fbxService;
        private readonly ISkeletonAnimationLookUpHelper _skeletonLookUpHelper;

        public AnimToFbxExporter(AutodeskFbxService fbxService, ISkeletonAnimationLookUpHelper skeletonLookUpHelper)
        {
            _fbxService = fbxService;
            _skeletonLookUpHelper = skeletonLookUpHelper;
        }

        internal ExportSupportEnum CanExportFile(PackFile file)
        {
            if (FileExtensionHelper.IsAnimFile(file.Name))
                return ExportSupportEnum.Supported;

            return ExportSupportEnum.NotSupported;
        }

        public void Export(PackFile source, string outputPath, bool blenderFriendlyOrientation = false)
        {
            var animationFile = AnimationFile.Create(source);
            var skeletonName = animationFile.Header.SkeletonName;
            var skeletonFile = string.IsNullOrWhiteSpace(skeletonName)
                ? null
                : _skeletonLookUpHelper.GetSkeletonFileFromName(skeletonName);

            if (skeletonFile == null)
                throw new InvalidOperationException($"Could not find skeleton '{skeletonName}' for animation '{source.Name}'. Load the CA pack files containing the skeleton .anim first.");

            _fbxService.ExportAnimationToFbx(
                skeletonFile,
                animationFile,
                outputPath,
                ascii: false,
                blenderFriendlyOrientation: blenderFriendlyOrientation);
        }
    }
}
