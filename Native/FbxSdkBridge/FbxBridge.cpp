#include "FbxBridge.h"
#include <fbxsdk.h>
#include <vector>
#include <string>
#include <unordered_map>
#include <algorithm>
#include <cmath>
#include <stdexcept>

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Runtime::InteropServices;

namespace
{
    std::string ToStd(String^ value)
    {
        if (String::IsNullOrEmpty(value))
            return std::string();
        IntPtr nativeValue = Marshal::StringToHGlobalAnsi(value);
        const char* chars = static_cast<const char*>(nativeValue.ToPointer());
        std::string result = chars ? std::string(chars) : std::string();
        Marshal::FreeHGlobal(nativeValue);
        return result;
    }

    String^ ToManaged(const char* value) { return gcnew String(value ? value : ""); }

    constexpr double ExportCentimetersPerUnit = 2.54;
    constexpr double MetersToExportUnits = 100.0 / ExportCentimetersPerUnit;
    constexpr double ExportSkeletonSize = 20.0;
    constexpr double ExportSkeletonLimbLength = 1.0;

    float ReadFloat(cli::array<float>^ values, int index, float fallback)
    {
        return values != nullptr && index >= 0 && index < values->Length ? values[index] : fallback;
    }

    FbxDouble3 ReadDouble3(cli::array<float>^ values, double fallbackX, double fallbackY, double fallbackZ)
    {
        return FbxDouble3(ReadFloat(values, 0, static_cast<float>(fallbackX)), ReadFloat(values, 1, static_cast<float>(fallbackY)), ReadFloat(values, 2, static_cast<float>(fallbackZ)));
    }

    FbxDouble3 ReadExportTranslation(cli::array<float>^ values, double fallbackX, double fallbackY, double fallbackZ)
    {
        auto value = ReadDouble3(values, fallbackX, fallbackY, fallbackZ);
        return FbxDouble3(
            value[0] * MetersToExportUnits,
            value[1] * MetersToExportUnits,
            value[2] * MetersToExportUnits);
    }

    FbxVector4 ReadExportPosition(cli::array<float>^ values)
    {
        return FbxVector4(
            ReadFloat(values, 0, 0.0f) * MetersToExportUnits,
            ReadFloat(values, 1, 0.0f) * MetersToExportUnits,
            ReadFloat(values, 2, 0.0f) * MetersToExportUnits,
            1.0);
    }

    double ToExportTranslationValue(double valueInMeters)
    {
        return valueInMeters * MetersToExportUnits;
    }

    FbxQuaternion ReadQuaternion(cli::array<float>^ values)
    {
        return FbxQuaternion(ReadFloat(values, 0, 0.0f), ReadFloat(values, 1, 0.0f), ReadFloat(values, 2, 0.0f), ReadFloat(values, 3, 1.0f));
    }

    FbxDouble3 QuaternionToEuler(cli::array<float>^ values)
    {
        FbxAMatrix matrix;
        matrix.SetQ(ReadQuaternion(values));
        auto rotation = matrix.GetR();
        return FbxDouble3(rotation[0], rotation[1], rotation[2]);
    }

    const char* AttributeName(FbxNodeAttribute::EType type)
    {
        switch (type)
        {
        case FbxNodeAttribute::eNull: return "Null";
        case FbxNodeAttribute::eSkeleton: return "Skeleton";
        case FbxNodeAttribute::eMesh: return "Mesh";
        case FbxNodeAttribute::eCamera: return "Camera";
        case FbxNodeAttribute::eLight: return "Light";
        default: return "Other";
        }
    }

    struct FbxContext
    {
        FbxManager* Manager = nullptr;
        FbxScene* Scene = nullptr;
        explicit FbxContext(const char* sceneName)
        {
            Manager = FbxManager::Create();
            if (!Manager) throw std::runtime_error("FbxManager::Create failed.");
            auto ios = FbxIOSettings::Create(Manager, IOSROOT);
            Manager->SetIOSettings(ios);
            Scene = FbxScene::Create(Manager, sceneName);
            if (!Scene) throw std::runtime_error("FbxScene::Create failed.");
        }
        ~FbxContext() { if (Manager) Manager->Destroy(); }
    };

    void LoadScene(FbxContext& ctx, const char* path)
    {
        auto importer = FbxImporter::Create(ctx.Manager, "");
        if (!importer->Initialize(path, -1, ctx.Manager->GetIOSettings()))
        {
            std::string message = importer->GetStatus().GetErrorString();
            importer->Destroy();
            throw std::runtime_error("FBX import initialize failed: " + message);
        }
        if (!importer->Import(ctx.Scene))
        {
            std::string message = importer->GetStatus().GetErrorString();
            importer->Destroy();
            throw std::runtime_error("FBX import failed: " + message);
        }
        importer->Destroy();
    }

    int FindWriterFormat(FbxManager* manager, bool ascii)
    {
        if (!ascii)
            return -1;
        auto registry = manager->GetIOPluginRegistry();
        for (int i = 0; i < registry->GetWriterFormatCount(); ++i)
        {
            if (!registry->WriterIsFBX(i))
                continue;
            std::string description = registry->GetWriterFormatDescription(i);
            if (description.find("ascii") != std::string::npos || description.find("ASCII") != std::string::npos)
                return i;
        }
        return -1;
    }

    void SaveScene(FbxContext& ctx, const char* path, bool ascii)
    {
        auto exporter = FbxExporter::Create(ctx.Manager, "");
        int format = FindWriterFormat(ctx.Manager, ascii);
        if (!exporter->Initialize(path, format, ctx.Manager->GetIOSettings()))
        {
            std::string message = exporter->GetStatus().GetErrorString();
            exporter->Destroy();
            throw std::runtime_error("FBX export initialize failed: " + message);
        }
        if (!exporter->Export(ctx.Scene))
        {
            std::string message = exporter->GetStatus().GetErrorString();
            exporter->Destroy();
            throw std::runtime_error("FBX export failed: " + message);
        }
        exporter->Destroy();
    }

    void CollectNodes(FbxNode* node, int depth, List<AssetEditor::Native::FbxSdkBridge::FbxNodeInfo^>^ output)
    {
        if (!node) return;
        auto item = gcnew AssetEditor::Native::FbxSdkBridge::FbxNodeInfo();
        item->Name = ToManaged(node->GetName());
        item->ParentName = node->GetParent() ? ToManaged(node->GetParent()->GetName()) : nullptr;
        item->Depth = depth;
        item->AttributeType = node->GetNodeAttribute() ? ToManaged(AttributeName(node->GetNodeAttribute()->GetAttributeType())) : "None";
        output->Add(item);
        for (int i = 0; i < node->GetChildCount(); ++i) CollectNodes(node->GetChild(i), depth + 1, output);
    }

    void CollectMeshes(FbxNode* node, List<AssetEditor::Native::FbxSdkBridge::FbxMeshInfo^>^ output)
    {
        if (!node) return;
        auto mesh = node->GetMesh();
        if (mesh)
        {
            auto item = gcnew AssetEditor::Native::FbxSdkBridge::FbxMeshInfo();
            item->Name = ToManaged(node->GetName());
            item->ControlPointCount = mesh->GetControlPointsCount();
            item->PolygonCount = mesh->GetPolygonCount();
            item->SkinDeformerCount = mesh->GetDeformerCount(FbxDeformer::eSkin);
            output->Add(item);
        }
        for (int i = 0; i < node->GetChildCount(); ++i) CollectMeshes(node->GetChild(i), output);
    }

