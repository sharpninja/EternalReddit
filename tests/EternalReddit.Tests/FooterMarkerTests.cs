namespace EternalReddit.Tests;

public class FooterMarkerTests
{
    // The EternalSocial gateway replaces this marker with the estate's pinned footer
    // on every proxied HTML page. It must stay in the host template verbatim.
    private const string Marker = "<!--ETERNALSOCIAL-FOOTER-->";

    [Fact]
    public void Host_page_template_carries_the_gateway_footer_marker()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EternalReddit.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var index = Path.Combine(dir!.FullName, "src", "EternalReddit.Client", "wwwroot", "index.html");
        Assert.True(File.Exists(index), $"missing {index}");
        var html = File.ReadAllText(index);
        Assert.Contains(Marker, html);
        // Templates reserve footer space via the gateway-published height variable
        // (collapses to zero when the app is reached without the gateway).
        Assert.Contains("var(--es-footer-h", html);
    }
}
