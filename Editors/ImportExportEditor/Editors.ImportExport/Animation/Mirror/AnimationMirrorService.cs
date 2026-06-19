using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Editors.ImportExport.Animation.Mirror;

public sealed class AnimationMirrorService
{
    private enum ReflectionNormalAxis
    {
        X,
        Y,
        Z
    }

    public AnimationFile Mirror(AnimationFile source, AnimationMirrorPlane plane)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Header.Version == 8)
            throw new NotSupportedException("Version 8 animations are not supported by the current animation writer.");

        if (source.Bones == null || source.Bones.Length == 0)
            throw new InvalidOperationException("The animation has no bones.");

        if (source.AnimationParts.Count == 0)
            throw new InvalidOperationException("The animation has no animation parts.");

        var boneIndexByName = source.Bones
            .Select((bone, index) => new { bone.Name, Index = index })
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var parentIndices = BuildParentIndexLookup(source);
        var normalAxis = GetNormalAxis(plane);

        var reflected = new AnimationFile
        {
            Header = CloneHeader(source.Header),
            Bones = source.Bones.Select(CloneBone).ToArray(),
            AnimationParts = []
        };

        foreach (var sourcePart in source.AnimationParts)
        {
            reflected.AnimationParts.Add(ReflectAnimationPart(
                source,
                sourcePart,
                boneIndexByName,
                parentIndices,
                normalAxis));
        }

        return reflected;
    }

    private static AnimationFile.AnimationPart ReflectAnimationPart(
        AnimationFile sourceFile,
        AnimationFile.AnimationPart sourcePart,
        IReadOnlyDictionary<string, int> boneIndexByName,
        int[] parentIndices,
        ReflectionNormalAxis normalAxis)
    {
        var boneCount = sourceFile.Bones.Length;
        var outputPart = new AnimationFile.AnimationPart();

        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            outputPart.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
            outputPart.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
        }

        if (sourcePart.DynamicFrames.Count == 0)
            return outputPart;

        var referenceLocalMatrices = BuildLocalMatrices(sourcePart, sourcePart.DynamicFrames[0], boneCount);
        var referenceGlobalMatrices = BuildGlobalMatrices(parentIndices, referenceLocalMatrices, boneCount);

        foreach (var sourceFrame in sourcePart.DynamicFrames)
        {
            var sourceLocalMatrices = BuildLocalMatrices(sourcePart, sourceFrame, boneCount);
            var sourceGlobalMatrices = BuildGlobalMatrices(parentIndices, sourceLocalMatrices, boneCount);

            var outputLocalMatrices = new Matrix[boneCount];
            var outputGlobalMatrices = new Matrix[boneCount];
            var outputComputed = new bool[boneCount];

            for (var targetBoneIndex = 0; targetBoneIndex < boneCount; targetBoneIndex++)
            {
                ComputeMirroredOutputMatrix(
                    targetBoneIndex,
                    sourceFile,
                    boneIndexByName,
                    parentIndices,
                    sourceGlobalMatrices,
                    referenceGlobalMatrices,
                    outputLocalMatrices,
                    outputGlobalMatrices,
                    outputComputed,
                    normalAxis);
            }

            var outputFrame = new AnimationFile.Frame();

            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                var decomposed = DecomposeLocalMatrix(outputLocalMatrices[boneIndex]);
                outputFrame.Transforms.Add(new RmvVector3(decomposed.Translation));
                outputFrame.Quaternion.Add(ToRmvVector4(decomposed.Rotation));
            }

            outputPart.DynamicFrames.Add(outputFrame);
        }

        return outputPart;
    }

    private static int[] BuildParentIndexLookup(AnimationFile sourceFile)
    {
        var boneIndexById = sourceFile.Bones
            .Select((bone, index) => new { bone.Id, Index = index })
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Index);

        var parentIndices = new int[sourceFile.Bones.Length];

        for (var boneIndex = 0; boneIndex < sourceFile.Bones.Length; boneIndex++)
        {
            var parentId = sourceFile.Bones[boneIndex].ParentId;

            if (parentId < 0)
            {
                parentIndices[boneIndex] = -1;
                continue;
            }

            if (boneIndexById.TryGetValue(parentId, out var parentIndex))
            {
                parentIndices[boneIndex] = parentIndex;
                continue;
            }

            if (parentId >= 0 && parentId < sourceFile.Bones.Length)
            {
                parentIndices[boneIndex] = parentId;
                continue;
            }

            throw new InvalidOperationException($"Bone '{sourceFile.Bones[boneIndex].Name}' references missing parent id '{parentId}'.");
        }

        return parentIndices;
    }

    private static void ComputeGlobalMatrix(
        int boneIndex,
        IReadOnlyList<int> parentIndices,
        IReadOnlyList<Matrix> localMatrices,
        Matrix[] globalMatrices,
        bool[] computed)
    {
        if (computed[boneIndex])
            return;

        var parentIndex = parentIndices[boneIndex];

        if (parentIndex < 0)
        {
            globalMatrices[boneIndex] = localMatrices[boneIndex];
        }
        else
        {
            ComputeGlobalMatrix(parentIndex, parentIndices, localMatrices, globalMatrices, computed);
            globalMatrices[boneIndex] = localMatrices[boneIndex] * globalMatrices[parentIndex];
        }

        computed[boneIndex] = true;
    }
    private static Matrix[] BuildLocalMatrices(
        AnimationFile.AnimationPart sourcePart,
        AnimationFile.Frame sourceFrame,
        int boneCount)
    {
        var localMatrices = new Matrix[boneCount];

        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            var translation = ReadTranslation(sourcePart, sourceFrame, boneIndex).ToVector3();
            var rotation = NormalizeQuaternion(ReadRotation(sourcePart, sourceFrame, boneIndex).ToQuaternion());
            localMatrices[boneIndex] = CreateLocalMatrix(translation, rotation);
        }

        return localMatrices;
    }

    private static Matrix[] BuildGlobalMatrices(
        IReadOnlyList<int> parentIndices,
        IReadOnlyList<Matrix> localMatrices,
        int boneCount)
    {
        var globalMatrices = new Matrix[boneCount];
        var computed = new bool[boneCount];

        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            ComputeGlobalMatrix(
                boneIndex,
                parentIndices,
                localMatrices,
                globalMatrices,
                computed);
        }

        return globalMatrices;
    }


    private static void ComputeMirroredOutputMatrix(
        int targetBoneIndex,
        AnimationFile sourceFile,
        IReadOnlyDictionary<string, int> boneIndexByName,
        IReadOnlyList<int> parentIndices,
        IReadOnlyList<Matrix> sourceGlobalMatrices,
        IReadOnlyList<Matrix> referenceGlobalMatrices,
        Matrix[] outputLocalMatrices,
        Matrix[] outputGlobalMatrices,
        bool[] computed,
        ReflectionNormalAxis normalAxis)
    {
        if (computed[targetBoneIndex])
            return;

        var targetBoneName = sourceFile.Bones[targetBoneIndex].Name;
        var sourceBoneIndex = ResolveReflectedSourceBoneIndex(targetBoneName, boneIndexByName);

        // Do not reflect the absolute bone pose directly. That mirrors the bind frame/roll too,
        // which makes skinned meshes appear upside down even when the debug skeleton looks right.
        //
        // Instead, mirror the animation delta relative to the reference frame, then apply that
        // mirrored delta on top of the target bone reference frame. This preserves the CA bind
        // skeleton and its skinning basis while still producing a true chiral mirrored motion.
        var sourceReferenceGlobalMatrix = referenceGlobalMatrices[sourceBoneIndex];
        var targetReferenceGlobalMatrix = referenceGlobalMatrices[targetBoneIndex];
        var sourcePoseGlobalMatrix = sourceGlobalMatrices[sourceBoneIndex];
        var sourceDeltaMatrix = Matrix.Invert(sourceReferenceGlobalMatrix) * sourcePoseGlobalMatrix;
        var mirroredDeltaMatrix = ReflectMatrixAcrossPlane(sourceDeltaMatrix, normalAxis);
        var desiredGlobalMatrix = targetReferenceGlobalMatrix * mirroredDeltaMatrix;

        var parentIndex = parentIndices[targetBoneIndex];

        if (parentIndex < 0)
        {
            outputLocalMatrices[targetBoneIndex] = desiredGlobalMatrix;
            outputGlobalMatrices[targetBoneIndex] = desiredGlobalMatrix;
        }
        else
        {
            ComputeMirroredOutputMatrix(
                parentIndex,
                sourceFile,
                boneIndexByName,
                parentIndices,
                sourceGlobalMatrices,
                referenceGlobalMatrices,
                outputLocalMatrices,
                outputGlobalMatrices,
                computed,
                normalAxis);

            var inverseParentGlobalMatrix = Matrix.Invert(outputGlobalMatrices[parentIndex]);
            var localMatrix = desiredGlobalMatrix * inverseParentGlobalMatrix;

            outputLocalMatrices[targetBoneIndex] = localMatrix;
            outputGlobalMatrices[targetBoneIndex] = localMatrix * outputGlobalMatrices[parentIndex];
        }

        computed[targetBoneIndex] = true;
    }

    private static Matrix CreateLocalMatrix(Vector3 translation, Quaternion rotation)
    {
        return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
    }

    private static Matrix ReflectMatrixAcrossPlane(Matrix matrix, ReflectionNormalAxis normalAxis)
    {
        var reflection = CreateReflectionMatrix(normalAxis);
        return reflection * matrix * reflection;
    }

    private static Matrix CreateReflectionMatrix(ReflectionNormalAxis normalAxis)
    {
        return normalAxis switch
        {
            ReflectionNormalAxis.X => Matrix.CreateScale(-1, 1, 1),
            ReflectionNormalAxis.Y => Matrix.CreateScale(1, -1, 1),
            ReflectionNormalAxis.Z => Matrix.CreateScale(1, 1, -1),
            _ => Matrix.Identity
        };
    }

    private static (Vector3 Translation, Quaternion Rotation) DecomposeLocalMatrix(Matrix matrix)
    {
        if (!matrix.Decompose(out _, out var rotation, out var translation))
            rotation = Quaternion.CreateFromRotationMatrix(matrix);

        return (translation, NormalizeQuaternion(rotation));
    }

    private static ReflectionNormalAxis GetNormalAxis(AnimationMirrorPlane plane)
    {
        return plane switch
        {
            AnimationMirrorPlane.YZ => ReflectionNormalAxis.X,
            AnimationMirrorPlane.XZ => ReflectionNormalAxis.Y,
            AnimationMirrorPlane.XY => ReflectionNormalAxis.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, "Unsupported mirror plane.")
        };
    }

    private static AnimationFile.AnimationHeader CloneHeader(AnimationFile.AnimationHeader source)
    {
        return new AnimationFile.AnimationHeader
        {
            Version = source.Version,
            Unknown0_alwaysOne = source.Unknown0_alwaysOne,
            FrameRate = source.FrameRate,
            SkeletonName = source.SkeletonName,
            FlagCount = source.FlagCount,
            FlagVariables = source.FlagVariables.ToList(),
            AnimationTotalPlayTimeInSec = source.AnimationTotalPlayTimeInSec,
            UnknownValue_v8 = source.UnknownValue_v8
        };
    }

    private static AnimationFile.BoneInfo CloneBone(AnimationFile.BoneInfo source)
    {
        return new AnimationFile.BoneInfo
        {
            Name = source.Name,
            Id = source.Id,
            ParentId = source.ParentId
        };
    }

    private static int ResolveReflectedSourceBoneIndex(string targetBoneName, IReadOnlyDictionary<string, int> boneIndexByName)
    {
        // Keep the same named bone as the source. The plane reflection already produces the
        // chiral side change in pose space; swapping left/right names here inverted the final
        // result for Total War humanoid skeletons.
        return boneIndexByName[targetBoneName];
    }

    private static string SwapLeftRightName(string name)
    {
        var replacements = new (string Left, string Right)[]
        {
            ("_left_", "_right_"),
            ("_right_", "_left_"),
            ("_left", "_right"),
            ("_right", "_left"),
            ("left_", "right_"),
            ("right_", "left_"),
            (".L", ".R"),
            (".R", ".L"),
            ("_L", "_R"),
            ("_R", "_L")
        };

        foreach (var (left, right) in replacements)
        {
            var index = name.IndexOf(left, StringComparison.Ordinal);
            if (index >= 0)
                return name.Remove(index, left.Length).Insert(index, right);
        }

        return name;
    }

    private static RmvVector3 ReadTranslation(AnimationFile.AnimationPart part, AnimationFile.Frame dynamicFrame, int boneIndex)
    {
        var mapping = part.TranslationMappings[boneIndex];

        if (mapping.IsDynamic)
            return dynamicFrame.Transforms[mapping.Id];

        if (mapping.IsStatic && part.StaticFrame != null)
            return part.StaticFrame.Transforms[mapping.Id];

        return new RmvVector3(0, 0, 0);
    }

    private static RmvVector4 ReadRotation(AnimationFile.AnimationPart part, AnimationFile.Frame dynamicFrame, int boneIndex)
    {
        var mapping = part.RotationMappings[boneIndex];

        if (mapping.IsDynamic)
            return dynamicFrame.Quaternion[mapping.Id];

        if (mapping.IsStatic && part.StaticFrame != null)
            return part.StaticFrame.Quaternion[mapping.Id];

        return new RmvVector4(0, 0, 0, 1);
    }

    private static Quaternion NormalizeQuaternion(Quaternion quaternion)
    {
        var length = MathF.Sqrt(
            quaternion.X * quaternion.X +
            quaternion.Y * quaternion.Y +
            quaternion.Z * quaternion.Z +
            quaternion.W * quaternion.W);

        if (length <= 0.000001f)
            return Quaternion.Identity;

        return new Quaternion(
            quaternion.X / length,
            quaternion.Y / length,
            quaternion.Z / length,
            quaternion.W / length);
    }

    private static RmvVector4 ToRmvVector4(Quaternion quaternion)
    {
        var normalized = NormalizeQuaternion(quaternion);
        return new RmvVector4(normalized.X, normalized.Y, normalized.Z, normalized.W);
    }
}