    void FindNodeByName(FbxNode* node, const std::string& name, FbxNode*& result)
    {
        if (!node || result) return;
        if (name == node->GetName()) { result = node; return; }
        for (int i = 0; i < node->GetChildCount(); ++i) FindNodeByName(node->GetChild(i), name, result);
    }

    bool IsRealSkeletonNode(FbxNode* node)
    {
        return node && node->GetNodeAttribute() && node->GetNodeAttribute()->GetAttributeType() == FbxNodeAttribute::eSkeleton;
    }

    bool HasRealSkeletonChild(FbxNode* node)
    {
        if (!node)
            return false;

        for (int i = 0; i < node->GetChildCount(); ++i)
        {
            if (IsRealSkeletonNode(node->GetChild(i)))
                return true;
        }

        return false;
    }

    bool IsBlenderNullAnimRoot(FbxNode* node)
    {
        if (!node || !node->GetNodeAttribute())
            return false;

        if (node->GetNodeAttribute()->GetAttributeType() != FbxNodeAttribute::eNull)
            return false;

        std::string name = node->GetName() ? node->GetName() : "";
        return name == "animroot" && HasRealSkeletonChild(node);
    }

    bool IsSkeletonNode(FbxNode* node)
    {
        return IsRealSkeletonNode(node) || IsBlenderNullAnimRoot(node);
    }

    void CollectSkeletonNodes(FbxNode* node, bool includeEndBones, std::vector<FbxNode*>& output)
    {
        if (!node) return;
        std::string name = node->GetName();
        bool isEndBone = name.size() >= 4 && name.substr(name.size() - 4) == "_end";
        if (IsSkeletonNode(node) && (includeEndBones || !isEndBone)) output.push_back(node);
        for (int i = 0; i < node->GetChildCount(); ++i) CollectSkeletonNodes(node->GetChild(i), includeEndBones, output);
    }

    int FindParentIndex(FbxNode* node, const std::unordered_map<FbxNode*, int>& boneIndexByNode)
    {
        auto parent = node ? node->GetParent() : nullptr;
        while (parent)
        {
            auto it = boneIndexByNode.find(parent);
            if (it != boneIndexByNode.end()) return it->second;
            parent = parent->GetParent();
        }
        return -1;
    }

    AssetEditor::Native::FbxSdkBridge::FbxSkeletonInfo^ BuildSkeletonInfo(FbxScene* scene, String^ skeletonRootName, bool includeEndBones)
    {
        auto rootName = ToStd(skeletonRootName);
        FbxNode* skeletonRoot = nullptr;
        if (!rootName.empty()) FindNodeByName(scene->GetRootNode(), rootName, skeletonRoot);
        if (!skeletonRoot) skeletonRoot = scene->GetRootNode();
        std::vector<FbxNode*> boneNodes;
        CollectSkeletonNodes(skeletonRoot, includeEndBones, boneNodes);
        if (boneNodes.empty()) throw std::runtime_error("No skeleton nodes found in FBX scene.");
        std::unordered_map<FbxNode*, int> boneIndexByNode;
        for (int i = 0; i < static_cast<int>(boneNodes.size()); ++i) boneIndexByNode[boneNodes[i]] = i;
        auto skeleton = gcnew AssetEditor::Native::FbxSdkBridge::FbxSkeletonInfo();
        skeleton->RootName = String::IsNullOrWhiteSpace(skeletonRootName) ? ToManaged(skeletonRoot->GetName()) : skeletonRootName;
        skeleton->Bones = gcnew cli::array<AssetEditor::Native::FbxSdkBridge::FbxSkeletonBoneInfo^>(static_cast<int>(boneNodes.size()));
        for (int i = 0; i < static_cast<int>(boneNodes.size()); ++i)
        {
            auto transform = boneNodes[i]->EvaluateLocalTransform(FBXSDK_TIME_INFINITE);
            auto translation = transform.GetT();
            auto quaternion = transform.GetQ();
            auto bone = gcnew AssetEditor::Native::FbxSdkBridge::FbxSkeletonBoneInfo();
            bone->Name = ToManaged(boneNodes[i]->GetName());
            bone->ParentId = FindParentIndex(boneNodes[i], boneIndexByNode);
            bone->LocalTranslation = gcnew cli::array<float>(3);
            bone->LocalRotationQuaternion = gcnew cli::array<float>(4);
            bone->LocalTranslation[0] = static_cast<float>(translation[0]); bone->LocalTranslation[1] = static_cast<float>(translation[1]); bone->LocalTranslation[2] = static_cast<float>(translation[2]);
            bone->LocalRotationQuaternion[0] = static_cast<float>(quaternion[0]); bone->LocalRotationQuaternion[1] = static_cast<float>(quaternion[1]); bone->LocalRotationQuaternion[2] = static_cast<float>(quaternion[2]); bone->LocalRotationQuaternion[3] = static_cast<float>(quaternion[3]);
            skeleton->Bones[i] = bone;
        }
        return skeleton;
    }



    double SceneMetersPerUnit(FbxScene* scene)
    {
        if (!scene)
            return 1.0;
        return scene->GetGlobalSettings().GetSystemUnit().GetScaleFactor() / 100.0;
    }

    bool HasNodeNamed(FbxScene* scene, const std::string& name)
    {
        if (!scene || !scene->GetRootNode())
            return false;

        FbxNode* node = nullptr;
        FindNodeByName(scene->GetRootNode(), name, node);
        return node != nullptr;
    }

    bool LooksLikeTotalWarArmature(FbxScene* scene)
    {
        return HasNodeNamed(scene, "animroot")
            && HasNodeNamed(scene, "root")
            && HasNodeNamed(scene, "spine_0")
            && HasNodeNamed(scene, "upperleg_left")
            && HasNodeNamed(scene, "upperleg_right");
    }

    double DetectAnimationTranslationMetersPerUnit(FbxScene* scene, const std::vector<FbxNode*>& boneNodes, const FbxTime& sampleTime)
    {
        double metersPerUnit = SceneMetersPerUnit(scene);
        std::vector<double> scaledMagnitudes;
        scaledMagnitudes.reserve(boneNodes.size());

        for (auto boneNode : boneNodes)
        {
            if (!boneNode)
                continue;

            auto transform = boneNode->EvaluateLocalTransform(sampleTime);
            auto translation = transform.GetT();
            double x = translation[0] * metersPerUnit;
            double y = translation[1] * metersPerUnit;
            double z = translation[2] * metersPerUnit;
            double magnitude = std::sqrt((x * x) + (y * y) + (z * z));

            if (std::isfinite(magnitude) && magnitude > 0.000001)
                scaledMagnitudes.push_back(magnitude);
        }

        if (scaledMagnitudes.empty())
            return metersPerUnit;

        std::sort(scaledMagnitudes.begin(), scaledMagnitudes.end());
        double medianMagnitude = scaledMagnitudes[scaledMagnitudes.size() / 2];

        if (metersPerUnit > 0.5 && medianMagnitude > 2.0)
            return metersPerUnit / MetersToExportUnits;

        // Blender commonly re-exports our inch-based FBX as a centimeter scene while
        // keeping animation curve values numerically in the original inch unit domain.
        // Applying the centimeter system unit directly would shrink Total War skeleton
        // translations by exactly 1 / 2.54. For Total War armatures in that state,
        // keep interpreting animation translation curves as inches.
        if (metersPerUnit > 0.009 && metersPerUnit < 0.011
            && medianMagnitude < 0.08
            && LooksLikeTotalWarArmature(scene))
        {
            return 1.0 / MetersToExportUnits;
        }

        return metersPerUnit;
    }

