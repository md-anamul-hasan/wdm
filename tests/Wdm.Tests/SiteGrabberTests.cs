using Wdm.Core.Engine;
using Xunit;

namespace Wdm.Tests;

public class SiteGrabberTests
{
    [Fact]
    public async Task GrabLinksAsync_DirectMediaUrl_ReturnsImmediateMediaItem()
    {
        var grabber = new SiteGrabberService();
        var results = await grabber.GrabLinksAsync("https://example.com/assets/video_clip.mp4");

        Assert.Single(results);
        Assert.Equal("video_clip.mp4", results[0].FileName);
        Assert.Equal("Video", results[0].Category);
        Assert.Equal(".mp4", results[0].Extension);
        Assert.True(results[0].IsSelected);
    }

    [Fact]
    public async Task GrabLinksAsync_DirectArchiveUrl_ReturnsCompressedCategory()
    {
        var grabber = new SiteGrabberService();
        var results = await grabber.GrabLinksAsync("https://download.server.net/packages/app-v1.0.zip");

        Assert.Single(results);
        Assert.Equal("app-v1.0.zip", results[0].FileName);
        Assert.Equal("Compressed", results[0].Category);
        Assert.Equal(".zip", results[0].Extension);
    }

    [Fact]
    public async Task GrabLinksAsync_EmptyUrl_ReturnsEmptyList()
    {
        var grabber = new SiteGrabberService();
        var results = await grabber.GrabLinksAsync("");

        Assert.Empty(results);
    }
}
