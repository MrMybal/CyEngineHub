#pragma once
#include "CoreMinimal.h"
#include "Dom/JsonObject.h"

namespace CyHubEditorClient
{
    FString AccountPipeName();
    // Blocking bounded IPC; called only on a worker, never on the editor thread.
    TSharedPtr<FJsonObject> Send(const TSharedRef<FJsonObject>& Request);
}
