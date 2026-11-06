"""Generate one disposable Git-install check; open it normally in Unity, without CLI tooling."""
import argparse
import io
import json
from pathlib import Path
import shutil
import subprocess
import tarfile

PACKAGE = "com.vertoker.culling-system-dots"
SUBPATH = "Assets/CullingSystemDOTS"


def create(project_root: Path, destination: Path, revision: str = "master") -> Path:
    project_root, destination = project_root.resolve(), destination.resolve()
    allowed = (project_root / ".superpowers/consumers/GitInstall").resolve()
    if destination != allowed or destination.exists():
        raise ValueError("Only a new .superpowers/consumers/GitInstall destination is supported.")
    commit = subprocess.check_output(["git", "-C", str(project_root), "rev-parse", "--verify", revision + "^{commit}"], text=True).strip()
    archive = subprocess.check_output(["git", "-C", str(project_root), "archive", "--format=tar", commit, SUBPATH + "/Samples~/Basic"])
    original = json.loads((project_root / "Packages/manifest.json").read_text(encoding="utf-8-sig"))["dependencies"]
    names = ("com.coplaydev.unity-mcp", "com.unity.entities.graphics", "com.unity.physics", "com.unity.render-pipelines.universal", "com.unity.test-framework")
    dependencies = {name: original[name] for name in names}
    dependencies[PACKAGE] = (project_root / ".git").as_uri() + "?path=/" + SUBPATH + "#" + commit
    (destination / "Packages").mkdir(parents=True)
    (destination / "Packages/manifest.json").write_text(json.dumps({"dependencies": dependencies, "testables": [PACKAGE]}, indent=2)+"\n", encoding="utf-8")
    settings = destination / "ProjectSettings"
    settings.mkdir()
    for name in ("ProjectVersion.txt", "GraphicsSettings.asset", "QualitySettings.asset", "ProjectSettings.asset"):
        shutil.copy2(project_root / "ProjectSettings" / name, settings / name)
    assets = destination / "Assets"
    assets.mkdir()
    shutil.copytree(project_root / "Assets/Settings", assets / "Settings")
    shutil.copy2(project_root / "Assets/Settings.meta", assets / "Settings.meta")
    prefix = SUBPATH + "/Samples~/Basic/"
    sample = assets / "Samples/CullingSystemDOTS/Basic"
    with tarfile.open(fileobj=io.BytesIO(archive)) as contents:
        for member in contents:
            if not member.name.startswith(prefix) or member.isdir():
                continue
            relative = Path(member.name[len(prefix):])
            if not member.isfile() or relative.is_absolute() or ".." in relative.parts:
                raise ValueError("Unsupported sample archive entry.")
            target = sample / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(contents.extractfile(member).read())
    for name in ("PlayMode", "Fixtures", "Delivery/PlayMode"):
        source = project_root / "Assets/Tests" / name
        if source.exists():
            shutil.copytree(source, assets / "Tests" / name)
    (destination / "GitInstall.json").write_text(json.dumps({"commit": commit, "package": PACKAGE, "gitUrl": dependencies[PACKAGE]}, indent=2)+"\n", encoding="utf-8")
    return destination


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--revision", default="master")
    args = parser.parse_args()
    print(create(args.project_root, args.project_root / ".superpowers/consumers/GitInstall", args.revision))