    bool IsAnimRootNode(FbxNode* node)
    {
        return node && std::string(node->GetName()) == "animroot";
    }

    bool UsesBlenderMixedTotalWarAnimationUnits(FbxScene* scene, double animationMetersPerUnit)
    {
        double sceneMetersPerUnit = SceneMetersPerUnit(scene);

        return sceneMetersPerUnit > 0.009 && sceneMetersPerUnit < 0.011
            && std::abs(animationMetersPerUnit - (1.0 / MetersToExportUnits)) < 0.0001
            && LooksLikeTotalWarArmature(scene);
    }

    double AnimationTranslationMetersPerUnitForNode(FbxScene* scene, FbxNode* node, double animationMetersPerUnit)
    {
        // Blender re-exports this pipeline in a mixed state:
        // - skeleton bone translation curves stay numerically in inch-domain values;
        // - the armature/root animroot motion is baked as centimeter-domain values.
        // The global detector therefore correctly picks inch-domain for most bones,
        // but animroot needs the real scene unit to avoid a 2.54x root-motion offset.
        if (IsAnimRootNode(node) && UsesBlenderMixedTotalWarAnimationUnits(scene, animationMetersPerUnit))
            return SceneMetersPerUnit(scene);

        return animationMetersPerUnit;
    }

    FbxNode* FindSkeletonRoot(FbxScene* scene, const std::string& requestedRootName)
    {
        if (!scene || !scene->GetRootNode())
            return nullptr;

        FbxNode* skeletonRoot = nullptr;
        if (!requestedRootName.empty())
            FindNodeByName(scene->GetRootNode(), requestedRootName, skeletonRoot);
        if (skeletonRoot)
            return skeletonRoot;

        FbxNode* blenderAnimRoot = nullptr;
        FindNodeByName(scene->GetRootNode(), "animroot", blenderAnimRoot);
        if (IsBlenderNullAnimRoot(blenderAnimRoot))
            return blenderAnimRoot;

        std::vector<FbxNode*> skeletonNodes;
        CollectSkeletonNodes(scene->GetRootNode(), true, skeletonNodes);
        for (auto node : skeletonNodes)
        {
            if (!IsRealSkeletonNode(node))
                continue;

            auto attribute = static_cast<FbxSkeleton*>(node->GetNodeAttribute());
            if (attribute && attribute->GetSkeletonType() == FbxSkeleton::eRoot)
                return node;
        }

        return skeletonNodes.empty() ? scene->GetRootNode() : skeletonNodes[0];
    }

    struct NativeWeight
    {
        int BoneIndex = -1;
        double Weight = 0.0;
    };

    void AddWeight(std::vector<NativeWeight>& weights, int boneIndex, double weight)
    {
        if (boneIndex < 0 || weight <= 0.0)
            return;

        for (auto& item : weights)
        {
            if (item.BoneIndex == boneIndex)
            {
                item.Weight += weight;
                return;
            }
        }

        NativeWeight item;
        item.BoneIndex = boneIndex;
        item.Weight = weight;
        weights.push_back(item);
    }

    void NormalizeAndTrimWeights(std::vector<NativeWeight>& weights)
    {
        weights.erase(
            std::remove_if(weights.begin(), weights.end(), [](const NativeWeight& value) { return value.BoneIndex < 0 || value.Weight <= 0.0; }),
            weights.end());

        std::sort(weights.begin(), weights.end(), [](const NativeWeight& left, const NativeWeight& right) { return left.Weight > right.Weight; });

        if (weights.size() > 4)
            weights.resize(4);

        double total = 0.0;
        for (const auto& item : weights)
            total += item.Weight;

        if (total <= 0.0)
            return;

        for (auto& item : weights)
            item.Weight /= total;
    }

    std::vector<std::vector<NativeWeight>> BuildControlPointWeights(FbxMesh* mesh, const std::unordered_map<FbxNode*, int>& boneIndexByNode)
    {
        std::vector<std::vector<NativeWeight>> output;
        if (!mesh)
            return output;

        output.resize(mesh->GetControlPointsCount());
        const int skinCount = mesh->GetDeformerCount(FbxDeformer::eSkin);
        for (int skinIndex = 0; skinIndex < skinCount; ++skinIndex)
        {
            auto skin = static_cast<FbxSkin*>(mesh->GetDeformer(skinIndex, FbxDeformer::eSkin));
            if (!skin)
                continue;

            const int clusterCount = skin->GetClusterCount();
            for (int clusterIndex = 0; clusterIndex < clusterCount; ++clusterIndex)
            {
                auto cluster = skin->GetCluster(clusterIndex);
                if (!cluster || !cluster->GetLink())
                    continue;

                auto boneIt = boneIndexByNode.find(cluster->GetLink());
                if (boneIt == boneIndexByNode.end())
                    continue;

                const int boneIndex = boneIt->second;
                const int* indices = cluster->GetControlPointIndices();
                const double* weights = cluster->GetControlPointWeights();
                const int count = cluster->GetControlPointIndicesCount();
                for (int i = 0; i < count; ++i)
                {
                    const int controlPointIndex = indices[i];
                    if (controlPointIndex >= 0 && controlPointIndex < static_cast<int>(output.size()))
                        AddWeight(output[controlPointIndex], boneIndex, weights[i]);
                }
            }
        }

        for (auto& weights : output)
            NormalizeAndTrimWeights(weights);

        return output;
    }

    cli::array<float>^ MakeFloatArray2(float x, float y)
    {
        auto values = gcnew cli::array<float>(2);
        values[0] = x;
        values[1] = y;
        return values;
    }

    cli::array<float>^ MakeFloatArray3(float x, float y, float z)
    {
        auto values = gcnew cli::array<float>(3);
        values[0] = x;
        values[1] = y;
        values[2] = z;
        return values;
    }

    void FillManagedWeights(
        AssetEditor::Native::FbxSdkBridge::FbxImportedVertex^ vertex,
        const std::vector<NativeWeight>& weights)
    {
        vertex->BoneIndices = gcnew cli::array<int>(static_cast<int>(weights.size()));
        vertex->BoneWeights = gcnew cli::array<float>(static_cast<int>(weights.size()));
        for (int i = 0; i < static_cast<int>(weights.size()); ++i)
        {
            vertex->BoneIndices[i] = weights[i].BoneIndex;
            vertex->BoneWeights[i] = static_cast<float>(weights[i].Weight);
        }
    }

    FbxAMatrix GetGeometryTransform(FbxNode* node)
    {
        FbxAMatrix geometry;
        geometry.SetIdentity();
        if (!node)
            return geometry;

        geometry.SetT(node->GetGeometricTranslation(FbxNode::eSourcePivot));
        geometry.SetR(node->GetGeometricRotation(FbxNode::eSourcePivot));
        geometry.SetS(node->GetGeometricScaling(FbxNode::eSourcePivot));
        return geometry;
    }

