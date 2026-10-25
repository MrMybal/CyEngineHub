#include "CyHubEditorClient.h"
#include "Misc/Paths.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformTime.h"
#include "Runtime/Launch/Resources/Version.h"
#include "Policies/CondensedJsonPrintPolicy.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#if PLATFORM_WINDOWS
#include "Windows/AllowWindowsPlatformTypes.h"
#include <windows.h>
#include <bcrypt.h>
#define SECURITY_WIN32
#include <security.h>
#include "Windows/HideWindowsPlatformTypes.h"
#endif

namespace
{
    TSharedPtr<FJsonObject> Failure(const FString& Message)
    {
        auto Result = MakeShared<FJsonObject>(); Result->SetBoolField(TEXT("ok"), false);
        Result->SetStringField(TEXT("error"), Message); return Result;
    }
#if PLATFORM_WINDOWS
    bool Transfer(HANDLE Pipe, void* Buffer, DWORD Size, DWORD& Count, bool Write, DWORD Timeout)
    {
        OVERLAPPED Operation = {}; Operation.hEvent = CreateEventW(nullptr, true, false, nullptr);
        if (!Operation.hEvent) return false;
        BOOL Ok = Write ? WriteFile(Pipe, Buffer, Size, &Count, &Operation) : ReadFile(Pipe, Buffer, Size, &Count, &Operation);
        if (!Ok && GetLastError() == ERROR_IO_PENDING)
        {
            if (WaitForSingleObject(Operation.hEvent, Timeout) != WAIT_OBJECT_0)
            {
                CancelIoEx(Pipe, &Operation); GetOverlappedResult(Pipe, &Operation, &Count, true);
                CloseHandle(Operation.hEvent); return false;
            }
            Ok = GetOverlappedResult(Pipe, &Operation, &Count, false);
        }
        CloseHandle(Operation.hEvent); return Ok != 0;
    }
    FString PipeName()
    {
        WCHAR User[512]; ULONG Length = sizeof(User) / sizeof(WCHAR);
        if (!GetUserNameExW(NameSamCompatible, User, &Length)) return FString();
        FTCHARToUTF8 Identity(User);
        BCRYPT_ALG_HANDLE Algorithm = nullptr;
        if (BCryptOpenAlgorithmProvider(&Algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0) return FString();
        UCHAR Hash[32]; DWORD ObjectLength = 0, PropertyBytes = 0;
        NTSTATUS Status = BCryptGetProperty(Algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&ObjectLength), sizeof(ObjectLength), &PropertyBytes, 0);
        TArray<uint8> HashObject; HashObject.SetNumUninitialized(static_cast<int32>(ObjectLength));
        BCRYPT_HASH_HANDLE HashHandle = nullptr;
        if (Status == 0) Status = BCryptCreateHash(Algorithm, &HashHandle, HashObject.GetData(), ObjectLength, nullptr, 0, 0);
        if (Status == 0) Status = BCryptHashData(HashHandle, reinterpret_cast<PUCHAR>(const_cast<ANSICHAR*>(Identity.Get())), Identity.Length(), 0);
        if (Status == 0) Status = BCryptFinishHash(HashHandle, Hash, sizeof(Hash), 0);
        if (HashHandle) BCryptDestroyHash(HashHandle);
        BCryptCloseAlgorithmProvider(Algorithm, 0);
        if (Status != 0) return FString();
        FString Suffix;
        for (int32 Index = 0; Index < 12; ++Index) Suffix += FString::Printf(TEXT("%02X"), Hash[Index]);
        return TEXT("\\\\.\\pipe\\CyEngineHub.Editor.") + Suffix;
    }
#endif
}

FString CyHubEditorClient::AccountPipeName()
{
#if PLATFORM_WINDOWS
    return PipeName();
#else
    return FString();
#endif
}

TSharedPtr<FJsonObject> CyHubEditorClient::Send(const TSharedRef<FJsonObject>& Request)
{
#if PLATFORM_WINDOWS
    Request->SetNumberField(TEXT("protocol"), 1);
    Request->SetStringField(TEXT("engineVersion"), FString::Printf(TEXT("%d.%d"), ENGINE_MAJOR_VERSION, ENGINE_MINOR_VERSION));
    Request->SetStringField(TEXT("projectFile"), FPaths::ConvertRelativePathToFull(FPaths::GetProjectFilePath()));
    FString Json;
    FJsonSerializer::Serialize(Request, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json));
    Json += TEXT("\n"); FTCHARToUTF8 Payload(*Json);
    FString Pipe = PipeName();
#if WITH_DEV_AUTOMATION_TESTS
    FString TestPipe;
    if (FParse::Value(FCommandLine::Get(), TEXT("CyHubBridgeQAPipe="), TestPipe) && TestPipe.StartsWith(TEXT("CyEngineHub.Editor.Test.")) && TestPipe.Len() < 100)
        Pipe = TEXT("\\\\.\\pipe\\") + TestPipe;
#endif
    if (Pipe.IsEmpty()) return Failure(TEXT("Impossible de déterminer le compte Windows."));
    HANDLE Handle = INVALID_HANDLE_VALUE;
    const double ConnectEnd = FPlatformTime::Seconds() + 3;
    do
    {
        Handle = CreateFileW(*Pipe, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IMPERSONATION, nullptr);
        if (Handle != INVALID_HANDLE_VALUE) break;
        WaitNamedPipeW(*Pipe, 200); FPlatformProcess::Sleep(0.05f);
    } while (FPlatformTime::Seconds() < ConnectEnd);
    if (Handle == INVALID_HANDLE_VALUE) return Failure(TEXT("Hub déconnecté. Ouvrez CyEngineHub et activez la connexion éditeur dans la fiche moteur."));
    DWORD Written = 0;
    if (!Transfer(Handle, const_cast<ANSICHAR*>(Payload.Get()), Payload.Length(), Written, true, 20000) || Written != static_cast<DWORD>(Payload.Length()))
    { CloseHandle(Handle); return Failure(TEXT("Envoi interrompu. Vérifiez les tâches du Hub avant de réessayer.")); }
    TArray<uint8> Bytes; const double ReadEnd = FPlatformTime::Seconds() + 20;
    bool Complete = false;
    while (FPlatformTime::Seconds() < ReadEnd && Bytes.Num() <= 1048576)
    {
        uint8 Buffer[8192]; DWORD Count = 0;
        const bool Read = Transfer(Handle, Buffer, sizeof(Buffer), Count, false, FMath::Max(1, static_cast<int32>((ReadEnd - FPlatformTime::Seconds()) * 1000)));
        if (Count > 0)
        {
            for (DWORD I = 0; I < Count; ++I) { if (Buffer[I] == '\n') { Complete = true; break; } Bytes.Add(Buffer[I]); }
            if (Complete) break;
        }
        if (!Read) break;
    }
    CloseHandle(Handle);
    if (!Complete || Bytes.Num() > 1048576) return Failure(TEXT("Réponse interrompue. Consultez les tâches du Hub : une installation confirmée peut continuer."));
    FUTF8ToTCHAR Text(reinterpret_cast<const ANSICHAR*>(Bytes.GetData()), Bytes.Num());
    TSharedPtr<FJsonObject> Response;
    const FString ResponseText(Text.Length(), Text.Get());
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(ResponseText), Response) || !Response.IsValid()) return Failure(TEXT("Réponse du Hub illisible."));
    return Response;
#else
    return Failure(TEXT("CyEngineHub Editor Alpha nécessite Windows."));
#endif
}
