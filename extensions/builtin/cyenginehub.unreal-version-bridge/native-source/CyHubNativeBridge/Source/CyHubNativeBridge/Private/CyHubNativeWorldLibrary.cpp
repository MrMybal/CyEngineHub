#include "CyHubNativeWorldLibrary.h"
#include "CyHubNativeObjectIO.h"
#include "Editor.h"
#include "FileHelpers.h"
#include "Engine/World.h"
#include "Engine/Level.h"
#include "Engine/LevelScriptBlueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Engine/LevelScriptActor.h"
#include "GameFramework/WorldSettings.h"
#include "Engine/Brush.h"
#include "Engine/Polys.h"
#include "Model.h"
#include "BSPOps.h"
#include "Components/BrushComponent.h"
#include "Components/MeshComponent.h"
#include "Materials/MaterialInterface.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "GameFramework/PhysicsVolume.h"
#include "Components/ActorComponent.h"
#include "Components/HierarchicalInstancedStaticMeshComponent.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraphUtilities.h"
#include "EdGraphSchema_K2.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Factories/LevelFactory.h"
#include "Exporters/Exporter.h"
#include "UnrealExporter.h"
#include "Misc/OutputDevice.h"
#if __has_include("Misc/StringOutputDevice.h")
#include "Misc/StringOutputDevice.h"
#endif
#include "InstancedFoliageActor.h"
#include "FoliageType_InstancedStaticMesh.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Misc/FileHelper.h"
#include "Misc/PackageName.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/MemoryWriter.h"
#include "Serialization/MemoryReader.h"
#include "UObject/SavePackage.h"
#include "UObject/Package.h"
#include "UObject/UObjectHash.h"
#include "UObject/UnrealType.h"
#include "HAL/FileManager.h"
#include "Internationalization/TextPackageNamespaceUtil.h"

