using System.Globalization;
using System.IO;
using AssetEditor.Native.FbxSdkBridge;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.RigidModel.Vertex;
using XNA = Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel.Transforms;

namespace Editors.ImportExport.Common.FbxSdk;

public sealed class AutodeskFbxService
{
    public FbxSceneInfo InspectScene(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FbxBridge.InspectScene(path);
    }

    public IReadOnlyList<string> GetNodeHierarchy(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FbxBridge.GetNodeHierarchy(path);
    }

    public FbxSkeletonInfo GetSkeleton(string path, string? skeletonRootName = null, bool includeEndBones = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FbxBridge.ExtractSkeleton(path, skeletonRootName ?? string.Empty, includeEndBones);
    }

    public void CopyFbx(string inputPath, string outputPath, bool ascii = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        FbxBridge.CopyScene(inputPath, outputPath, ascii);
    }

    public void ExportScene(FbxExportScene scene, string outputPath, bool ascii = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        FbxBridge.ExportScene(scene, outputPath, ascii);
    }

    public void ExportRmvToFbx(
        RmvFile rmvFile,
        AnimationFile? skeletonFile,
        IReadOnlyList<AnimationFile> animationFiles,
        string outputPath,
        bool exportAnimations,
        bool exportMaterials = true,
        bool ascii = false,
        bool meshBlenderFriendlyOrientation = true,
        bool skeletonBlenderFriendlyOrientation = true,
        bool animationBlenderFriendlyOrientation = false)
    {
        ArgumentNullException.ThrowIfNull(rmvFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var scene = CreateExportSceneFromRmv(
            rmvFile,
            skeletonFile,
            exportAnimations ? animationFiles : [],
            exportMaterials,
            meshBlenderFriendlyOrientation,
            skeletonBlenderFriendlyOrientation,
            animationBlenderFriendlyOrientation);
        ExportScene(scene, outputPath, ascii);
    }

    public void ExportAnimationToFbx(
        AnimationFile skeletonFile,
        AnimationFile animationFile,
        string outputPath,
        bool ascii = false,
        bool blenderFriendlyOrientation = false)
    {
        ArgumentNullException.ThrowIfNull(skeletonFile);
        ArgumentNullException.ThrowIfNull(animationFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var scene = CreateAnimationOnlyScene(skeletonFile, animationFile, blenderFriendlyOrientation);
        ExportScene(scene, outputPath, ascii);
    }

    public AnimationFile ConvertFbxAnimationToAnimFile(
        string inputFbxPath,
        string skeletonName,
        float frameRate = 20,
        uint version = 7,
        bool includeEndBones = false,
        bool blenderFriendlyOrientation = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFbxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(skeletonName);
        var clip = FbxBridge.ExtractFirstAnimationClip(inputFbxPath, skeletonName, frameRate, includeEndBones);
        return ConvertClipToAnimFile(clip, skeletonName, version, blenderFriendlyOrientation);
    }

    public AnimationFile ConvertFbxAnimationToAnimFile(
        string inputFbxPath,
        AnimationFile targetSkeleton,
        float frameRate = 20,
        uint version = 7,
        bool includeEndBones = false,
        bool blenderFriendlyOrientation = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFbxPath);
        ArgumentNullException.ThrowIfNull(targetSkeleton);

        var skeletonName = targetSkeleton.Header.SkeletonName;
        if (string.IsNullOrWhiteSpace(skeletonName))
            skeletonName = "fbx_imported";

        var clip = FbxBridge.ExtractFirstAnimationClip(inputFbxPath, skeletonName, frameRate, includeEndBones);
        return ConvertClipToAnimFile(clip, targetSkeleton, version, blenderFriendlyOrientation);
    }

    public void ConvertFbxAnimationToAnimFile(
        string inputFbxPath,
        string outputAnimPath,
        string skeletonName,
        float frameRate = 20,
        uint version = 7,
        bool includeEndBones = false,
        bool blenderFriendlyOrientation = false)
    {
        var animationFile = ConvertFbxAnimationToAnimFile(inputFbxPath, skeletonName, frameRate, version, includeEndBones, blenderFriendlyOrientation);
        var bytes = AnimationFile.ConvertToBytes(animationFile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputAnimPath))!);
        File.WriteAllBytes(outputAnimPath, bytes);
    }

    public void ConvertFbxAnimationToAnimFile(
        string inputFbxPath,
        string outputAnimPath,
        AnimationFile targetSkeleton,
        float frameRate = 20,
        uint version = 7,
        bool includeEndBones = false,
        bool blenderFriendlyOrientation = false)
    {
        var animationFile = ConvertFbxAnimationToAnimFile(inputFbxPath, targetSkeleton, frameRate, version, includeEndBones, blenderFriendlyOrientation);
        var bytes = AnimationFile.ConvertToBytes(animationFile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputAnimPath))!);
        File.WriteAllBytes(outputAnimPath, bytes);
    }


    public FbxImportedScene ImportScene(string path, string? skeletonRootName = null, bool includeEndBones = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FbxBridge.ImportScene(path, skeletonRootName ?? string.Empty, includeEndBones);
    }

    public RmvFile ImportFbxToRmv(
        string inputFbxPath,
        AnimationFile? skeletonFile,
        string skeletonName,
        bool mirrorMesh = true,
        bool importMaterials = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFbxPath);

        var importedScene = ImportScene(inputFbxPath, null, false);
        return CreateRmvFromImportedFbx(importedScene, skeletonFile, skeletonName, mirrorMesh, importMaterials);
    }

    public RmvFile CreateRmvFromImportedFbxScene(
        FbxImportedScene importedScene,
        AnimationFile? skeletonFile,
        string skeletonName,
        bool mirrorMesh = true,
        bool importMaterials = true)
    {
        ArgumentNullException.ThrowIfNull(importedScene);
        return CreateRmvFromImportedFbx(importedScene, skeletonFile, skeletonName, mirrorMesh, importMaterials);
    }

    public FbxExportScene CreateExportSceneFromRmv(
        RmvFile rmvFile,
        AnimationFile? skeletonFile,
        IReadOnlyList<AnimationFile> animationFiles,
        bool exportMaterials = true,
        bool meshBlenderFriendlyOrientation = true,
        bool skeletonBlenderFriendlyOrientation = true,
        bool animationBlenderFriendlyOrientation = false)
    {
        ArgumentNullException.ThrowIfNull(rmvFile);

        var scene = new FbxExportScene
        {
            Name = Path.GetFileNameWithoutExtension(rmvFile.Header.SkeletonName) ?? "AssetEditorScene",
            SkeletonName = rmvFile.Header.SkeletonName ?? skeletonFile?.Header.SkeletonName ?? string.Empty,
            Bones = skeletonFile != null ? CreateBonesFromSkeleton(skeletonFile, skeletonBlenderFriendlyOrientation) : [],
            Meshes = CreateMeshesFromRmv(rmvFile, meshBlenderFriendlyOrientation, exportMaterials),
            Animations = skeletonFile != null ? CreateAnimationClips(skeletonFile, animationFiles, animationBlenderFriendlyOrientation) : [],
        };

        return scene;
    }

    public FbxExportScene CreateAnimationOnlyScene(AnimationFile skeletonFile, AnimationFile animationFile, bool mirror = false)
    {
        ArgumentNullException.ThrowIfNull(skeletonFile);
        ArgumentNullException.ThrowIfNull(animationFile);

        return new FbxExportScene
        {
            Name = Path.GetFileNameWithoutExtension(animationFile.Header.SkeletonName) ?? "Animation",
            SkeletonName = skeletonFile.Header.SkeletonName,
            Bones = CreateBonesFromSkeleton(skeletonFile, mirror),
            Meshes = [],
            Animations = CreateAnimationClips(skeletonFile, [animationFile], mirror),
        };
    }

    private static FbxExportBone[] CreateBonesFromSkeleton(AnimationFile skeletonFile, bool mirror)
    {
        var bindFrame = GetFirstFrame(skeletonFile);
        var bones = new FbxExportBone[skeletonFile.Bones.Length];
        for (var i = 0; i < skeletonFile.Bones.Length; i++)
        {
            var translation = i < bindFrame.Transforms.Count ? bindFrame.Transforms[i] : new RmvVector3(0, 0, 0);
            var rotation = i < bindFrame.Quaternion.Count ? bindFrame.Quaternion[i] : new RmvVector4(0, 0, 0, 1);
            bones[i] = new FbxExportBone
            {
                Name = skeletonFile.Bones[i].Name,
                ParentId = skeletonFile.Bones[i].ParentId,
                Translation = ToArray(FlipVector(translation, mirror)),
                RotationQuaternion = ToArray(NormalizeQuaternion(FlipQuaternion(rotation, mirror))),
            };
        }
        return bones;
    }

    private static FbxExportMesh[] CreateMeshesFromRmv(RmvFile rmvFile, bool mirror, bool exportMaterials)
    {
        var meshes = new List<FbxExportMesh>();
        for (var lodIndex = 0; lodIndex < rmvFile.ModelList.Length; lodIndex++)
        {
            var lod = rmvFile.ModelList[lodIndex];
            for (var modelIndex = 0; modelIndex < lod.Length; modelIndex++)
            {
                var model = lod[modelIndex];
                var vertices = new FbxExportVertex[model.Mesh.VertexList.Length];
                for (var vertexIndex = 0; vertexIndex < model.Mesh.VertexList.Length; vertexIndex++)
                {
                    var vertex = model.Mesh.VertexList[vertexIndex];
                    vertices[vertexIndex] = new FbxExportVertex
                    {
                        Position = [MirrorX(vertex.Position.X, mirror), vertex.Position.Y, vertex.Position.Z],
                        Normal = [MirrorX(vertex.Normal.X, mirror), vertex.Normal.Y, vertex.Normal.Z],
                        Uv = [vertex.Uv.X, 1.0f - vertex.Uv.Y],
                        BoneIndices = CreateBoneIndices(vertex.BoneIndex, vertex.WeightCount),
                        BoneWeights = CreateBoneWeights(vertex.BoneWeight, vertex.WeightCount),
                    };
                }

                meshes.Add(new FbxExportMesh
                {
                    Name = BuildExportMeshName(model, lodIndex, modelIndex),
                    MaterialName = BuildExportMaterialName(model, lodIndex, modelIndex),
                    Textures = exportMaterials ? CreateExportTextures(model.Material) : [],
                    Vertices = vertices,
                    Indices = CreateIndices(model.Mesh.IndexList, mirror),
                });
            }
        }
        return meshes.ToArray();
    }


    private static string BuildExportMeshName(RmvModel model, int lodIndex, int modelIndex)
    {
        if (model.Material is WeightedMaterial weighted && !string.IsNullOrWhiteSpace(weighted.ModelName))
            return $"lod{lodIndex}_{SanitizeFbxName(weighted.ModelName)}";

        return $"lod{lodIndex}_mesh{modelIndex}";
    }

    private static string BuildExportMaterialName(RmvModel model, int lodIndex, int modelIndex)
    {
        var materialId = model.Material?.MaterialId.ToString() ?? "material";
        return $"{materialId}_lod{lodIndex}_mesh{modelIndex}";
    }

    private static string SanitizeFbxName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "mesh";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch == ' ' ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static FbxTextureReference[] CreateExportTextures(IRmvMaterial? material)
    {
        if (material == null)
            return [];

        return material
            .GetAllTextures()
            .Where(texture => !string.IsNullOrWhiteSpace(texture.Path))
            .Select(texture => new FbxTextureReference
            {
                Type = texture.TexureType.ToString(),
                Path = NormalizeTexturePath(texture.Path),
            })
            .ToArray();
    }

    private static WeightedMaterial CreateImportedMaterial(
        FbxImportedMesh importedMesh,
        AnimationFile? skeletonFile,
        bool importMaterials)
    {
        var material = new WeightedMaterial
        {
            BinaryVertexFormat = skeletonFile != null ? VertexFormat.Cinematic : VertexFormat.Static,
            MaterialId = skeletonFile != null ? ModelMaterialEnum.weighted : ModelMaterialEnum.default_type,
            ModelName = importedMesh.Name,
            TextureDirectory = BuildTextureDirectoryFromMeshName(importedMesh.Name),
            MatrixIndex = 0,
            ParentMatrixIndex = -1,
        };

        if (importMaterials && importedMesh.Textures != null)
        {
            foreach (var texture in importedMesh.Textures)
            {
                if (texture == null || string.IsNullOrWhiteSpace(texture.Path))
                    continue;

                var textureType = ResolveRmvTextureType(texture.Type, texture.Path);
                if (textureType == null)
                    continue;

                material.SetTexture(textureType.Value, NormalizeTexturePath(texture.Path));
            }
        }

        return material;
    }

    private static string BuildTextureDirectoryFromMeshName(string? meshName)
    {
        var safeName = SanitizeFbxName(meshName ?? "fbx_import");
        return $"variantmeshes\\fbx_import\\{safeName}\\tex";
    }

    private static string NormalizeTexturePath(string path)
    {
        return path.Replace('/', '\\').Trim();
    }

    private static TextureType? ResolveRmvTextureType(string? rawType, string? path)
    {
        if (!string.IsNullOrWhiteSpace(rawType))
        {
            var normalizedType = rawType.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
            foreach (TextureType textureType in Enum.GetValues(typeof(TextureType)))
            {
                var normalizedCandidate = textureType.ToString().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
                if (string.Equals(normalizedType, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                    return textureType;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
            return null;

        var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (fileName.EndsWith("_n") || fileName.Contains("normal"))
            return TextureType.Normal;
        if (fileName.Contains("material") || fileName.Contains("metal") || fileName.Contains("rough"))
            return TextureType.MaterialMap;
        if (fileName.Contains("mask"))
            return TextureType.Mask;
        if (fileName.Contains("ao") || fileName.Contains("ambient"))
            return TextureType.Ambient_occlusion;
        if (fileName.Contains("spec"))
            return TextureType.Specular;
        if (fileName.Contains("gloss"))
            return TextureType.Gloss;
        if (fileName.Contains("emissive") || fileName.Contains("emit"))
            return TextureType.Emissive;
        if (fileName.Contains("blood"))
            return TextureType.Blood;
        if (fileName.Contains("diffuse") || fileName.Contains("base") || fileName.Contains("colour") || fileName.Contains("color") || fileName.Contains("albedo"))
            return TextureType.BaseColour;

        return TextureType.BaseColour;
    }

    private static FbxExportAnimationClip[] CreateAnimationClips(AnimationFile skeletonFile, IReadOnlyList<AnimationFile> animationFiles, bool mirror)
    {
        var output = new List<FbxExportAnimationClip>();
        foreach (var animationFile in animationFiles)
        {
            if (animationFile.AnimationParts.Count == 0 || animationFile.AnimationParts[0].DynamicFrames.Count == 0)
                continue;

            var part = animationFile.AnimationParts[0];
            var frames = new FbxAnimationFrame[part.DynamicFrames.Count];
            for (var frameIndex = 0; frameIndex < part.DynamicFrames.Count; frameIndex++)
            {
                var sourceFrame = part.DynamicFrames[frameIndex];
                var bones = new FbxBoneFrame[skeletonFile.Bones.Length];
                for (var boneIndex = 0; boneIndex < skeletonFile.Bones.Length; boneIndex++)
                {
                    var sourceBoneIndex = ResolveMappedBoneIndex(part.TranslationMappings, boneIndex);
                    var rotationBoneIndex = ResolveMappedBoneIndex(part.RotationMappings, boneIndex);
                    var translation = sourceBoneIndex >= 0 && sourceBoneIndex < sourceFrame.Transforms.Count ? sourceFrame.Transforms[sourceBoneIndex] : new RmvVector3(0, 0, 0);
                    var rotation = rotationBoneIndex >= 0 && rotationBoneIndex < sourceFrame.Quaternion.Count ? sourceFrame.Quaternion[rotationBoneIndex] : new RmvVector4(0, 0, 0, 1);

                    bones[boneIndex] = new FbxBoneFrame
                    {
                        Translation = ToArray(FlipVector(translation, mirror)),
                        RotationQuaternion = ToArray(NormalizeQuaternion(FlipQuaternion(rotation, mirror))),
                    };
                }

                frames[frameIndex] = new FbxAnimationFrame { Bones = bones };
            }

            var durationSeconds = animationFile.Header.AnimationTotalPlayTimeInSec;
            output.Add(new FbxExportAnimationClip
            {
                Name = BuildExportAnimationName(Path.GetFileNameWithoutExtension(animationFile.Header.SkeletonName) ?? "Animation", durationSeconds),
                FrameRate = animationFile.Header.FrameRate <= 0 ? 20 : animationFile.Header.FrameRate,
                Frames = frames,
            });
        }

        return output.ToArray();
    }

    private static int ResolveMappedBoneIndex(IReadOnlyList<AnimationFile.AnimationBoneMapping> mappings, int boneIndex)
    {
        if (boneIndex >= mappings.Count)
            return boneIndex;

        var mapping = mappings[boneIndex];
        return mapping.HasValue ? mapping.Id : -1;
    }

    private const string ExportDurationMarker = "__AE_DURATION_";

    private static string BuildExportAnimationName(string name, float durationSeconds)
    {
        if (durationSeconds <= 0)
            return name;

        return name + ExportDurationMarker + durationSeconds.ToString("0.######", CultureInfo.InvariantCulture);
    }


    private static AnimationFile.Frame GetFirstFrame(AnimationFile animationFile)
    {
        if (animationFile.AnimationParts.Count == 0)
            throw new InvalidOperationException("The skeleton animation file contains no animation part.");

        var part = animationFile.AnimationParts[0];
        if (part.DynamicFrames.Count > 0)
            return part.DynamicFrames[0];

        if (part.StaticFrame != null)
            return part.StaticFrame;

        throw new InvalidOperationException("The skeleton animation file contains no frame.");
    }

    private static int[] CreateBoneIndices(byte[]? boneIndex, int weightCount)
    {
        if (boneIndex == null || boneIndex.Length == 0 || weightCount <= 0)
            return [];

        var count = Math.Min(weightCount, boneIndex.Length);
        var output = new int[count];
        for (var i = 0; i < count; i++)
            output[i] = boneIndex[i];
        return output;
    }

    private static float[] CreateBoneWeights(float[]? boneWeight, int weightCount)
    {
        if (boneWeight == null || boneWeight.Length == 0 || weightCount <= 0)
            return [];

        var count = Math.Min(weightCount, boneWeight.Length);
        var output = new float[count];
        var total = 0.0f;
        for (var i = 0; i < count; i++)
        {
            output[i] = Math.Max(0, boneWeight[i]);
            total += output[i];
        }

        if (total > float.Epsilon)
        {
            for (var i = 0; i < output.Length; i++)
                output[i] /= total;
        }

        return output;
    }

    private static int[] CreateIndices(ushort[] source, bool mirror)
    {
        var output = new int[source.Length];
        if (!mirror)
        {
            for (var i = 0; i < source.Length; i++)
                output[i] = source[i];
            return output;
        }

        for (var i = 0; i + 2 < source.Length; i += 3)
        {
            output[i] = source[i];
            output[i + 1] = source[i + 2];
            output[i + 2] = source[i + 1];
        }
        return output;
    }


    private static RmvFile CreateRmvFromImportedFbx(
        FbxImportedScene importedScene,
        AnimationFile? skeletonFile,
        string skeletonName,
        bool mirrorMesh,
        bool importMaterials)
    {
        ArgumentNullException.ThrowIfNull(importedScene);
        if (importedScene.Meshes.Length == 0)
            throw new InvalidOperationException("The FBX scene does not contain any mesh.");

        var lodMeshes = GroupImportedMeshesByLod(importedScene.Meshes);
        var lodCount = lodMeshes.Count;

        var rmvFile = new RmvFile
        {
            Header = new RmvFileHeader
            {
                _fileType = System.Text.Encoding.ASCII.GetBytes("RMV2"),
                SkeletonName = skeletonName,
                Version = RmvVersionEnum.RMV2_V7,
                LodCount = (uint)lodCount,
            },
            LodHeaders = new RmvLodHeader[lodCount],
            ModelList = new RmvModel[lodCount][],
        };

        for (var lodIndex = 0; lodIndex < lodCount; lodIndex++)
        {
            var meshes = lodMeshes[lodIndex];
            rmvFile.LodHeaders[lodIndex] = LodHeaderFactory.Create().CreateEmpty(RmvVersionEnum.RMV2_V7, 100.0f, 0, 0);
            rmvFile.LodHeaders[lodIndex].MeshCount = (uint)meshes.Count;

            var models = new List<RmvModel>();
            foreach (var mesh in meshes)
                models.Add(CreateRmvModelFromImportedMesh(mesh, importedScene.Bones, skeletonFile, mirrorMesh, importMaterials));

            rmvFile.ModelList[lodIndex] = models.ToArray();
        }

        rmvFile.RecalculateOffsets();
        return rmvFile;
    }

    private static List<List<FbxImportedMesh>> GroupImportedMeshesByLod(IReadOnlyList<FbxImportedMesh> meshes)
    {
        var groups = meshes
            .GroupBy(mesh => TryReadLodPrefix(mesh.Name, out var lodIndex) ? lodIndex : 0)
            .OrderBy(group => group.Key)
            .Select(group => group.ToList())
            .ToList();

        return groups.Count == 0 ? [meshes.ToList()] : groups;
    }

    private static bool TryReadLodPrefix(string? meshName, out int lodIndex)
    {
        lodIndex = 0;

        if (string.IsNullOrWhiteSpace(meshName) || meshName.Length < 5)
            return false;

        if (!meshName.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
            return false;

        var digitStart = 3;
        var digitEnd = digitStart;
        while (digitEnd < meshName.Length && char.IsDigit(meshName[digitEnd]))
            digitEnd++;

        if (digitEnd == digitStart || digitEnd >= meshName.Length || meshName[digitEnd] != '_')
            return false;

        return int.TryParse(meshName.AsSpan(digitStart, digitEnd - digitStart), out lodIndex) && lodIndex >= 0;
    }

    private static RmvModel CreateRmvModelFromImportedMesh(
        FbxImportedMesh importedMesh,
        IReadOnlyList<FbxSkeletonBoneInfo> fbxBones,
        AnimationFile? skeletonFile,
        bool mirrorMesh,
        bool importMaterials)
    {
        var rmvMesh = new RmvMesh
        {
            VertexList = new CommonVertex[importedMesh.Vertices.Length],
            IndexList = CreateImportedIndices(importedMesh.Indices, mirrorMesh),
        };

        for (var i = 0; i < importedMesh.Vertices.Length; i++)
            rmvMesh.VertexList[i] = ConvertImportedVertex(importedMesh.Vertices[i], fbxBones, skeletonFile, mirrorMesh, importMaterials);

        TangentBasisCalculator.CalculateForRmv2Mesh(rmvMesh);

        var material = CreateImportedMaterial(importedMesh, skeletonFile, importMaterials);

        var model = new RmvModel
        {
            CommonHeader = RmvCommonHeader.CreateDefault(),
            Material = material,
            Mesh = rmvMesh,
        };

        UpdateBoundingBox(model);
        return model;
    }

    private static CommonVertex ConvertImportedVertex(
        FbxImportedVertex importedVertex,
        IReadOnlyList<FbxSkeletonBoneInfo> fbxBones,
        AnimationFile? skeletonFile,
        bool mirrorMesh,
        bool importMaterials)
    {
        var position = importedVertex.Position;
        var normal = importedVertex.Normal;
        var uv = importedVertex.Uv;

        var vertex = new CommonVertex
        {
            Position = new XNA.Vector4(MirrorX(Read(position, 0), mirrorMesh), Read(position, 1), Read(position, 2), 1),
            Normal = new XNA.Vector3(MirrorX(Read(normal, 0), mirrorMesh), Read(normal, 1, 1), Read(normal, 2)),
            Uv = new XNA.Vector2(Read(uv, 0), 1.0f - Read(uv, 1)),
        };

        if (skeletonFile == null || importedVertex.BoneIndices.Length == 0)
        {
            vertex.WeightCount = 0;
            vertex.BoneIndex = [];
            vertex.BoneWeight = [];
            return vertex;
        }

        var weights = CreateImportedWeights(importedVertex, fbxBones, skeletonFile);
        ApplyCinematicWeights(vertex, weights);
        return vertex;
    }


    private static void ApplyCinematicWeights(CommonVertex vertex, IReadOnlyList<(int BoneIndex, float Weight)> weights)
    {
        var indices = new byte[4];
        var values = new float[4];

        if (weights.Count == 0)
        {
            indices[0] = 0;
            values[0] = 1.0f;
            vertex.WeightCount = 4;
            vertex.BoneIndex = indices;
            vertex.BoneWeight = values;
            return;
        }

        var count = Math.Min(4, weights.Count);
        var total = 0.0f;
        for (var i = 0; i < count; i++)
        {
            indices[i] = (byte)Math.Clamp(weights[i].BoneIndex, 0, byte.MaxValue);
            values[i] = Math.Max(0, weights[i].Weight);
            total += values[i];
        }

        if (total <= float.Epsilon)
        {
            Array.Clear(indices);
            Array.Clear(values);
            values[0] = 1.0f;
        }
        else
        {
            for (var i = 0; i < values.Length; i++)
                values[i] /= total;
        }

        vertex.WeightCount = 4;
        vertex.BoneIndex = indices;
        vertex.BoneWeight = values;
    }

    private static List<(int BoneIndex, float Weight)> CreateImportedWeights(
        FbxImportedVertex importedVertex,
        IReadOnlyList<FbxSkeletonBoneInfo> fbxBones,
        AnimationFile skeletonFile)
    {
        var output = new List<(int BoneIndex, float Weight)>();
        for (var i = 0; i < importedVertex.BoneIndices.Length && i < importedVertex.BoneWeights.Length; i++)
        {
            var fbxBoneIndex = importedVertex.BoneIndices[i];
            if (fbxBoneIndex < 0 || fbxBoneIndex >= fbxBones.Count)
                continue;

            var skeletonBoneIndex = Array.FindIndex(skeletonFile.Bones, x => string.Equals(x.Name, fbxBones[fbxBoneIndex].Name, StringComparison.OrdinalIgnoreCase));
            if (skeletonBoneIndex < 0 || skeletonBoneIndex > byte.MaxValue)
                continue;

            var weight = Math.Max(0, importedVertex.BoneWeights[i]);
            if (weight > 0)
                output.Add((skeletonBoneIndex, weight));
        }

        output = output
            .GroupBy(x => x.BoneIndex)
            .Select(x => (x.Key, x.Sum(y => y.Weight)))
            .OrderByDescending(x => x.Item2)
            .Take(4)
            .ToList();

        var total = output.Sum(x => x.Item2);
        if (total > float.Epsilon)
            output = output.Select(x => (x.BoneIndex, x.Item2 / total)).ToList();

        return output;
    }

    private static ushort[] CreateImportedIndices(IReadOnlyList<int> source, bool mirrorMesh)
    {
        if (source.Count > ushort.MaxValue + 1)
            throw new InvalidOperationException("Unsupported mesh: RMV2 only supports 65536 vertices per mesh.");

        var output = new ushort[source.Count];
        if (!mirrorMesh)
        {
            for (var i = 0; i < source.Count; i++)
                output[i] = checked((ushort)source[i]);
            return output;
        }

        for (var i = 0; i + 2 < source.Count; i += 3)
        {
            output[i] = checked((ushort)source[i]);
            output[i + 1] = checked((ushort)source[i + 2]);
            output[i + 2] = checked((ushort)source[i + 1]);
        }

        return output;
    }

    private static void UpdateBoundingBox(RmvModel model)
    {
        var points = new XNA.Vector3[model.Mesh.VertexList.Length];
        for (var i = 0; i < model.Mesh.VertexList.Length; i++)
        {
            points[i].X = model.Mesh.VertexList[i].Position.X;
            points[i].Y = model.Mesh.VertexList[i].Position.Y;
            points[i].Z = model.Mesh.VertexList[i].Position.Z;
        }

        model.UpdateBoundingBox(XNA.BoundingBox.CreateFromPoints(points));
        model.UpdateModelTypeFlag(model.Material.MaterialId);
    }

    private static float Read(IReadOnlyList<float>? values, int index, float fallback = 0)
        => values != null && index >= 0 && index < values.Count ? values[index] : fallback;

    private static AnimationFile ConvertClipToAnimFile(FbxAnimationClip clip, string skeletonName, uint version, bool blenderFriendlyOrientation = false)
    {
        if (clip.Bones.Length == 0)
            throw new InvalidOperationException("The FBX clip does not contain any skeleton bone.");
        if (clip.Frames.Length == 0)
            throw new InvalidOperationException("The FBX clip does not contain any sampled frame.");

        var animationFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = version,
                Unknown0_alwaysOne = 1,
                FrameRate = clip.FrameRate,
                SkeletonName = skeletonName,
                FlagCount = 0,
                AnimationTotalPlayTimeInSec = GetAnimationDuration(clip),
            },
            Bones = clip.Bones.Select((bone, index) => new AnimationFile.BoneInfo { Name = bone.Name, Id = index, ParentId = bone.ParentId }).ToArray(),
        };

        var animationPart = new AnimationFile.AnimationPart();
        for (var boneIndex = 0; boneIndex < clip.Bones.Length; boneIndex++)
        {
            animationPart.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
            animationPart.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
        }

        foreach (var sourceFrame in clip.Frames)
        {
            var targetFrame = new AnimationFile.Frame();
            foreach (var sourceBoneFrame in sourceFrame.Bones)
            {
                targetFrame.Transforms.Add(ReadClipTranslation(sourceBoneFrame, blenderFriendlyOrientation));
                targetFrame.Quaternion.Add(ReadClipRotation(sourceBoneFrame, blenderFriendlyOrientation));
            }
            animationPart.DynamicFrames.Add(targetFrame);
        }

        animationFile.AnimationParts.Add(animationPart);
        return animationFile;
    }

    private static AnimationFile ConvertClipToAnimFile(FbxAnimationClip clip, AnimationFile targetSkeleton, uint version, bool blenderFriendlyOrientation = false)
    {
        if (clip.Bones.Length == 0)
            throw new InvalidOperationException("The FBX clip does not contain any skeleton bone.");
        if (clip.Frames.Length == 0)
            throw new InvalidOperationException("The FBX clip does not contain any sampled frame.");

        var targetBones = targetSkeleton.Bones;
        if (targetBones.Length == 0)
            throw new InvalidOperationException("The target Total War skeleton contains no bones.");

        var clipBoneIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < clip.Bones.Length; i++)
        {
            var name = clip.Bones[i].Name;
            if (!string.IsNullOrWhiteSpace(name) && !clipBoneIndexByName.ContainsKey(name))
                clipBoneIndexByName.Add(name, i);
        }

        var fallbackFrame = TryGetFallbackFrame(targetSkeleton);

        var animationFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = version,
                Unknown0_alwaysOne = 1,
                FrameRate = clip.FrameRate,
                SkeletonName = string.IsNullOrWhiteSpace(targetSkeleton.Header.SkeletonName) ? clip.SkeletonName : targetSkeleton.Header.SkeletonName,
                FlagCount = targetSkeleton.Header.FlagCount,
                AnimationTotalPlayTimeInSec = GetAnimationDuration(clip),
            },
            Bones = targetBones
                .Select((bone, index) => new AnimationFile.BoneInfo
                {
                    Name = bone.Name,
                    Id = index,
                    ParentId = bone.ParentId,
                })
                .ToArray(),
        };

        var animationPart = new AnimationFile.AnimationPart();
        for (var boneIndex = 0; boneIndex < targetBones.Length; boneIndex++)
        {
            animationPart.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
            animationPart.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
        }

        foreach (var sourceFrame in clip.Frames)
        {
            var targetFrame = new AnimationFile.Frame();
            for (var targetBoneIndex = 0; targetBoneIndex < targetBones.Length; targetBoneIndex++)
            {
                var boneName = targetBones[targetBoneIndex].Name;
                if (!string.IsNullOrWhiteSpace(boneName)
                    && clipBoneIndexByName.TryGetValue(boneName, out var sourceBoneIndex)
                    && sourceBoneIndex >= 0
                    && sourceBoneIndex < sourceFrame.Bones.Length)
                {
                    var sourceBoneFrame = sourceFrame.Bones[sourceBoneIndex];
                    targetFrame.Transforms.Add(ReadClipTranslation(sourceBoneFrame, blenderFriendlyOrientation));
                    targetFrame.Quaternion.Add(ReadClipRotation(sourceBoneFrame, blenderFriendlyOrientation));
                    continue;
                }

                targetFrame.Transforms.Add(ReadFallbackTranslation(fallbackFrame, targetBoneIndex));
                targetFrame.Quaternion.Add(ReadFallbackRotation(fallbackFrame, targetBoneIndex));
            }
            animationPart.DynamicFrames.Add(targetFrame);
        }

        animationFile.AnimationParts.Add(animationPart);
        return animationFile;
    }

    private static float GetAnimationDuration(FbxAnimationClip clip)
    {
        if (clip.DurationSeconds > 0)
            return (float)clip.DurationSeconds;

        if (clip.FrameRate <= 0 || clip.Frames.Length <= 1)
            return 0;

        return (float)((clip.Frames.Length - 1) / clip.FrameRate);
    }

    private static AnimationFile.Frame? TryGetFallbackFrame(AnimationFile animationFile)
    {
        if (animationFile.AnimationParts.Count == 0)
            return null;

        var part = animationFile.AnimationParts[0];
        if (part.DynamicFrames.Count > 0)
            return part.DynamicFrames[0];

        return part.StaticFrame;
    }

    private static RmvVector3 ReadClipTranslation(FbxBoneFrame sourceBoneFrame, bool blenderFriendlyOrientation)
    {
        var translation = new RmvVector3(
            Read(sourceBoneFrame.Translation, 0),
            Read(sourceBoneFrame.Translation, 1),
            Read(sourceBoneFrame.Translation, 2));

        return FlipVector(translation, blenderFriendlyOrientation);
    }

    private static RmvVector4 ReadClipRotation(FbxBoneFrame sourceBoneFrame, bool blenderFriendlyOrientation)
    {
        var rotation = NormalizeQuaternion(new RmvVector4(
            Read(sourceBoneFrame.RotationQuaternion, 0),
            Read(sourceBoneFrame.RotationQuaternion, 1),
            Read(sourceBoneFrame.RotationQuaternion, 2),
            Read(sourceBoneFrame.RotationQuaternion, 3, 1)));

        return NormalizeQuaternion(FlipQuaternion(rotation, blenderFriendlyOrientation));
    }

    private static RmvVector3 ReadFallbackTranslation(AnimationFile.Frame? fallbackFrame, int boneIndex)
    {
        return fallbackFrame != null && boneIndex >= 0 && boneIndex < fallbackFrame.Transforms.Count
            ? fallbackFrame.Transforms[boneIndex]
            : new RmvVector3(0, 0, 0);
    }

    private static RmvVector4 ReadFallbackRotation(AnimationFile.Frame? fallbackFrame, int boneIndex)
    {
        return fallbackFrame != null && boneIndex >= 0 && boneIndex < fallbackFrame.Quaternion.Count
            ? NormalizeQuaternion(fallbackFrame.Quaternion[boneIndex])
            : new RmvVector4(0, 0, 0, 1);
    }

    private static RmvVector3 FlipVector(RmvVector3 vector, bool mirror)
        => mirror ? new RmvVector3(-vector.X, vector.Y, vector.Z) : vector;

    private static RmvVector4 FlipQuaternion(RmvVector4 quaternion, bool mirror)
        => mirror ? new RmvVector4(quaternion.X, -quaternion.Y, -quaternion.Z, quaternion.W) : quaternion;

    private static float MirrorX(float value, bool mirror) => mirror ? -value : value;

    private static float[] ToArray(RmvVector3 vector) => [vector.X, vector.Y, vector.Z];
    private static float[] ToArray(RmvVector4 vector) => [vector.X, vector.Y, vector.Z, vector.W];

    private static RmvVector4 NormalizeQuaternion(RmvVector4 quaternion)
    {
        var length = MathF.Sqrt(quaternion.X * quaternion.X + quaternion.Y * quaternion.Y + quaternion.Z * quaternion.Z + quaternion.W * quaternion.W);
        if (length <= float.Epsilon)
            return new RmvVector4(0, 0, 0, 1);
        return new RmvVector4(quaternion.X / length, quaternion.Y / length, quaternion.Z / length, quaternion.W / length);
    }
}
