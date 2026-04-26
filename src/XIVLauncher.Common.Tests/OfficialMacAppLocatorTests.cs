using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using XIVLauncher.Common.Game.OfficialMacApp;

namespace XIVLauncher.Common.Tests
{
    [TestClass]
    public class OfficialMacAppLocatorTests
    {
        private readonly List<string> temporaryRoots = new();

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var temporaryRoot in temporaryRoots)
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
        }

        [TestMethod]
        public void TryResolveAcceptsOfficialBundle()
        {
            var root = CreateOfficialBundle();
            var result = OfficialMacAppLocator.TryResolve(root);

            Assert.IsNotNull(result);
            var expectedWinePrefix = Path.Combine(
                root.FullName,
                "Contents",
                "SharedSupport",
                "finalfantasyxiv",
                "support",
                "published_Final_Fantasy");
            var expectedGameRoot = Path.Combine(
                expectedWinePrefix,
                "drive_c",
                "Program Files (x86)",
                "SquareEnix",
                "FINAL FANTASY XIV - A Realm Reborn");
            var expectedWineExecutable = Path.Combine(
                root.FullName,
                "Contents",
                "SharedSupport",
                "finalfantasyxiv",
                "FINAL FANTASY XIV ONLINE",
                "wine");

            Assert.AreEqual(root.FullName, result.AppBundle.FullName);
            Assert.AreEqual(expectedGameRoot, result.GameRoot.FullName);
            Assert.AreEqual(expectedWineExecutable, result.WineExecutable.FullName);
            Assert.AreEqual(expectedWinePrefix, result.WinePrefix.FullName);
        }

        [TestMethod]
        public void TryResolveRejectsWrongBundleIdentifier()
        {
            var root = CreateOfficialBundle(bundleIdentifier: "example.not.ffxiv");

            Assert.IsNull(OfficialMacAppLocator.TryResolve(root));
        }

        [TestMethod]
        public void TryResolveRejectsMissingGameFolders()
        {
            var root = CreateOfficialBundle(createGameFolders: false);

            Assert.IsNull(OfficialMacAppLocator.TryResolve(root));
        }

        private DirectoryInfo CreateOfficialBundle(
            string bundleIdentifier = "com.square-enix.finalfantasyxiv",
            bool createGameFolders = true)
        {
            var temporaryRoot = Path.Combine(Path.GetTempPath(), "OfficialMacAppLocatorTests", Guid.NewGuid().ToString("N"));
            temporaryRoots.Add(temporaryRoot);

            var appBundle = new DirectoryInfo(Path.Combine(temporaryRoot, "FINAL FANTASY XIV ONLINE.app"));
            var contents = Directory.CreateDirectory(Path.Combine(appBundle.FullName, "Contents"));
            WriteInfoPlist(Path.Combine(contents.FullName, "Info.plist"), bundleIdentifier);

            var sharedSupport = Path.Combine(appBundle.FullName, "Contents", "SharedSupport", "finalfantasyxiv");
            var wineDirectory = Directory.CreateDirectory(Path.Combine(sharedSupport, "FINAL FANTASY XIV ONLINE"));
            File.WriteAllText(Path.Combine(wineDirectory.FullName, "wine"), string.Empty);

            var winePrefix = Directory.CreateDirectory(Path.Combine(sharedSupport, "support", "published_Final_Fantasy"));
            var gameRoot = Path.Combine(
                winePrefix.FullName,
                "drive_c",
                "Program Files (x86)",
                "SquareEnix",
                "FINAL FANTASY XIV - A Realm Reborn");

            if (createGameFolders)
            {
                Directory.CreateDirectory(Path.Combine(gameRoot, "boot"));
                Directory.CreateDirectory(Path.Combine(gameRoot, "game"));
            }

            return appBundle;
        }

        private static void WriteInfoPlist(string path, string bundleIdentifier)
        {
            File.WriteAllText(path,
                $"""
                 <?xml version="1.0" encoding="UTF-8"?>
                 <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                 <plist version="1.0">
                 <dict>
                     <key>CFBundleIdentifier</key>
                     <string>{bundleIdentifier}</string>
                 </dict>
                 </plist>
                 """);
        }
    }
}
