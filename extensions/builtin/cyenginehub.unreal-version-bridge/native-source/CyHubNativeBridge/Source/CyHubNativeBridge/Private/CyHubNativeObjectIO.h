#pragma once
#include "CoreMinimal.h"
#include "Dom/JsonObject.h"
namespace CyHubObjectIO
{
TSharedRef<FJsonObject> Properties(UObject* Object, bool All = false);
TSharedRef<FJsonObject> StructProperties(UStruct* Type, const void* Object, const void* Defaults, UObject* Owner);
bool Apply(UObject* Object, const TSharedPtr<FJsonObject>& Fields, TArray<TSharedPtr<FJsonValue>>& Adaptations, FString& Error);
bool ApplyStruct(UStruct* Type, void* Object, UObject* Owner, const TSharedPtr<FJsonObject>& Fields, TArray<TSharedPtr<FJsonValue>>& Adaptations, FString& Error);
bool Compare(UObject* Object, const TSharedPtr<FJsonObject>& Fields, FString& Error);
bool CompareStruct(UStruct* Type, const void* Object, UObject* Owner, const TSharedPtr<FJsonObject>& Fields, FString& Error);
}
