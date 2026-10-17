using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Compatibility;

public sealed record ToolchainRemediation(string ButtonLabel, Uri? DownloadPage, string Instructions)
{
    public static ToolchainRemediation For(ToolchainCompatibilityCheck check, string engineVersion)
    {
        var version = Version.TryParse(engineVersion, out var parsed) ? $"{parsed.Major}.{parsed.Minor}" : null;
        string Epic(string page) => "https://dev.epicgames.com/documentation/en-us/unreal-engine/" + page +
            (version is null ? "" : "?application_version=" + version);
        var (url, instructions) = check.Kind switch
        {
            ToolchainKind.VisualStudio or ToolchainKind.Msvc => ("https://visualstudio.microsoft.com/downloads/",
                "Dans Visual Studio Installer, ajoutez Développement de jeux en C++ et le composant MSVC indiqué dans Requis. Conservez les versions utilisées par vos autres moteurs."),
            ToolchainKind.WindowsSdk => ("https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads-archive",
                "Choisissez le SDK indiqué dans Requis, pas automatiquement le plus récent. Un SDK valide mais non recommandé est un avertissement, pas une absence. Les anciens SDK peuvent ne plus être maintenus ; consultez les avertissements Microsoft avant installation."),
            ToolchainKind.LinuxToolchain => (Epic("linux-development-requirements-for-unreal-engine"),
                "Téléchargez la cross-toolchain correspondant exactement à cette version Unreal sur la page Epic, puis installez-la. Une toolchain d’un autre moteur ne la remplace pas."),
            ToolchainKind.AndroidSdk or ToolchainKind.AndroidNdk or ToolchainKind.CMake or ToolchainKind.Java or ToolchainKind.Adb =>
                (Epic("setting-up-android-sdk-and-ndk-for-unreal"),
                "Suivez la configuration Android de cette version Unreal. Dans Android Studio / SDK Manager, activez Show Package Details et installez les versions indiquées dans Requis (SDK, NDK, CMake). Respectez aussi la version Java prévue par le moteur."),
            ToolchainKind.Gradle => ((string?)null,
                "Le wrapper Gradle appartient à cette installation Unreal. Ne téléchargez pas une version Gradle globale au hasard. Vérifiez/réparez les fichiers du moteur officiel ; pour les sources, utilisez Setup dans Options d’installation, puis relancez l’analyse."),
            ToolchainKind.DotNet => (Epic("hardware-and-software-specifications-for-unreal-engine"),
                "Privilégiez le runtime .NET livré dans Engine/Binaries/ThirdParty/DotNet de ce moteur. S’il manque, réparez l’installation ou utilisez Setup pour les sources. Consultez les prérequis de cette version avant de télécharger un SDK système."),
            _ => (Epic("hardware-and-software-specifications-for-unreal-engine"), "Consultez les prérequis officiels correspondant à ce moteur."),
        };
        return new(url is null ? "Réparer le moteur…" : "Obtenir le prérequis…", url is null ? null : new Uri(url), instructions);
    }
}
