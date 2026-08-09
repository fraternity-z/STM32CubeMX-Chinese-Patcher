using STM32CubeMX.ChinesePatcher.Core.Models;
using STM32CubeMX.ChinesePatcher.Tests.Support;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class PayloadBundleTests
{
    [TestMethod]
    public void Constructor_ComputesStableUppercaseHashes()
    {
        var payload = new FakePayloadProvider().GetPayload();

        Assert.AreEqual(64, payload.AgentJarSha256.Length);
        Assert.AreEqual(payload.AgentJarSha256.ToUpperInvariant(), payload.AgentJarSha256);
        Assert.AreEqual(64, payload.DictionarySha256.Length);
    }

    [TestMethod]
    public void Constructor_RejectsEmptyAgent()
    {
        TestAssert.Throws<ArgumentException>(() => new PayloadBundle([], [1]));
    }

    [TestMethod]
    public void Constructor_RejectsEmptyDictionary()
    {
        TestAssert.Throws<ArgumentException>(() => new PayloadBundle([1], []));
    }

    [TestMethod]
    public void OperationResultFactoriesPreserveDetails()
    {
        var success = OperationResult.Success("ok", "detail");
        var failure = OperationResult.Failure("bad");

        Assert.IsTrue(success.Succeeded);
        CollectionAssert.Contains(success.Details.ToArray(), "detail");
        Assert.IsFalse(failure.Succeeded);
    }
}
