using FluentAssertions;
using Deltanium.Blocker.Core;
using Xunit;

namespace deltanium_blocker.Tests.Services;

public class BuildInfoTests
{
    [Fact]
    public void Reports_csproj_version_and_build_time()
    {
        BuildInfo.Version.Should().Be("1.3.0");
        BuildInfo.BuiltAtUtc.Should().NotBeNullOrEmpty();
        BuildInfo.Describe("x").Should().NotBeNull();
    }
}
