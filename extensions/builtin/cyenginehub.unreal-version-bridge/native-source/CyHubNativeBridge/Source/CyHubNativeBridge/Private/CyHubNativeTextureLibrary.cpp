#include "CyHubNativeTextureLibrary.h"
#include "Engine/Texture2D.h"
#include "Engine/TextureCube.h"
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
int32 PixelBytes(ETextureSourceFormat Format)
{
    switch (Format) { case TSF_BGRA8: case TSF_BGRE8: return 4; case TSF_G8: return 1; case TSF_RGBA16: case TSF_RGBA16F: return 8; case TSF_RGBA32F: return 16; default: return 0; }
}
FString PixelFormat(ETextureSourceFormat Format)
{
    switch (Format) { case TSF_BGRA8: return TEXT("BGRA8"); case TSF_BGRE8: return TEXT("BGRE8"); case TSF_G8: return TEXT("G8"); case TSF_RGBA16: return TEXT("RGBA16"); case TSF_RGBA16F: return TEXT("RGBA16F"); case TSF_RGBA32F: return TEXT("RGBA32F"); default: return TEXT(""); }
}
ETextureSourceFormat ParseFormat(const FString& Name)
{ for (auto Format : { TSF_BGRA8, TSF_BGRE8, TSF_G8, TSF_RGBA16, TSF_RGBA16F, TSF_RGBA32F }) if (PixelFormat(Format) == Name) return Format; return TSF_Invalid; }
bool Snapshot(UTexture* Texture, TSharedRef<FJsonObject> Entry, TArray64<uint8>& Pixels, FString& Error)
{
    if (!Texture || (Texture->GetClass() != UTexture2D::StaticClass() && Texture->GetClass() != UTextureCube::StaticClass()))
    { Error = TEXT("Exact Texture2D or TextureCube class required"); return false; }
    auto& Source = Texture->Source;
    if (!Source.IsValid() || Source.GetNumBlocks() != 1 || Source.GetNumLayers() != 1 ||
        (Source.GetNumSlices() != 1 && !(Texture->IsA<UTextureCube>() && Source.GetNumSlices() == 6)) || Source.GetNumMips() < 1 || Source.GetNumMips() > 15 || Texture->VirtualTextureStreaming)
    { Error = TEXT("Non-virtual, single-block/layer source required (one slice or six cube faces)"); return false; }
    const auto Format = Source.GetFormat();
    if (!PixelBytes(Format)) { Error = TEXT("Source pixel format requires an adapter"); return false; }
    Pixels.Reset();
    for (int32 Mip = 0; Mip < Source.GetNumMips(); ++Mip)
    {
        TArray64<uint8> Data; const int64 Expected = int64(FMath::Max(1, Source.GetSizeX() >> Mip)) * FMath::Max(1, Source.GetSizeY() >> Mip) * PixelBytes(Format) * Source.GetNumSlices();
        if (Source.GetSizeX() <= 0 || Source.GetSizeY() <= 0 || Expected > 256LL * 1024 * 1024 || !Source.GetMipData(Data, Mip) || Data.Num() != Expected || Pixels.Num() + Expected > 256LL * 1024 * 1024)
        { Error = TEXT("Invalid or oversized source pixels (256 MiB maximum)"); return false; }
        Pixels.Append(Data);
    }
    auto Properties = MakeShared<FJsonObject>();
    auto DefaultProperties = MakeShared<FJsonObject>();
    UObject* Defaults = Texture->GetClass()->GetDefaultObject();
    TArray<FString> UnsupportedProperties;
    for (TFieldIterator<FProperty> It(Texture->GetClass()); It; ++It)
    {
        FProperty* Property = *It;
        if (Fields.Contains(Property->GetFName()))
        {
            Properties->SetStringField(Property->GetName(), TextValue(Property, Texture));
            DefaultProperties->SetStringField(Property->GetName(), TextValue(Property, Defaults));
        }
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
    Entry->SetStringField(TEXT("format"), PixelFormat(Format));
    Entry->SetStringField(TEXT("class"), Texture->GetClass()->GetPathName());
    Entry->SetNumberField(TEXT("slices"), Source.GetNumSlices()); Entry->SetNumberField(TEXT("mips"), Source.GetNumMips());
    Entry->SetNumberField(TEXT("width"), Source.GetSizeX());
    Entry->SetNumberField(TEXT("height"), Source.GetSizeY());
    Entry->SetObjectField(TEXT("properties"), Properties);
    Entry->SetObjectField(TEXT("defaults"), DefaultProperties);
    return true;
}
bool SameSnapshot(const TSharedPtr<FJsonObject>& Expected, const TSharedRef<FJsonObject>& Actual)
{
    if ((Expected->HasField(TEXT("class")) && Expected->GetStringField(TEXT("class")) != Actual->GetStringField(TEXT("class"))) ||
        (Expected->HasField(TEXT("slices")) && Expected->GetIntegerField(TEXT("slices")) != Actual->GetIntegerField(TEXT("slices"))) ||
        (Expected->HasField(TEXT("mips")) && Expected->GetIntegerField(TEXT("mips")) != Actual->GetIntegerField(TEXT("mips")))) return false;
    if (Expected->GetStringField(TEXT("path")) != Actual->GetStringField(TEXT("path")) ||
        Expected->GetStringField(TEXT("format")) != Actual->GetStringField(TEXT("format")) ||
        Expected->GetIntegerField(TEXT("width")) != Actual->GetIntegerField(TEXT("width")) ||
        Expected->GetIntegerField(TEXT("height")) != Actual->GetIntegerField(TEXT("height"))) return false;
    auto Left = Expected->GetObjectField(TEXT("properties"));
    auto Right = Actual->GetObjectField(TEXT("properties"));
    for (auto& Pair : Left->Values)
    {
        FString Value;
        if (!Right->TryGetStringField(Pair.Key, Value))
        {
            const TSharedPtr<FJsonObject>* Defaults = nullptr; FString Default;
            if (Expected->TryGetObjectField(TEXT("defaults"), Defaults) && (*Defaults)->TryGetStringField(Pair.Key, Default) && Default == Pair.Value->AsString()) continue;
            return false;
        }
        if (Value != Pair.Value->AsString())
        {
            // UTexture::ValidateSettings explicitly clears this ignored flag when SRGB is off.
            FString SourceSRGB, TargetSRGB;
            if(Pair.Key==TEXT("bUseLegacyGamma")&&Left->TryGetStringField(TEXT("SRGB"),SourceSRGB)&&Right->TryGetStringField(TEXT("SRGB"),TargetSRGB)&&SourceSRGB==TEXT("False")&&TargetSRGB==TEXT("False")&&Value==TEXT("False"))continue;
            UE_LOG(LogTemp,Error,TEXT("Texture property mismatch: %s source=%s target=%s"),*Pair.Key,*Pair.Value->AsString(),*Value);return false;
        }
    }
    for (auto& Pair : Right->Values)
    {
        if (!Left->HasField(Pair.Key) && Actual->GetObjectField(TEXT("defaults"))->GetStringField(Pair.Key) != Pair.Value->AsString()) return false;
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
    TArray<TSharedPtr<FJsonValue>> Entries, Imported, Errors, Adaptations;
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
            auto Texture = LoadObject<UTexture>(nullptr, *AssetPath);
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
            const ETextureSourceFormat Format = ParseFormat(FormatName);
            const FString Class = Entry->HasField(TEXT("class")) ? Entry->GetStringField(TEXT("class")) : TEXT("/Script/Engine.Texture2D");
            const int32 Slices = Entry->HasField(TEXT("slices")) ? Entry->GetIntegerField(TEXT("slices")) : 1;
            const int32 Mips = Entry->HasField(TEXT("mips")) ? Entry->GetIntegerField(TEXT("mips")) : 1;
            int64 ExpectedBytes = 0;
            if (Width > 0 && Height > 0 && Width <= 16384 && Height <= 16384 && Slices > 0 && Slices <= 6 && Mips > 0 && Mips <= 15)
                for (int32 Mip = 0; Mip < Mips; ++Mip) ExpectedBytes += int64(FMath::Max(1, Width >> Mip)) * FMath::Max(1, Height >> Mip) * Slices * PixelBytes(Format);
            TArray64<uint8> Pixels;
            if (!ValidAssetPath(AssetPath) || FPaths::GetCleanFilename(Payload) != Payload || Payload.Contains(TEXT("..")) ||
                !PixelBytes(Format) || ExpectedBytes <= 0 || ExpectedBytes > 256LL * 1024 * 1024 ||
                ((Class != TEXT("/Script/Engine.Texture2D") || Slices != 1) && (Class != TEXT("/Script/Engine.TextureCube") || (Slices != 6 && Slices != 1))) ||
                IFileManager::Get().FileSize(*(Root / Payload)) > 256LL * 1024 * 1024 ||
                !FFileHelper::LoadFileToArray(Pixels, *(Root / Payload)) || Pixels.Num() != ExpectedBytes)
            { Fail(TEXT("Invalid texture payload/path/dimensions")); break; }
            UTexture* Texture = nullptr;
            if (Operation == TEXT("import"))
            {
                FString PackageName, ObjectName;
                AssetPath.Split(TEXT("."), &PackageName, &ObjectName);
                if (FPackageName::DoesPackageExist(PackageName)) { Fail(TEXT("Refusing to overwrite target package")); break; }
                UPackage* Package = CreatePackage(*PackageName);
                Texture = NewObject<UTexture>(Package, Class == TEXT("/Script/Engine.TextureCube") ? UTextureCube::StaticClass() : UTexture2D::StaticClass(), *ObjectName, RF_Public | RF_Standalone);
                Texture->Source.Init(Width, Height, Slices, Mips, Format, Pixels.GetData());
                for (auto& Pair : Entry->GetObjectField(TEXT("properties"))->Values)
                {
                    FProperty* Property = FindFProperty<FProperty>(Texture->GetClass(), *Pair.Key);
                    if (!Property && Fields.Contains(FName(*Pair.Key)))
                    {
                        const TSharedPtr<FJsonObject>* Defaults = nullptr; FString Default;
                        if (Entry->TryGetObjectField(TEXT("defaults"), Defaults) && (*Defaults)->TryGetStringField(Pair.Key, Default) && Default == Pair.Value->AsString())
                        { Adaptations.Add(MakeShared<FJsonValueString>(AssetPath + TEXT(".") + Pair.Key + TEXT(": source default unsupported in target; no data discarded"))); continue; }
                    }
                    if (!Fields.Contains(FName(*Pair.Key)) || !Property ||
                        !Property->ImportText_Direct(*Pair.Value->AsString(), Property->ContainerPtrToValuePtr<void>(Texture), Texture, PPF_None))
                    { Fail(TEXT("Property cannot be adapted: ") + Pair.Key); break; }
                }
                if (Errors.Num()) break;
                Texture->PostEditChange();
                if(!Texture->SRGB && Entry->GetObjectField(TEXT("properties"))->GetStringField(TEXT("bUseLegacyGamma"))==TEXT("True"))
                    Adaptations.Add(MakeShared<FJsonValueString>(AssetPath+TEXT(".bUseLegacyGamma: ignored because SRGB=false; target ValidateSettings normalizes it to false")));
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
            else Texture = LoadObject<UTexture>(nullptr, *AssetPath);
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
    Report->SetArrayField(TEXT("adaptations"), Adaptations);
    const bool Saved = WriteJson(Config->GetStringField(TEXT("reportPath")), Report);
    return Saved && Errors.IsEmpty();
}
