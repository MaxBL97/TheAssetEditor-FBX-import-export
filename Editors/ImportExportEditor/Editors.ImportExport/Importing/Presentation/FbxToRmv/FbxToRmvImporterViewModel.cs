using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.ImportExport.Importing.Importers.FbxToRmv;
using Editors.ImportExport.Importing.Presentation;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Importing.Presentation.FbxToRmv
{
    public partial class FbxToRmvImporterViewModel : ObservableObject, IImporterViewModel, IFileNameAwareImporterViewModel, IViewProvider<FbxToRmvImporterView>
    {
        private readonly FbxImporter _importer;

        public string DisplayName => "FBX Importer";
        public string OutputExtension => ".rigid_model_v2";
        public string[] InputExtensions => [".fbx"];

        public IReadOnlyList<FbxImportContentMode> ImportModes { get; } =
        [
            FbxImportContentMode.Auto,
            FbxImportContentMode.StaticMesh,
            FbxImportContentMode.RiggedMesh,
            FbxImportContentMode.Animation
        ];

        [ObservableProperty] FbxImportContentMode _selectedImportMode = FbxImportContentMode.RiggedMesh;
        [ObservableProperty] bool _importMeshes = true;
        [ObservableProperty] bool _importSkeleton = true;
        [ObservableProperty] bool _importMaterials = false;
        [ObservableProperty] bool _convertFromBlenderMaterialMap = false;
        [ObservableProperty] bool _convertNormalTextureToOrange = false;
        [ObservableProperty] bool _importAnimations = false;
        [ObservableProperty] float _animationKeysPerSecond = 20.0f;
        [ObservableProperty] bool _customSkeleton = false;
        [ObservableProperty] bool _blenderFriendlyMeshOrientation = true;
        [ObservableProperty] bool _blenderFriendlyAnimationOrientation = false;
        private bool _isApplyingModeDefaults;

        public FbxToRmvImporterViewModel(FbxImporter importer)
        {
            _importer = importer;
        }

        public ImportSupportEnum CanImportFile(PackFile file) => _importer.CanImportFile(file);

        public void ConfigureFromInputFile(PackFile file)
        {
            var mode = DetectModeFromFileName(file.Name);
            SelectedImportMode = mode;
            ApplyModeDefaults(mode);
        }

        partial void OnSelectedImportModeChanged(FbxImportContentMode value)
        {
            if (_isApplyingModeDefaults)
                return;

            ApplyModeDefaults(value);
        }

        private void ApplyModeDefaults(FbxImportContentMode mode)
        {
            _isApplyingModeDefaults = true;
            try
            {
                switch (mode)
                {
                    case FbxImportContentMode.Animation:
                        ImportMeshes = false;
                        ImportSkeleton = false;
                        ImportMaterials = false;
                        ImportAnimations = true;
                        CustomSkeleton = false;
                        BlenderFriendlyMeshOrientation = false;
                        BlenderFriendlyAnimationOrientation = true;
                        break;

                    case FbxImportContentMode.StaticMesh:
                        ImportMeshes = true;
                        ImportSkeleton = false;
                        ImportMaterials = true;
                        ImportAnimations = false;
                        CustomSkeleton = false;
                        BlenderFriendlyMeshOrientation = true;
                        BlenderFriendlyAnimationOrientation = false;
                        break;

                    case FbxImportContentMode.RiggedMesh:
                    case FbxImportContentMode.Auto:
                    default:
                        ImportMeshes = true;
                        ImportSkeleton = true;
                        ImportMaterials = true;
                        ImportAnimations = false;
                        CustomSkeleton = false;
                        BlenderFriendlyMeshOrientation = true;
                        BlenderFriendlyAnimationOrientation = false;
                        break;
                }
            }
            finally
            {
                _isApplyingModeDefaults = false;
            }
        }

        private static FbxImportContentMode DetectModeFromFileName(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);

            if (fileName.EndsWith("_anim", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("_animation", StringComparison.OrdinalIgnoreCase))
                return FbxImportContentMode.Animation;

            if (fileName.EndsWith("_static", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("_staticmesh", StringComparison.OrdinalIgnoreCase))
                return FbxImportContentMode.StaticMesh;

            return FbxImportContentMode.RiggedMesh;
        }

        public void Execute(PackFile importSource, string outputPath, IPackFileContainer packFileContainer, GameTypeEnum gameType)
        {
            var settings = new FbxImporterSettings(
                InputFbxFile: importSource.Name,
                DestinationPackPath: outputPath,
                DestinationPackFileContainer: packFileContainer,
                SelectedGame: gameType,
                ImportContentMode: SelectedImportMode,
                ImportMeshes: ImportMeshes,
                ImportSkeleton: ImportSkeleton,
                ImportMaterials: ImportMaterials,
                ConvertMaterialFromBlenderType: ConvertFromBlenderMaterialMap,
                ConvertNormalTextureFromBlueToOrangeType: ConvertNormalTextureToOrange,
                ImportAnimations: ImportAnimations,
                AnimationKeysPerSecond: AnimationKeysPerSecond,
                MirrorMesh: false,
                CustomSkeleton: CustomSkeleton,
                BlenderFriendlyMeshOrientation: BlenderFriendlyMeshOrientation,
                BlenderFriendlyAnimationOrientation: BlenderFriendlyAnimationOrientation);

            _importer.Import(settings);
        }
    }
}