    FbxVector4 TransformNormalByPoints(const FbxAMatrix& transform, const FbxVector4& normal)
    {
        auto origin = transform.MultT(FbxVector4(0.0, 0.0, 0.0, 0.0));
        auto end = transform.MultT(FbxVector4(normal[0], normal[1], normal[2], 0.0));
        FbxVector4 transformed(end[0] - origin[0], end[1] - origin[1], end[2] - origin[2], 0.0);
        transformed.Normalize();
        return transformed;
    }



    bool ManagedEquals(String^ left, String^ right)
    {
        auto safeLeft = left == nullptr ? String::Empty : left;
        auto safeRight = right == nullptr ? String::Empty : right;
        return String::Equals(safeLeft, safeRight, StringComparison::OrdinalIgnoreCase);
    }

    void AddTextureReference(
        List<AssetEditor::Native::FbxSdkBridge::FbxTextureReference^>^ output,
        String^ type,
        String^ path)
    {
        if (String::IsNullOrWhiteSpace(type) || String::IsNullOrWhiteSpace(path))
            return;

        path = path->Replace('/', '\\');
        for each (auto existing in output)
        {
            if (existing != nullptr && ManagedEquals(existing->Type, type) && ManagedEquals(existing->Path, path))
                return;
        }

        auto item = gcnew AssetEditor::Native::FbxSdkBridge::FbxTextureReference();
        item->Type = type;
        item->Path = path;
        output->Add(item);
    }

    String^ ReadTexturePath(FbxFileTexture* texture)
    {
        if (!texture)
            return String::Empty;

        const char* relative = texture->GetRelativeFileName();
        if (relative && relative[0] != '\0')
            return ToManaged(relative);

        const char* absolute = texture->GetFileName();
        if (absolute && absolute[0] != '\0')
            return ToManaged(absolute);

        return String::Empty;
    }

    String^ InferTextureTypeFromName(String^ value)
    {
        if (String::IsNullOrWhiteSpace(value))
            return nullptr;

        auto lower = value->ToLowerInvariant();
        if (lower->Contains("normal") || lower->EndsWith("_n") || lower->Contains("_normal"))
            return "Normal";
        if (lower->Contains("material") || lower->Contains("metal") || lower->Contains("rough"))
            return "MaterialMap";
        if (lower->Contains("mask"))
            return "Mask";
        if (lower->Contains("ao") || lower->Contains("ambient"))
            return "Ambient_occlusion";
        if (lower->Contains("spec"))
            return "Specular";
        if (lower->Contains("gloss"))
            return "Gloss";
        if (lower->Contains("emissive") || lower->Contains("emit"))
            return "Emissive";
        if (lower->Contains("blood"))
            return "Blood";
        if (lower->Contains("diffuse") || lower->Contains("base") || lower->Contains("colour") || lower->Contains("color") || lower->Contains("albedo"))
            return "BaseColour";

        return nullptr;
    }

    void AddPropertyFileTextures(
        FbxProperty property,
        String^ type,
        List<AssetEditor::Native::FbxSdkBridge::FbxTextureReference^>^ output)
    {
        if (!property.IsValid())
            return;

        int textureCount = property.GetSrcObjectCount<FbxFileTexture>();
        for (int i = 0; i < textureCount; ++i)
        {
            auto texture = property.GetSrcObject<FbxFileTexture>(i);
            AddTextureReference(output, type, ReadTexturePath(texture));
        }
    }

    cli::array<AssetEditor::Native::FbxSdkBridge::FbxTextureReference^>^ ExtractMaterialTextures(FbxSurfaceMaterial* material)
    {
        auto output = gcnew List<AssetEditor::Native::FbxSdkBridge::FbxTextureReference^>();
        if (!material)
            return output->ToArray();

        for (auto property = material->GetFirstProperty(); property.IsValid(); property = material->GetNextProperty(property))
        {
            String^ propertyName = ToManaged(property.GetName());
            if (propertyName->StartsWith("AE_Texture_", StringComparison::OrdinalIgnoreCase))
            {
                auto type = propertyName->Substring(11);
                FbxString path = property.Get<FbxString>();
                AddTextureReference(output, type, ToManaged(path.Buffer()));
            }
        }

        AddPropertyFileTextures(material->FindProperty(FbxSurfaceMaterial::sDiffuse), "BaseColour", output);
        AddPropertyFileTextures(material->FindProperty(FbxSurfaceMaterial::sNormalMap), "Normal", output);
        AddPropertyFileTextures(material->FindProperty(FbxSurfaceMaterial::sBump), "Normal", output);
        AddPropertyFileTextures(material->FindProperty(FbxSurfaceMaterial::sSpecular), "MaterialMap", output);
        AddPropertyFileTextures(material->FindProperty(FbxSurfaceMaterial::sShininess), "Gloss", output);
        AddPropertyFileTextures(material->FindProperty(FbxSurfaceMaterial::sTransparentColor), "Mask", output);
        AddPropertyFileTextures(material->FindProperty(FbxSurfaceMaterial::sEmissive), "Emissive", output);

        for (auto property = material->GetFirstProperty(); property.IsValid(); property = material->GetNextProperty(property))
        {
            String^ propertyName = ToManaged(property.GetName());
            String^ inferredType = InferTextureTypeFromName(propertyName);
            if (String::IsNullOrWhiteSpace(inferredType))
                continue;

            AddPropertyFileTextures(property, inferredType, output);
        }

        return output->ToArray();
    }

    const char* ResolveExportPropertyName(String^ type)
    {
        if (ManagedEquals(type, "BaseColour") || ManagedEquals(type, "Diffuse"))
            return FbxSurfaceMaterial::sDiffuse;
        if (ManagedEquals(type, "Normal"))
            return FbxSurfaceMaterial::sNormalMap;
        if (ManagedEquals(type, "MaterialMap") || ManagedEquals(type, "Specular"))
            return FbxSurfaceMaterial::sSpecular;
        if (ManagedEquals(type, "Gloss"))
            return FbxSurfaceMaterial::sShininess;
        if (ManagedEquals(type, "Mask"))
            return FbxSurfaceMaterial::sTransparentColor;
        if (ManagedEquals(type, "Emissive"))
            return FbxSurfaceMaterial::sEmissive;

        return nullptr;
    }

    void AddExportTexture(
        FbxScene* scene,
        FbxSurfacePhong* material,
        AssetEditor::Native::FbxSdkBridge::FbxTextureReference^ textureReference)
    {
        if (!scene || !material || textureReference == nullptr || String::IsNullOrWhiteSpace(textureReference->Path))
            return;

        std::string type = ToStd(String::IsNullOrWhiteSpace(textureReference->Type) ? "Texture" : textureReference->Type);
        std::string path = ToStd(textureReference->Path->Replace('/', '\\'));
        std::string materialName = material->GetName();

        auto customPropertyName = std::string("AE_Texture_") + type;
        auto customProperty = material->FindProperty(customPropertyName.c_str());
        if (!customProperty.IsValid())
            customProperty = FbxProperty::Create(material, FbxStringDT, customPropertyName.c_str());
        customProperty.Set(FbxString(path.c_str()));

        auto textureName = materialName + "_" + type;
        auto texture = FbxFileTexture::Create(scene, textureName.c_str());
        texture->SetFileName(path.c_str());
        texture->SetRelativeFileName(path.c_str());
        texture->SetTextureUse(FbxTexture::eStandard);
        texture->SetMappingType(FbxTexture::eUV);
        texture->SetMaterialUse(FbxFileTexture::eModelMaterial);
        texture->UVSet.Set("UVSet0");

        auto propertyName = ResolveExportPropertyName(textureReference->Type);
        if (!propertyName)
            return;

        auto property = material->FindProperty(propertyName);
        if (property.IsValid())
            property.ConnectSrcObject(texture);
    }