namespace
{
bool PersistentActor(AActor* Actor)
{
    // Editor-created debug/navigation helpers are not map payload and must be recreated by the target.
    return Actor&&!Actor->HasAnyFlags(RF_Transient)&&!Actor->GetClass()->HasAnyClassFlags(CLASS_Transient)&&!Actor->IsA<ALevelScriptActor>();
}
bool AuxiliaryClass(const FString& Path)
{return Path==TEXT("/Script/Engine.NavigationSystemConfig")||Path==TEXT("/Script/NavigationSystem.NavigationSystemModuleConfig")||Path==TEXT("/Script/UnrealEd.CubeBuilder");}
TSharedRef<FJsonObject> DynamicParameters(UMaterialInstanceDynamic* Material)
{
    auto All=CyHubObjectIO::Properties(Material,true),Result=MakeShared<FJsonObject>();
    for(auto Key:{TEXT("Parent"),TEXT("ScalarParameterValues"),TEXT("VectorParameterValues"),TEXT("TextureParameterValues"),TEXT("FontParameterValues"),TEXT("RuntimeVirtualTextureParameterValues"),TEXT("SparseVolumeTextureParameterValues"),TEXT("DoubleVectorParameterValues"),TEXT("BasePropertyOverrides")})
        if(All->HasField(Key))Result->SetField(Key,All->Values.FindChecked(Key));
    return Result;
}
TSharedPtr<FJsonObject> RemapDynamicReferences(const TSharedPtr<FJsonObject>& Fields,const TMap<FString,FString>& Aliases)
{
    if(Aliases.IsEmpty())return Fields;
    FString Text;FJsonSerializer::Serialize(Fields.ToSharedRef(),TJsonWriterFactory<>::Create(&Text));
    // Match the complete quoted UObject path, not an arbitrary substring or a persistent asset reference.
    for(auto Pair:Aliases)Text.ReplaceInline(*(Pair.Key+TEXT("'")),*(Pair.Value+TEXT("'")),ESearchCase::CaseSensitive);
    TSharedPtr<FJsonObject> Result;FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Result);return Result;
}
bool MaterialBindings(UMeshComponent* Component,TSharedRef<FJsonObject> Entry,bool Import,bool Verify,TArray<TSharedPtr<FJsonValue>>& Adaptations,FString& Error)
{
    if(!Import&&!Verify)
    {
        TArray<TSharedPtr<FJsonValue>> Bindings;
        for(int32 Slot=0;Slot<Component->GetNumMaterials();++Slot)
        {
            auto Item=MakeShared<FJsonObject>();auto Material=Component->GetMaterial(Slot);
            Item->SetStringField(TEXT("path"),Material?Material->GetPathName():TEXT(""));
            if(auto Dynamic=Cast<UMaterialInstanceDynamic>(Material))Item->SetObjectField(TEXT("dynamicParameters"),DynamicParameters(Dynamic));
            Bindings.Add(MakeShared<FJsonValueObject>(Item));
        }
        Entry->SetArrayField(TEXT("materialBindings"),Bindings);return true;
    }
    auto& Bindings=Entry->GetArrayField(TEXT("materialBindings"));
    if(Component->GetNumMaterials()!=Bindings.Num()){Error=TEXT("World material slot count mismatch: ")+Component->GetPathName();return false;}
    for(int32 Slot=0;Slot<Bindings.Num();++Slot)
    {
        auto Item=Bindings[Slot]->AsObject();const TSharedPtr<FJsonObject>* Parameters=nullptr;
        if(Item->TryGetObjectField(TEXT("dynamicParameters"),Parameters))
        {
            auto Dynamic=Cast<UMaterialInstanceDynamic>(Component->GetMaterial(Slot));
            if(Import)
            {
                // Dynamic materials are regenerated transient objects. Their names differ across engine versions.
                // Bind/compare parent + parameter values rather than treating a generated object name as an asset path.
                if(!Dynamic){auto ParentField=(*Parameters)->GetObjectField(TEXT("Parent"));FString ParentText=ParentField->GetStringField(TEXT("value"));FString Class,ParentPath;FPackageName::ParseExportTextPath(ParentText,&Class,&ParentPath);auto Parent=LoadObject<UMaterialInterface>(nullptr,*ParentPath);if(Parent)Dynamic=UMaterialInstanceDynamic::Create(Parent,Component);}
                if(!Dynamic||!CyHubObjectIO::Apply(Dynamic,*Parameters,Adaptations,Error)){Error=TEXT("Cannot reconstruct dynamic material: ")+Error;return false;}
                Dynamic->PostEditChange();Component->SetMaterial(Slot,Dynamic);
            }
            if(!Dynamic||!CyHubObjectIO::Compare(Dynamic,*Parameters,Error)){Error=TEXT("Dynamic material binding/parameters: ")+Error;return false;}
        }
        else
        {
            auto Path=Item->GetStringField(TEXT("path"));if(Import){auto Material=Path.IsEmpty()?nullptr:LoadObject<UMaterialInterface>(nullptr,*Path);if(!Path.IsEmpty()&&!Material){Error=TEXT("World material asset missing: ")+Path;return false;}Component->SetMaterial(Slot,Material);}
            auto Actual=Component->GetMaterial(Slot);if((Actual?Actual->GetPathName():TEXT(""))!=Path){Error=TEXT("World material binding mismatch: ")+Component->GetPathName();return false;}
        }
    }
    return true;
}
bool BrushBytes(ABrush* Brush,FArchive& Ar)
{
    if(!Brush->Brush||!Brush->Brush->Polys)return false;
    auto& Polys=Brush->Brush->Polys->Element;int32 Count=Polys.Num();Ar<<Count;if(Count<0||Count>1000000)return false;
    if(Ar.IsLoading())Polys.SetNum(Count);
    for(auto& Poly:Polys)
    {
        int32 Vertices=Poly.Vertices.Num();Ar<<Vertices;if(Vertices<0||Vertices>65536)return false;if(Ar.IsLoading())Poly.Vertices.SetNum(Vertices);
        for(auto& Vertex:Poly.Vertices)Ar<<Vertex;
        // BSP/surface link indices are rebuilt by the target validator; they are not polygon geometry.
        Ar<<Poly.Base<<Poly.Normal<<Poly.TextureU<<Poly.TextureV<<Poly.PolyFlags<<Poly.SmoothingMask<<Poly.LightMapScale;
        FString Material=Poly.Material?Poly.Material->GetPathName():TEXT(""),Item=Poly.ItemName.ToString(),Rules=Poly.RulesetVariation.ToString();Ar<<Material<<Item<<Rules;
        if(Ar.IsLoading()){Poly.Material=Material.IsEmpty()?nullptr:LoadObject<UMaterialInterface>(nullptr,*Material);if(!Material.IsEmpty()&&!Poly.Material)return false;Poly.ItemName=FName(*Item);Poly.RulesetVariation=FName(*Rules);Poly.Actor=Brush;}
    }
    return !Ar.IsError();
}
bool BrushGeometry(ABrush* Brush,const FString& Root,TSharedRef<FJsonObject> Entry,bool Import,bool Verify,TArray<TSharedPtr<FJsonValue>>& Adaptations,FString& Error)
{
    if(!Import&&!Verify)
    {
        if(!Brush->Brush||!Brush->Brush->Polys)return true;
        TArray<uint8> Bytes;FMemoryWriter Writer(Bytes);if(!BrushBytes(Brush,Writer)){Error=TEXT("Brush geometry export failed");return false;}
        FString File=TEXT("brush-")+FGuid::NewGuid().ToString(EGuidFormats::Digits)+TEXT(".bin");if(!FFileHelper::SaveArrayToFile(Bytes,*(Root/File)))return false;Entry->SetStringField(TEXT("brushPayload"),File);
        TArray<TSharedPtr<FJsonValue>> Settings;FLightmassPrimitiveSettings Defaults;
        for(auto& Poly:Brush->Brush->Polys->Element)Settings.Add(MakeShared<FJsonValueObject>(CyHubObjectIO::StructProperties(FLightmassPrimitiveSettings::StaticStruct(),&Poly.LightmassSettings,&Defaults,Brush)));
        Entry->SetArrayField(TEXT("brushLightmass"),Settings);return true;
    }
    if(!Entry->HasField(TEXT("brushPayload")))return !Brush->Brush||!Brush->Brush->Polys||Brush->Brush->Polys->Element.IsEmpty();
    FString File=Entry->GetStringField(TEXT("brushPayload"));TArray<uint8> Expected;
    if(File!=FPaths::GetCleanFilename(File)||File.Contains(TEXT(".."))||!FFileHelper::LoadFileToArray(Expected,*(Root/File))||Expected.Num()>256*1024*1024){Error=TEXT("Invalid brush payload");return false;}
    if(Import)
    {
        if(!Brush->Brush){Brush->Brush=NewObject<UModel>(Brush,TEXT("Brush"),RF_Transactional);Brush->Brush->Initialize(Brush,true);}
        if(!Brush->Brush->Polys)Brush->Brush->Polys=NewObject<UPolys>(Brush->Brush,NAME_None,RF_Transactional);
        FMemoryReader Reader(Expected);if(!BrushBytes(Brush,Reader)||Reader.Tell()!=Expected.Num()){Error=TEXT("Brush geometry import failed");return false;}
        Brush->GetBrushComponent()->Brush=Brush->Brush;
        Brush->Brush->BuildBound();if(!Brush->IsStaticBrush())FBSPOps::RebuildBrush(Brush->Brush);FBSPOps::bspValidateBrush(Brush->Brush,true,false);
        Adaptations.Add(MakeShared<FJsonValueString>(Brush->GetPathName()+TEXT(": target BSP/surface link indices rebuilt; polygon geometry/materials checked independently")));
    }
    TArray<uint8> Actual;FMemoryWriter Writer(Actual);if(!BrushBytes(Brush,Writer)||Actual!=Expected)
    {int32 First=0;while(First<FMath::Min(Actual.Num(),Expected.Num())&&Actual[First]==Expected[First])++First;FFileHelper::SaveArrayToFile(Actual,*(Root/(TEXT("target-")+File)));Error=FString::Printf(TEXT("Brush polygons/materials mismatch: %s byte %d (%d / %d bytes)"),*Brush->GetPathName(),First,Expected.Num(),Actual.Num());return false;}
    auto& Settings=Entry->GetArrayField(TEXT("brushLightmass"));if(Settings.Num()!=Brush->Brush->Polys->Element.Num())return false;
    for(int32 I=0;I<Settings.Num();++I){auto& Fields=Brush->Brush->Polys->Element[I].LightmassSettings;if((Import&&!CyHubObjectIO::ApplyStruct(FLightmassPrimitiveSettings::StaticStruct(),&Fields,Brush,Settings[I]->AsObject(),Adaptations,Error))||!CyHubObjectIO::CompareStruct(FLightmassPrimitiveSettings::StaticStruct(),&Fields,Brush,Settings[I]->AsObject(),Error))return false;}
    return true;
}
class FOrderedWorldExportContext final : public FExportObjectInnerContext
{
public:
    FOrderedWorldExportContext(UWorld* World,const TArray<UObject*>& Ignore):FExportObjectInnerContext(Ignore)
    {
        auto& Inners=ObjectToInnerMap.FindOrAdd(World->PersistentLevel);Inners.Reset();
        // ULevelFactory requires WorldSettings at actor index zero. Object iterator order is not level order.
        for(auto Actor:World->PersistentLevel->Actors)if(PersistentActor(Actor))Inners.Add(Actor);
    }
};
TSharedPtr<FJsonObject> Read(const FString& File)
{ FString Text;TSharedPtr<FJsonObject> Json;return FFileHelper::LoadFileToString(Text,*File)&&FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Json)?Json:nullptr; }
bool Write(const FString& File,TSharedRef<FJsonObject> Json)
{FString Text;FJsonSerializer::Serialize(Json,TJsonWriterFactory<>::Create(&Text));return FFileHelper::SaveStringToFile(Text,*File,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);}
bool Valid(const FString& Path)
{FString P,N;return Path.Split(TEXT("."),&P,&N)&&P.StartsWith(TEXT("/Game/"))&&FPackageName::IsValidLongPackageName(P)&&(N==FPackageName::GetShortName(P)||N==FPackageName::GetShortName(P)+TEXT("_C"));}
TSharedRef<FJsonObject> Editable(UObject* Object)
{
    auto P=CyHubObjectIO::Properties(Object,false);
    // These are editor/build caches; source-version lightmaps are not reusable by the target renderer.
    for(auto Key:{TEXT("MapBuildData"),TEXT("MapBuildDataId"),TEXT("LightingGuid"),TEXT("ActorGuid"),TEXT("ActorInstanceGuid")})P->RemoveField(Key);
    if(Object->GetClass()->GetPathName()==TEXT("/Script/CinematicCamera.CineCameraComponent"))
        // Lens/filmback/focal-length/focus settings are transported; these are recalculated outputs.
        for(auto Key:{TEXT("CurrentHorizontalFOV"),TEXT("CurrentVerticalFOV"),TEXT("FieldOfView"),TEXT("CurrentFocusDistance")})P->RemoveField(Key);
    return P;
}
TArray<TSharedPtr<FJsonValue>> Graphs(ULevelScriptBlueprint* Blueprint)
{
    TArray<TSharedPtr<FJsonValue>> Result;if(!Blueprint)return Result;
    TArray<UEdGraph*> All;Blueprint->GetAllGraphs(All);
    for(auto Graph:All)
    {
        auto Entry=MakeShared<FJsonObject>();Entry->SetStringField(TEXT("name"),Graph->GetName());
        Entry->SetStringField(TEXT("kind"),Blueprint->UbergraphPages.Contains(Graph)?TEXT("event"):Blueprint->FunctionGraphs.Contains(Graph)?TEXT("function"):Blueprint->MacroGraphs.Contains(Graph)?TEXT("macro"):TEXT("nested"));
        TSet<UObject*> Nodes;for(auto Node:Graph->Nodes)Nodes.Add(Node);FString Text;FEdGraphUtilities::ExportNodesToText(Nodes,Text);Entry->SetStringField(TEXT("nodes"),Text);
        TArray<TSharedPtr<FJsonValue>> Pins;
        for(auto Node:Graph->Nodes)
        {
            auto Item=MakeShared<FJsonObject>();Item->SetStringField(TEXT("guid"),Node->NodeGuid.ToString());Item->SetStringField(TEXT("class"),Node->GetClass()->GetPathName());
            TArray<TSharedPtr<FJsonValue>> NodePins;
            for(auto Pin:Node->Pins)
            {
                auto P=MakeShared<FJsonObject>();P->SetStringField(TEXT("id"),Pin->PinId.ToString());P->SetStringField(TEXT("name"),Pin->PinName.ToString());P->SetStringField(TEXT("value"),Pin->DefaultValue);P->SetStringField(TEXT("object"),Pin->DefaultObject?Pin->DefaultObject->GetPathName():TEXT(""));
                P->SetNumberField(TEXT("direction"),int32(Pin->Direction));P->SetStringField(TEXT("category"),Pin->PinType.PinCategory.ToString());P->SetStringField(TEXT("subcategory"),Pin->PinType.PinSubCategory.ToString());
                P->SetStringField(TEXT("typeObject"),Pin->PinType.PinSubCategoryObject.IsValid()?Pin->PinType.PinSubCategoryObject->GetPathName():TEXT(""));P->SetNumberField(TEXT("container"),int32(Pin->PinType.ContainerType));P->SetBoolField(TEXT("reference"),Pin->PinType.bIsReference);P->SetBoolField(TEXT("const"),Pin->PinType.bIsConst);
                TArray<TSharedPtr<FJsonValue>> Links;for(auto Link:Pin->LinkedTo)Links.Add(MakeShared<FJsonValueString>(Link->GetOwningNode()->NodeGuid.ToString()+TEXT("/")+Link->PinId.ToString()));P->SetArrayField(TEXT("links"),Links);NodePins.Add(MakeShared<FJsonValueObject>(P));
            }
            Item->SetArrayField(TEXT("pins"),NodePins);Pins.Add(MakeShared<FJsonValueObject>(Item));
        }
        Entry->SetArrayField(TEXT("pinSnapshot"),Pins);Result.Add(MakeShared<FJsonValueObject>(Entry));
    }
    return Result;
}
TSharedRef<FJsonObject> BlueprintProperties(ULevelScriptBlueprint* Blueprint)
{
    auto All=CyHubObjectIO::Properties(Blueprint,true);auto Result=MakeShared<FJsonObject>();
    for(auto Key:{TEXT("ParentClass"),TEXT("NewVariables"),TEXT("ImplementedInterfaces"),TEXT("BlueprintCategory"),TEXT("BlueprintNamespace"),TEXT("bGenerateConstClass"),TEXT("bGenerateAbstractClass")})
        if(All->HasField(Key))Result->SetField(Key,All->Values[Key]);
    return Result;
}
TSharedRef<FJsonObject> BlueprintDefaults(ULevelScriptBlueprint* Blueprint)
{
    auto Result=CyHubObjectIO::Properties(Blueprint->GeneratedClass->GetDefaultObject(),true);TArray<FString> Remove;
    for(auto& Pair:Result->Values){auto P=FindFProperty<FProperty>(Blueprint->GeneratedClass,*Pair.Key);if(!P||P->GetOwnerStruct()!=Blueprint->GeneratedClass)Remove.Add(Pair.Key);}
    for(auto& Key:Remove)Result->RemoveField(Key);return Result;
}
bool ImportGraphs(ULevelScriptBlueprint* Blueprint,const TArray<TSharedPtr<FJsonValue>>& Entries,FString& Error)
{
    if(!Blueprint && Entries.Num()) {Error=TEXT("Level script blueprint unavailable");return false;}
    for(auto Value:Entries)
    {
        auto E=Value->AsObject();FString Name=E->GetStringField(TEXT("name")),Kind=E->GetStringField(TEXT("kind"));
        if(Kind==TEXT("nested")){Error=TEXT("Nested blueprint graph needs explicit parent graph reconstruction");return false;}
        UEdGraph* Graph=FindObject<UEdGraph>(Blueprint,*Name);
        if(!Graph)
        {
            Graph=FBlueprintEditorUtils::CreateNewGraph(Blueprint,FName(*Name),UEdGraph::StaticClass(),UEdGraphSchema_K2::StaticClass());
            if(Kind==TEXT("event"))FBlueprintEditorUtils::AddUbergraphPage(Blueprint,Graph);
            else if(Kind==TEXT("function"))FBlueprintEditorUtils::AddFunctionGraph(Blueprint,Graph,false,static_cast<UClass*>(nullptr));
            else if(Kind==TEXT("macro"))FBlueprintEditorUtils::AddMacroGraph(Blueprint,Graph,false,static_cast<UClass*>(nullptr));
            else {Error=TEXT("Unknown blueprint graph kind");return false;}
        }
        auto Old=Graph->Nodes;for(auto Node:Old)Node->DestroyNode();
        TSet<UEdGraphNode*> Imported;FEdGraphUtilities::ImportNodesFromText(Graph,E->GetStringField(TEXT("nodes")),Imported);
        if(Imported.Num()!=E->GetArrayField(TEXT("pinSnapshot")).Num()){Error=TEXT("Blueprint graph lost nodes");return false;}
    }
    FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);FKismetEditorUtilities::CompileBlueprint(Blueprint);
    if(Blueprint->Status==BS_Error){Error=TEXT("Target level blueprint compilation failed");return false;}return true;
}
bool CheckGraphs(ULevelScriptBlueprint* Blueprint,const TArray<TSharedPtr<FJsonValue>>& Expected,FString& Error)
{
    auto Actual=Graphs(Blueprint);if(Actual.Num()!=Expected.Num()){Error=TEXT("Blueprint graph count mismatch");return false;}
    for(auto E:Expected)
    {
        auto Match=Actual.FindByPredicate([&](auto A){return A->AsObject()->GetStringField(TEXT("name"))==E->AsObject()->GetStringField(TEXT("name"));});
        if(!Match){Error=TEXT("Blueprint graph missing");return false;}
        // Match stable node/pin GUIDs rather than exporter ordering or generated bytecode.
        auto& Left=E->AsObject()->GetArrayField(TEXT("pinSnapshot"));auto& Right=(*Match)->AsObject()->GetArrayField(TEXT("pinSnapshot"));
        if(Left.Num()!=Right.Num()){Error=TEXT("Blueprint node count mismatch");return false;}
        for(auto L:Left)
        {
            auto R=Right.FindByPredicate([&](auto V){return V->AsObject()->GetStringField(TEXT("guid"))==L->AsObject()->GetStringField(TEXT("guid"));});
            if(!R){Error=TEXT("Blueprint node identity mismatch");return false;}
            FString A,B;FJsonSerializer::Serialize(L->AsObject().ToSharedRef(),TJsonWriterFactory<>::Create(&A));FJsonSerializer::Serialize((*R)->AsObject().ToSharedRef(),TJsonWriterFactory<>::Create(&B));
            if(A!=B){Error=TEXT("Blueprint pin/default/reference/link mismatch");return false;}
        }
    }
    return true;
}
bool Foliage(AInstancedFoliageActor* Actor,const FString& Root,TSharedRef<FJsonObject> Entry,bool Import,bool Verify,TArray<TSharedPtr<FJsonValue>>& Adaptations,FString& Error)
{
    if(!Import&&!Verify)
    {
        TArray<TSharedPtr<FJsonValue>> Types;
        Actor->ForEachFoliageInfo([&](UFoliageType* Type,FFoliageInfo& Info)
        {
            auto Item=MakeShared<FJsonObject>();Item->SetStringField(TEXT("class"),Type->GetClass()->GetPathName());Item->SetStringField(TEXT("name"),Type->GetName());Item->SetObjectField(TEXT("properties"),Editable(Type));
            if(auto Component=Info.GetComponent())Item->SetStringField(TEXT("component"),Component->GetName());
            TArray<uint8> Bytes;FMemoryWriter Ar(Bytes);int32 Count=Info.Instances.Num();Ar<<Count;
            for(auto Instance:Info.Instances)
            {
                Ar<<Instance.Location<<Instance.Rotation<<Instance.PreAlignRotation<<Instance.DrawScale3D<<Instance.Flags<<Instance.ProceduralGuid;
                auto Base=Actor->InstanceBaseCache.GetInstanceBasePtr(Instance.BaseId).Get();FString Path=Base?Base->GetPathName():TEXT("");Ar<<Path;
            }
            FString File=TEXT("foliage-")+FGuid::NewGuid().ToString(EGuidFormats::Digits)+TEXT(".bin");Item->SetStringField(TEXT("payload"),File);FFileHelper::SaveArrayToFile(Bytes,*(Root/File));Types.Add(MakeShared<FJsonValueObject>(Item));return true;
        });Entry->SetArrayField(TEXT("foliage"),Types);return true;
    }
    auto& Types=Entry->GetArrayField(TEXT("foliage"));
    if(Import){TArray<UFoliageType*> Previous;Actor->ForEachFoliageInfo([&](UFoliageType* Type,FFoliageInfo& Info){Previous.Add(Type);return true;});if(Previous.Num())Actor->RemoveFoliageType(Previous.GetData(),Previous.Num());}
    if(Verify && Actor->GetFoliageInfos().Num()!=Types.Num()){Error=TEXT("Foliage type count mismatch");return false;}
    for(auto V:Types)
    {
        auto T=V->AsObject();FString Name=T->GetStringField(TEXT("name"));
        UFoliageType* Type=nullptr;FFoliageInfo* Info=nullptr;
        if(Import)
        {
            auto Class=LoadClass<UFoliageType>(nullptr,*T->GetStringField(TEXT("class")));
            if(Class!=UFoliageType_InstancedStaticMesh::StaticClass()){Error=TEXT("Actor foliage type needs its own adapter");return false;}
            Type=NewObject<UFoliageType>(Actor,Class,*Name,RF_Transactional);if(!CyHubObjectIO::Apply(Type,T->GetObjectField(TEXT("properties")),Adaptations,Error))return false;
            Actor->AddFoliageInfo(Type);Info=Actor->FindInfo(Type);
        }
        else {Actor->ForEachFoliageInfo([&](UFoliageType* Candidate,FFoliageInfo& CandidateInfo){if(Candidate->GetName()==Name){Type=Candidate;Info=&CandidateInfo;}return true;});}
        if(!Type||!Info||!CyHubObjectIO::Compare(Type,T->GetObjectField(TEXT("properties")),Error))return false;
        FString File=T->GetStringField(TEXT("payload"));TArray<uint8> Bytes;if(File!=FPaths::GetCleanFilename(File)||File.Contains(TEXT(".."))||!FFileHelper::LoadFileToArray(Bytes,*(Root/File)))return false;
        FMemoryReader Ar(Bytes);int32 Count;Ar<<Count;if(Count<0||Count>5000000||(!Import&&Info->Instances.Num()!=Count)){Error=TEXT("Foliage instance count mismatch");return false;}
        for(int32 I=0;I<Count;++I)
        {
            FFoliageInstance Instance;FString Base;Ar<<Instance.Location<<Instance.Rotation<<Instance.PreAlignRotation<<Instance.DrawScale3D<<Instance.Flags<<Instance.ProceduralGuid<<Base;
            auto Component=Base.IsEmpty()?nullptr:FindObject<UActorComponent>(nullptr,*Base);if(!Base.IsEmpty()&&!Component){Error=TEXT("Foliage base reference missing");return false;}
            if(Import)Info->AddInstance(Type,Instance,Component);
            else {auto Actual=Info->Instances[I];if(Actual.Location!=Instance.Location||Actual.Rotation!=Instance.Rotation||Actual.PreAlignRotation!=Instance.PreAlignRotation||Actual.DrawScale3D!=Instance.DrawScale3D||Actual.Flags!=Instance.Flags){Error=TEXT("Foliage instance transform/settings mismatch");return false;}}
        }
        if(Ar.IsError()||Ar.Tell()!=Bytes.Num()){Error=TEXT("Invalid foliage data");return false;}
        if(Import&&T->HasField(TEXT("component"))&&Info->GetComponent())Info->GetComponent()->Rename(*T->GetStringField(TEXT("component")),Actor,REN_DontCreateRedirectors|REN_NonTransactional);
    }return true;
}
}

