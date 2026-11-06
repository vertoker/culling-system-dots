"""Create an independent delivery consumer; never launches Unity or installs CLI tooling.

Open the returned project normally in Unity6000.6.8f1. Tests and builds run through
that project's explicitly selected Coplay MCP instance. An existing destination is
never overwritten, including a partial project left for inspection after a failure.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil


def create(project_root: Path, destination: Path) -> Path:
    project_root, destination = project_root.resolve(), destination.resolve()
    allowed = (project_root / ".superpowers/consumers").resolve()
    if allowed not in destination.parents or destination.exists():
        raise ValueError("Destination must be new and inside this project's .superpowers/consumers.")
    package_names = ("com.vertoker.perfect-culling-dots", "com.unity.entities.graphics")
    sample = project_root / "Packages/com.vertoker.perfect-culling-dots/Samples~/Basic"
    if not (sample / "Basic.unity").is_file():
        raise ValueError("Generate and package the complete Basic sample first.")
    destination.mkdir(parents=True)
    packages = destination / "Packages"
    packages.mkdir()
    for name in package_names:
        shutil.copytree(project_root / "Packages" / name, packages / name)
    original = json.loads((project_root / "Packages/manifest.json").read_text(encoding="utf-8-sig"))["dependencies"]
    dependencies = {name: original[name] for name in (
        "com.coplaydev.unity-mcp", "com.unity.entities", "com.unity.collections", "com.unity.burst",
        "com.unity.mathematics", "com.unity.physics", "com.unity.render-pipelines.universal",
        "com.unity.test-framework", "com.unity.test-framework.performance")}
    dependencies.update({name: version for name, version in original.items() if name.startswith("com.unity.modules.")})
    manifest = {"dependencies": dependencies, "testables": ["com.vertoker.perfect-culling-dots"]}
    (packages / "manifest.json").write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
    settings = destination / "ProjectSettings"
    settings.mkdir()
    for name in ("ProjectVersion.txt", "GraphicsSettings.asset", "QualitySettings.asset", "ProjectSettings.asset"):
        shutil.copy2(project_root / "ProjectSettings" / name, settings / name)
    assets = destination / "Assets"
    assets.mkdir()
    shutil.copytree(project_root / "Assets/Settings", assets / "Settings")
    shutil.copy2(project_root / "Assets/Settings.meta", assets / "Settings.meta")
    shutil.copytree(sample, assets / "Samples/PerfectCullingDOTS/Basic")
    tests = assets / "Tests"
    render_tests = tests / "Rendering"
    render_tests.mkdir(parents=True)
    for name in ("AutomaticCameraTests.cs", "AutomaticUrpStackTests.cs", "RenderParityTests.cs", "RenderSimulationParityTests.cs", "StreamingParityTests.cs"):
        shutil.copy2(project_root / "Assets/Tests/PlayMode" / name, render_tests / name)
    assembly = {"name": "PerfectCullingDOTS.Consumer.Tests", "references": [
        "PerfectCullingDOTS.Core", "PerfectCullingDOTS.Runtime", "PerfectCullingDOTS.Rendering",
        "Unity.Entities", "Unity.Entities.Graphics", "Unity.Scenes", "Unity.Transforms",
        "Unity.Collections", "Unity.Mathematics", "Unity.Physics", "Unity.RenderPipelines.Universal.Runtime"],
        "optionalUnityReferences": ["TestAssemblies"]}
    (render_tests / "PerfectCullingDOTS.Consumer.Tests.asmdef").write_text(json.dumps(assembly, indent=2)+"\n", encoding="utf-8")
    shutil.copytree(project_root / "Assets/Tests/Fixtures/Streaming", tests / "Fixtures/Streaming")
    shutil.copytree(project_root / "Assets/Tests/Delivery/PlayMode", tests / "Delivery")
    forbidden = list(assets.rglob("*.dll"))+list(packages.rglob("*.dll"))
    if forbidden or (assets / "Reference").exists() or (destination / "Sources").exists():
        raise ValueError("Consumer contains forbidden native/reference artifacts; inspect the partial project.")
    hashes = {str(path.relative_to(destination)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
              for base in (assets, packages) for path in sorted(base.rglob("*")) if path.is_file()}
    (destination / "DeliveryContentHashes.json").write_text(json.dumps(hashes, indent=2)+"\n", encoding="utf-8")
    return destination


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--destination", type=Path)
    args = parser.parse_args()
    target = args.destination or args.project_root / ".superpowers/consumers/Delivery6000_6_8f1"
    print(create(args.project_root, target))
