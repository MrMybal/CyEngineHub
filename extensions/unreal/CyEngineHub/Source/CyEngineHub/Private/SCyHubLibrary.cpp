#include "SCyHubLibrary.h"
#include "CyHubEditorClient.h"
#include "Async/Async.h"
#if __has_include("AssetRegistry/AssetRegistryModule.h")
#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetRegistry/IAssetRegistry.h"
#else
#include "AssetRegistryModule.h"
#include "IAssetRegistry.h"
#endif
#include "Modules/ModuleManager.h"
#include "Misc/MessageDialog.h"
#include "Misc/Paths.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Input/SSearchBox.h"
#include "Widgets/Input/STextComboBox.h"
#include "Widgets/Layout/SScrollBox.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Notifications/SProgressBar.h"
#include "Widgets/Text/STextBlock.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonWriter.h"
#include "Policies/CondensedJsonPrintPolicy.h"

void SCyHubLibrary::Construct(const FArguments& Args)
{
    Status = TEXT("Connectez CyEngineHub pour parcourir votre bibliothèque.");
    ChildSlot [ SNew(SVerticalBox)
        + SVerticalBox::Slot().AutoHeight().Padding(10) [ SNew(STextBlock).Text(FText::FromString(TEXT("CYENGINEHUB · ALPHA"))) ]
        + SVerticalBox::Slot().AutoHeight().Padding(10, 0) [ SNew(STextBlock).Text(FText::FromString(FPaths::ConvertRelativePathToFull(FPaths::GetProjectFilePath()))).AutoWrapText(true) ]
        + SVerticalBox::Slot().AutoHeight().Padding(10) [ SNew(SHorizontalBox)
            + SHorizontalBox::Slot().FillWidth(1) [ SNew(SSearchBox).HintText(FText::FromString(TEXT("Rechercher un pack ou une collection…")))
                .OnTextCommitted_Lambda([this](const FText& Text, ETextCommit::Type) { Query = Text.ToString(); Offset = 0; Search(); }) ]
            + SHorizontalBox::Slot().AutoWidth().Padding(8, 0) [ SNew(SButton).Text(FText::FromString(TEXT("Connecter / Actualiser"))).IsEnabled_Lambda([this] { return !Busy; }).OnClicked_Lambda([this] { Search(); return FReply::Handled(); }) ] ]
        + SVerticalBox::Slot().AutoHeight().Padding(10, 0) [ SNew(STextBlock).Text_Lambda([this] { return FText::FromString(Status); }).AutoWrapText(true) ]
        + SVerticalBox::Slot().AutoHeight().Padding(10, 4) [ SNew(SProgressBar).Percent_Lambda([this]() -> TOptional<float> { return Busy ? TOptional<float>() : TOptional<float>(0.f); })
            .Visibility_Lambda([this] { return Busy ? EVisibility::Visible : EVisibility::Collapsed; }) ]
        + SVerticalBox::Slot().FillHeight(1).Padding(10) [ SAssignNew(Results, SScrollBox) ]
        + SVerticalBox::Slot().AutoHeight().Padding(10) [ SNew(SHorizontalBox)
            + SHorizontalBox::Slot().AutoWidth() [ SNew(SButton).Text(FText::FromString(TEXT("Précédent"))).IsEnabled_Lambda([this] { return !Busy && Offset > 0; }).OnClicked_Lambda([this] { Offset = FMath::Max(0, Offset - 40); Search(); return FReply::Handled(); }) ]
            + SHorizontalBox::Slot().AutoWidth().Padding(8, 0) [ SNew(SButton).Text(FText::FromString(TEXT("Suivant"))).IsEnabled_Lambda([this] { return !Busy && Offset + 40 < Total; }).OnClicked_Lambda([this] { Offset += 40; Search(); return FReply::Handled(); }) ]
            + SHorizontalBox::Slot().AutoWidth() [ SNew(SButton).Text(FText::FromString(TEXT("Actualiser le contenu"))).OnClicked_Lambda([this] { RefreshContent(); return FReply::Handled(); }) ] ]
        + SVerticalBox::Slot().AutoHeight().Padding(10) [ SNew(STextBlock).Text_Lambda([this] { return FText::FromString(TaskStatus); }).AutoWrapText(true) ]
        + SVerticalBox::Slot().AutoHeight().MaxHeight(170).Padding(10, 0) [ SNew(SScrollBox) + SScrollBox::Slot() [ SAssignNew(TaskRows, SVerticalBox) ] ]
        + SVerticalBox::Slot().AutoHeight().Padding(10) [ SNew(STextBlock).Text(FText::FromString(TEXT("Imports additifs uniquement. Conversions, scripts avancés et collections à préparer dans le Hub. Plugins : redémarrage requis après installation."))).AutoWrapText(true) ]
    ];
}

