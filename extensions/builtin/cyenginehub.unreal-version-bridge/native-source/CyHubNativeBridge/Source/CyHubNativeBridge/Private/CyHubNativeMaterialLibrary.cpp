#include "CyHubNativeMaterialLibrary.h"
#include "Materials/Material.h"
#include "Materials/MaterialInstanceConstant.h"
#include "Materials/MaterialExpression.h"
#include "Materials/MaterialExpressionComment.h"
#include "Materials/MaterialFunction.h"
#include "CyHubNativeObjectIO.h"
#include "MaterialExpressionIO.h"
#include "AssetCompilingManager.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/PackageName.h"
#include "HAL/FileManager.h"
#include "UObject/UnrealType.h"
#include "UObject/SavePackage.h"
#include "UObject/Package.h"
#include "Runtime/Launch/Resources/Version.h"
#include "Camera/CameraComponent.h"

namespace
{
const TSet<FString> Nodes = {
    TEXT("MaterialExpressionConstant"), TEXT("MaterialExpressionConstant2Vector"), TEXT("MaterialExpressionConstant3Vector"), TEXT("MaterialExpressionConstant4Vector"),
    TEXT("MaterialExpressionScalarParameter"), TEXT("MaterialExpressionVectorParameter"), TEXT("MaterialExpressionTextureSample"), TEXT("MaterialExpressionTextureSampleParameter2D"),
    TEXT("MaterialExpressionTextureCoordinate"), TEXT("MaterialExpressionMultiply"), TEXT("MaterialExpressionAdd"), TEXT("MaterialExpressionSubtract"), TEXT("MaterialExpressionDivide"),
    TEXT("MaterialExpressionLinearInterpolate"), TEXT("MaterialExpressionOneMinus"), TEXT("MaterialExpressionClamp"), TEXT("MaterialExpressionSaturate"),
    TEXT("MaterialExpressionAppendVector"), TEXT("MaterialExpressionComponentMask"), TEXT("MaterialExpressionStaticSwitchParameter"),
    TEXT("MaterialExpressionAbs"), TEXT("MaterialExpressionBumpOffset"), TEXT("MaterialExpressionCameraPositionWS"),
    TEXT("MaterialExpressionComment"), TEXT("MaterialExpressionCrossProduct"), TEXT("MaterialExpressionDesaturation"),
    TEXT("MaterialExpressionDistance"), TEXT("MaterialExpressionDotProduct"), TEXT("MaterialExpressionFrac"),
    TEXT("MaterialExpressionFunctionInput"), TEXT("MaterialExpressionFunctionOutput"), TEXT("MaterialExpressionGetMaterialAttributes"),
    TEXT("MaterialExpressionIf"), TEXT("MaterialExpressionMakeMaterialAttributes"), TEXT("MaterialExpressionMaterialFunctionCall"),
    TEXT("MaterialExpressionNoise"), TEXT("MaterialExpressionNormalize"), TEXT("MaterialExpressionObjectBounds"),
    TEXT("MaterialExpressionObjectPositionWS"), TEXT("MaterialExpressionObjectRadius"), TEXT("MaterialExpressionPixelNormalWS"),
    TEXT("MaterialExpressionPower"), TEXT("MaterialExpressionReflectionVectorWS"), TEXT("MaterialExpressionReroute"),
    TEXT("MaterialExpressionRotateAboutAxis"), TEXT("MaterialExpressionSceneTexture"), TEXT("MaterialExpressionSetMaterialAttributes"),
    TEXT("MaterialExpressionSine"), TEXT("MaterialExpressionStaticComponentMaskParameter"), TEXT("MaterialExpressionTextureObjectParameter"),
    TEXT("MaterialExpressionTextureSampleParameterCube"), TEXT("MaterialExpressionTime"), TEXT("MaterialExpressionTransform"),
    TEXT("MaterialExpressionTransformPosition"), TEXT("MaterialExpressionTwoSidedSign"), TEXT("MaterialExpressionVertexColor"), TEXT("MaterialExpressionWorldPosition")
};
TSharedPtr<FJsonObject> Read(const FString& File)
{
    FString Text; TSharedPtr<FJsonObject> Json;
    if (!FFileHelper::LoadFileToString(Text, *File) || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Json)) return nullptr;
    return Json;
}
bool Write(const FString& File, const TSharedRef<FJsonObject>& Json)
{
    FString Text; FJsonSerializer::Serialize(Json, TJsonWriterFactory<>::Create(&Text));
    return FFileHelper::SaveStringToFile(Text, *File, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}
bool ValidPath(const FString& Path)
{
    FString Package, Object;
    return Path.Split(TEXT("."), &Package, &Object) && Package.StartsWith(TEXT("/Game/")) && FPackageName::IsValidLongPackageName(Package) && Object == FPackageName::GetShortName(Package);
}
FString Value(FProperty* Property, const void* Container, UObject* Owner, int32 Index = 0)
{
    FString Text; Property->ExportText_InContainer(Index, Text, Container, Container, Owner, PPF_None); return Text;
}
FString TypeName(FProperty* Property)
{ FString Extended; FString Type = Property->GetCPPType(&Extended); return (Type + Extended).TrimStartAndEnd(); }
FString TargetText(FProperty* Property, const TSharedPtr<FJsonObject>& Field)
{
    FString Text = Field->GetStringField(TEXT("value"));
    // Same named post-tonemap stage, renamed by Epic after 5.3. Never map
    // an unknown stage by numeric enum index or silently accept other values.
    auto Byte = CastField<FByteProperty>(Property);
    if (Byte && Byte->Enum && Byte->Enum->GetFName() == TEXT("EBlendableLocation") &&
        Text == TEXT("BL_SceneColorAfterTonemapping") && Byte->Enum->GetIndexByNameString(Text) == INDEX_NONE &&
        Byte->Enum->GetIndexByNameString(TEXT("BL_AfterTonemapping")) != INDEX_NONE)
        return TEXT("BL_AfterTonemapping");
    return Text;
}
// Reflection is an exchange contract, not a raw 5.7 Serialize stream read blindly by 5.3.
// Custom/non-reflected material caches are regenerated by the target engine.
TSharedRef<FJsonObject> PropertiesOf(UStruct* Type, const void* Container, const void* Defaults, UObject* Owner, bool All)
{
    auto Result = MakeShared<FJsonObject>();
    for (TFieldIterator<FProperty> It(Type); It; ++It)
    {
        FProperty* Property = *It;
        if (Property->HasAnyPropertyFlags(CPF_Transient | CPF_Deprecated | CPF_DuplicateTransient | CPF_Config) || Property->GetFName() == TEXT("ExpressionCollection")) continue;
        if (!All && !Property->HasAnyPropertyFlags(CPF_Edit) && Property->GetFName() != TEXT("ShadingModel") && Property->GetFName() != TEXT("ShadingModels")) continue;
        auto Field = MakeShared<FJsonObject>();
        Field->SetStringField(TEXT("value"), Value(Property, Container, Owner));
        Field->SetStringField(TEXT("default"), Value(Property, Defaults, Owner));
        Field->SetStringField(TEXT("type"), TypeName(Property));
        Field->SetNumberField(TEXT("arrayDim"), Property->ArrayDim);
        if (Property->ArrayDim > 1)
        {
            TArray<TSharedPtr<FJsonValue>> Elements;
            for (int32 Index = 0; Index < Property->ArrayDim; ++Index)
            {
                auto Element = MakeShared<FJsonObject>();
                Element->SetStringField(TEXT("value"), Value(Property, Container, Owner, Index));
                Element->SetStringField(TEXT("default"), Value(Property, Defaults, Owner, Index));
                Elements.Add(MakeShared<FJsonValueObject>(Element));
            }
            Field->SetArrayField(TEXT("elements"), Elements);
        }
        // Explicitly decomposed structures only. Arbitrary custom Serialize state is not
        // assumed to be covered by reflection (notably expression input structs).
        if (auto Struct = CastField<FStructProperty>(Property); Struct && Property->ArrayDim == 1 &&
            (Struct->Struct->GetFName() == TEXT("BodyInstance") || Struct->Struct->GetFName() == TEXT("KAggregateGeom") || Struct->Struct->GetFName() == TEXT("PostProcessSettings") || Struct->Struct->GetFName() == TEXT("LightmassWorldInfoSettings") ||
             Struct->Struct->GetFName() == TEXT("CameraFocusSettings") || Struct->Struct->GetFName() == TEXT("CameraFilmbackSettings") || Struct->Struct->GetFName() == TEXT("CameraLensSettings") ||
             Struct->Struct->GetFName() == TEXT("MeshNaniteSettings") || Struct->Struct->GetFName() == TEXT("MeshBuildSettings") ||
             Struct->Struct->GetFName() == TEXT("MeshReductionSettings") || Struct->Struct->GetFName() == TEXT("PerPlatformFloat") ||
             Struct->Struct->GetFName() == TEXT("MaterialInstanceBasePropertyOverrides") ||
             Struct->Struct->GetFName() == TEXT("MaterialOverrideNanite") ||
             Struct->Struct->GetFName() == TEXT("ColorMaterialInput") ||
             Struct->Struct->GetFName() == TEXT("ScalarMaterialInput") ||
             Struct->Struct->GetFName() == TEXT("VectorMaterialInput") ||
             Struct->Struct->GetFName() == TEXT("Vector2MaterialInput") ||
             Struct->Struct->GetFName() == TEXT("ExpressionInput") ||
             Struct->Struct->GetFName() == TEXT("MaterialAttributesInput") ||
             Struct->Struct->GetFName() == TEXT("ShadingModelMaterialInput")))
            Field->SetObjectField(TEXT("members"), PropertiesOf(Struct->Struct, Property->ContainerPtrToValuePtr<void>(Container), Property->ContainerPtrToValuePtr<void>(Defaults), Owner, true));
        Result->SetObjectField(Property->GetName(), Field);
    }
    return Result;
}
TSharedRef<FJsonObject> Properties(UObject* Object, bool All)
{ return PropertiesOf(Object->GetClass(), Object, Object->GetClass()->GetDefaultObject(), Object, All); }
bool SourceDefault(const TSharedPtr<FJsonObject>& Field)
{
    if (Field->GetStringField(TEXT("value")) != Field->GetStringField(TEXT("default"))) return false;
    const TArray<TSharedPtr<FJsonValue>>* Elements = nullptr;
    if (Field->TryGetArrayField(TEXT("elements"), Elements)) for (auto& Element : *Elements)
        if (!SourceDefault(Element->AsObject())) return false;
    return true;
}
bool LegacyTargetEquivalent(UObject* Object,UStruct* Type,const FString& Name,const TSharedPtr<FJsonObject>& Field)
{
#if ENGINE_MAJOR_VERSION == 5 && ENGINE_MINOR_VERSION == 3
    if(auto Camera=Cast<UCameraComponent>(Object);Camera&&Camera->ProjectionMode==ECameraProjectionMode::Perspective&&
        (Name==TEXT("bAutoCalculateOrthoPlanes")||Name==TEXT("bUpdateOrthoPlanes")||Name==TEXT("bUseCameraHeightAsViewTarget"))&&Field->GetStringField(TEXT("value"))==TEXT("False"))return true;
    // 5.3 implements only the legacy PDO shader formula. The later mode selector
    // is absent, but an explicit PDOM_Legacy is exactly that existing behavior.
    return Type==UMaterial::StaticClass()&&Name==TEXT("PixelDepthOffsetMode")&&
        Field->GetStringField(TEXT("type"))==TEXT("TEnumAsByte<EPixelDepthOffsetMode>")&&Field->GetStringField(TEXT("value"))==TEXT("PDOM_Legacy");
#else
    return false;
#endif
}
bool ApplyTo(UStruct* Type, void* Container, UObject* Object, const TSharedPtr<FJsonObject>& Fields, TArray<TSharedPtr<FJsonValue>>& Adaptations, FString& Error)
{
    for (auto& Pair : Fields->Values)
    {
        auto Field = Pair.Value->AsObject(); auto Property = FindFProperty<FProperty>(Type, *Pair.Key);
        const FString Text = Field->GetStringField(TEXT("value"));
        if (!Property || TypeName(Property) != Field->GetStringField(TEXT("type")) || Property->ArrayDim != Field->GetIntegerField(TEXT("arrayDim")))
        {
            if(!Property&&LegacyTargetEquivalent(Object,Type,Pair.Key,Field)){Adaptations.Add(MakeShared<FJsonValueString>(Object->GetPathName()+TEXT(".")+Pair.Key+TEXT(": explicit legacy behavior or disabled orthographic-only option in a perspective camera")));continue;}
            if (SourceDefault(Field))
            { Adaptations.Add(MakeShared<FJsonValueString>(Object->GetPathName() + TEXT(".") + Pair.Key + TEXT(": source-default absent/different in target; target default retained"))); continue; }
            Error = TEXT("Non-default field unavailable in target: ") + Object->GetPathName() + TEXT(".") + Pair.Key + TEXT(" source-type=") + Field->GetStringField(TEXT("type")) + TEXT(" target-type=") + (Property?TypeName(Property):TEXT("absent")); return false;
        }
        const TArray<TSharedPtr<FJsonValue>>* Elements = nullptr;
        if (Field->TryGetArrayField(TEXT("elements"), Elements))
        {
            if (Elements->Num() != Property->ArrayDim) { Error = TEXT("Fixed array length mismatch"); return false; }
            for (int32 Index = 0; Index < Elements->Num(); ++Index)
                if (!Property->ImportText_Direct(*(*Elements)[Index]->AsObject()->GetStringField(TEXT("value")), Property->ContainerPtrToValuePtr<void>(Container, Index), Object, PPF_None))
                { Error = TEXT("Fixed array import failed: ") + Pair.Key; return false; }
            continue;
        }
        const TSharedPtr<FJsonObject>* Members = nullptr;
        if (Field->TryGetObjectField(TEXT("members"), Members))
        {
            auto Struct = CastField<FStructProperty>(Property);
            if (!Struct || !ApplyTo(Struct->Struct, Property->ContainerPtrToValuePtr<void>(Container), Object, *Members, Adaptations, Error)) return false;
            continue;
        }
        const FString Adapted = TargetText(Property, Field);
        if (Adapted != Text) Adaptations.Add(MakeShared<FJsonValueString>(Pair.Key + TEXT(": ") + Text + TEXT(" -> ") + Adapted));
        if (!Property->ImportText_Direct(*Adapted, Property->ContainerPtrToValuePtr<void>(Container), Object, PPF_None))
        { Error = TEXT("Cannot import native property: ") + Pair.Key; return false; }
    }
    return true;
}
bool Apply(UObject* Object, const TSharedPtr<FJsonObject>& Fields, TArray<TSharedPtr<FJsonValue>>& Adaptations, FString& Error)
{ return ApplyTo(Object->GetClass(), Object, Object, Fields, Adaptations, Error); }
bool CompareTo(UStruct* Type, const void* Container, UObject* Object, const TSharedPtr<FJsonObject>& Fields, FString& Error)
{
    for (auto& Pair : Fields->Values)
    {
        auto Field = Pair.Value->AsObject(); auto Property = FindFProperty<FProperty>(Type, *Pair.Key);
        if (!Property || TypeName(Property) != Field->GetStringField(TEXT("type")) || Property->ArrayDim != Field->GetIntegerField(TEXT("arrayDim")))
        {
            if(!Property&&LegacyTargetEquivalent(Object,Type,Pair.Key,Field))continue;
            if (SourceDefault(Field)) continue;
            Error = TEXT("Non-default property/type unavailable: ") + Pair.Key; return false;
        }
        const TArray<TSharedPtr<FJsonValue>>* Elements = nullptr;
        if (Field->TryGetArrayField(TEXT("elements"), Elements))
        {
            if (Elements->Num() != Property->ArrayDim) { Error = TEXT("Fixed array length mismatch"); return false; }
            for (int32 Index = 0; Index < Elements->Num(); ++Index)
                if (Value(Property, Container, Object, Index) != (*Elements)[Index]->AsObject()->GetStringField(TEXT("value")))
                { Error = TEXT("Fixed array value/reference mismatch: ") + Pair.Key; return false; }
            continue;
        }
        const TSharedPtr<FJsonObject>* Members = nullptr;
        if (Field->TryGetObjectField(TEXT("members"), Members))
        {
            auto Struct = CastField<FStructProperty>(Property);
            if (!Struct || !CompareTo(Struct->Struct, Property->ContainerPtrToValuePtr<void>(Container), Object, *Members, Error)) return false;
            continue;
        }
        if (Value(Property, Container, Object) != TargetText(Property, Field))
        { Error = TEXT("Native property/reference mismatch: ") + Object->GetPathName() + TEXT(".") + Pair.Key + TEXT(" expected=") + Field->GetStringField(TEXT("value")) + TEXT(" actual=") + Value(Property, Container, Object); return false; }
    }
    return true;
}
bool Compare(UObject* Object, const TSharedPtr<FJsonObject>& Fields, FString& Error)
{ return CompareTo(Object->GetClass(), Object, Object, Fields, Error); }
UObject* EditorData(UObject* Object)
{
    if (auto Material = Cast<UMaterial>(Object)) return Material->GetEditorOnlyData();
    if (auto Instance = Cast<UMaterialInstanceConstant>(Object)) return Instance->GetEditorOnlyData();
    if (auto Function = Cast<UMaterialFunction>(Object)) return Function->GetEditorOnlyData();
    return nullptr;
}
FMaterialExpressionCollection* ExpressionsOf(UObject* Object)
{
    if (auto Material = Cast<UMaterial>(Object)) return &Material->GetExpressionCollection();
    if (auto Function = Cast<UMaterialFunction>(Object)) return &Function->GetExpressionCollection();
    return nullptr;
}
bool Snapshot(UObject* Object, const TSharedRef<FJsonObject>& Entry, FString& Error)
{
    if (!Object || (Object->GetClass() != UMaterial::StaticClass() && Object->GetClass() != UMaterialInstanceConstant::StaticClass() && Object->GetClass() != UMaterialFunction::StaticClass()))
    { Error = TEXT("Exact Material, MaterialFunction or MaterialInstanceConstant required"); return false; }
    // 5.7 changed FColorMaterialInput constants from FColor to FLinearColor.
    // Do not silently quantize an active constant when writing 5.5/5.3.
    if (auto Data = EditorData(Object)) for (TFieldIterator<FProperty> It(Data->GetClass()); It; ++It)
        if (auto Struct = CastField<FStructProperty>(*It); Struct && Struct->Struct->GetFName() == TEXT("ColorMaterialInput") &&
            Struct->ContainerPtrToValuePtr<FColorMaterialInput>(Data)->UseConstant)
        { Error = TEXT("Active color input constant needs a lossless expression adapter: ") + It->GetName(); return false; }
    Entry->SetStringField(TEXT("path"), Object->GetPathName()); Entry->SetStringField(TEXT("class"), Object->GetClass()->GetPathName());
    Entry->SetObjectField(TEXT("properties"), Properties(Object, false));
    TArray<TSharedPtr<FJsonValue>> Auxiliary;
    if(auto Thumbnail=FindFProperty<FObjectPropertyBase>(Object->GetClass(),TEXT("ThumbnailInfo")))
        if(auto Info=Thumbnail->GetObjectPropertyValue_InContainer(Object);Info&&Info->GetOuter()==Object)
        {
            auto Item=MakeShared<FJsonObject>();Item->SetStringField(TEXT("name"),Info->GetName());Item->SetStringField(TEXT("class"),Info->GetClass()->GetPathName());Item->SetObjectField(TEXT("properties"),Properties(Info,true));Auxiliary.Add(MakeShared<FJsonValueObject>(Item));
        }
    Entry->SetArrayField(TEXT("auxiliary"),Auxiliary);
    if (auto Data = EditorData(Object)) Entry->SetObjectField(TEXT("editorProperties"), Properties(Data, true));
    TArray<TSharedPtr<FJsonValue>> Expressions;
    if (auto CollectionPtr = ExpressionsOf(Object))
    {
        const auto& Collection = *CollectionPtr;
        if (Collection.ExpressionExecBegin)
        { Error = TEXT("Execution-flow material graph needs a dedicated adapter"); return false; }
        TArray<UMaterialExpression*> All;
        for (auto Expression : Collection.Expressions) All.Add(Expression);
        for (auto Comment : Collection.EditorComments) All.Add(Comment);
        for (auto Expression : All)
        {
            if (!Expression || Expression->GetOuter() != Object || !Nodes.Contains(Expression->GetClass()->GetName()) ||
                Expression->GetClass()->GetOutermost()->GetName() != TEXT("/Script/Engine"))
            { Error = TEXT("Unsupported material expression/subgraph"); return false; }
            auto Node = MakeShared<FJsonObject>(); Node->SetStringField(TEXT("name"), Expression->GetName());
            Node->SetStringField(TEXT("class"), Expression->GetClass()->GetPathName());
            Node->SetObjectField(TEXT("properties"), Properties(Expression, true)); Expressions.Add(MakeShared<FJsonValueObject>(Node));
        }
    }
    Entry->SetArrayField(TEXT("nodes"), Expressions); return true;
}
bool Check(UObject* Object, const TSharedPtr<FJsonObject>& Entry, FString& Error)
{
    if (!Object || Object->GetClass()->GetPathName() != Entry->GetStringField(TEXT("class")) || !Compare(Object, Entry->GetObjectField(TEXT("properties")), Error)) return false;
    const TArray<TSharedPtr<FJsonValue>>* Auxiliary=nullptr;
    if(Entry->TryGetArrayField(TEXT("auxiliary"),Auxiliary))for(auto& Item:*Auxiliary)
    {auto Aux=Item->AsObject();auto Info=FindObject<UObject>(Object,*Aux->GetStringField(TEXT("name")));if(!Info||Info->GetClass()->GetPathName()!=Aux->GetStringField(TEXT("class"))||!Compare(Info,Aux->GetObjectField(TEXT("properties")),Error)){Error=TEXT("Thumbnail metadata mismatch: ")+Error;return false;}}
    const TSharedPtr<FJsonObject>* Editor = nullptr;
    if (Entry->TryGetObjectField(TEXT("editorProperties"), Editor) && (!EditorData(Object) || !Compare(EditorData(Object), *Editor, Error))) return false;
    if (auto Collection = ExpressionsOf(Object))
    {
        auto& Expected = Entry->GetArrayField(TEXT("nodes"));
        if (Collection->Expressions.Num() + Collection->EditorComments.Num() != Expected.Num()) { Error = TEXT("Material expression/comment count mismatch"); return false; }
        for (auto& Value : Expected)
        {
            auto Node = Value->AsObject(); auto Expression = FindObject<UMaterialExpression>(Object, *Node->GetStringField(TEXT("name")));
            if (!Expression || Expression->GetClass()->GetPathName() != Node->GetStringField(TEXT("class")) || !Compare(Expression, Node->GetObjectField(TEXT("properties")), Error)) return false;
        }
    }
    return true;
}
}

