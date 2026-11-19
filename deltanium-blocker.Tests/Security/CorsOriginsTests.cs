using Deltanium.Blocker.Security;
using FluentAssertions;
using Xunit;

namespace Deltanium.Blocker.Tests.Security;

public class CorsOriginsTests
{
    [Fact]
    public void Defaults_ToThisNetworksWebApp()
    {
        CorsOrigins.Resolve(null, "").Should().Equal("https://app.deltanium.com");
        CorsOrigins.Resolve(null, "deltanium-test").Should().Equal("https://testnet-app.deltanium.com");
    }

    [Fact]
    public void Configured_Wins_Trimmed_AndDeduplicated()
    {
        CorsOrigins.Resolve(new[] { " https://x.example/ ", "https://X.example", "" }, "")
            .Should().Equal("https://x.example");
    }
}