void SCyHubLibrary::Request(TSharedRef<FJsonObject> Args, TFunction<void(TSharedPtr<FJsonObject>)> Callback)
{
    if (Busy) return;
    Busy = true;
    TWeakPtr<SCyHubLibrary> Weak = SharedThis(this);
    FString RequestJson;
    FJsonSerializer::Serialize(Args, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&RequestJson));
    // UE 4.27 JSON shared pointers use non-atomic reference counts. Move plain
    // UTF-8/JSON data across threads, not shared JSON objects or copied Slate refs.
    Async(EAsyncExecution::ThreadPool, [Weak = MoveTemp(Weak), RequestJson = MoveTemp(RequestJson), Callback = MoveTemp(Callback)]() mutable
    {
        TSharedPtr<FJsonObject> WorkerArgs;
        FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestJson), WorkerArgs);
        const auto WorkerResponse = CyHubEditorClient::Send(WorkerArgs.ToSharedRef());
        FString ResponseJson;
        FJsonSerializer::Serialize(WorkerResponse.ToSharedRef(), TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&ResponseJson));
        AsyncTask(ENamedThreads::GameThread, [Weak = MoveTemp(Weak), ResponseJson = MoveTemp(ResponseJson), Callback = MoveTemp(Callback)]()
        {
            const auto Self = Weak.Pin(); if (!Self.IsValid()) return;
            Self->Busy = false;
            TSharedPtr<FJsonObject> Response;
            if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(ResponseJson), Response))
            { Self->Status = TEXT("Réponse locale illisible. Consultez le Hub."); return; }
            bool Ok = false;
            if (!Response->TryGetBoolField(TEXT("ok"), Ok) || !Ok)
            { Self->Status = Response->GetStringField(TEXT("error")); return; }
            Callback(Response);
        });
    });
}

void SCyHubLibrary::Search()
{
    if (Busy) return;
    auto Args = MakeShared<FJsonObject>(); Args->SetStringField(TEXT("method"), TEXT("search"));
    Args->SetStringField(TEXT("query"), Query); Args->SetStringField(TEXT("source"), Source); Args->SetNumberField(TEXT("offset"), Offset);
    Request(Args, [this](TSharedPtr<FJsonObject> Response)
    {
        auto Result = Response->GetObjectField(TEXT("result")); Total = static_cast<int32>(Result->GetNumberField(TEXT("total")));
        Connected = true; NextPoll = 0;
        const auto Project = Result->GetObjectField(TEXT("project"));
        Status = FString::Printf(TEXT("Connecté · %s · UE %s · %d pack(s) · page %d"), *Project->GetStringField(TEXT("name")), *Project->GetStringField(TEXT("engineVersion")), Total, Offset / 40 + 1);
        DrawResults(Result->GetArrayField(TEXT("results")));
    });
}

