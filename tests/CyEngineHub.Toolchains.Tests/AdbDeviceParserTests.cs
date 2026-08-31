using CyEngineHub.Toolchains.Devices;

namespace CyEngineHub.Toolchains.Tests;

public sealed class AdbDeviceParserTests
{
    [Fact]
    public void ParserRecognizesQuestAndPreservesOfflineDevices()
    {
        var result = AdbDeviceParser.Parse(
        [
            "List of devices attached",
            "1WMHH12345 device product:eureka model:Quest_3 device:eureka transport_id:7",
            "emulator-5554 offline product:sdk_gphone model:Pixel_8 device:emu transport_id:9",
        ]);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsQuest);
        Assert.Equal("Quest 3", result[0].Model);
        Assert.Equal("offline", result[1].State);
        Assert.False(result[1].IsQuest);
    }
}
