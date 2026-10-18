#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "CyHubNativeMeshLibrary.generated.h"
UCLASS()
class CYHUBNATIVEBRIDGE_API UCyHubNativeMeshLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="CyEngineHub")
    static bool Run(const FString& ConfigFile);
};
