using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class UiLayoutTests
{
    [Fact]
    public void OptionsTree_countsOneGroupPerRow()
    {
        var n = Count(
            new UiBox(
                new UiRow(new UiLabel("Camera"), new UiItem("Standard"), new UiItem("Reverse")),
                new UiRow(new UiLabel("Vibration"), new UiItem("On"), new UiItem("Off")),
                new UiRow(new UiLabel("Audio"), new UiItem("English"), new UiItem("Japanese")),
                new UiRow(new UiLabel("Exit To Title"), new UiItem("Yes"), new UiItem("No"))));
        Assert.Equal(4, n);
    }

    [Fact]
    public void LoadTree_countsOneGroupPerSlot()
    {
        var n = Count(
            new UiBox(
                new UiItem(new UiLabel("1:Ghost Pantry"),
                    new UiRow(new UiLabel("3h 13m 00s"), new UiLabel("Justin / Sue / Feena"))),
                new UiItem(new UiLabel("2:Dight Inn"),
                    new UiRow(new UiLabel("1h 02m 11s"), new UiLabel("Justin / Feena"))),
                new UiItem(new UiLabel("3:-- Empty --")),
                new UiItem(new UiLabel("4:-- Empty --")),
                new UiItem(new UiLabel("5:-- Empty --"))),
            new UiBox(7, new UiLabel("Select location")));
        Assert.Equal(5, n);
    }

    private static int Count(params UiWidget[] body)
    {
        var ui = new GameUi();
        return ui.FocusCount(body);
    }
}
