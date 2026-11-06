import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest


class DeliveryConsumerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "Development"
        self.root.mkdir()
        self.git("init", "-b", "master")
        package = self.root / "Assets/CullingSystemDOTS"
        (package / "Samples~/Basic").mkdir(parents=True)
        (package / "package.json").write_text('{"name":"com.vertoker.culling-system-dots","version":"0.1.0"}')
        (package / "Samples~/Basic/Basic.unity").write_text("committed-sample")
        (package / "Samples~/Basic/Basic.unity.meta").write_text("guid: retained-scene-guid")
        (self.root / "ProjectSettings").mkdir()
        for name in ("ProjectVersion.txt", "GraphicsSettings.asset", "QualitySettings.asset", "ProjectSettings.asset"):
            (self.root / "ProjectSettings" / name).write_text(name)
        (self.root / "Packages").mkdir()
        names = ("com.coplaydev.unity-mcp", "com.unity.entities.graphics", "com.unity.physics", "com.unity.render-pipelines.universal", "com.unity.test-framework", "com.unity.modules.ai", "com.unity.pipeline")
        (self.root / "Packages/manifest.json").write_text(json.dumps({"dependencies": {name: "1.0.0" for name in names}}))
        (self.root / "Assets/Settings").mkdir()
        (self.root / "Assets/Settings/URP.asset").write_text("settings")
        (self.root / "Assets/Settings.meta").write_text("guid: settings-guid")
        for name in ("Assets/Tests/PlayMode", "Assets/Tests/Fixtures/Streaming", "Assets/Tests/Delivery/PlayMode"):
            (self.root / name).mkdir(parents=True)
        self.git("add", ".")
        self.git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-qm", "fixture")
        script = Path(__file__).resolve().parents[1] / "CreateDeliveryConsumer.py"
        spec = importlib.util.spec_from_file_location("delivery_consumer", script)
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.root), *args], text=True).strip()

    def test_consumer_installs_committed_git_subpath_and_stock_graphics(self):
        target = self.root / ".superpowers/consumers/GitInstall"
        self.module.create(self.root, target)
        manifest = json.loads((target / "Packages/manifest.json").read_text())
        url = manifest["dependencies"]["com.vertoker.culling-system-dots"]
        self.assertTrue(url.startswith("file:///"))
        self.assertIn(".git?path=/Assets/CullingSystemDOTS#", url)
        self.assertTrue(url.endswith(self.git("rev-parse", "master")))
        self.assertEqual("1.0.0", manifest["dependencies"]["com.unity.entities.graphics"])
        self.assertEqual(["manifest.json"], sorted(p.name for p in (target / "Packages").iterdir()))
        self.assertNotIn("com.unity.pipeline", manifest["dependencies"])
        self.assertEqual("committed-sample", (target / "Assets/Samples/CullingSystemDOTS/Basic/Basic.unity").read_text())

    def test_only_one_named_consumer_and_no_overwriting(self):
        target = self.root / ".superpowers/consumers/GitInstall"
        target.mkdir(parents=True)
        marker = target / "untouched.txt"
        marker.write_text("user-bytes")
        with self.assertRaises(ValueError):
            self.module.create(self.root, target)
        self.assertEqual("user-bytes", marker.read_text())
        outside = Path(self.temp.name) / "Outside"
        with self.assertRaises(ValueError):
            self.module.create(self.root, outside)
        self.assertFalse(outside.exists())


if __name__ == "__main__":
    unittest.main()
