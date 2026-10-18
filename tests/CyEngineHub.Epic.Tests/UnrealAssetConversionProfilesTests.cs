using CyEngineHub.Fab.Installation;

namespace CyEngineHub.Epic.Tests;

public sealed class UnrealAssetConversionProfilesTests
{
    private static UnrealAssetConversionProfileSet Profiles() => UnrealAssetConversionProfiles.Load(Path.Combine(
        AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "profiles", "ue-5.6-to-5.5.json"));
    private static UnrealAssetConversionInventoryItem Asset(string type = "Texture2D") => new("/Game/A.A", type, [], [], []);
    private static UnrealAssetConversionProfileSet Native57Profiles() => UnrealAssetConversionProfiles.Load(Path.Combine(
        AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "profiles", "ue-5.7-native.json"));
    private static UnrealAssetConversionPlan Plan(UnrealAssetConversionInventoryItem asset, string mode = "portable", bool consent = true) =>
        UnrealAssetConversionProfiles.Analyze(Profiles(), [asset], "5.6", "5.5", mode, consent);

    [Fact]
    public void BuiltInCatalogHasElevenExplicitVersionedTypeProfiles()
    {
        Assert.Equal(11, Profiles().Profiles.Length);
        Assert.All(Profiles().Profiles, p => { Assert.Equal("5.6", p.SourceVersion); Assert.Equal("5.5", p.TargetVersion); Assert.NotEmpty(p.Checks); });
    }
    [Theory]
    [InlineData("StaticMesh")]
    [InlineData("Blueprint")]
    public void NativeIsNeverClaimedAvailableWithoutAnAdapter(string type)
    {
        var plan = Plan(Asset(type), "native");
        Assert.False(plan.CanConvert); Assert.Equal("unsupported", plan.Assets[0].Status);
    }
    [Fact]
    public void NativeTextureRequiresInspectionAndNeverFallsBackToPortable()
    {
        Assert.False(Plan(Asset(), "native").CanConvert);
        var candidate = Asset() with { Features = ["native-texture-candidate"] };
        var plan = Plan(candidate, "native", consent: false);
        Assert.True(plan.CanConvert);
        Assert.Equal("native-texture-v1", plan.Assets[0].Adapter);
        Assert.Equal("adaptation-required", plan.Assets[0].Status);
        Assert.False(Plan(candidate with { Features = ["native-texture-candidate", "virtual-texture"] }, "native").CanConvert);
        Assert.False(Plan(candidate with { Dependencies = ["/Engine/CompositeTexture"] }, "native").CanConvert);
    }

    [Fact]
    public void NativeTextureAdapterCannotBeEnabledForUnvalidatedVersionPair()
    {
        var profile = Profiles().Profiles.Single(p => p.AssetType == "Texture2D") with { SourceVersion = "5.8" };
        var plan = UnrealAssetConversionProfiles.Analyze(new(1, [profile]), [Asset() with { Features = ["native-texture-candidate"] }], "5.8", "5.5", "native", false);
        Assert.False(plan.CanConvert);
    }
    [Theory]
    [InlineData("5.5")]
    [InlineData("5.3")]
    public void Native57GraphRequiresCompleteConvertibleDependencyClosure(string target)
    {
        var texture = Asset() with { Features = ["native-texture-candidate"] };
        var material = Asset("Material") with { Path = "/Game/M.M", Dependencies = ["/Game/A", "/Script/Engine"] };
        var instance = Asset("MaterialInstanceConstant") with { Path = "/Game/MI.MI", Dependencies = ["/Game/M"] };
        UnrealAssetConversionPlan Analyze(params UnrealAssetConversionInventoryItem[] assets) =>
            UnrealAssetConversionProfiles.Analyze(Native57Profiles(), assets, "5.7", target, "native", false);
        var plan = Analyze(texture, material, instance);
        Assert.True(plan.CanConvert);
        Assert.Equal(["native-texture-v1", "native-material-v1", "native-material-v1"], plan.Assets.Select(a => a.Adapter));
        Assert.False(Analyze(material, instance).CanConvert);
        Assert.False(Analyze(texture, material with { Dependencies = ["/ThirdParty/M"] }, instance).CanConvert);
        Assert.False(Analyze(texture with { Features = ["virtual-texture"] }, material, instance).CanConvert);
        Assert.False(Analyze(texture, material, instance, Asset("Blueprint") with { Path = "/Game/B.B" }).CanConvert);
        Assert.False(UnrealAssetConversionProfiles.Analyze(Native57Profiles(), [texture, material, instance], "5.7", target, "portable", true).CanConvert);
    }

