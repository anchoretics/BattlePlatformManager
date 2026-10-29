using BPM.Infrastructure.Win32;
using FluentAssertions;
using Xunit;

namespace BPM.Infrastructure.Tests;

public class BrowserAuthenticationPolicyTests
{
    internal const string Url = "https://account.battlenet.com.cn/login?flow=a&ref=b&return=c&returnHttps=d&secure=e&state=f";

    [Fact]
    public void AcceptsCompleteEntryWithEncodedReturnAddress()
    {
        BrowserAuthenticationPolicy.IsAllowed(Url + "%2B%2F%3D").Should().BeTrue();
    }

    [Fact]
    public void ExtractsOriginalUrlFromShellDisplayNameWithoutDecodingQuery()
    {
        var raw = Url + "%2B%2F%3D";
        BrowserAuthenticationPolicy.Extract("\"" + raw + "\"").Should().Be(raw);
        BrowserAuthenticationPolicy.Extract("not a URL").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("'")]
    public void PreservesLiteralQueryPunctuationRatherThanTruncatingSignedRequest(string wrapper)
    {
        var raw = Url + "literal'quote)";
        BrowserAuthenticationPolicy.Extract(wrapper + raw + wrapper).Should().Be(raw);
        BrowserAuthenticationPolicy.IsAllowed(raw).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://account.battlenet.com.cn")]
    [InlineData("https://account.battlenet.com.cn.attacker.test")]
    [InlineData("https://account.battlenet.com.cn:444")]
    [InlineData("https://name@account.battlenet.com.cn")]
    [InlineData("file://account.battlenet.com.cn")]
    public void RejectsUntrustedSchemeHostPortOrUserInfo(string origin)
    {
        BrowserAuthenticationPolicy.IsAllowed(origin + Url[Url.IndexOf("/login", StringComparison.Ordinal)..]).Should().BeFalse();
    }

    [Theory]
    [InlineData("#fragment")]
    [InlineData("&state=duplicate")]
    [InlineData("\r\nargument")]
    [InlineData("\" --argument")]
    [InlineData("\\path")]
    public void RejectsAmbiguousOrCommandLinePayload(string suffix) =>
        BrowserAuthenticationPolicy.IsAllowed(Url + suffix).Should().BeFalse();

    [Theory]
    [InlineData("https://account.battlenet.com.cn/login?state=abc")]
    [InlineData("https://oauth.g.mkey.163.com/login?state=abc")]
    public void RejectsIncompleteLogUrls(string value) => BrowserAuthenticationPolicy.IsAllowed(value).Should().BeFalse();

    [Fact]
    public void BoundsMemoryWithoutTruncatingLongUrls()
    {
        BrowserAuthenticationPolicy.IsAllowed(Url + new string('a', 3000)).Should().BeTrue();
        BrowserAuthenticationPolicy.IsAllowed(Url + new string('a', 16384)).Should().BeFalse();
    }

    [Theory]
    [InlineData(-6, false)]
    [InlineData(-5, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void RejectsStaleAndFutureEvents(int offsetSeconds, bool allowed)
    {
        var now = DateTime.UtcNow;
        BrowserAuthenticationPolicy.IsFresh(now.AddSeconds(offsetSeconds), now).Should().Be(allowed);
    }
}
