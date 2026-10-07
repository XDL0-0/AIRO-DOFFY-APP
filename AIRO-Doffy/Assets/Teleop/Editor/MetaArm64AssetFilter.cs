using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace Doffy.Editor
{
    /// <summary>Filters only the generated SDK archive for the dedicated ARM64 Meta build.</summary>
    public sealed class MetaArm64AssetFilter : IPostGenerateGradleAndroidProject
    {
        private const string Arm32Assets = "assets/lib/armeabi-v7a/";
        internal static bool Enabled;
        internal static string BackupDirectory;
        private static string generatedArchive;
        private static string originalArchive;

        public int callbackOrder => 100000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!Enabled) return;
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64 ||
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != "com.AIROLab.AIRODOFFY")
                throw new BuildFailedException("SDK asset filtering requires the ARM64 Meta update build.");

            generatedArchive = Path.Combine(path, "libs", "OVRPlugin.aar");
            if (!File.Exists(generatedArchive))
                throw new BuildFailedException("Generated OVRPlugin.aar was not found: " + generatedArchive);
            Directory.CreateDirectory(BackupDirectory);
            originalArchive = Path.Combine(BackupDirectory, "OVRPlugin.original.aar");
            File.Copy(generatedArchive, originalArchive, true);
            string filteredArchive = Path.Combine(BackupDirectory, "OVRPlugin.arm64-assets.aar");

            int removedFiles = 0;
            using (var archive = ZipFile.OpenRead(originalArchive))
            using (var output = File.Create(filteredArchive))
            using (var filtered = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith(Arm32Assets, StringComparison.Ordinal))
                    {
                        if (!entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        {
                            string counterpartName = entry.FullName.Replace(Arm32Assets, "assets/lib/arm64-v8a/");
                            var counterpart = archive.GetEntry(counterpartName);
                            if (!entry.FullName.EndsWith(".so", StringComparison.Ordinal) ||
                                !HasElfClass(entry, 1) || counterpart == null || !HasElfClass(counterpart, 2))
                                throw new BuildFailedException("Unexpected SDK asset or missing ARM64 counterpart: " + entry.FullName);
                            removedFiles++;
                        }
                        continue;
                    }
                    var retained = filtered.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                    retained.LastWriteTime = entry.LastWriteTime;
                    retained.ExternalAttributes = entry.ExternalAttributes;
                    using (var input = entry.Open())
                    using (var destination = retained.Open())
                        input.CopyTo(destination);
                }
            }
            if (removedFiles != 13)
                throw new BuildFailedException("Expected 13 SDK ARM32 assets, found " + removedFiles + ". Review the SDK before filtering.");
            File.Copy(filteredArchive, generatedArchive, true);
            Debug.Log("DOFFY ARM64 asset filter removed 13 SDK ARM32 libraries from generated OVRPlugin.aar.");
        }

        private static bool HasElfClass(ZipArchiveEntry entry, byte expectedClass)
        {
            using (var stream = entry.Open())
                return stream.ReadByte() == 0x7f && stream.ReadByte() == 'E' &&
                    stream.ReadByte() == 'L' && stream.ReadByte() == 'F' && stream.ReadByte() == expectedClass;
        }

        internal static void RestoreGeneratedArchive()
        {
            if (originalArchive == null || generatedArchive == null) return;
            File.Copy(originalArchive, generatedArchive, true);
            originalArchive = null;
            generatedArchive = null;
            Debug.Log("DOFFY restored the unmodified generated OVRPlugin.aar after the build.");
        }
    }
}
