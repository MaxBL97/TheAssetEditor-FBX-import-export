using System.IO;
using Shared.ByteParsing;
using CommonControls.BaseDialogs.ErrorListDialog;
using Editors.ImportExport.Common.FbxSdk;
using Editors.ImportExport.Importing.Importers.PngToDds;
using Editors.ImportExport.Misc;
using GameWorld.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Transforms;

namespace Editors.ImportExport.Importing.Importers.FbxToRmv;

public sealed class FbxImporter
{
    private readonly AutodeskFbxService _fbxService;
    private readonly IPackFileService _packFileService;
    private readonly IStandardDialogs _dialogService;
    private readonly ISkeletonAnimationLookUpHelper _skeletonLookUpHelper;

    private enum SkeletonResolveSource
    {
        None,
        CaCache,
        CustomPack,
        FbxFallback,
    }

    private sealed record SkeletonResolveResult(AnimationFile? Skeleton, SkeletonResolveSource Source);

    public FbxImporter(
        AutodeskFbxService fbxService,
        IPackFileService packFileService,
        IStandardDialogs dialogService,
        ISkeletonAnimationLookUpHelper skeletonLookUpHelper)
    {
        _fbxService = fbxService;
        _packFileService = packFileService;
        _dialogService = dialogService;
        _skeletonLookUpHelper = skeletonLookUpHelper;
    }

    public ImportSupportEnum CanImportFile(PackFile file)
    {
        if (FileExtensionHelper.IsFbxFile(file.Name))
            return ImportSupportEnum.HighPriority;

        return ImportSupportEnum.NotSupported;
    }

