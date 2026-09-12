#include "CyHubNativeTextureLibrary.h"
#include "Engine/Texture2D.h"
#include "AssetCompilingManager.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/PackageName.h"
#include "HAL/FileManager.h"
#include "UObject/UnrealType.h"
#include "UObject/Package.h"
#include "UObject/SavePackage.h"

namespace
{
const TSet<FName> Fields = {
    TEXT("SRGB"), TEXT("CompressionSettings"), TEXT("MipGenSettings"), TEXT("LODGroup"),
    TEXT("LODBias"), TEXT("MaxTextureSize"), TEXT("Filter"), TEXT("AddressX"), TEXT("AddressY"),
    TEXT("CompressionNoAlpha"), TEXT("CompressionNone"), TEXT("CompressionQuality"),
    TEXT("bFlipGreenChannel"), TEXT("bPreserveBorder"), TEXT("NeverStream"),
    TEXT("AdjustBrightness"), TEXT("AdjustBrightnessCurve"), TEXT("AdjustVibrance"),
    TEXT("AdjustSaturation"), TEXT("AdjustRGBCurve"), TEXT("AdjustHue"),
    TEXT("AdjustMinAlpha"), TEXT("AdjustMaxAlpha"), TEXT("AlphaCoverageThresholds"),
    TEXT("bDoScaleMipsForAlphaCoverage"), TEXT("bUseLegacyGamma"), TEXT("PowerOfTwoMode"),
    TEXT("PaddingColor"), TEXT("bPadWithBorderColor"), TEXT("ResizeDuringBuildX"), TEXT("ResizeDuringBuildY"),
    TEXT("bUseNewMipFilter"), TEXT("CompositeTextureMode"), TEXT("bNormalizeNormals")
};

TSharedPtr<FJsonObject> ReadJson(const FString& File)
{
    FString Text;
    TSharedPtr<FJsonObject> Json;
    if (!FFileHelper::LoadFileToString(Text, *File) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Json)) return nullptr;
    return Json;
}
bool WriteJson(const FString& File, const TSharedRef<FJsonObject>& Json)
{
    FString Text;
    FJsonSerializer::Serialize(Json, TJsonWriterFactory<>::Create(&Text));
    return FFileHelper::SaveStringToFile(Text, *File, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}
bool ValidAssetPath(const FString& Path)
{
    FString Package, Object;
    return Path.Split(TEXT("."), &Package, &Object) && Package.StartsWith(TEXT("/Game/")) &&
        FPackageName::IsValidLongPackageName(Package) && Object == FPackageName::GetShortName(Package);
}
FString TextValue(FProperty* Property, UObject* Object)
{
    FString Text;
    Property->ExportText_InContainer(0, Text, Object, Object, Object, PPF_None);
    return Text;
}
bool Snapshot(UTexture2D* Texture, TSharedRef<FJsonObject> Entry, TArray64<uint8>& Pixels, FString& Error)
{
    if (!Texture || Texture->GetClass() != UTexture2D::StaticClass())
    { Error = TEXT("Exact Texture2D class required"); return false; }
    auto& Source = Texture->Source;
    if (!Source.IsValid() || Source.GetNumBlocks() != 1 || Source.GetNumLayers() != 1 ||
        Source.GetNumSlices() != 1 || Source.GetNumMips() != 1 || Texture->VirtualTextureStreaming)
    { Error = TEXT("Only non-virtual, single-block/layer/slice/mip source textures are supported"); return false; }
    const auto Format = Source.GetFormat();
    if (Format != TSF_BGRA8 && Format != TSF_G8)
    { Error = TEXT("Only BGRA8 and G8 source pixels supported by native-texture-v1"); return false; }
    const int64 Expected = int64(Source.GetSizeX()) * Source.GetSizeY() * (Format == TSF_BGRA8 ? 4 : 1);
    if (Source.GetSizeX() <= 0 || Source.GetSizeY() <= 0 || Expected > 256LL * 1024 * 1024 ||
        !Source.GetMipData(Pixels, 0) || Pixels.Num() != Expected)
    { Error = TEXT("Invalid or oversized source pixels (256 MiB maximum)"); return false; }
    auto Properties = MakeShared<FJsonObject>();
    UObject* Defaults = Texture->GetClass()->GetDefaultObject();
    TArray<FString> UnsupportedProperties;
    for (TFieldIterator<FProperty> It(Texture->GetClass()); It; ++It)
    {
        FProperty* Property = *It;
        if (Fields.Contains(Property->GetFName()))
            Properties->SetStringField(Property->GetName(), TextValue(Property, Texture));
        else if (Property->HasAnyPropertyFlags(CPF_Edit) &&
            !Property->HasAnyPropertyFlags(CPF_Transient | CPF_Deprecated) &&
            Property->GetFName() != TEXT("AssetImportData") &&
            Property->GetFName() != TEXT("DeferCompression") &&
            Property->GetFName() != TEXT("OodleTextureSdkVersion") &&
            !Property->Identical_InContainer(Texture, Defaults))
        { UnsupportedProperties.Add(Property->GetName()); }
    }
    if (!UnsupportedProperties.IsEmpty())
    { Error = TEXT("Non-default properties without adapter: ") + FString::Join(UnsupportedProperties, TEXT(", ")); return false; }
    Entry->SetStringField(TEXT("path"), Texture->GetPathName());
    Entry->SetStringField(TEXT("format"), Format == TSF_BGRA8 ? TEXT("BGRA8") : TEXT("G8"));
    Entry->SetNumberField(TEXT("width"), Source.GetSizeX());
    Entry->SetNumberField(TEXT("height"), Source.GetSizeY());
    Entry->SetObjectField(TEXT("properties"), Properties);
    return true;
}
bool SameSnapshot(const TSharedPtr<FJsonObject>& Expected, const TSharedRef<FJsonObject>& Actual)
{
    if (Expected->GetStringField(TEXT("path")) != Actual->GetStringField(TEXT("path")) ||
        Expected->GetStringField(TEXT("format")) != Actual->GetStringField(TEXT("format")) ||
        Expected->GetIntegerField(TEXT("width")) != Actual->GetIntegerField(TEXT("width")) ||
        Expected->GetIntegerField(TEXT("height")) != Actual->GetIntegerField(TEXT("height"))) return false;
    auto Left = Expected->GetObjectField(TEXT("properties"));
    auto Right = Actual->GetObjectField(TEXT("properties"));
    if (Left->Values.Num() != Right->Values.Num()) return false;
    for (auto& Pair : Left->Values)
    {
        FString Value;
        if (!Right->TryGetStringField(Pair.Key, Value) || Value != Pair.Value->AsString()) return false;
    }
    return true;
}
}

