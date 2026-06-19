using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;

namespace Editors.ImportExport.Importing.Importers.FbxToRmv;

public sealed record FbxImporterSettings(
    string InputFbxFile,
    string DestinationPackPath,
    IPackFileContainer DestinationPackFileContainer,
    GameTypeEnum SelectedGame,
    FbxImportContentMode ImportContentMode,
    bool ImportMeshes,
    bool ImportSkeleton,
    bool ImportMaterials,
    bool ConvertMaterialFromBlenderType,
    bool ConvertNormalTextureFromBlueToOrangeType,
    bool ImportAnimations,
    float AnimationKeysPerSecond,
    bool MirrorMesh,
    bool CustomSkeleton,
    bool BlenderFriendlyMeshOrientation,
    bool BlenderFriendlyAnimationOrientation);