bool UCyHubNativeMaterialLibrary::Run(const FString& ConfigFile)
{
    auto Config = Read(ConfigFile); if (!Config) return false;
    const FString Operation = Config->GetStringField(TEXT("operation"));
    const FString ManifestPath = Config->GetStringField(TEXT("exchangeRoot")) / TEXT("native-materials.json");
    TArray<TSharedPtr<FJsonValue>> Entries, Imported, Errors, Adaptations;
    auto Fail = [&](const FString& Message) { Errors.Add(MakeShared<FJsonValueString>(Message)); UE_LOG(LogTemp, Error, TEXT("%s"), *Message); };
    if (Operation == TEXT("export"))
    {
        auto Plan = Read(Config->GetStringField(TEXT("planPath")));
        if (!Plan || !Plan->GetBoolField(TEXT("canConvert"))) Fail(TEXT("Blocked native plan"));
        else for (auto& Value : Plan->GetArrayField(TEXT("assets")))
        {
            auto Decision = Value->AsObject(); const FString Path = Decision->GetStringField(TEXT("assetPath"));
            if (!ValidPath(Path) || Decision->GetStringField(TEXT("adapter")) != TEXT("native-material-v1")) { Fail(TEXT("Invalid material plan entry")); break; }
            auto Entry = MakeShared<FJsonObject>(); FString Error;
            if (!Snapshot(LoadObject<UObject>(nullptr, *Path), Entry, Error)) { Fail(Path + TEXT(": ") + Error); break; }
            Entries.Add(MakeShared<FJsonValueObject>(Entry));
        }
        auto Manifest = MakeShared<FJsonObject>(); Manifest->SetNumberField(TEXT("schemaVersion"), 1); Manifest->SetArrayField(TEXT("assets"), Entries);
        if (!Write(ManifestPath, Manifest)) Fail(TEXT("Cannot write native material manifest"));
    }
    else if (Operation == TEXT("import") || Operation == TEXT("verify"))
    {
        auto Manifest = Read(ManifestPath);
        if (!Manifest || Manifest->GetIntegerField(TEXT("schemaVersion")) != 1) Fail(TEXT("Invalid native material manifest"));
        else
        {
            Entries = Manifest->GetArrayField(TEXT("assets"));
            // Allocate every asset and expression first. References resolve against actual target objects.
            if (Operation == TEXT("import")) for (auto& Value : Entries)
            {
                auto Entry = Value->AsObject(); const FString Path = Entry->GetStringField(TEXT("path")); FString PackageName, Name;
                const FString Class = Entry->GetStringField(TEXT("class"));
                if (!ValidPath(Path) || (Class != TEXT("/Script/Engine.Material") && Class != TEXT("/Script/Engine.MaterialInstanceConstant") && Class != TEXT("/Script/Engine.MaterialFunction"))) { Fail(TEXT("Invalid material asset/class")); break; }
                Path.Split(TEXT("."), &PackageName, &Name);
                if (FPackageName::DoesPackageExist(PackageName)) { Fail(TEXT("Refusing to overwrite target package")); break; }
                auto Package = CreatePackage(*PackageName);
                UObject* Object = NewObject<UObject>(Package, LoadClass<UObject>(nullptr, *Class), *Name, RF_Public | RF_Standalone);
                const TArray<TSharedPtr<FJsonValue>>* Auxiliary=nullptr;
                if(Entry->TryGetArrayField(TEXT("auxiliary"),Auxiliary))for(auto& Item:*Auxiliary)
                {
                    auto Aux=Item->AsObject();FString AuxClass=Aux->GetStringField(TEXT("class")),AuxName=Aux->GetStringField(TEXT("name"));
                    if((AuxClass!=TEXT("/Script/UnrealEd.SceneThumbnailInfoWithPrimitive")&&AuxClass!=TEXT("/Script/UnrealEd.SceneThumbnailInfo"))||AuxName.IsEmpty()||AuxName.Contains(TEXT("/"))||AuxName.Contains(TEXT("."))||AuxName.Contains(TEXT(":"))){Fail(TEXT("Unsupported thumbnail metadata object"));break;}
                    auto Loaded=LoadClass<UObject>(nullptr,*AuxClass);if(!Loaded){Fail(TEXT("Thumbnail class unavailable"));break;}NewObject<UObject>(Object,Loaded,*AuxName,RF_Transactional);
                }
                if (auto Collection = ExpressionsOf(Object)) for (auto& NodeValue : Entry->GetArrayField(TEXT("nodes")))
                {
                    auto Node = NodeValue->AsObject(); FString ClassName = Node->GetStringField(TEXT("class"));
                    FString Prefix, ShortClass; ClassName.Split(TEXT("."), &Prefix, &ShortClass);
                    const FString NodeName = Node->GetStringField(TEXT("name"));
                    if (Prefix != TEXT("/Script/Engine") || !Nodes.Contains(ShortClass) || NodeName.IsEmpty() || NodeName.Contains(TEXT("/")) || NodeName.Contains(TEXT(".")) || NodeName.Contains(TEXT(":"))) { Fail(TEXT("Unsupported expression class/name")); break; }
                    auto NodeClass = LoadClass<UMaterialExpression>(nullptr, *ClassName);
                    if (!NodeClass) { Fail(TEXT("Expression class unavailable in target")); break; }
                    auto Expression = NewObject<UMaterialExpression>(Object, NodeClass, *NodeName, RF_Transactional);
                    if (auto Comment = Cast<UMaterialExpressionComment>(Expression)) Collection->AddComment(Comment);
                    else Collection->AddExpression(Expression);
                }
            }
            if (Operation == TEXT("import") && Errors.IsEmpty()) for (auto& Value : Entries)
            {
                auto Entry = Value->AsObject(); auto Object = FindObject<UObject>(nullptr, *Entry->GetStringField(TEXT("path"))); FString Error;
                if (!Object || !Apply(Object, Entry->GetObjectField(TEXT("properties")), Adaptations, Error)) { Fail(Error); break; }
                const TArray<TSharedPtr<FJsonValue>>* Auxiliary=nullptr;
                if(Entry->TryGetArrayField(TEXT("auxiliary"),Auxiliary))for(auto& Item:*Auxiliary)
                {auto Aux=Item->AsObject();auto Info=FindObject<UObject>(Object,*Aux->GetStringField(TEXT("name")));if(!Info||!Apply(Info,Aux->GetObjectField(TEXT("properties")),Adaptations,Error)){Fail(Error);break;}}
                const TSharedPtr<FJsonObject>* Editor = nullptr;
                if (Entry->TryGetObjectField(TEXT("editorProperties"), Editor) && (!EditorData(Object) || !Apply(EditorData(Object), *Editor, Adaptations, Error))) { Fail(Error); break; }
                for (auto& NodeValue : Entry->GetArrayField(TEXT("nodes")))
                {
                    auto Node = NodeValue->AsObject(); auto Expression = FindObject<UMaterialExpression>(Object, *Node->GetStringField(TEXT("name")));
                    if (!Expression || !Apply(Expression, Node->GetObjectField(TEXT("properties")), Adaptations, Error)) { Fail(Error); break; }
                }
            }
            if (Operation == TEXT("import") && Errors.IsEmpty())
            {
                // Native PostEditChange rebuilds target-version shader/cached data, not source-version byte streams.
                for (auto& Value : Entries)
                { auto Object = FindObject<UObject>(nullptr, *Value->AsObject()->GetStringField(TEXT("path"))); Object->PostEditChange(); }
                FAssetCompilingManager::Get().FinishAllCompilation();
                for (auto& Value : Entries)
                {
                    auto Object = FindObject<UObject>(nullptr, *Value->AsObject()->GetStringField(TEXT("path")));
                    if (auto Instance = Cast<UMaterialInstanceConstant>(Object)) { Instance->UpdateStaticPermutation(); Instance->UpdateCachedData(); }
                    auto Package = Object->GetOutermost(); Package->MarkPackageDirty(); FAssetRegistryModule::AssetCreated(Object);
                    const FString Filename = FPackageName::LongPackageNameToFilename(Package->GetName(), FPackageName::GetAssetPackageExtension());
                    IFileManager::Get().MakeDirectory(*FPaths::GetPath(Filename), true); FSavePackageArgs Args; Args.TopLevelFlags = RF_Public | RF_Standalone; Args.SaveFlags = SAVE_NoError;
                    if (!UPackage::SavePackage(Package, Object, *Filename, Args)) { Fail(TEXT("Target native material SavePackage failed")); break; }
                }
            }
            if (Errors.IsEmpty()) for (auto& Value : Entries)
            {
                auto Entry = Value->AsObject(); const FString Path = Entry->GetStringField(TEXT("path")); FString Error;
                if (!Check(LoadObject<UObject>(nullptr, *Path), Entry, Error)) { Fail(Path + TEXT(": ") + Error); break; }
                Imported.Add(MakeShared<FJsonValueString>(Path));
            }
        }
    }
    else Fail(TEXT("Unknown native material operation"));
    if ((Operation == TEXT("export") && Entries.IsEmpty()) || (Operation != TEXT("export") && Imported.IsEmpty())) Fail(TEXT("No native materials processed"));
    auto Report = MakeShared<FJsonObject>(); Report->SetStringField(TEXT("adapter"), TEXT("native-material-v1"));
    Report->SetArrayField(TEXT("exported"), Operation == TEXT("export") ? Entries : TArray<TSharedPtr<FJsonValue>>{});
    Report->SetArrayField(TEXT("imported"), Imported); Report->SetArrayField(TEXT("errors"), Errors); Report->SetArrayField(TEXT("unsupported"), {}); Report->SetArrayField(TEXT("adaptations"), Adaptations);
    return Write(Config->GetStringField(TEXT("reportPath")), Report) && Errors.IsEmpty();
}

namespace CyHubObjectIO
{
TSharedRef<FJsonObject> Properties(UObject* Object, bool All) { return ::Properties(Object, All); }
TSharedRef<FJsonObject> StructProperties(UStruct* Type, const void* Object, const void* Defaults, UObject* Owner) { return ::PropertiesOf(Type, Object, Defaults, Owner, true); }
bool Apply(UObject* Object, const TSharedPtr<FJsonObject>& Fields, TArray<TSharedPtr<FJsonValue>>& Adaptations, FString& Error) { return ::Apply(Object, Fields, Adaptations, Error); }
bool ApplyStruct(UStruct* Type, void* Object, UObject* Owner, const TSharedPtr<FJsonObject>& Fields, TArray<TSharedPtr<FJsonValue>>& Adaptations, FString& Error) { return ::ApplyTo(Type, Object, Owner, Fields, Adaptations, Error); }
bool Compare(UObject* Object, const TSharedPtr<FJsonObject>& Fields, FString& Error) { return ::Compare(Object, Fields, Error); }
bool CompareStruct(UStruct* Type, const void* Object, UObject* Owner, const TSharedPtr<FJsonObject>& Fields, FString& Error) { return ::CompareTo(Type, Object, Owner, Fields, Error); }
}
