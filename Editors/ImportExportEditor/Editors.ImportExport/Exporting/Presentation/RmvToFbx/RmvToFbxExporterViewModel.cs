using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Exporters.RmvToFbx;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Exporting.Presentation.RmvToFbx
{
    internal partial class RmvToFbxExporterViewModel : ObservableObject, IExporterViewModel, IViewProvider<RmvToFbxExporterView>
    {
        private readonly RmvToFbxExporter _exporter;
        private readonly IPackFileService _packFileService;

        public string DisplayName => "RMV2 to FBX";
        public string OutputExtension => ".fbx";

        [ObservableProperty] bool _exportTextures = false;
        [ObservableProperty] bool _exportSkeleton = true;
        [ObservableProperty] bool _exportAnimations = false;
        [ObservableProperty] bool _blenderFriendlyMeshOrientation = true;
        [ObservableProperty] bool _blenderFriendlySkeletonOrientation = true;
        [ObservableProperty] bool _blenderFriendlyAnimationOrientation = false;
        [ObservableProperty] string _animationPackPaths = string.Empty;

        public RmvToFbxExporterViewModel(RmvToFbxExporter exporter, IPackFileService packFileService)
        {
            _exporter = exporter;
            _packFileService = packFileService;
        }

        public ExportSupportEnum CanExportFile(PackFile file) => _exporter.CanExportFile(file);

        public void Execute(PackFile exportSource, string outputPath, bool generateImporter)
        {
            var settings = new RmvToFbxExporterSettings(
                InputModelFile: exportSource,
                InputAnimationFiles: ResolveAnimationFiles(),
                OutputPath: outputPath,
                ExportTextures: ExportTextures,
                ExportSkeleton: ExportSkeleton,
                ExportAnimations: ExportAnimations,
                BlenderFriendlyMeshOrientation: BlenderFriendlyMeshOrientation,
                BlenderFriendlySkeletonOrientation: BlenderFriendlySkeletonOrientation,
                BlenderFriendlyAnimationOrientation: BlenderFriendlyAnimationOrientation);

            _exporter.Export(settings);
        }

        private List<PackFile> ResolveAnimationFiles()
        {
            if (!ExportAnimations || string.IsNullOrWhiteSpace(AnimationPackPaths))
                return [];

            var animationPaths = AnimationPackPaths
                .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim('"'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            var output = new List<PackFile>();
            foreach (var animationPath in animationPaths)
            {
                var normalizedPath = animationPath.Replace('/', '\\');
                var packFile = _packFileService.FindFile(normalizedPath);
                if (packFile == null)
                    throw new FileNotFoundException($"Animation file was not found in loaded pack files: {normalizedPath}");

                output.Add(packFile);
            }

            return output;
        }
    }
}
