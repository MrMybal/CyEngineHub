namespace CyEngineHub.Desktop.ImGui;

internal enum ActionTone { Neutral, Start, Danger, Primary }

internal static class ActionColorPolicy
{
    internal static ActionTone Resolve(string? command, bool primary = false, bool danger = false)
    {
        if (danger) return ActionTone.Danger;
        // Classify command identifiers only, never asset names or translated labels.
        if (command is not null && !command.StartsWith("Cancel", StringComparison.Ordinal) &&
            (command.StartsWith("RequestRemove", StringComparison.Ordinal) || command.StartsWith("ConfirmRemove", StringComparison.Ordinal) ||
             command.StartsWith("RequestUninstall", StringComparison.Ordinal) || command.StartsWith("ConfirmUninstall", StringComparison.Ordinal) ||
             command.StartsWith("RequestDelete", StringComparison.Ordinal) || command.StartsWith("ConfirmDelete", StringComparison.Ordinal) ||
             command is "RemoveCommand" or "DeleteCommand" or "DeleteProjectProfileCommand" or "RequestUnregisterCommand" or "ConfirmUnregisterCommand" or "ApplyEngineAssociationCommand"))
            return ActionTone.Danger;
        if (command is "ToggleProjectCreatorCommand" or "ToggleEngineImporterCommand" or "PrepareUnrealUtilityCommand" or "ConfirmUtilityLaunchCommand" or
            "BuildEditorCommand" or "BuildPluginCommand" or "RunCommand" or "RunCrossCompileCommand" or "SetupCommand" ||
            command?.StartsWith("Launch", StringComparison.Ordinal) == true)
            return ActionTone.Start;
        return primary ? ActionTone.Primary : ActionTone.Neutral;
    }
}