bool UCyHubNativeTextureLibrary::Run(const FString& ConfigFile)
{
    auto Config = ReadJson(ConfigFile);
    if (!Config) return false;
    const FString Root = FPaths::ConvertRelativePathToFull(Config->GetStringField(TEXT("exchangeRoot")));
    const FString Operation = Config->GetStringField(TEXT("operation"));
    const FString ManifestPath = Root / TEXT("native-textures.json");
    TArray<TSharedPtr<FJsonValue>> Entries, Imported, Errors;
    auto Fail = [&](const FString& Message) { Errors.Add(MakeShared<FJsonValueString>(Message)); UE_LOG(LogTemp, Error, TEXT("%s"), *Message); };
    if (Operation == TEXT("export"))
    {
        auto Plan = ReadJson(Config->GetStringField(TEXT("planPath")));
        if (!Plan || !Plan->GetBoolField(TEXT("canConvert")) || Plan->GetStringField(TEXT("mode")) != TEXT("native"))
            Fail(TEXT("Native plan missing or blocked"));
        else for (auto& Value : Plan->GetArrayField(TEXT("assets")))
        {
            auto Decision = Value->AsObject();
            const FString AssetPath = Decision->GetStringField(TEXT("assetPath"));
            if (!ValidAssetPath(AssetPath) || Decision->GetStringField(TEXT("adapter")) != TEXT("native-texture-v1"))
            { Fail(TEXT("Unsupported native plan entry")); break; }
            auto Texture = LoadObject<UTexture2D>(nullptr, *AssetPath);
            auto Entry = MakeShared<FJsonObject>();
            TArray64<uint8> Pixels;
            FString Error;
            if (!Snapshot(Texture, Entry, Pixels, Error)) { Fail(AssetPath + TEXT(": ") + Error); break; }
            const FString Payload = FString::Printf(TEXT("pixels-%d.bin"), Entries.Num());
            Entry->SetStringField(TEXT("payload"), Payload);
            if (!FFileHelper::SaveArrayToFile(Pixels, *(Root / Payload))) { Fail(TEXT("Cannot write raw pixels")); break; }
            Entries.Add(MakeShared<FJsonValueObject>(Entry));
        }
        auto Manifest = MakeShared<FJsonObject>();
        Manifest->SetNumberField(TEXT("schemaVersion"), 1);
        Manifest->SetArrayField(TEXT("textures"), Entries);
        if (!WriteJson(ManifestPath, Manifest)) Fail(TEXT("Cannot write texture manifest"));
    }
    else if (Operation == TEXT("import") || Operation == TEXT("verify"))
    {
        auto Manifest = ReadJson(ManifestPath);
        if (!Manifest || Manifest->GetIntegerField(TEXT("schemaVersion")) != 1) Fail(TEXT("Invalid texture manifest"));
        else for (auto& Value : Manifest->GetArrayField(TEXT("textures")))
        {
            auto Entry = Value->AsObject();
            const FString AssetPath = Entry->GetStringField(TEXT("path"));
            const FString Payload = Entry->GetStringField(TEXT("payload"));
            const int32 Width = Entry->GetIntegerField(TEXT("width")), Height = Entry->GetIntegerField(TEXT("height"));
            const FString FormatName = Entry->GetStringField(TEXT("format"));
            const bool IsBGRA = FormatName == TEXT("BGRA8");
            TArray64<uint8> Pixels;
            if (!ValidAssetPath(AssetPath) || FPaths::GetCleanFilename(Payload) != Payload || Payload.Contains(TEXT("..")) ||
                (FormatName != TEXT("G8") && !IsBGRA) || Width <= 0 || Height <= 0 || Width > 16384 || Height > 16384 ||
                IFileManager::Get().FileSize(*(Root / Payload)) > 256LL * 1024 * 1024 ||
                !FFileHelper::LoadFileToArray(Pixels, *(Root / Payload)) || Pixels.Num() != int64(Width) * Height * (IsBGRA ? 4 : 1))
            { Fail(TEXT("Invalid texture payload/path/dimensions")); break; }
            UTexture2D* Texture = nullptr;
            if (Operation == TEXT("import"))
            {
                FString PackageName, ObjectName;
                AssetPath.Split(TEXT("."), &PackageName, &ObjectName);
                if (FPackageName::DoesPackageExist(PackageName)) { Fail(TEXT("Refusing to overwrite target package")); break; }
                UPackage* Package = CreatePackage(*PackageName);
                Texture = NewObject<UTexture2D>(Package, *ObjectName, RF_Public | RF_Standalone);
                Texture->Source.Init(Width, Height, 1, 1, IsBGRA ? TSF_BGRA8 : TSF_G8, Pixels.GetData());
                for (auto& Pair : Entry->GetObjectField(TEXT("properties"))->Values)
                {
                    FProperty* Property = FindFProperty<FProperty>(Texture->GetClass(), *Pair.Key);
                    if (!Fields.Contains(FName(*Pair.Key)) || !Property ||
                        !Property->ImportText_Direct(*Pair.Value->AsString(), Property->ContainerPtrToValuePtr<void>(Texture), Texture, PPF_None))
                    { Fail(TEXT("Property cannot be adapted: ") + Pair.Key); break; }
                }
                if (Errors.Num()) break;
                Texture->PostEditChange();
                FAssetCompilingManager::Get().FinishAllCompilation();
                FAssetRegistryModule::AssetCreated(Texture);
                Package->MarkPackageDirty();
                FString Filename = FPackageName::LongPackageNameToFilename(PackageName, FPackageName::GetAssetPackageExtension());
                IFileManager::Get().MakeDirectory(*FPaths::GetPath(Filename), true);
                FSavePackageArgs Args;
                Args.TopLevelFlags = RF_Public | RF_Standalone;
                Args.SaveFlags = SAVE_NoError;
                if (!UPackage::SavePackage(Package, Texture, *Filename, Args)) { Fail(TEXT("Native SavePackage failed")); break; }
            }
            else Texture = LoadObject<UTexture2D>(nullptr, *AssetPath);
            auto Actual = MakeShared<FJsonObject>();
            TArray64<uint8> ActualPixels;
            FString Error;
            if (!Snapshot(Texture, Actual, ActualPixels, Error) || !SameSnapshot(Entry, Actual) || ActualPixels != Pixels)
            { Fail(AssetPath + TEXT(": native comparison failed: ") + Error); break; }
            Imported.Add(MakeShared<FJsonValueString>(AssetPath));
        }
    }
    else Fail(TEXT("Unknown native operation"));
    if ((Operation == TEXT("export") && Entries.IsEmpty()) || (Operation != TEXT("export") && Imported.IsEmpty()))
        Fail(TEXT("No native textures processed"));
    auto Report = MakeShared<FJsonObject>();
    Report->SetStringField(TEXT("adapter"), TEXT("native-texture-v1"));
    Report->SetStringField(TEXT("adaptationPolicy"), TEXT("Source pixels and supported texture settings preserved; GPU data and Oodle SDK version rebuilt by target engine. AssetImportData and deferred compression state not transferred."));
    Report->SetArrayField(TEXT("exported"), Entries);
    Report->SetArrayField(TEXT("imported"), Imported);
    Report->SetArrayField(TEXT("unsupported"), {});
    Report->SetArrayField(TEXT("errors"), Errors);
    const bool Saved = WriteJson(Config->GetStringField(TEXT("reportPath")), Report);
    return Saved && Errors.IsEmpty();
}
