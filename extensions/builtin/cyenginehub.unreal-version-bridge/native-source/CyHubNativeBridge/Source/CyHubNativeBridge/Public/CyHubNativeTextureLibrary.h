#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "CyHubNativeTextureLibrary.generated.h"

UCLASS()
class CYHUBNATIVEBRIDGE_API UCyHubNativeTextureLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="CyEngineHub")
    static bool Run(const FString& ConfigFile);
};
