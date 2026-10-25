#include "CoreMinimal.h"
#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformTime.h"
#include "CyHubEditorClient.h"
#include "SCyHubLibrary.h"
#include "UnrealEdMisc.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FCyHubEditorSmoke, "CyEngineHub.Editor.PanelAndBridge", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FCyHubEditorSmoke::RunTest(const FString& Parameters)
{
    const auto Panel = SNew(SCyHubLibrary);
    TestTrue(TEXT("Native Slate panel constructs"), Panel->GetChildren()->Num() > 0);
    FString ExpectedPipe;
    if (FParse::Value(FCommandLine::Get(), TEXT("CyHubExpectedPipe="), ExpectedPipe))
        TestEqual(TEXT("C++ and .NET derive the identical Windows-account endpoint"), CyHubEditorClient::AccountPipeName(), ExpectedPipe);
    FString TestPipe;
    if (!FParse::Value(FCommandLine::Get(), TEXT("CyHubBridgeQAPipe="), TestPipe))
    { AddInfo(TEXT("IPC installation fixture not requested; panel-only smoke.")); return true; }
    // A headless UE 4.27 window cannot report desktop placement on exit.
    // This affects the disposable automation run only, not normal editor use.
    FUnrealEdMisc::Get().AllowSavingLayoutOnClose(false);
    auto Request = MakeShared<FJsonObject>(); Request->SetStringField(TEXT("method"), TEXT("search"));
    Request->SetStringField(TEXT("query"), TEXT("editor-bridge-fixture"));
    auto Response = CyHubEditorClient::Send(Request);
    if (!TestTrue(TEXT("C++ client connects to .NET desktop bridge"), Response->GetBoolField(TEXT("ok")))) { AddError(Response->GetStringField(TEXT("error"))); return false; }
    const auto Assets = Response->GetObjectField(TEXT("result"))->GetArrayField(TEXT("results"));
    if (!TestEqual(TEXT("Isolated QA catalog"), Assets.Num(), 1)) return false;
    const auto Asset = Assets[0]->AsObject();
    if (!TestEqual(TEXT("Only known QA product can be installed by this test"), Asset->GetStringField(TEXT("id")), FString(TEXT("editor-bridge-fixture")))) return false;
    auto Install = MakeShared<FJsonObject>(); Install->SetStringField(TEXT("method"), TEXT("install"));
    Install->SetStringField(TEXT("assetId"), TEXT("editor-bridge-fixture")); Install->SetStringField(TEXT("source"), TEXT("Fab"));
    Install->SetStringField(TEXT("variantId"), TEXT("fixture-variant")); Install->SetStringField(TEXT("requestId"), FGuid::NewGuid().ToString(EGuidFormats::Digits));
    Install->SetBoolField(TEXT("confirmed"), false);
    TestFalse(TEXT("Unconfirmed native requests cannot install"), CyHubEditorClient::Send(Install)->GetBoolField(TEXT("ok")));
    Install->SetBoolField(TEXT("confirmed"), true);
    Response = CyHubEditorClient::Send(Install);
    if (!TestTrue(TEXT("Explicit editor action queues real hub import workflow"), Response->GetBoolField(TEXT("ok")))) { AddError(Response->GetStringField(TEXT("error"))); return false; }
    const FString Id = Response->GetObjectField(TEXT("result"))->GetStringField(TEXT("taskId"));
    auto Replay = CyHubEditorClient::Send(Install);
    TestEqual(TEXT("Replay returns original task, not another installation"), Replay->GetObjectField(TEXT("result"))->GetStringField(TEXT("taskId")), Id);
    const double End = FPlatformTime::Seconds() + 45;
    while (FPlatformTime::Seconds() < End)
    {
        auto Tasks = MakeShared<FJsonObject>(); Tasks->SetStringField(TEXT("method"), TEXT("tasks"));
        auto TaskResponse = CyHubEditorClient::Send(Tasks);
        if (!TaskResponse->GetBoolField(TEXT("ok"))) return false;
        for (auto& Value : TaskResponse->GetArrayField(TEXT("result")))
        {
            auto Task = Value->AsObject(); if (Task->GetStringField(TEXT("id")) != Id) continue;
            const FString Status = Task->GetStringField(TEXT("status"));
            if (Status == TEXT("Completed")) return true;
            if (Status == TEXT("Failed") || Status == TEXT("Canceled")) { AddError(TEXT("QA import failed: ") + Task->GetStringField(TEXT("message"))); return false; }
        }
        FPlatformProcess::Sleep(0.2f);
    }
    AddError(TEXT("QA import completion timeout")); return false;
}
#endif
