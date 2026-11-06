"""Distribution checks against the actual installable tree, not the Unity cache."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "Assets/CullingSystemDOTS"


class PackageInstallationTests(unittest.TestCase):
    def test_git_subpath_contains_complete_package(self):
        descriptor = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8-sig"))
        self.assertEqual("com.vertoker.culling-system-dots", descriptor["name"])
        for folder in ("Runtime", "Editor", "Tests", "Samples~", "Documentation~"):
            self.assertTrue((PACKAGE / folder).is_dir(), folder)
        for sample in descriptor["samples"]:
            self.assertTrue((PACKAGE / sample["path"] / "Basic.unity").is_file())
        self.assertFalse(any((ROOT / "Packages").glob("com.vertoker.*")))

    def test_no_hardcoded_development_location_in_production_code(self):
        bad = []
        for folder in ("Runtime", "Editor"):
            for path in (PACKAGE / folder).rglob("*.cs"):
                content = path.read_text(encoding="utf-8-sig")
                if '"Packages/com.vertoker.' in content or '"Assets/CullingSystemDOTS/' in content:
                    bad.append(str(path.relative_to(PACKAGE)))
        self.assertEqual([], bad, "Resolve package assets from their assembly/script location.")


if __name__ == "__main__":
    unittest.main()
