#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "CyHubNativeWorldLibrary.generated.h"
UCLASS()
class CYHUBNATIVEBRIDGE_API UCyHubNativeWorldLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="CyEngineHub")
    static bool Run(const FString& ConfigFile);
};
