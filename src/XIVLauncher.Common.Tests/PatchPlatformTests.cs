using Microsoft.VisualStudio.TestTools.UnitTesting;
using XIVLauncher.Common.Game.Patch;

namespace XIVLauncher.Common.Tests
{
    [TestClass]
    public class PatchPlatformTests
    {
        [TestMethod]
        public void GetPatchRouteKeepsWin32ForCurrentSupportedPlatforms()
        {
            Assert.AreEqual("win32", PatchPlatform.GetPatchRoute(Platform.Win32));
            Assert.AreEqual("win32", PatchPlatform.GetPatchRoute(Platform.Win32OnLinux));
            Assert.AreEqual("win32", PatchPlatform.GetPatchRoute(Platform.Linux));
        }

        [TestMethod]
        public void GetPatchRouteUsesWin32ForOfficialMacWrapperInitialSupport()
        {
            Assert.AreEqual("win32", PatchPlatform.GetPatchRoute(Platform.Mac));
        }
    }
}
