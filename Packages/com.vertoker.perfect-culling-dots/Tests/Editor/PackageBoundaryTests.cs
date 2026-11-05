using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Runtime;
namespace PerfectCullingDOTS.Tests
{
    public class PackageBoundaryTests
    {
        [Test]
        public void RuntimeAssembliesDoNotLinkLegacyOrNativeBakers()
        {
            foreach (var assembly in new[] { typeof(CullingVolumeBlob).Assembly,typeof(CullingWorldSystem).Assembly,typeof(PerfectCullingDOTS.Rendering.EcsRenderVisibilityAdapter).Assembly })
            {
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    Assert.That(reference.Name, Is.Not.EqualTo("PerfectCullingRuntime").And.Not.EqualTo("PerfectCullingEditor"));
                }
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
            var root = Path.GetFullPath("Packages/com.vertoker.perfect-culling-dots");
            var native = Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)
                .Where(p=>new[] {".dll",".so",".dylib",".exe"}.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
            CollectionAssert.IsEmpty(native,"The clean consumer/Player build remains a separate delivery gate.");
        }
    }
}
