#include "Modules/ModuleManager.h"
#include "Framework/Docking/TabManager.h"
#include "Widgets/Docking/SDockTab.h"
#include "ToolMenus.h"
#include "SCyHubLibrary.h"

class FCyEngineHubModule : public IModuleInterface
{
public:
    virtual bool SupportsDynamicReloading() override { return false; }
    virtual void StartupModule() override
    {
        if (IsRunningCommandlet()) return;
        FGlobalTabmanager::Get()->RegisterNomadTabSpawner(TEXT("CyEngineHub"), FOnSpawnTab::CreateLambda([](const FSpawnTabArgs&)
        { return SNew(SDockTab).TabRole(ETabRole::NomadTab) [ SNew(SCyHubLibrary) ]; }))
            .SetDisplayName(FText::FromString(TEXT("CyEngineHub · Alpha"))).SetMenuType(ETabSpawnerMenuType::Hidden);
        UToolMenus::RegisterStartupCallback(FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FCyEngineHubModule::RegisterMenus));
    }
    virtual void ShutdownModule() override
    {
        if (IsRunningCommandlet()) return;
        UToolMenus::UnRegisterStartupCallback(this);
        UToolMenus::UnregisterOwner(this);
        FGlobalTabmanager::Get()->UnregisterNomadTabSpawner(TEXT("CyEngineHub"));
    }
private:
    void RegisterMenus()
    {
        FToolMenuOwnerScoped Owner(this);
        auto* Menu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.MainMenu.Window"));
        Menu->FindOrAddSection(TEXT("CyEngineHub")).AddMenuEntry(TEXT("CyEngineHub"), FText::FromString(TEXT("CyEngineHub · Alpha")),
            FText::FromString(TEXT("Installer vos packs CyEngineHub dans le projet ouvert")), FSlateIcon(),
            FUIAction(FExecuteAction::CreateLambda([] { FGlobalTabmanager::Get()->TryInvokeTab(FTabId(FName(TEXT("CyEngineHub")))); })));
    }
};
IMPLEMENT_MODULE(FCyEngineHubModule, CyEngineHub)