    AssetEditor::Native::FbxSdkBridge::FbxImportedMesh^ ImportMesh(
        FbxScene* scene,
        FbxNode* node,
        FbxMesh* mesh,
        double metersPerUnit,
        const std::unordered_map<FbxNode*, int>& boneIndexByNode)
    {
        auto importedMesh = gcnew AssetEditor::Native::FbxSdkBridge::FbxImportedMesh();
        importedMesh->Name = ToManaged(node->GetName());
        auto firstMaterial = node->GetMaterialCount() > 0 ? node->GetMaterial(0) : nullptr;
        importedMesh->MaterialName = firstMaterial ? ToManaged(firstMaterial->GetName()) : "material";
        importedMesh->Textures = ExtractMaterialTextures(firstMaterial);

        const int polygonCount = mesh->GetPolygonCount();
        const int vertexCount = polygonCount * 3;
        importedMesh->Vertices = gcnew cli::array<AssetEditor::Native::FbxSdkBridge::FbxImportedVertex^>(vertexCount);
        importedMesh->Indices = gcnew cli::array<int>(vertexCount);

        auto controlPoints = mesh->GetControlPoints();
        auto controlPointWeights = BuildControlPointWeights(mesh, boneIndexByNode);

        // FBX mesh control points are local to the mesh node. Blender commonly writes
        // object scale/rotation on the mesh node while the armature/skeleton remains
        // in scene space. If we import raw control points, the RMV2 vertices become
        // too small or offset relative to the skeleton. Bake the mesh node transform
        // into the imported vertices so rigged mesh imports use the same space as
        // the skeleton and skin weights.
        const FbxAMatrix meshGlobalTransform = node->EvaluateGlobalTransform(FBXSDK_TIME_INFINITE) * GetGeometryTransform(node);

        FbxStringList uvSetNames;
        mesh->GetUVSetNames(uvSetNames);
        const char* uvSetName = uvSetNames.GetCount() > 0 ? uvSetNames.GetStringAt(0) : nullptr;

        int outputIndex = 0;
        for (int polygonIndex = 0; polygonIndex < polygonCount; ++polygonIndex)
        {
            if (mesh->GetPolygonSize(polygonIndex) != 3)
                continue;

            for (int polygonVertexIndex = 0; polygonVertexIndex < 3; ++polygonVertexIndex)
            {
                const int controlPointIndex = mesh->GetPolygonVertex(polygonIndex, polygonVertexIndex);
                FbxVector4 position = meshGlobalTransform.MultT(controlPoints[controlPointIndex]);
                FbxVector4 normal(0, 1, 0, 0);
                mesh->GetPolygonVertexNormal(polygonIndex, polygonVertexIndex, normal);
                normal.Normalize();
                normal = TransformNormalByPoints(meshGlobalTransform, normal);

                FbxVector2 uv(0, 0);
                bool unmapped = false;
                if (uvSetName)
                    mesh->GetPolygonVertexUV(polygonIndex, polygonVertexIndex, uvSetName, uv, unmapped);

                auto vertex = gcnew AssetEditor::Native::FbxSdkBridge::FbxImportedVertex();
                vertex->Position = MakeFloatArray3(
                    static_cast<float>(position[0] * metersPerUnit),
                    static_cast<float>(position[1] * metersPerUnit),
                    static_cast<float>(position[2] * metersPerUnit));
                vertex->Normal = MakeFloatArray3(static_cast<float>(normal[0]), static_cast<float>(normal[1]), static_cast<float>(normal[2]));
                vertex->Uv = MakeFloatArray2(static_cast<float>(uv[0]), static_cast<float>(uv[1]));

                if (controlPointIndex >= 0 && controlPointIndex < static_cast<int>(controlPointWeights.size()))
                    FillManagedWeights(vertex, controlPointWeights[controlPointIndex]);
                else
                    FillManagedWeights(vertex, std::vector<NativeWeight>());

                importedMesh->Vertices[outputIndex] = vertex;
                importedMesh->Indices[outputIndex] = outputIndex;
                ++outputIndex;
            }
        }

        if (outputIndex != vertexCount)
        {
            auto compactVertices = gcnew cli::array<AssetEditor::Native::FbxSdkBridge::FbxImportedVertex^>(outputIndex);
            auto compactIndices = gcnew cli::array<int>(outputIndex);
            for (int i = 0; i < outputIndex; ++i)
            {
                compactVertices[i] = importedMesh->Vertices[i];
                compactIndices[i] = importedMesh->Indices[i];
            }
            importedMesh->Vertices = compactVertices;
            importedMesh->Indices = compactIndices;
        }

        return importedMesh;
    }

    void ImportMeshesRecursive(
        FbxScene* scene,
        FbxNode* node,
        double metersPerUnit,
        const std::unordered_map<FbxNode*, int>& boneIndexByNode,
        List<AssetEditor::Native::FbxSdkBridge::FbxImportedMesh^>^ output)
    {
        if (!node)
            return;

        auto mesh = node->GetMesh();
        if (mesh && mesh->GetPolygonCount() > 0)
            output->Add(ImportMesh(scene, node, mesh, metersPerUnit, boneIndexByNode));

        for (int i = 0; i < node->GetChildCount(); ++i)
            ImportMeshesRecursive(scene, node->GetChild(i), metersPerUnit, boneIndexByNode, output);
    }

    bool IsPropSocketName(System::String^ name)
    {
        if (System::String::IsNullOrWhiteSpace(name))
            return false;
        return name->StartsWith("be_prop_", System::StringComparison::OrdinalIgnoreCase);
    }

    bool HasExportBoneChild(cli::array<AssetEditor::Native::FbxSdkBridge::FbxExportBone^>^ bones, int parentIndex)
    {
        if (bones == nullptr)
            return false;
        for (int i = 0; i < bones->Length; ++i)
        {
            if (bones[i] != nullptr && bones[i]->ParentId == parentIndex)
                return true;
        }
        return false;
    }

    FbxNode* CreateBoneNode(FbxScene* scene, AssetEditor::Native::FbxSdkBridge::FbxExportBone^ source, bool isRoot)
    {
        auto attribute = FbxSkeleton::Create(scene, ToStd(source->Name + "_attr").c_str());
        attribute->SetSkeletonType(isRoot ? FbxSkeleton::eRoot : FbxSkeleton::eLimbNode);
        attribute->Size.Set(ExportSkeletonSize);
        attribute->LimbLength.Set(ExportSkeletonLimbLength);

        auto node = FbxNode::Create(scene, ToStd(source->Name).c_str());
        node->SetNodeAttribute(attribute);
        node->LclTranslation.Set(ReadExportTranslation(source->Translation, 0, 0, 0));
        node->LclRotation.Set(QuaternionToEuler(source->RotationQuaternion));
        node->LclScaling.Set(FbxDouble3(1, 1, 1));
        return node;
    }

