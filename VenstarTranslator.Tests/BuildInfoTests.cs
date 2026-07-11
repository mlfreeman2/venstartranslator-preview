using Microsoft.AspNetCore.Mvc;

using VenstarTranslator.Controllers;
using VenstarTranslator.Services;

using Xunit;

namespace VenstarTranslator.Tests;

public class BuildInfoTests
{
    [Fact]
    public void Version_IsNeverNullOrEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Version));
    }

    [Fact]
    public void Commit_IsNeverNullOrEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Commit));
    }

    [Fact]
    public void UnstampedBuild_FallsBackToDevLocal()
    {
        // The test build is unstamped (csproj defaults), so the fallbacks apply.
        Assert.Equal("dev", BuildInfo.Version);
        Assert.Equal("local", BuildInfo.Commit);
    }

    [Fact]
    public void ApiVersion_ReturnsVersionAndCommit()
    {
        // GetVersion touches no injected dependencies, so nulls are safe here.
        var controller = new API(null, null, null, null, null, null, null);

        var result = controller.GetVersion() as JsonResult;

        Assert.NotNull(result);
        var payload = result.Value;
        var versionProp = payload.GetType().GetProperty("version").GetValue(payload) as string;
        var commitProp = payload.GetType().GetProperty("commit").GetValue(payload) as string;
        Assert.False(string.IsNullOrWhiteSpace(versionProp));
        Assert.False(string.IsNullOrWhiteSpace(commitProp));
    }
}