void SCyHubLibrary::DrawResults(const TArray<TSharedPtr<FJsonValue>>& Assets)
{
    Results->ClearChildren();
    for (const auto& Value : Assets)
    {
        const auto Asset = Value->AsObject();
        if (!Asset.IsValid()) continue;
        const auto Options = MakeShared<TArray<TSharedPtr<FString>>>();
        for (const auto& V : Asset->GetArrayField(TEXT("variants"))) Options->Add(MakeShared<FString>(V->AsObject()->GetStringField(TEXT("id"))));
        const auto Selected = MakeShared<FString>(Options->Num() > 0 ? *(*Options)[0] : FString());
        const FString Label = Asset->GetStringField(TEXT("name")) + TEXT(" · ") + Asset->GetStringField(TEXT("source")) + (Asset->GetBoolField(TEXT("downloaded")) ? TEXT(" · Téléchargé") : TEXT(""));
        Results->AddSlot().Padding(0, 3) [ SNew(SBorder).Padding(10) [ SNew(SVerticalBox)
            + SVerticalBox::Slot().AutoHeight() [ SNew(STextBlock).Text(FText::FromString(Label)).AutoWrapText(true) ]
            + SVerticalBox::Slot().AutoHeight().Padding(0, 6) [ SNew(SHorizontalBox)
                + SHorizontalBox::Slot().FillWidth(1) [ SNew(STextComboBox).OptionsSource(&Options.Get()).InitiallySelectedItem(Options->Num() ? (*Options)[0] : nullptr)
                    .OnSelectionChanged_Lambda([Selected, Options](TSharedPtr<FString> Choice, ESelectInfo::Type) { if (Choice.IsValid()) *Selected = *Choice; }) ]
                + SHorizontalBox::Slot().AutoWidth().Padding(8, 0) [ SNew(SButton).Text(FText::FromString(Options->Num() ? TEXT("Installer dans ce projet…") : TEXT("Pas de variante compatible")))
                    .IsEnabled_Lambda([this, Options] { return !Busy && Options->Num() > 0; })
                    .OnClicked_Lambda([this, Asset, Selected, Options] { Install(Asset, *Selected); return FReply::Handled(); }) ]
                + SHorizontalBox::Slot().AutoWidth() [ SNew(SButton).Text(FText::FromString(TEXT("Options dans le Hub…"))).IsEnabled_Lambda([this] { return !Busy; })
                    .OnClicked_Lambda([this, Asset]
                    {
                        auto Args = MakeShared<FJsonObject>(); Args->SetStringField(TEXT("method"), TEXT("review"));
                        Args->SetStringField(TEXT("assetId"), Asset->GetStringField(TEXT("id"))); Args->SetStringField(TEXT("source"), Asset->GetStringField(TEXT("source")));
                        Request(Args, [this](TSharedPtr<FJsonObject>) { Status = TEXT("Options préparées dans CyEngineHub. Aucune installation lancée."); });
                        return FReply::Handled();
                    }) ] ]
            + SVerticalBox::Slot().AutoHeight() [ SNew(STextBlock).Text(FText::FromString(Options->Num() ? TEXT("Variante compatible avec le moteur du projet") : TEXT("Pas de variante installable ici — consultez le Hub pour les conversions / autres fournisseurs."))).AutoWrapText(true) ]
        ] ];
    }
}

