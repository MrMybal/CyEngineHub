#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "CyHubNativeMaterialLibrary.generated.h"

UCLASS()
class CYHUBNATIVEBRIDGE_API UCyHubNativeMaterialLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="CyEngineHub")
    static bool Run(const FString& ConfigFile);
};
