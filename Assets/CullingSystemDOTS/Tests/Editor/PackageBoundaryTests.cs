using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Runtime;
namespace CullingSystemDOTS.Tests
{
    public class PackageBoundaryTests
    {
        [Test]
        public void RuntimeAssembliesDoNotLinkLegacyOrNativeBakers()
        {
            foreach (var assembly in new[] { typeof(CullingVolumeBlob).Assembly,typeof(CullingWorldSystem).Assembly,typeof(CullingSystemDOTS.Rendering.EcsRenderVisibilityAdapter).Assembly })
            {
                foreach (var method in assembly.GetTypes().SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly)))
                {
                    Assert.That(method.GetCustomAttribute<DllImportAttribute>(), Is.Null,method.DeclaringType+"."+method.Name);
                    // Unity's Jobs IL postprocessor adds an Editor-only EarlyInit to Editor DLLs.
                    // Player isolation is tested with an actual consumer build, not this Editor binary.
                    foreach (var attribute in method.GetCustomAttributesData().Where(a=>a.AttributeType.Namespace?.StartsWith("UnityEditor",StringComparison.Ordinal) == true))
                        Assert.That(method.DeclaringType.Name, Does.StartWith("__JobReflectionRegistrationOutput__"),method.Name+" -> "+attribute.AttributeType);
                }
            }
        }
        [Test]
        public void DistributablePackageContainsNoNativeBakeBinaries()
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(UnityEditor.AssetDatabase.GUIDToAssetPath("8be568d3ba746de43866684d8d6c2ac9")));
            var native = Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)
                .Where(p=>new[] {".dll",".so",".dylib",".exe"}.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
            CollectionAssert.IsEmpty(native,"The clean consumer/Player build remains a separate delivery gate.");
        }
    }
}