void SCyHubLibrary::Install(TSharedPtr<FJsonObject> Asset, const FString& Variant)
{
    if (Busy || Variant.IsEmpty()) return;
    const FString Prompt = TEXT("Installer « ") + Asset->GetStringField(TEXT("name")) + TEXT(" »\nVariante : ") + Variant + TEXT("\nProjet : ") + FPaths::ConvertRelativePathToFull(FPaths::GetProjectFilePath()) + TEXT("\n\nAucun fichier existant ne sera remplacé. Les tâches continuent dans le Hub si vous fermez cet onglet. Les nouveaux plugins nécessitent un redémarrage. Confirmer ?");
    if (FMessageDialog::Open(EAppMsgType::YesNo, FText::FromString(Prompt)) != EAppReturnType::Yes) return;
    const FString Key = Asset->GetStringField(TEXT("source")) + TEXT(":") + Asset->GetStringField(TEXT("id")) + TEXT(":") + Variant;
    FString* Id = InstallRequests.Find(Key);
    if (!Id) { InstallRequests.Add(Key, FGuid::NewGuid().ToString(EGuidFormats::Digits)); Id = InstallRequests.Find(Key); }
    auto Args = MakeShared<FJsonObject>(); Args->SetStringField(TEXT("method"), TEXT("install"));
    Args->SetStringField(TEXT("assetId"), Asset->GetStringField(TEXT("id"))); Args->SetStringField(TEXT("source"), Asset->GetStringField(TEXT("source")));
    Args->SetStringField(TEXT("variantId"), Variant); Args->SetStringField(TEXT("requestId"), *Id); Args->SetBoolField(TEXT("confirmed"), true);
    Request(Args, [this](TSharedPtr<FJsonObject> Response)
    {
        PendingTasks.Add(Response->GetObjectField(TEXT("result"))->GetStringField(TEXT("taskId")));
        Status = TEXT("Installation confirmée · suivi ci-dessous. Aucune relance automatique."); NextPoll = 0;
    });
}

void SCyHubLibrary::RefreshContent()
{
    auto& Registry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();
    Registry.ScanPathsSynchronous({ TEXT("/Game") }, true);
}

void SCyHubLibrary::Tick(const FGeometry& Geometry, const double Time, const float Delta)
{
    SCompoundWidget::Tick(Geometry, Time, Delta);
    if (Busy || Time < NextPoll || !Connected) return;
    NextPoll = Time + 2;
    auto Args = MakeShared<FJsonObject>(); Args->SetStringField(TEXT("method"), TEXT("tasks"));
    Request(Args, [this](TSharedPtr<FJsonObject> Response)
    {
        TaskStatus = TEXT("ACTIVITÉ DU PROJET"); TaskRows->ClearChildren(); bool Refresh = false;
        for (const auto& V : Response->GetArrayField(TEXT("result")))
        {
            auto Task = V->AsObject(); const FString Id = Task->GetStringField(TEXT("id"));
            const FString State = Task->GetStringField(TEXT("status")); double Progress = 0;
            const bool HasProgress = Task->TryGetNumberField(TEXT("progress"), Progress);
            const bool Complete = State == TEXT("Completed"), Failed = State == TEXT("Failed"), Canceled = State == TEXT("Canceled");
            const FString Label = Task->GetStringField(TEXT("title")) + TEXT(" · ") + (Complete ? TEXT("Terminé") : Failed ? TEXT("Échec") : Canceled ? TEXT("Annulé") : State);
            const FLinearColor Color = Complete ? FLinearColor(0.15f, 0.7f, 0.4f) : Failed ? FLinearColor(0.9f, 0.2f, 0.2f) : Canceled ? FLinearColor(1.f, 0.5f, 0.1f) : FLinearColor(0.2f, 0.5f, 0.9f);
            const TOptional<float> Percent = Complete ? TOptional<float>(1.f) : HasProgress ? TOptional<float>(static_cast<float>(Progress)) : TOptional<float>();
            TaskRows->AddSlot().AutoHeight().Padding(0, 3) [ SNew(SVerticalBox)
                + SVerticalBox::Slot().AutoHeight() [ SNew(STextBlock).Text(FText::FromString(Label)).ColorAndOpacity(Color).AutoWrapText(true) ]
                + SVerticalBox::Slot().AutoHeight() [ SNew(SProgressBar).Percent(Percent).FillColorAndOpacity(Color) ] ];
            if (State == TEXT("Completed") || State == TEXT("Failed") || State == TEXT("Canceled"))
            {
                if (Complete && PendingTasks.Contains(Id) && !FinishedTasks.Contains(Id)) Refresh = true;
                PendingTasks.Remove(Id); FinishedTasks.Add(Id);
            }
            else PendingTasks.Add(Id);
        }
        if (Refresh) { RefreshContent(); Status = TEXT("Installation terminée · contenu actualisé. Pour les plugins, redémarrez Unreal."); }
    });
}