    [Fact]
    public void Native57CatalogCannotEnableArbitraryAdaptersOrOtherTargets()
    {
        var p = Native57Profiles().Profiles.Single(p => p.AssetType == "Material" && p.TargetVersion == "5.5");
        Assert.False(UnrealAssetConversionProfiles.Analyze(new(1, [p with { NativeAdapter = "arbitrary-native" }]), [Asset("Material")], "5.7", "5.5", "native", true).CanConvert);
        Assert.False(UnrealAssetConversionProfiles.Analyze(new(1, [p with { TargetVersion = "5.4" }]), [Asset("Material")], "5.7", "5.4", "native", true).CanConvert);
    }
    [Theory]
    [InlineData("5.5")]
    [InlineData("5.3")]
    public void Native57WholePackRoutesAllKnownFamiliesButNotArbitraryGeneratedClasses(string target)
    {
        var texture=Asset() with {Features=["native-texture-candidate"]};
        var cube=Asset("TextureCube") with {Path="/Game/C.C",Features=["native-texture-candidate"]};
        var function=Asset("MaterialFunction") with {Path="/Game/F.F",Dependencies=["/Game/A"]};
        var material=Asset("Material") with {Path="/Game/M.M",Dependencies=["/Game/F","/Game/C"]};
        var mesh=Asset("StaticMesh") with {Path="/Game/S.S",Dependencies=["/Game/M"],Features=["nanite","multiple-lods"]};
        var world=Asset("World") with {Path="/Game/W.W",Dependencies=["/Game/S"]};
        var generated=Asset("BlueprintGeneratedClass") with {Path="/Game/W.W_C",Dependencies=["/Game/W"]};
        UnrealAssetConversionPlan Analyze(params UnrealAssetConversionInventoryItem[] items)=>UnrealAssetConversionProfiles.Analyze(Native57Profiles(),items,"5.7",target,"native",false);
        Assert.True(Analyze(texture,cube,function,material,mesh,world,generated).CanConvert);
        Assert.False(Analyze(texture,cube,function,material,mesh,generated).CanConvert);
        Assert.False(Analyze(texture,cube,function,material,mesh,world,generated with {Path="/Game/Arbitrary.Arbitrary_C"}).CanConvert);
        Assert.False(Analyze(texture,cube,function,material,mesh with {Dependencies=["/ThirdParty/M"]},world,generated).CanConvert);
    }
    [Theory]
    [InlineData("StaticMesh")]
    [InlineData("Texture2D")]
    [InlineData("SoundWave")]
    public void PortableRequiresExplicitConsent(string type)
    {
        Assert.False(Plan(Asset(type), consent: false).CanConvert);
        Assert.Equal("loss-confirmation-required", Plan(Asset(type), consent: false).Assets[0].Status);
        Assert.True(Plan(Asset(type)).CanConvert);
    }
    [Theory]
    [InlineData("Blueprint")]
    [InlineData("World")]
    [InlineData("Material")]
    [InlineData("MaterialInstanceConstant")]
    [InlineData("SkeletalMesh")]
    [InlineData("Skeleton")]
    [InlineData("AnimSequence")]
    [InlineData("NiagaraSystem")]
    [InlineData("DataTable")]
    public void LossConsentDoesNotEnableUnimplementedTypes(string type) => Assert.False(Plan(Asset(type)).CanConvert);

    [Theory]
    [InlineData("nanite")]
    [InlineData("multiple-lods")]
    [InlineData("virtual-texture")]
    public void FeaturesWithoutAdaptersBlock(string feature) => Assert.False(Plan(Asset() with { Features = [feature] }).CanConvert);

    [Theory]
    [InlineData("/Game/Material")]
    [InlineData("/ThirdPartyPlugin/Material")]
    public void DependenciesWithoutRelinkingBlock(string dependency) => Assert.False(Plan(Asset() with { Dependencies = [dependency] }).CanConvert);

    [Fact]
    public void UnknownVersionPairDoesNotReuseAnotherProfile() => Assert.False(UnrealAssetConversionProfiles.Analyze(
        Profiles(), [Asset()], "5.7", "5.5", "portable", true).CanConvert);

    [Fact]
    public void IncompleteInspectionBlocks() => Assert.False(Plan(Asset() with { InspectionErrors = ["No source data"] }).CanConvert);

    [Fact]
    public void OneUnsupportedAssetBlocksWholePack()
    {
        var plan = UnrealAssetConversionProfiles.Analyze(Profiles(), [Asset(), Asset("Blueprint") with { Path = "/Game/B.B" }], "5.6", "5.5", "portable", true);
        Assert.False(plan.CanConvert); Assert.Equal("lossy-confirmed", plan.Assets[0].Status);
    }

    [Fact]
    public void DuplicateProfilesAndEmptyInventoriesAreRejected()
    {
        var p = Profiles().Profiles[0];
        Assert.Throws<InvalidOperationException>(() => UnrealAssetConversionProfiles.Validate(new(1, [p, p with { Id = "other" }])));
        Assert.Throws<InvalidOperationException>(() => UnrealAssetConversionProfiles.Analyze(Profiles(), [], "5.6", "5.5", "portable", true));
    }

    [Fact]
    public void CustomJsonCannotEnableArbitraryAdapterOrRemoveConsentRequirement()
    {
        var p = Profiles().Profiles.Single(p => p.AssetType == "Texture2D");
        var custom = new UnrealAssetConversionProfileSet(1, [p with { NativeAdapter = "pretend-native", PortableAdapter = "arbitrary-code" }]);
        Assert.False(UnrealAssetConversionProfiles.Analyze(custom, [Asset()], "5.6", "5.5", "portable", true).CanConvert);
        custom = new(1, [p with { Losses = [] }]);
        Assert.False(UnrealAssetConversionProfiles.Analyze(custom, [Asset()], "5.6", "5.5", "portable", false).CanConvert);
    }
}
