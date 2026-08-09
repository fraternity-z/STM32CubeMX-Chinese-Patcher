using System.Text;
using STM32CubeMX.ChinesePatcher.Core.Services;
using STM32CubeMX.ChinesePatcher.Tests.Support;

namespace STM32CubeMX.ChinesePatcher.Tests;

[TestClass]
public sealed class AtomicFileTransactionTests
{
    [TestMethod]
    public void Commit_RejectsTargetChangedAfterStageWithoutOverwritingExternalContent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "STM32CubeMX-Chinese-Patcher.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, "target.txt");

        try
        {
            File.WriteAllText(targetPath, "original", Encoding.UTF8);
            using var transaction = new AtomicFileTransaction();
            transaction.Stage(targetPath, Encoding.UTF8.GetBytes("patched"));
            File.WriteAllText(targetPath, "external", Encoding.UTF8);

            var exception = TestAssert.Throws<IOException>(() =>
                transaction.Commit(CancellationToken.None));

            StringAssert.Contains(exception.Message, "其他程序修改");
            Assert.AreEqual("external", File.ReadAllText(targetPath, Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Stage_RejectsDuplicateTarget()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "STM32CubeMX-Chinese-Patcher.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, "target.txt");

        try
        {
            using var transaction = new AtomicFileTransaction();
            transaction.Stage(targetPath, [1]);

            TestAssert.Throws<InvalidOperationException>(() => transaction.Stage(targetPath, [2]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void InstallationOperationLock_BlocksConcurrentOwnerForSameInstallation()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var operationLock = InstallationOperationLock.Acquire(rootPath);

        var exception = Task.Run(() => TestAssert.Throws<InvalidOperationException>(() =>
            InstallationOperationLock.Acquire(rootPath))).GetAwaiter().GetResult();

        StringAssert.Contains(exception.Message, "另一个汉化操作");
    }
}