bool UCyHubNativeWorldLibrary::Run(const FString& ConfigFile)
{
    auto Config=Read(ConfigFile);if(!Config)return false;const FString Root=Config->GetStringField(TEXT("exchangeRoot")),Op=Config->GetStringField(TEXT("operation"));
    TArray<TSharedPtr<FJsonValue>> Entries,Imported,Errors,Adaptations;auto Fail=[&](FString E){Errors.Add(MakeShared<FJsonValueString>(E));UE_LOG(LogTemp,Error,TEXT("%s"),*E);};
    if(Op==TEXT("export"))
    {
        auto Plan=Read(Config->GetStringField(TEXT("planPath")));if(!Plan||!Plan->GetBoolField(TEXT("canConvert")))Fail(TEXT("Blocked world plan"));
        else for(auto V:Plan->GetArrayField(TEXT("assets")))
        {
            FString Path=V->AsObject()->GetStringField(TEXT("assetPath")),Error;if(!Valid(Path)){Fail(TEXT("Invalid world path"));break;}
            auto Entry=MakeShared<FJsonObject>();Entry->SetStringField(TEXT("path"),Path);
            auto AssetType=V->AsObject()->GetStringField(TEXT("assetType"));
            if(AssetType==TEXT("BlueprintGeneratedClass")){Entry->SetBoolField(TEXT("generatedClass"),true);Entries.Add(MakeShared<FJsonValueObject>(Entry));continue;}
            if(AssetType!=TEXT("World")){Fail(TEXT("World adapter received an unsupported asset class"));break;}
            auto World=UEditorLoadingAndSavingUtils::LoadMap(Path.Left(Path.Find(TEXT("."))));
            if(!World||World->GetWorldPartition()||World->GetStreamingLevels().Num()){Fail(TEXT("World Partition/streaming requires a dedicated multi-level adapter"));break;}
            Entry->SetStringField(TEXT("localizationNamespace"),TextNamespaceUtil::GetPackageNamespace(World));
            // Level script actors are regenerated from their Blueprint, not pasted as an unresolved source class.
            TArray<UObject*> Ignore;for(auto Actor:World->PersistentLevel->Actors)if(Actor&&!PersistentActor(Actor))Ignore.Add(Actor);
            FOrderedWorldExportContext Context(World,Ignore);FStringOutputDevice Text;
            FString File=TEXT("world-")+FGuid::NewGuid().ToString(EGuidFormats::Digits)+TEXT(".t3d");
            if(!UExporter::ExportToOutputDevice(&Context,World,nullptr,Text,TEXT("t3d"),0)||!FFileHelper::SaveStringToFile(Text,*(Root/File))){Fail(TEXT("Native world text export failed"));break;}Entry->SetStringField(TEXT("payload"),File);
            TArray<TSharedPtr<FJsonValue>> Actors;
            for(auto Actor:World->PersistentLevel->Actors)
            {
                if(!PersistentActor(Actor)||Actor==World->GetDefaultPhysicsVolume()||(!Actor->ShouldExport()&&!Actor->IsA<AInstancedFoliageActor>()))continue;
                auto A=MakeShared<FJsonObject>();A->SetStringField(TEXT("name"),Actor->GetName());A->SetStringField(TEXT("class"),Actor->GetClass()->GetPathName());A->SetStringField(TEXT("transform"),Actor->GetActorTransform().ToString());A->SetObjectField(TEXT("properties"),Editable(Actor));
                TArray<TSharedPtr<FJsonValue>> Components;TInlineComponentArray<UActorComponent*> All(Actor);
                for(auto Component:All)
                {
                    // Editor visualization components are regenerated and not saved in the source map either.
                    if(Component->HasAnyFlags(RF_Transient))continue;
                    auto C=MakeShared<FJsonObject>();C->SetStringField(TEXT("name"),Component->GetName());C->SetStringField(TEXT("class"),Component->GetClass()->GetPathName());auto Fields=Editable(Component);
                    if(auto Mesh=Cast<UMeshComponent>(Component)){Fields->RemoveField(TEXT("OverrideMaterials"));if(!MaterialBindings(Mesh,C,false,false,Adaptations,Error)){Fail(Error);break;}}
                    C->SetObjectField(TEXT("properties"),Fields);Components.Add(MakeShared<FJsonValueObject>(C));
                }
                if(!Errors.IsEmpty())break;
                A->SetArrayField(TEXT("components"),Components);
                TArray<TSharedPtr<FJsonValue>> Auxiliary;
                if(Actor->IsA<AWorldSettings>()||Actor->IsA<ABrush>())
                {
                    TArray<UObject*> Owned;GetObjectsWithOuter(Actor,Owned,false);
                    for(auto Object:Owned)if(AuxiliaryClass(Object->GetClass()->GetPathName()))
                    {auto Item=MakeShared<FJsonObject>();Item->SetStringField(TEXT("name"),Object->GetName());Item->SetStringField(TEXT("class"),Object->GetClass()->GetPathName());Item->SetObjectField(TEXT("properties"),CyHubObjectIO::Properties(Object,true));Auxiliary.Add(MakeShared<FJsonValueObject>(Item));}
                }
                A->SetArrayField(TEXT("auxiliary"),Auxiliary);
                if(auto Brush=Cast<ABrush>(Actor))if(!BrushGeometry(Brush,Root,A,false,false,Adaptations,Error)){Fail(Error);break;}
                if(auto IFA=Cast<AInstancedFoliageActor>(Actor))if(!Foliage(IFA,Root,A,false,false,Adaptations,Error)){Fail(Error);break;}Actors.Add(MakeShared<FJsonValueObject>(A));
            }
            Entry->SetArrayField(TEXT("actors"),Actors);auto Blueprint=World->PersistentLevel->GetLevelScriptBlueprint(true);Entry->SetArrayField(TEXT("graphs"),Graphs(Blueprint));
            if(Blueprint){Entry->SetObjectField(TEXT("blueprintProperties"),BlueprintProperties(Blueprint));if(Blueprint->GeneratedClass)Entry->SetObjectField(TEXT("blueprintDefaults"),BlueprintDefaults(Blueprint));}
            Entries.Add(MakeShared<FJsonValueObject>(Entry));
        }
        auto Manifest=MakeShared<FJsonObject>();Manifest->SetNumberField(TEXT("schemaVersion"),2);Manifest->SetArrayField(TEXT("assets"),Entries);if(!Write(Root/TEXT("native-worlds.json"),Manifest))Fail(TEXT("Cannot write world manifest"));
    }
    else if(Op==TEXT("import")||Op==TEXT("verify"))
    {
        auto Manifest=Read(Root/TEXT("native-worlds.json"));if(!Manifest||Manifest->GetIntegerField(TEXT("schemaVersion"))!=2)Fail(TEXT("Invalid world manifest"));
        else
        {
            auto Ordered=Manifest->GetArrayField(TEXT("assets"));Ordered.StableSort([](auto A,auto B){return !A->AsObject()->HasField(TEXT("generatedClass"))&&B->AsObject()->HasField(TEXT("generatedClass"));});
            for(auto V:Ordered)
            {
                auto E=V->AsObject();FString Path=E->GetStringField(TEXT("path")),Error;if(!Valid(Path)){Fail(TEXT("Invalid world asset path"));break;}
                if(E->HasField(TEXT("generatedClass"))){auto Class=LoadObject<UClass>(nullptr,*Path);if(!Class||!Class->IsChildOf(ALevelScriptActor::StaticClass())){Fail(TEXT("Level script class missing"));break;}Imported.Add(MakeShared<FJsonValueString>(Path));continue;}
                FString Pkg,Name;Path.Split(TEXT("."),&Pkg,&Name);UWorld* World=nullptr;
                if(Op==TEXT("import"))
                {
                    if(FPackageName::DoesPackageExist(Pkg)){Fail(TEXT("Refusing world overwrite"));break;}
                    // Let the editor own map contexts and clean up the preceding world/subsystems.
                    World=UEditorLoadingAndSavingUtils::NewBlankMap(false);
                    if(!World||!World->Rename(*Name,CreatePackage(*Pkg),REN_DontCreateRedirectors|REN_NonTransactional)){Fail(TEXT("Cannot create target map"));break;}
                    // This is the same logical package in an older engine, not a newly authored localized asset.
                    TextNamespaceUtil::ForcePackageNamespace(World,E->GetStringField(TEXT("localizationNamespace")));
                    auto& SourceActors=E->GetArrayField(TEXT("actors"));
                    for(auto A:SourceActors)if(A->AsObject()->GetStringField(TEXT("class"))==TEXT("/Script/Engine.WorldSettings"))
                        World->GetWorldSettings()->Rename(*A->AsObject()->GetStringField(TEXT("name")),World->PersistentLevel,REN_DontCreateRedirectors|REN_NonTransactional);
                    if(SourceActors.Num()>1&&SourceActors[1]->AsObject()->GetStringField(TEXT("class"))==TEXT("/Script/Engine.Brush"))
                        World->GetDefaultBrush()->Rename(*SourceActors[1]->AsObject()->GetStringField(TEXT("name")),World->PersistentLevel,REN_DontCreateRedirectors|REN_NonTransactional);
                    FString File=E->GetStringField(TEXT("payload")),Text;if(File!=FPaths::GetCleanFilename(File)||File.Contains(TEXT(".."))||!FFileHelper::LoadFileToString(Text,*(Root/File))){Fail(TEXT("Invalid world payload"));break;}
                    TSet<AActor*> Existing;for(auto Actor:World->PersistentLevel->Actors)if(Actor)Existing.Add(Actor);
                    TArray<FString> ActorNames,ActorClasses,Lines;Text.ParseIntoArrayLines(Lines);int32 PayloadActorIndex=0;
                    for(auto Line:Lines)
                    {
                        Line.TrimStartInline();if(!Line.StartsWith(TEXT("Begin Actor ")))continue;
                        FString ActorName,ActorClass;FParse::Value(*Line,TEXT("Name="),ActorName);FParse::Value(*Line,TEXT("Class="),ActorClass);
                        int32 ThisActorIndex=PayloadActorIndex++;
                        if((ThisActorIndex==0&&ActorClass==TEXT("/Script/Engine.WorldSettings"))||(ThisActorIndex==1&&ActorClass==TEXT("/Script/Engine.Brush")))continue;
                        if(ActorClass==TEXT("/Script/Engine.DefaultPhysicsVolume"))continue;
                        ActorNames.Add(ActorName);ActorClasses.Add(ActorClass);
                    }
                    auto Factory=NewObject<ULevelFactory>();const TCHAR* Buffer=*Text;Factory->FactoryCreateText(UWorld::StaticClass(),World->PersistentLevel,NAME_None,RF_Transactional,nullptr,TEXT("T3D"),Buffer,Buffer+Text.Len(),GWarn);
                    TArray<AActor*> Created;for(auto Actor:World->PersistentLevel->Actors)if(Actor&&!Existing.Contains(Actor))Created.Add(Actor);
                    if(Created.Num()!=ActorNames.Num()){Fail(FString::Printf(TEXT("World actor import count mismatch: %d / %d"),Created.Num(),ActorNames.Num()));break;}
                    // ULevelFactory strips numeric FName suffixes. Its level insertion order follows the T3D order.
                    // Validate every class before restoring source names, in two passes to avoid name collisions.
                    for(int32 I=0;I<Created.Num();++I)
                    {
                        if(Created[I]->GetClass()->GetPathName()!=ActorClasses[I]){Fail(TEXT("World actor import order/class mismatch: ")+ActorNames[I]);break;}
                        Created[I]->Rename(*(TEXT("CyHubImport")+FGuid::NewGuid().ToString(EGuidFormats::Digits)),World->PersistentLevel,REN_DontCreateRedirectors|REN_NonTransactional);
                    }
                    if(!Errors.IsEmpty())break;
                    for(int32 I=0;I<Created.Num();++I)
                    {
                        if(!Created[I]->Rename(*ActorNames[I],World->PersistentLevel,REN_Test|REN_DontCreateRedirectors|REN_NonTransactional)||!Created[I]->Rename(*ActorNames[I],World->PersistentLevel,REN_DontCreateRedirectors|REN_NonTransactional)){Fail(TEXT("Cannot restore world actor name: ")+ActorNames[I]);break;}
                    }
                    if(!Errors.IsEmpty())break;
                    for(auto Actor:Created)if(Actor->GetClass()->ClassGeneratedBy&&Actor->GetClass()->GetPathName().StartsWith(TEXT("/Engine/")))Actor->RerunConstructionScripts();
                }
                else World=UEditorLoadingAndSavingUtils::LoadMap(Pkg);
                if(!World){Fail(TEXT("Target world unavailable"));break;}
                if(TextNamespaceUtil::GetPackageNamespace(World)!=E->GetStringField(TEXT("localizationNamespace"))){Fail(TEXT("World localization namespace mismatch"));break;}
                for(auto A:E->GetArrayField(TEXT("actors")))
                {
                    auto Data=A->AsObject();auto Actor=FindObject<AActor>(World->PersistentLevel,*Data->GetStringField(TEXT("name")));
                    if(!Actor&&Op==TEXT("import")&&Data->GetStringField(TEXT("class"))==TEXT("/Script/Foliage.InstancedFoliageActor"))
                    {Actor=AInstancedFoliageActor::GetInstancedFoliageActorForLevel(World->PersistentLevel,true);Actor->Rename(*Data->GetStringField(TEXT("name")),World->PersistentLevel,REN_DontCreateRedirectors|REN_NonTransactional);}
                    if(!Actor||Actor->GetClass()->GetPathName()!=Data->GetStringField(TEXT("class"))){Fail(TEXT("World actor/class missing: ")+Data->GetStringField(TEXT("name")));break;}
                    for(auto Aux:Data->GetArrayField(TEXT("auxiliary")))
                    {
                        auto Item=Aux->AsObject();auto ClassPath=Item->GetStringField(TEXT("class"));
                        if(!AuxiliaryClass(ClassPath)){Fail(TEXT("Unsupported world auxiliary class"));break;}
                        auto Object=FindObject<UObject>(Actor,*Item->GetStringField(TEXT("name")));
                        if(!Object&&Op==TEXT("import")){auto Class=LoadClass<UObject>(nullptr,*ClassPath);if(Class)Object=NewObject<UObject>(Actor,Class,*Item->GetStringField(TEXT("name")),RF_Transactional);}
                        if(!Object||Object->GetClass()->GetPathName()!=ClassPath||(Op==TEXT("import")&&!CyHubObjectIO::Apply(Object,Item->GetObjectField(TEXT("properties")),Adaptations,Error))||!CyHubObjectIO::Compare(Object,Item->GetObjectField(TEXT("properties")),Error)){Fail(TEXT("World auxiliary: ")+Error);break;}
                    }
                    if(!Errors.IsEmpty())break;
                    if(auto Brush=Cast<ABrush>(Actor))if(!BrushGeometry(Brush,Root,Data.ToSharedRef(),Op==TEXT("import"),Op==TEXT("verify"),Adaptations,Error)){Fail(Error);break;}
                    if(Op==TEXT("import"))if(auto IFA=Cast<AInstancedFoliageActor>(Actor))if(!Foliage(IFA,Root,Data.ToSharedRef(),true,false,Adaptations,Error)){Fail(Error);break;}
                    TMap<FString,FString> MaterialAliases;
                    for(auto C:Data->GetArrayField(TEXT("components")))
                    {
                        auto Item=C->AsObject();auto Mesh=FindObject<UMeshComponent>(Actor,*Item->GetStringField(TEXT("name")));if(!Mesh)continue;
                        if(Op==TEXT("import")&&!MaterialBindings(Mesh,Item.ToSharedRef(),true,false,Adaptations,Error)){Fail(Error);break;}
                        auto& Bindings=Item->GetArrayField(TEXT("materialBindings"));for(int32 Slot=0;Slot<Bindings.Num();++Slot)if(Bindings[Slot]->AsObject()->HasField(TEXT("dynamicParameters")))
                            if(auto Dynamic=Cast<UMaterialInstanceDynamic>(Mesh->GetMaterial(Slot)))MaterialAliases.Add(Bindings[Slot]->AsObject()->GetStringField(TEXT("path")),Dynamic->GetPathName());
                    }
                    if(!Errors.IsEmpty())break;
                    auto ActorFields=RemapDynamicReferences(Data->GetObjectField(TEXT("properties")),MaterialAliases);
                    if(Op==TEXT("import")&&!CyHubObjectIO::Apply(Actor,ActorFields,Adaptations,Error)){Fail(Error);break;}
                    for(auto C:Data->GetArrayField(TEXT("components")))
                    {
                        auto Item=C->AsObject();auto Component=FindObject<UActorComponent>(Actor,*Item->GetStringField(TEXT("name")));
                        if(!Component||Component->GetClass()->GetPathName()!=Item->GetStringField(TEXT("class"))){Fail(TEXT("World component missing: ")+Item->GetStringField(TEXT("name")));break;}
                        auto ComponentFields=RemapDynamicReferences(Item->GetObjectField(TEXT("properties")),MaterialAliases);
                        if(Op==TEXT("import")&&!CyHubObjectIO::Apply(Component,ComponentFields,Adaptations,Error)){Fail(Error);break;}
                        if(auto Mesh=Cast<UMeshComponent>(Component))if(!MaterialBindings(Mesh,Item.ToSharedRef(),Op==TEXT("import"),Op==TEXT("verify"),Adaptations,Error)){Fail(Error);break;}
                        if(!CyHubObjectIO::Compare(Component,ComponentFields,Error)){Fail(Error);break;}
                    }
                    if(!Errors.IsEmpty())break;
                    if(!CyHubObjectIO::Compare(Actor,ActorFields,Error)){Fail(Error);break;}
                    if(Op==TEXT("verify"))if(auto IFA=Cast<AInstancedFoliageActor>(Actor))if(!Foliage(IFA,Root,Data.ToSharedRef(),false,true,Adaptations,Error)){Fail(Error);break;}
                }
                if(!Errors.IsEmpty())break;
                auto& GraphEntries=E->GetArrayField(TEXT("graphs"));auto Blueprint=World->PersistentLevel->GetLevelScriptBlueprint(!E->HasField(TEXT("blueprintProperties")));
                const TSharedPtr<FJsonObject>* BlueprintFields=nullptr;
                if(E->TryGetObjectField(TEXT("blueprintProperties"),BlueprintFields))
                {if(!Blueprint||(Op==TEXT("import")&&!CyHubObjectIO::Apply(Blueprint,*BlueprintFields,Adaptations,Error))){Fail(TEXT("Blueprint metadata: ")+Error);break;}}
                if(Op==TEXT("import")&&!GraphEntries.IsEmpty()&&!ImportGraphs(Blueprint,GraphEntries,Error)){Fail(Error);break;}
                if(BlueprintFields&&!CyHubObjectIO::Compare(Blueprint,*BlueprintFields,Error)){Fail(Error);break;}
                const TSharedPtr<FJsonObject>* CDOFields=nullptr;
                if(E->TryGetObjectField(TEXT("blueprintDefaults"),CDOFields))
                {auto CDO=Blueprint&&Blueprint->GeneratedClass?Blueprint->GeneratedClass->GetDefaultObject():nullptr;if(!CDO||(Op==TEXT("import")&&!CyHubObjectIO::Apply(CDO,*CDOFields,Adaptations,Error))||!CyHubObjectIO::Compare(CDO,*CDOFields,Error)){Fail(TEXT("Blueprint defaults: ")+Error);break;}}
                if(!CheckGraphs(Blueprint,GraphEntries,Error)){Fail(Error);break;}
                if(Op==TEXT("import"))
                {
                    auto Package=World->GetOutermost();Package->MarkPackageDirty();Package->SetPackageFlags(PKG_ContainsMap);FString Filename=FPackageName::LongPackageNameToFilename(Pkg,FPackageName::GetMapPackageExtension());IFileManager::Get().MakeDirectory(*FPaths::GetPath(Filename),true);FSavePackageArgs Args;Args.TopLevelFlags=RF_Public|RF_Standalone;Args.SaveFlags=SAVE_NoError;
                    if(!UPackage::SavePackage(Package,World,*Filename,Args)){Fail(TEXT("World SavePackage failed"));break;}
                }
                Imported.Add(MakeShared<FJsonValueString>(Path));
            }
        }
    }
    else Fail(TEXT("Unknown world operation"));
    auto Report=MakeShared<FJsonObject>();Report->SetArrayField(TEXT("exported"),Entries);Report->SetArrayField(TEXT("imported"),Imported);Report->SetArrayField(TEXT("errors"),Errors);Report->SetArrayField(TEXT("adaptations"),Adaptations);Report->SetArrayField(TEXT("unsupported"),{});return Write(Config->GetStringField(TEXT("reportPath")),Report)&&Errors.IsEmpty();
}
