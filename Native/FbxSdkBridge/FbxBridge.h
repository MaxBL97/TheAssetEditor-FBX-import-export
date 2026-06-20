#pragma once

namespace AssetEditor::Native::FbxSdkBridge
{
    public ref class FbxNodeInfo sealed { public: property System::String^ Name; property System::String^ ParentName; property System::String^ AttributeType; property int Depth; };
    public ref class FbxMeshInfo sealed { public: property System::String^ Name; property int ControlPointCount; property int PolygonCount; property int SkinDeformerCount; };
    public ref class FbxAnimationStackInfo sealed { public: property System::String^ Name; property double DurationSeconds; };
    public ref class FbxSceneInfo sealed { public: property cli::array<FbxNodeInfo^>^ Nodes; property cli::array<FbxMeshInfo^>^ Meshes; property cli::array<FbxAnimationStackInfo^>^ Animations; };

    public ref class FbxSkeletonBoneInfo sealed { public: property System::String^ Name; property int ParentId; property cli::array<float>^ LocalTranslation; property cli::array<float>^ LocalRotationQuaternion; };
    public ref class FbxSkeletonInfo sealed { public: property System::String^ RootName; property cli::array<FbxSkeletonBoneInfo^>^ Bones; };

    public ref class FbxBoneInfo sealed { public: property System::String^ Name; property int ParentId; };
    public ref class FbxBoneFrame sealed { public: property cli::array<float>^ Translation; property cli::array<float>^ RotationQuaternion; };
    public ref class FbxAnimationFrame sealed { public: property cli::array<FbxBoneFrame^>^ Bones; };
    public ref class FbxAnimationClip sealed { public: property System::String^ Name; property System::String^ SkeletonName; property float FrameRate; property double DurationSeconds; property cli::array<FbxBoneInfo^>^ Bones; property cli::array<FbxAnimationFrame^>^ Frames; };



    public ref class FbxImportedVertex sealed
    {
    public:
        property cli::array<float>^ Position;
        property cli::array<float>^ Normal;
        property cli::array<float>^ Uv;
        property cli::array<int>^ BoneIndices;
        property cli::array<float>^ BoneWeights;
    };

    public ref class FbxTextureReference sealed
    {
    public:
        property System::String^ Type;

        // Original RMV/pack texture path. For AE roundtrips this should stay as the .dds path
        // stored in the rigid_model_v2 material, even when the FBX is linked to a Blender PNG.
        property System::String^ Path;

        // External texture file linked by the FBX material, normally a PNG next to the FBX.
        // Import uses this as the source image to convert/copy back to the original Path.
        property System::String^ ExternalPath;
    };

    public ref class FbxImportedMesh sealed
    {
    public:
        property System::String^ Name;
        property System::String^ MaterialName;
        property System::String^ MaterialId;
        property System::String^ VertexFormat;
        property System::String^ MaterialHint;
        property System::String^ TextureDirectory;
        property cli::array<FbxTextureReference^>^ Textures;
        property cli::array<FbxImportedVertex^>^ Vertices;
        property cli::array<int>^ Indices;
    };

    public ref class FbxImportedScene sealed
    {
    public:
        property System::String^ SkeletonName;
        property cli::array<FbxSkeletonBoneInfo^>^ Bones;
        property cli::array<FbxImportedMesh^>^ Meshes;
    };

    public ref class FbxExportVertex sealed
    {
    public:
        property cli::array<float>^ Position;
        property cli::array<float>^ Normal;
        property cli::array<float>^ Uv;
        property cli::array<int>^ BoneIndices;
        property cli::array<float>^ BoneWeights;
    };

    public ref class FbxExportMesh sealed
    {
    public:
        property System::String^ Name;
        property System::String^ MaterialName;
        property System::String^ MaterialId;
        property System::String^ VertexFormat;
        property System::String^ MaterialHint;
        property System::String^ TextureDirectory;
        property cli::array<FbxTextureReference^>^ Textures;
        property cli::array<FbxExportVertex^>^ Vertices;
        property cli::array<int>^ Indices;
    };

    public ref class FbxExportBone sealed
    {
    public:
        property System::String^ Name;
        property int ParentId;
        property cli::array<float>^ Translation;
        property cli::array<float>^ RotationQuaternion;
    };

    public ref class FbxExportAnimationClip sealed
    {
    public:
        property System::String^ Name;
        property float FrameRate;
        property cli::array<FbxAnimationFrame^>^ Frames;
    };

    public ref class FbxExportScene sealed
    {
    public:
        property System::String^ Name;
        property System::String^ SkeletonName;
        property cli::array<FbxExportBone^>^ Bones;
        property cli::array<FbxExportMesh^>^ Meshes;
        property cli::array<FbxExportAnimationClip^>^ Animations;
    };

    public ref class FbxBridge abstract sealed
    {
    public:
        static FbxSceneInfo^ InspectScene(System::String^ path);
        static cli::array<System::String^>^ GetNodeHierarchy(System::String^ path);
        static void CopyScene(System::String^ inputPath, System::String^ outputPath, bool ascii);
        static FbxSkeletonInfo^ ExtractSkeleton(System::String^ path, System::String^ skeletonRootName, bool includeEndBones);
        static FbxAnimationClip^ ExtractFirstAnimationClip(System::String^ path, System::String^ skeletonRootName, float frameRate, bool includeEndBones);
        static FbxImportedScene^ ImportScene(System::String^ path, System::String^ skeletonRootName, bool includeEndBones);
        static void ExportScene(FbxExportScene^ scene, System::String^ outputPath, bool ascii);
    };
}