    public void Import(FbxImporterSettings settings)
    {
        try
        {
            var importedScene = _fbxService.ImportScene(settings.InputFbxFile);
            var importMode = ResolveImportMode(settings, importedScene);

            switch (importMode)
            {
                case FbxImportContentMode.StaticMesh:
                    ImportMesh(settings, importedScene, skeleton: null, skeletonName: string.Empty);
                    break;

                case FbxImportContentMode.RiggedMesh:
                {
                    var skeletonResult = settings.ImportSkeleton
                        ? ResolveSkeleton(settings, importedScene.Bones, importedScene.SkeletonName)
                        : new SkeletonResolveResult(null, SkeletonResolveSource.None);

                    var skeleton = skeletonResult.Skeleton;
                    var skeletonName = skeleton?.Header.SkeletonName ?? importedScene.SkeletonName ?? string.Empty;
                    ImportMesh(settings, importedScene, skeleton, skeletonName);
                    break;
                }

                case FbxImportContentMode.Animation:
                {
                    var skeletonResult = ResolveSkeleton(settings, importedScene.Bones, importedScene.SkeletonName, allowFbxFallback: false);
                    if (skeletonResult.Skeleton == null)
                        throw new InvalidOperationException("No matching CA/custom skeleton was found for this FBX animation. Animation import needs a target Total War skeleton; it cannot use the FBX fallback skeleton.");

                    SaveAnimFileToPack(settings, skeletonResult.Skeleton);
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unsupported FBX import mode '{importMode}'.");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowExceptionWindow(ex);
        }
    }

    private FbxImportContentMode ResolveImportMode(FbxImporterSettings settings, AssetEditor.Native.FbxSdkBridge.FbxImportedScene importedScene)
    {
        if (settings.ImportContentMode != FbxImportContentMode.Auto)
            return settings.ImportContentMode;

        var nameMode = DetectModeFromFileName(settings.InputFbxFile);
        if (nameMode != null)
            return nameMode.Value;

        var hasMeshes = importedScene.Meshes.Length > 0;
        var hasBones = importedScene.Bones.Length > 0;

        if (settings.ImportAnimations && !settings.ImportMeshes)
            return FbxImportContentMode.Animation;

        if (settings.ImportMeshes && settings.ImportSkeleton && hasBones)
            return FbxImportContentMode.RiggedMesh;

        if (settings.ImportMeshes && hasMeshes)
            return FbxImportContentMode.StaticMesh;

        if (settings.ImportAnimations)
            return FbxImportContentMode.Animation;

        if (hasMeshes && hasBones)
            return FbxImportContentMode.RiggedMesh;

        if (hasMeshes)
            return FbxImportContentMode.StaticMesh;

        return FbxImportContentMode.RiggedMesh;
    }


    private static FbxImportContentMode? DetectModeFromFileName(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);

        if (fileName.EndsWith("_anim", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_animation", StringComparison.OrdinalIgnoreCase))
            return FbxImportContentMode.Animation;

        if (fileName.EndsWith("_static", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_staticmesh", StringComparison.OrdinalIgnoreCase))
            return FbxImportContentMode.StaticMesh;

        if (fileName.EndsWith("_rigged", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_riggedmesh", StringComparison.OrdinalIgnoreCase))
            return FbxImportContentMode.RiggedMesh;

        return null;
    }

    private static string GetOutputBaseName(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        foreach (var suffix in new[] { "_animation", "_anim", "_staticmesh", "_static", "_riggedmesh", "_rigged" })
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return fileName[..^suffix.Length];
        }

        return fileName;
    }

    private void ImportMesh(FbxImporterSettings settings, AssetEditor.Native.FbxSdkBridge.FbxImportedScene importedScene, AnimationFile? skeleton, string skeletonName)
    {
        if (!settings.ImportMeshes)
            return;

        var rmvFile = _fbxService.CreateRmvFromImportedFbxScene(
            importedScene,
            skeleton,
            skeletonName,
            settings.BlenderFriendlyMeshOrientation || settings.MirrorMesh,
            settings.ImportMaterials);

        if (settings.ImportMaterials)
            ImportExternalTextureFilesToPack(settings, rmvFile);

        SaveRmvFileToPack(settings, rmvFile);
    }



    private void ImportExternalTextureFilesToPack(FbxImporterSettings settings, RmvFile rmvFile)
    {
        var fbxDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.InputFbxFile)) ?? string.Empty;
        var texturePackFolder = CombinePackPath(settings.DestinationPackPath, "tex");
        var importedBySourcePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lod in rmvFile.ModelList)
        {
            foreach (var model in lod)
            {
                foreach (var texture in model.Material.GetAllTextures().ToList())
                {
                    if (string.IsNullOrWhiteSpace(texture.Path))
                        continue;

                    var externalPath = ResolveExternalTexturePath(texture.Path, fbxDirectory);
                    if (externalPath == null)
                        continue;

                    if (!importedBySourcePath.TryGetValue(externalPath, out var packTexturePath))
                    {
                        var textureFileName = BuildImportedTextureFileName(settings, model.Material, texture.TexureType, externalPath);
                        var texturePackFile = CreatePackTextureFromExternalFile(externalPath, texture.TexureType, settings, textureFileName);
                        _packFileService.AddFilesToPack(settings.DestinationPackFileContainer, [new NewPackFileEntry(texturePackFolder, texturePackFile)]);

                        packTexturePath = CombinePackPath(texturePackFolder, textureFileName);
                        importedBySourcePath[externalPath] = packTexturePath;
                    }

                    model.Material.SetTexture(texture.TexureType, packTexturePath);
                }
            }
        }
    }

    private static string? ResolveExternalTexturePath(string texturePath, string fbxDirectory)
    {
        var normalizedPath = texturePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return null;

        if (Path.IsPathRooted(normalizedPath) && File.Exists(normalizedPath))
            return Path.GetFullPath(normalizedPath);

        var relativeToFbx = Path.Combine(fbxDirectory, normalizedPath);
        if (File.Exists(relativeToFbx))
            return Path.GetFullPath(relativeToFbx);

        var relativeFileName = Path.Combine(fbxDirectory, Path.GetFileName(normalizedPath));
        if (File.Exists(relativeFileName))
            return Path.GetFullPath(relativeFileName);

        return null;
    }

    private static PackFile CreatePackTextureFromExternalFile(
        string externalPath,
        TextureType textureType,
        FbxImporterSettings settings,
        string textureFileName)
    {
        var extension = Path.GetExtension(externalPath);
        if (string.Equals(extension, ".dds", StringComparison.OrdinalIgnoreCase))
            return new PackFile(textureFileName, new MemorySource(File.ReadAllBytes(externalPath)));

        return PngToDdsImporter.Import(externalPath, textureType, settings.SelectedGame, textureFileName);
    }

    private static string BuildImportedTextureFileName(
        FbxImporterSettings settings,
        IRmvMaterial material,
        TextureType textureType,
        string externalPath)
    {
        var modelName = SanitizeFileName(material.ModelName);
        var baseName = SanitizeFileName(GetOutputBaseName(settings.InputFbxFile));
        var suffix = ResolveTextureFileSuffix(textureType);
        return $"{baseName}_{modelName}_{suffix}.dds";
    }

    private static string ResolveTextureFileSuffix(TextureType textureType)
    {
        return textureType switch
        {
            TextureType.Normal => "normal",
            TextureType.MaterialMap => "material_map",
            TextureType.Mask => "mask",
            TextureType.Ambient_occlusion => "ao",
            TextureType.Specular => "specular",
            TextureType.Gloss => "gloss",
            TextureType.Emissive => "emissive",
            TextureType.Blood => "blood",
            TextureType.BaseColour => "base_colour",
            TextureType.Diffuse => "diffuse",
            _ => textureType.ToString().ToLowerInvariant(),
        };
    }

    private static string SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "texture";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch == ' ' ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static string CombinePackPath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
            return right.Replace('/', '\\');

        return (left.TrimEnd('\\', '/') + "\\" + right.TrimStart('\\', '/')).Replace('/', '\\');
    }

    private void SaveRmvFileToPack(FbxImporterSettings settings, RmvFile rmvFile)
    {
        var bytes = ModelFactory.Create().Save(rmvFile);
        var importedFileName = GetOutputBaseName(settings.InputFbxFile) + ".rigid_model_v2";
        var packFile = new PackFile(importedFileName, new MemorySource(bytes));
        var newFile = new NewPackFileEntry(settings.DestinationPackPath, packFile);
        _packFileService.AddFilesToPack(settings.DestinationPackFileContainer, [newFile]);
    }

    private void TrySaveAnimFileToPack(FbxImporterSettings settings, AnimationFile skeleton, SkeletonResolveSource skeletonSource)
    {
        if (skeletonSource == SkeletonResolveSource.FbxFallback)
        {
            var errorList = new ErrorList();
            errorList.Warning(
                "FBX animation skipped",
                "The import is using a fallback skeleton created from the FBX. Animation import is skipped because no CA skeleton .anim was matched.");
            ErrorListWindow.ShowDialog("FBX Import", errorList);
            return;
        }

        try
        {
            SaveAnimFileToPack(settings, skeleton);
        }
        catch (Exception ex)
        {
            var errorList = new ErrorList();
            errorList.Warning(
                "FBX animation skipped",
                "The RMV2 import succeeded, but the first FBX animation stack could not be converted to .anim. " + ex.Message);
            ErrorListWindow.ShowDialog("FBX Import", errorList);
        }
    }

    private void SaveAnimFileToPack(FbxImporterSettings settings, AnimationFile skeleton)
    {
        var animFile = _fbxService.ConvertFbxAnimationToAnimFile(
            settings.InputFbxFile,
            skeleton,
            settings.AnimationKeysPerSecond,
            version: 7,
            includeEndBones: false,
            blenderFriendlyOrientation: settings.BlenderFriendlyAnimationOrientation);

        var bytes = AnimationFile.ConvertToBytes(animFile);
        var importedFileName = GetOutputBaseName(settings.InputFbxFile) + ".anim";
        var packFile = new PackFile(importedFileName, new MemorySource(bytes));
        var newFile = new NewPackFileEntry(settings.DestinationPackPath, packFile);
        _packFileService.AddFilesToPack(settings.DestinationPackFileContainer, [newFile]);
    }

    private SkeletonResolveResult ResolveSkeleton(
        FbxImporterSettings settings,
        IReadOnlyList<AssetEditor.Native.FbxSdkBridge.FbxSkeletonBoneInfo> importedBones,
        string? importedSkeletonName,
        bool allowFbxFallback = true)
    {
        var best = FindBestSkeletonInLoadedGameCache(importedBones, importedSkeletonName, settings.CustomSkeleton);
        if (best.Skeleton != null)
            return best;

        if (importedBones.Count == 0 || !allowFbxFallback)
            return new SkeletonResolveResult(null, SkeletonResolveSource.None);

        var fallbackSkeleton = CreateSkeletonFromImportedFbx(importedBones);
        if (fallbackSkeleton != null)
        {
            var errorList = new ErrorList();
            errorList.Warning(
                "Skeleton From FBX",
                settings.CustomSkeleton
                    ? "No matching Total War skeleton .anim was found in the loaded CA or custom packs. The imported FBX skeleton will be used instead, so the model can remain skinned."
                    : "No matching Total War skeleton .anim was found in the loaded CA All Game Packs. The imported FBX skeleton will be used instead, so the model can remain skinned. Enable custom_skeleton to also search the editable/mod pack.");
            ErrorListWindow.ShowDialog("FBX Import", errorList);
            return new SkeletonResolveResult(fallbackSkeleton, SkeletonResolveSource.FbxFallback);
        }

        return new SkeletonResolveResult(null, SkeletonResolveSource.None);
    }

    private SkeletonResolveResult FindBestSkeletonInLoadedGameCache(
        IReadOnlyList<AssetEditor.Native.FbxSdkBridge.FbxSkeletonBoneInfo> importedBones,
        string? importedSkeletonName,
        bool includeCustomSkeletons)
    {
        if (importedBones.Count == 0)
            return new SkeletonResolveResult(null, SkeletonResolveSource.None);

        var importedBoneNames = CreateBoneNameSet(importedBones);
        if (importedBoneNames.Count == 0)
            return new SkeletonResolveResult(null, SkeletonResolveSource.None);

        AnimationFile? bestSkeleton = null;
        var bestScore = 0;
        var bestNameMatch = false;
        var bestSource = SkeletonResolveSource.None;

        foreach (var (container, source) in GetSkeletonSearchContainers(includeCustomSkeletons))
        {
            var loadedAnimations = _packFileService.FindAllWithExtention(".anim", container);

            foreach (var (filePath, packFile) in loadedAnimations)
            {
                if (!IsCandidateSkeletonPath(filePath))
                    continue;

                var candidate = TryLoadAnimationFile(packFile);
                if (candidate == null)
                    continue;

                var score = ScoreSkeleton(candidate, importedBoneNames);
                var nameMatch = IsSkeletonNameMatch(candidate, filePath, importedSkeletonName);
                var weightedScore = score + (nameMatch ? importedBoneNames.Count + 1000 : 0);

                if (weightedScore > bestScore)
                {
                    bestScore = weightedScore;
                    bestSkeleton = candidate;
                    bestNameMatch = nameMatch;
                    bestSource = source;
                }
            }
        }

        if (bestSkeleton == null)
            return new SkeletonResolveResult(null, SkeletonResolveSource.None);

        var rawScore = ScoreSkeleton(bestSkeleton, importedBoneNames);
        if (bestNameMatch || IsGoodSkeletonMatch(rawScore, importedBoneNames.Count))
            return new SkeletonResolveResult(bestSkeleton, bestSource);

        return new SkeletonResolveResult(null, SkeletonResolveSource.None);
    }

    private IEnumerable<(IPackFileContainer Container, SkeletonResolveSource Source)> GetSkeletonSearchContainers(bool includeCustomSkeletons)
    {
        foreach (var container in _packFileService.GetAllPackfileContainers().Where(x => x.IsCaPackFile))
            yield return (container, SkeletonResolveSource.CaCache);

        if (!includeCustomSkeletons)
            yield break;

        foreach (var container in _packFileService.GetAllPackfileContainers().Where(x => !x.IsCaPackFile))
            yield return (container, SkeletonResolveSource.CustomPack);
    }

    private static bool IsSkeletonNameMatch(AnimationFile candidate, string filePath, string? importedSkeletonName)
    {
        if (string.IsNullOrWhiteSpace(importedSkeletonName))
            return false;

        var expected = Path.GetFileNameWithoutExtension(importedSkeletonName);
        var candidateHeader = Path.GetFileNameWithoutExtension(candidate.Header.SkeletonName ?? string.Empty);
        var candidateFile = Path.GetFileNameWithoutExtension(filePath);

        return string.Equals(expected, candidateHeader, StringComparison.OrdinalIgnoreCase)
            || string.Equals(expected, candidateFile, StringComparison.OrdinalIgnoreCase);
    }

    private static AnimationFile? TryLoadAnimationFile(PackFile packFile)
    {
        try
        {
            return AnimationFile.Create(packFile);
        }
        catch
        {
            try
            {
                var data = packFile.DataSource.ReadData();
                if (data.Length < 8)
                    return null;

                return AnimationFile.Create(new ByteChunk(data));
            }
            catch
            {
                return null;
            }
        }
    }

    private static AnimationFile? CreateSkeletonFromImportedFbx(IReadOnlyList<AssetEditor.Native.FbxSdkBridge.FbxSkeletonBoneInfo> importedBones)
    {
        if (importedBones.Count == 0)
            return null;

        var skeletonName = importedBones.Any(x => string.Equals(x.Name, "animroot", StringComparison.OrdinalIgnoreCase))
            ? "fbx_imported"
            : importedBones[0].Name;

        var animationFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 7,
                Unknown0_alwaysOne = 1,
                FrameRate = 20,
                SkeletonName = string.IsNullOrWhiteSpace(skeletonName) ? "fbx_imported" : skeletonName,
                FlagCount = 0,
                AnimationTotalPlayTimeInSec = 0.1f,
            },
            Bones = importedBones
                .Select((bone, index) => new AnimationFile.BoneInfo
                {
                    Name = bone.Name,
                    Id = index,
                    ParentId = bone.ParentId,
                })
                .ToArray(),
        };

        var bindFrame = new AnimationFile.Frame();
        foreach (var bone in importedBones)
        {
            bindFrame.Transforms.Add(new RmvVector3(
                ReadArray(bone.LocalTranslation, 0),
                ReadArray(bone.LocalTranslation, 1),
                ReadArray(bone.LocalTranslation, 2)));

            bindFrame.Quaternion.Add(NormalizeQuaternion(new RmvVector4(
                ReadArray(bone.LocalRotationQuaternion, 0),
                ReadArray(bone.LocalRotationQuaternion, 1),
                ReadArray(bone.LocalRotationQuaternion, 2),
                ReadArray(bone.LocalRotationQuaternion, 3, 1))));
        }

        var part = new AnimationFile.AnimationPart();
        for (var boneIndex = 0; boneIndex < importedBones.Count; boneIndex++)
        {
            part.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
            part.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
        }
        part.DynamicFrames.Add(bindFrame);
        animationFile.AnimationParts.Add(part);

        return animationFile;
    }