    void BuildSkeleton(FbxScene* scene, cli::array<AssetEditor::Native::FbxSdkBridge::FbxExportBone^>^ bones, std::vector<FbxNode*>& boneNodes)
    {
        boneNodes.clear();
        if (bones == nullptr || bones->Length == 0) return;
        boneNodes.resize(bones->Length, nullptr);
        for (int i = 0; i < bones->Length; ++i)
            boneNodes[i] = CreateBoneNode(scene, bones[i], bones[i]->ParentId < 0);
        for (int i = 0; i < bones->Length; ++i)
        {
            int parentId = bones[i]->ParentId;
            if (parentId >= 0 && parentId < static_cast<int>(boneNodes.size())) boneNodes[parentId]->AddChild(boneNodes[i]);
            else scene->GetRootNode()->AddChild(boneNodes[i]);
        }

    }

    FbxSurfacePhong* CreateMaterial(
        FbxScene* scene,
        String^ materialName,
        cli::array<AssetEditor::Native::FbxSdkBridge::FbxTextureReference^>^ textures)
    {
        std::string name = ToStd(String::IsNullOrWhiteSpace(materialName) ? "material" : materialName);
        auto material = FbxSurfacePhong::Create(scene, name.c_str());
        material->Diffuse.Set(FbxDouble3(0.75, 0.75, 0.75));
        material->Ambient.Set(FbxDouble3(0.1, 0.1, 0.1));

        if (textures != nullptr)
        {
            for each (auto textureReference in textures)
                AddExportTexture(scene, material, textureReference);
        }

        return material;
    }

    void AddMesh(FbxScene* scene, AssetEditor::Native::FbxSdkBridge::FbxExportMesh^ source, const std::vector<FbxNode*>& boneNodes)
    {
        if (source == nullptr || source->Vertices == nullptr || source->Indices == nullptr || source->Vertices->Length == 0 || source->Indices->Length < 3) return;
        auto mesh = FbxMesh::Create(scene, ToStd(source->Name + "_mesh").c_str());
        mesh->InitControlPoints(source->Vertices->Length);
        for (int i = 0; i < source->Vertices->Length; ++i)
        {
            auto vertex = source->Vertices[i];
            auto p = vertex ? vertex->Position : nullptr;
            mesh->SetControlPointAt(ReadExportPosition(p), i);
        }

        auto normalElement = mesh->CreateElementNormal();
        normalElement->SetMappingMode(FbxGeometryElement::eByPolygonVertex);
        normalElement->SetReferenceMode(FbxGeometryElement::eDirect);
        auto uvElement = mesh->CreateElementUV("UVSet0");
        uvElement->SetMappingMode(FbxGeometryElement::eByPolygonVertex);
        uvElement->SetReferenceMode(FbxGeometryElement::eDirect);

        for (int i = 0; i + 2 < source->Indices->Length; i += 3)
        {
            mesh->BeginPolygon(0);
            for (int j = 0; j < 3; ++j)
            {
                int index = source->Indices[i + j];
                if (index < 0 || index >= source->Vertices->Length) index = 0;
                auto vertex = source->Vertices[index];
                auto n = vertex ? vertex->Normal : nullptr;
                auto uv = vertex ? vertex->Uv : nullptr;
                mesh->AddPolygon(index);
                normalElement->GetDirectArray().Add(FbxVector4(ReadFloat(n, 0, 0), ReadFloat(n, 1, 1), ReadFloat(n, 2, 0), 0));
                uvElement->GetDirectArray().Add(FbxVector2(ReadFloat(uv, 0, 0), ReadFloat(uv, 1, 0)));
            }
            mesh->EndPolygon();
        }

        auto node = FbxNode::Create(scene, ToStd(source->Name).c_str());
        node->SetNodeAttribute(mesh);
        node->AddMaterial(CreateMaterial(scene, source->MaterialName, source->Textures));
        scene->GetRootNode()->AddChild(node);

        if (!boneNodes.empty())
        {
            auto skin = FbxSkin::Create(scene, ToStd(source->Name + "_skin").c_str());
            std::vector<FbxCluster*> clusters(boneNodes.size(), nullptr);
            for (int i = 0; i < static_cast<int>(boneNodes.size()); ++i)
            {
                clusters[i] = FbxCluster::Create(scene, ToStd(source->Name + "_cluster_" + gcnew String(boneNodes[i]->GetName())).c_str());
                clusters[i]->SetLink(boneNodes[i]);
                clusters[i]->SetLinkMode(FbxCluster::eNormalize);
                clusters[i]->SetTransformMatrix(node->EvaluateGlobalTransform());
                clusters[i]->SetTransformLinkMatrix(boneNodes[i]->EvaluateGlobalTransform());
            }
            for (int vertexIndex = 0; vertexIndex < source->Vertices->Length; ++vertexIndex)
            {
                auto vertex = source->Vertices[vertexIndex];
                if (!vertex || !vertex->BoneIndices || !vertex->BoneWeights) continue;
                int count = System::Math::Min(vertex->BoneIndices->Length, vertex->BoneWeights->Length);
                for (int weightIndex = 0; weightIndex < count; ++weightIndex)
                {
                    int boneIndex = vertex->BoneIndices[weightIndex];
                    double weight = vertex->BoneWeights[weightIndex];
                    if (boneIndex >= 0 && boneIndex < static_cast<int>(clusters.size()) && weight > 0.0) clusters[boneIndex]->AddControlPointIndex(vertexIndex, weight);
                }
            }
            for (auto cluster : clusters)
            {
                skin->AddCluster(cluster);
            }
            mesh->AddDeformer(skin);
        }
    }

    void AddBindPose(FbxScene* scene, const std::vector<FbxNode*>& boneNodes)
    {
        if (boneNodes.empty()) return;
        auto pose = FbxPose::Create(scene, "BindPose");
        pose->SetIsBindPose(true);
        for (auto node : boneNodes) pose->Add(node, node->EvaluateGlobalTransform());
        scene->AddPose(pose);
    }

    void AddVectorCurve(FbxAnimLayer* layer, FbxPropertyT<FbxDouble3>& property, const char* component, int axis, cli::array<AssetEditor::Native::FbxSdkBridge::FbxAnimationFrame^>^ frames, int boneIndex, float frameRate, bool rotation, double durationSeconds)
    {
        auto curve = property.GetCurve(layer, component, true);
        curve->KeyModifyBegin();
        FbxTime time;
        double lastKeyTime = 0.0;
        float lastValue = 0.0f;
        bool hasLastValue = false;
        for (int frameIndex = 0; frameIndex < frames->Length; ++frameIndex)
        {
            if (!frames[frameIndex] || !frames[frameIndex]->Bones || boneIndex >= frames[frameIndex]->Bones->Length || !frames[frameIndex]->Bones[boneIndex]) continue;
            auto boneFrame = frames[frameIndex]->Bones[boneIndex];
            double value = 0;
            if (rotation)
            {
                auto euler = QuaternionToEuler(boneFrame->RotationQuaternion);
                value = euler[axis];
            }
            else
            {
                value = ToExportTranslationValue(ReadFloat(boneFrame->Translation, axis, 0));
            }
            lastKeyTime = static_cast<double>(frameIndex) / frameRate;
            lastValue = static_cast<float>(value);
            hasLastValue = true;
            time.SetSecondDouble(lastKeyTime);
            int keyIndex = curve->KeyAdd(time);
            curve->KeySetValue(keyIndex, lastValue);
            curve->KeySetInterpolation(keyIndex, FbxAnimCurveDef::eInterpolationLinear);
        }
        if (hasLastValue && durationSeconds > lastKeyTime + 0.000001)
        {
            time.SetSecondDouble(durationSeconds);
            int keyIndex = curve->KeyAdd(time);
            curve->KeySetValue(keyIndex, lastValue);
            curve->KeySetInterpolation(keyIndex, FbxAnimCurveDef::eInterpolationLinear);
        }
        curve->KeyModifyEnd();
    }

