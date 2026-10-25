#pragma once
#include "CoreMinimal.h"
#include "Widgets/SCompoundWidget.h"
#include "Widgets/DeclarativeSyntaxSupport.h"
#include "Dom/JsonObject.h"

class SScrollBox;
class SVerticalBox;
class SCyHubLibrary : public SCompoundWidget
{
public:
    SLATE_BEGIN_ARGS(SCyHubLibrary) {} SLATE_END_ARGS()
    void Construct(const FArguments& Args);
    virtual void Tick(const FGeometry&, const double Time, const float Delta) override;
private:
    FString Query, Source, Status, TaskStatus;
    int32 Offset = 0, Total = 0;
    bool Busy = false;
    bool Connected = false;
    double NextPoll = 0;
    TSharedPtr<SScrollBox> Results;
    TSharedPtr<SVerticalBox> TaskRows;
    TSet<FString> PendingTasks;
    TSet<FString> FinishedTasks;
    TMap<FString, FString> InstallRequests;
    void Search();
    void Install(TSharedPtr<FJsonObject> Asset, const FString& Variant);
    void RefreshContent();
    void Request(TSharedRef<FJsonObject> Args, TFunction<void(TSharedPtr<FJsonObject>)> Callback);
    void DrawResults(const TArray<TSharedPtr<FJsonValue>>& Assets);
};