    private static float ReadArray(IReadOnlyList<float>? values, int index, float fallback = 0)
    {
        return values != null && index >= 0 && index < values.Count ? values[index] : fallback;
    }

    private static RmvVector4 NormalizeQuaternion(RmvVector4 quaternion)
    {
        var length = MathF.Sqrt(quaternion.X * quaternion.X + quaternion.Y * quaternion.Y + quaternion.Z * quaternion.Z + quaternion.W * quaternion.W);
        if (length <= float.Epsilon)
            return new RmvVector4(0, 0, 0, 1);

        return new RmvVector4(
            quaternion.X / length,
            quaternion.Y / length,
            quaternion.Z / length,
            quaternion.W / length);
    }

    private static HashSet<string> CreateBoneNameSet(IReadOnlyList<AssetEditor.Native.FbxSdkBridge.FbxSkeletonBoneInfo> importedBones)
    {
        return importedBones
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static int ScoreSkeleton(AnimationFile candidate, HashSet<string> importedBoneNames)
    {
        return candidate.Bones.Count(x => importedBoneNames.Contains(x.Name));
    }

    private static bool IsGoodSkeletonMatch(int score, int importedBoneCount)
    {
        var requiredScore = Math.Max(4, Math.Min(24, importedBoneCount / 3));
        return score >= requiredScore;
    }

    private static bool IsCandidateSkeletonPath(string filePath)
    {
        var normalizedPath = filePath.Replace('/', '\\');
        return normalizedPath.StartsWith("animations\\", StringComparison.OrdinalIgnoreCase)
            && normalizedPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
            && !normalizedPath.EndsWith(".anm.meta", StringComparison.OrdinalIgnoreCase);
    }
}