    void AddAnimations(FbxScene* scene, cli::array<AssetEditor::Native::FbxSdkBridge::FbxExportAnimationClip^>^ animations, const std::vector<FbxNode*>& boneNodes)
    {
        if (animations == nullptr || animations->Length == 0 || boneNodes.empty()) return;
        for (int clipIndex = 0; clipIndex < animations->Length; ++clipIndex)
        {
            auto clip = animations[clipIndex];
            if (!clip || !clip->Frames || clip->Frames->Length == 0) continue;
            float frameRate = clip->FrameRate > 0 ? clip->FrameRate : 20.0f;
            double sampledDuration = static_cast<double>(clip->Frames->Length - 1) / frameRate;
            double durationSeconds = sampledDuration;
            std::string stackName = ToStd(String::IsNullOrWhiteSpace(clip->Name) ? "Animation" : clip->Name);
            const std::string durationMarker = "__AE_DURATION_";
            size_t durationMarkerIndex = stackName.rfind(durationMarker);
            if (durationMarkerIndex != std::string::npos)
            {
                std::string durationText = stackName.substr(durationMarkerIndex + durationMarker.size());
                try
                {
                    double parsedDuration = std::stod(durationText);
                    if (parsedDuration > sampledDuration)
                        durationSeconds = parsedDuration;
                }
                catch (...)
                {
                }

                stackName = stackName.substr(0, durationMarkerIndex);
                if (stackName.empty())
                    stackName = "Animation";
            }

            auto stack = FbxAnimStack::Create(scene, stackName.c_str());
            auto layer = FbxAnimLayer::Create(scene, "BaseLayer");
            stack->AddMember(layer);
            FbxTime start; start.SetSecondDouble(0);
            FbxTime end; end.SetSecondDouble(durationSeconds);
            stack->LocalStart.Set(start); stack->LocalStop.Set(end);
            stack->ReferenceStart.Set(start); stack->ReferenceStop.Set(end);
            scene->GetGlobalSettings().SetTimelineDefaultTimeSpan(FbxTimeSpan(start, end));
            for (int boneIndex = 0; boneIndex < static_cast<int>(boneNodes.size()); ++boneIndex)
            {
                auto node = boneNodes[boneIndex];
                AddVectorCurve(layer, node->LclTranslation, FBXSDK_CURVENODE_COMPONENT_X, 0, clip->Frames, boneIndex, frameRate, false, durationSeconds);
                AddVectorCurve(layer, node->LclTranslation, FBXSDK_CURVENODE_COMPONENT_Y, 1, clip->Frames, boneIndex, frameRate, false, durationSeconds);
                AddVectorCurve(layer, node->LclTranslation, FBXSDK_CURVENODE_COMPONENT_Z, 2, clip->Frames, boneIndex, frameRate, false, durationSeconds);
                AddVectorCurve(layer, node->LclRotation, FBXSDK_CURVENODE_COMPONENT_X, 0, clip->Frames, boneIndex, frameRate, true, durationSeconds);
                AddVectorCurve(layer, node->LclRotation, FBXSDK_CURVENODE_COMPONENT_Y, 1, clip->Frames, boneIndex, frameRate, true, durationSeconds);
                AddVectorCurve(layer, node->LclRotation, FBXSDK_CURVENODE_COMPONENT_Z, 2, clip->Frames, boneIndex, frameRate, true, durationSeconds);
            }
        }
    }
}

namespace AssetEditor::Native::FbxSdkBridge
{
    FbxSceneInfo^ FbxBridge::InspectScene(String^ path)
    {
        auto inputPath = ToStd(path);
        FbxContext ctx("InspectScene");
        LoadScene(ctx, inputPath.c_str());
        auto nodes = gcnew List<FbxNodeInfo^>();
        auto meshes = gcnew List<FbxMeshInfo^>();
        CollectNodes(ctx.Scene->GetRootNode(), 0, nodes);
        CollectMeshes(ctx.Scene->GetRootNode(), meshes);
        auto animations = gcnew List<FbxAnimationStackInfo^>();
        int stackCount = ctx.Scene->GetSrcObjectCount<FbxAnimStack>();
        for (int i = 0; i < stackCount; ++i)
        {
            auto stack = ctx.Scene->GetSrcObject<FbxAnimStack>(i);
            auto item = gcnew FbxAnimationStackInfo();
            item->Name = ToManaged(stack->GetName());
            item->DurationSeconds = stack->GetLocalTimeSpan().GetDuration().GetSecondDouble();
            animations->Add(item);
        }
        auto sceneInfo = gcnew FbxSceneInfo();
        sceneInfo->Nodes = nodes->ToArray(); sceneInfo->Meshes = meshes->ToArray(); sceneInfo->Animations = animations->ToArray();
        return sceneInfo;
    }

    array<String^>^ FbxBridge::GetNodeHierarchy(String^ path)
    {
        auto sceneInfo = InspectScene(path);
        auto lines = gcnew array<String^>(sceneInfo->Nodes->Length);
        for (int i = 0; i < sceneInfo->Nodes->Length; ++i)
        {
            auto node = sceneInfo->Nodes[i];
            lines[i] = gcnew String(' ', node->Depth * 2) + node->Name + " [" + node->AttributeType + "]";
        }
        return lines;
    }

    void FbxBridge::CopyScene(String^ inputPath, String^ outputPath, bool ascii)
    {
        FbxContext ctx("CopyScene");
        LoadScene(ctx, ToStd(inputPath).c_str());
        SaveScene(ctx, ToStd(outputPath).c_str(), ascii);
    }

    FbxSkeletonInfo^ FbxBridge::ExtractSkeleton(String^ path, String^ skeletonRootName, bool includeEndBones)
    {
        FbxContext ctx("ExtractSkeleton");
        LoadScene(ctx, ToStd(path).c_str());
        return BuildSkeletonInfo(ctx.Scene, skeletonRootName, includeEndBones);
    }

