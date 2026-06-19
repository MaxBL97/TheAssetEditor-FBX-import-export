using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Exporting.Exporters.RmvToFbx;

public sealed record RmvToFbxExporterSettings(
    PackFile InputModelFile,
    List<PackFile> InputAnimationFiles,
    string OutputPath,
    bool ExportTextures,
    bool ExportSkeleton,
    bool ExportAnimations,
    bool BlenderFriendlyMeshOrientation,
    bool BlenderFriendlySkeletonOrientation,
    bool BlenderFriendlyAnimationOrientation);