    FbxAnimationClip^ FbxBridge::ExtractFirstAnimationClip(String^ path, String^ skeletonRootName, float frameRate, bool includeEndBones)
    {
        auto rootName = ToStd(skeletonRootName);
        FbxContext ctx("ExtractAnimationClip");
        LoadScene(ctx, ToStd(path).c_str());
        int stackCount = ctx.Scene->GetSrcObjectCount<FbxAnimStack>();
        if (stackCount == 0) throw std::runtime_error("No animation stack found in FBX scene.");
        auto stack = ctx.Scene->GetSrcObject<FbxAnimStack>(0);
        ctx.Scene->SetCurrentAnimationStack(stack);
        FbxNode* skeletonRoot = FindSkeletonRoot(ctx.Scene, rootName);
        if (!skeletonRoot) skeletonRoot = ctx.Scene->GetRootNode();
        std::vector<FbxNode*> boneNodes;
        CollectSkeletonNodes(skeletonRoot, includeEndBones, boneNodes);
        if (boneNodes.empty()) throw std::runtime_error("No skeleton nodes found in FBX scene.");
        std::unordered_map<FbxNode*, int> boneIndexByNode;
        for (int i = 0; i < static_cast<int>(boneNodes.size()); ++i) boneIndexByNode[boneNodes[i]] = i;
        FbxTimeSpan localTimeSpan = stack->GetLocalTimeSpan();
        FbxTime startTime = localTimeSpan.GetStart();
        double duration = localTimeSpan.GetDuration().GetSecondDouble();
        int frameCount = System::Math::Max(1, static_cast<int>(std::floor(duration * frameRate + 0.5)) + 1);
        double metersPerUnit = DetectAnimationTranslationMetersPerUnit(ctx.Scene, boneNodes, startTime);
        auto clip = gcnew FbxAnimationClip();
        clip->Name = ToManaged(stack->GetName()); clip->SkeletonName = String::IsNullOrWhiteSpace(skeletonRootName) ? ToManaged(skeletonRoot->GetName()) : skeletonRootName; clip->FrameRate = frameRate; clip->DurationSeconds = duration;
        clip->Bones = gcnew array<FbxBoneInfo^>(static_cast<int>(boneNodes.size())); clip->Frames = gcnew array<FbxAnimationFrame^>(frameCount);
        for (int i = 0; i < static_cast<int>(boneNodes.size()); ++i)
        {
            auto bone = gcnew FbxBoneInfo(); bone->Name = ToManaged(boneNodes[i]->GetName()); bone->ParentId = FindParentIndex(boneNodes[i], boneIndexByNode); clip->Bones[i] = bone;
        }
        FbxTime time;
        for (int frameIndex = 0; frameIndex < frameCount; ++frameIndex)
        {
            time.SetSecondDouble(startTime.GetSecondDouble() + static_cast<double>(frameIndex) / frameRate);
            auto frame = gcnew FbxAnimationFrame(); frame->Bones = gcnew array<FbxBoneFrame^>(static_cast<int>(boneNodes.size()));
            for (int boneIndex = 0; boneIndex < static_cast<int>(boneNodes.size()); ++boneIndex)
            {
                auto transform = boneNodes[boneIndex]->EvaluateLocalTransform(time);
                auto translation = transform.GetT(); auto quaternion = transform.GetQ();
                double nodeMetersPerUnit = AnimationTranslationMetersPerUnitForNode(ctx.Scene, boneNodes[boneIndex], metersPerUnit);
                auto boneFrame = gcnew FbxBoneFrame(); boneFrame->Translation = gcnew array<float>(3); boneFrame->RotationQuaternion = gcnew array<float>(4);
                boneFrame->Translation[0] = static_cast<float>(translation[0] * nodeMetersPerUnit); boneFrame->Translation[1] = static_cast<float>(translation[1] * nodeMetersPerUnit); boneFrame->Translation[2] = static_cast<float>(translation[2] * nodeMetersPerUnit);
                boneFrame->RotationQuaternion[0] = static_cast<float>(quaternion[0]); boneFrame->RotationQuaternion[1] = static_cast<float>(quaternion[1]); boneFrame->RotationQuaternion[2] = static_cast<float>(quaternion[2]); boneFrame->RotationQuaternion[3] = static_cast<float>(quaternion[3]);
                frame->Bones[boneIndex] = boneFrame;
            }
            clip->Frames[frameIndex] = frame;
        }
        return clip;
    }



    FbxImportedScene^ FbxBridge::ImportScene(String^ path, String^ skeletonRootName, bool includeEndBones)
    {
        FbxContext ctx("ImportScene");
        LoadScene(ctx, ToStd(path).c_str());

        FbxGeometryConverter converter(ctx.Manager);
        converter.Triangulate(ctx.Scene, true);

        auto rootName = ToStd(skeletonRootName);
        FbxNode* skeletonRoot = FindSkeletonRoot(ctx.Scene, rootName);
        std::vector<FbxNode*> boneNodes;
        CollectSkeletonNodes(skeletonRoot, includeEndBones, boneNodes);

        std::unordered_map<FbxNode*, int> boneIndexByNode;
        for (int i = 0; i < static_cast<int>(boneNodes.size()); ++i)
            boneIndexByNode[boneNodes[i]] = i;

        auto importedScene = gcnew FbxImportedScene();
        importedScene->SkeletonName = skeletonRoot ? ToManaged(skeletonRoot->GetName()) : "";
        importedScene->Bones = gcnew cli::array<FbxSkeletonBoneInfo^>(static_cast<int>(boneNodes.size()));

        for (int i = 0; i < static_cast<int>(boneNodes.size()); ++i)
        {
            auto transform = boneNodes[i]->EvaluateLocalTransform(FBXSDK_TIME_INFINITE);
            auto translation = transform.GetT();
            auto quaternion = transform.GetQ();
            auto bone = gcnew FbxSkeletonBoneInfo();
            bone->Name = ToManaged(boneNodes[i]->GetName());
            bone->ParentId = FindParentIndex(boneNodes[i], boneIndexByNode);
            bone->LocalTranslation = gcnew cli::array<float>(3);
            bone->LocalRotationQuaternion = gcnew cli::array<float>(4);
            bone->LocalTranslation[0] = static_cast<float>(translation[0] * SceneMetersPerUnit(ctx.Scene));
            bone->LocalTranslation[1] = static_cast<float>(translation[1] * SceneMetersPerUnit(ctx.Scene));
            bone->LocalTranslation[2] = static_cast<float>(translation[2] * SceneMetersPerUnit(ctx.Scene));
            bone->LocalRotationQuaternion[0] = static_cast<float>(quaternion[0]);
            bone->LocalRotationQuaternion[1] = static_cast<float>(quaternion[1]);
            bone->LocalRotationQuaternion[2] = static_cast<float>(quaternion[2]);
            bone->LocalRotationQuaternion[3] = static_cast<float>(quaternion[3]);
            importedScene->Bones[i] = bone;
        }

        auto meshes = gcnew List<FbxImportedMesh^>();
        ImportMeshesRecursive(ctx.Scene, ctx.Scene->GetRootNode(), SceneMetersPerUnit(ctx.Scene), boneIndexByNode, meshes);
        importedScene->Meshes = meshes->ToArray();
        return importedScene;
    }

    void FbxBridge::ExportScene(FbxExportScene^ sceneData, String^ outputPath, bool ascii)
    {
        if (sceneData == nullptr) throw gcnew ArgumentNullException("sceneData");
        FbxContext ctx("AssetEditorScene");
        ctx.Scene->GetGlobalSettings().SetAxisSystem(FbxAxisSystem::MayaYUp);
        ctx.Scene->GetGlobalSettings().SetSystemUnit(FbxSystemUnit::Inch);
        std::vector<FbxNode*> boneNodes;
        BuildSkeleton(ctx.Scene, sceneData->Bones, boneNodes);
        if (sceneData->Meshes != nullptr)
        {
            for (int i = 0; i < sceneData->Meshes->Length; ++i) AddMesh(ctx.Scene, sceneData->Meshes[i], boneNodes);
        }
        AddBindPose(ctx.Scene, boneNodes);
        AddAnimations(ctx.Scene, sceneData->Animations, boneNodes);
        SaveScene(ctx, ToStd(outputPath).c_str(), ascii);
    }
}
