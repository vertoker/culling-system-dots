"""Exercise documentation validation against real temporary Git worktrees."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "ValidateAgentDocs.py"


class AgentDocsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        subprocess.run(["git", "init", "--quiet", str(self.root)], check=True)
        self.write("AGENTS.md", "# Root guide\n\n[Tools](Tools/AGENTS.md)\n")
        self.write("Tools/AGENTS.md", "# Tools\n\nRead [parent](../AGENTS.md) first.\n")
        self.write("Tools/check.py", "print('hello')\n")

    def write(self, path, content):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def run_validator(self):
        return subprocess.run([sys.executable, str(SCRIPT), "--root", str(self.root)],
                              cwd=self.temporary.name, capture_output=True, text=True)

    def assert_invalid(self, rule):
        result = self.run_validator()
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn(rule, result.stdout + result.stderr)

    def test_valid_untracked_tree_and_any_working_directory(self):
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_missing_code_directory_guide(self):
        self.write("Tools/new/check.cs", "class Check {}\n")
        self.assert_invalid("coverage")

    def test_deleted_tracked_guide_is_not_silently_accepted(self):
        subprocess.run(["git", "-C", str(self.root), "add", "."], check=True)
        (self.root / "Tools/AGENTS.md").unlink()
        self.assert_invalid("coverage")

    def test_wrong_case_link_fails_on_windows_too(self):
        self.write("Tools/AGENTS.md", "# Tools\n\nRead [parent](../agents.md) first.\n")
        self.assert_invalid("link-case")

    def test_missing_link_and_anchor(self):
        self.write("Guide.md", "# Valid heading\n\n[missing](absent.md)\n[anchor](#absent)\n")
        result = self.run_validator()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("link-target", result.stdout)
        self.assertIn("link-anchor", result.stdout)

    def test_fences_inline_examples_and_reference_links(self):
        self.write("Guide.md", "# Heading!\n\n## Heading!\n\n[valid](#heading-1)\n"
                   "[reference][target]\n\n[target]: Tools/AGENTS.md\n"
                   "`[example](absent.md)`\n\n```markdown\n[example](absent.md)\n```\n"
                   "~~~\n[example](absent.md)\n~~~\n")
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_heading_code_text_is_part_of_anchor(self):
        self.write("Guide.md", "# Use `example` now\n\n[valid](#use-example-now)\n")
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_reference_parent_definition_can_be_late(self):
        self.write("Tools/AGENTS.md", "# Tools\n\nRead [parent][root] first.\n\n" +
                   "Instruction.\n" * 16 + "\n[root]: ../AGENTS.md\n")
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_parent_must_be_nearest_guide(self):
        self.write("Tools/child/AGENTS.md", "# Child\n\nRead [root](../../AGENTS.md).\n")
        self.assert_invalid("parent-link")

    def test_navigation_link_does_not_replace_early_parent(self):
        self.write("Tools/AGENTS.md", "# Tools\n\n" + "Some instruction.\n" * 18 +
                   "\n[Parent](../AGENTS.md)\n")
        self.assert_invalid("parent-link")

    def test_fenced_parent_is_not_real_parent(self):
        self.write("Tools/AGENTS.md", "# Tools\n\n```\nRead [parent](../AGENTS.md).\n```\n")
        self.assert_invalid("parent-link")

    def test_parent_cycle(self):
        self.write("Tools/AGENTS.md", "# Tools\n\nRead [parent](child/AGENTS.md).\n")
        self.write("Tools/child/AGENTS.md", "# Child\n\nRead [parent](../AGENTS.md).\n")
        self.assert_invalid("parent-cycle")

    def test_chain_budget_is_cumulative(self):
        self.write("AGENTS.md", "# Root\n\n" + "a" * 17000)
        self.write("Tools/AGENTS.md", "# Tools\n\nRead [parent](../AGENTS.md).\n\n" + "b" * 17000)
        self.assert_invalid("chain-budget")

    def test_standalone_package_counts_checkout_ancestor_budget(self):
        self.write("AGENTS.md", "# Root\n\n" + "a" * 17000)
        self.write("Assets/CullingSystemDOTS/AGENTS.md", "# Package\n\n" + "b" * 17000)
        self.assert_invalid("chain-budget")

    def test_skill_metadata_and_registration(self):
        self.write(".agents/skills/example/SKILL.md", "---\nname: example\n---\n\n# Example\n")
        self.assert_invalid("skill-frontmatter")
        self.write(".agents/skills/example/SKILL.md",
                   "---\nname: example\ndescription: Use for examples.\n---\n\n# Example\n")
        self.assert_invalid("skill-catalog")
        self.write(".agents/README.md", "# Skills\n\n[Example](skills/example/SKILL.md)\n")
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_blank_skill_description_is_not_frontmatter_delimiter(self):
        self.write(".agents/skills/example/SKILL.md",
                   "---\nname: example\ndescription:\n---\n\n# Example\n")
        self.assert_invalid("skill-frontmatter")

    def test_skill_block_description_and_quoted_name(self):
        self.write(".agents/skills/example/SKILL.md",
                   "---\nname: 'example'\ndescription: >-\n  Use when a reusable\n"
                   "  example is needed.\n---\n\n# Example\n")
        self.write(".agents/README.md", "# Skills\n\n[Example](skills/example/SKILL.md)\n")
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_skill_name_matches_directory(self):
        self.write(".agents/skills/example/SKILL.md",
                   "---\nname: wrong-name\ndescription: Use for examples.\n---\n\n# Example\n")
        self.assert_invalid("skill-frontmatter")

    def test_skill_rejects_plain_mapping_colons(self):
        self.write(".agents/README.md", "# Skills\n\n[Example](skills/example/SKILL.md)\n")
        for description in ("Use: examples", "Use examples:", "Use:\texamples"):
            with self.subTest(description=description):
                self.write(".agents/skills/example/SKILL.md",
                           f"---\nname: example\ndescription: {description}\n---\n\n# Example\n")
                self.assert_invalid("skill-frontmatter")

    def test_skill_rejects_non_string_plain_scalars(self):
        self.write(".agents/README.md", "# Skills\n\n[Example](skills/example/SKILL.md)\n")
        for description in ("42", "-2", "+0", "0.5", ".5", "5.", "1e3", "1_000",
                            "0x2A", "0o52", "0b1010", "+.inf", ".NaN", "42 # comment",
                            "true", "False", "NULL", "~", "yes", "OFF"):
            with self.subTest(description=description):
                self.write(".agents/skills/example/SKILL.md",
                           f"---\nname: example\ndescription: {description}\n---\n\n# Example\n")
                self.assert_invalid("skill-frontmatter")

    def test_skill_rejects_malformed_and_empty_quoted_scalars(self):
        self.write(".agents/README.md", "# Skills\n\n[Example](skills/example/SKILL.md)\n")
        for description in ('"Use examples', "'Use examples", '"" # comment',
                            "'' # comment", '"Use" trailing', "'Use' trailing"):
            with self.subTest(description=description):
                self.write(".agents/skills/example/SKILL.md",
                           f"---\nname: example\ndescription: {description}\n---\n\n# Example\n")
                self.assert_invalid("skill-frontmatter")

    def test_skill_accepts_string_scalar_colons_hashes_and_comments(self):
        self.write(".agents/README.md", "# Skills\n\n[Example](skills/example/SKILL.md)\n")
        for description in ("Use:examples in C#", "Use examples # ignored: comment",
                            "'Use: examples # literal'", '"42"',
                            "'Use ''quoted'' examples' # comment",
                            '"Use \\"quoted\\" examples" # comment',
                            "|- # comment\n  Use: examples # literal\n  when useful."):
            with self.subTest(description=description):
                self.write(".agents/skills/example/SKILL.md",
                           f"---\nname: example\ndescription: {description}\n---\n\n# Example\n")
                result = self.run_validator()
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_windows_drive_and_unc_markdown_links_are_not_external_uris(self):
        for destination in (r"C:\private\guide.md", "C:/private/guide.md", "C:guide.md",
                            r"\\server\share\guide.md", "//server/share/guide.md",
                            "C%3A%5Cprivate%5Cguide.md"):
            with self.subTest(destination=destination):
                self.write("Guide.md", f"# Guide\n\n[private]({destination})\n")
                self.assert_invalid("link-target")

    def test_package_standalone_root_and_copied_sample(self):
        self.write("Assets/CullingSystemDOTS/AGENTS.md", "# Package\n\nSelf-contained guide.\n")
        self.write("Assets/CullingSystemDOTS/Samples~/Basic/AGENTS.md",
                   "# Sample\n\nFollow host project guidance after import.\n")
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_package_mandatory_link_cannot_escape(self):
        self.write("Assets/CullingSystemDOTS/AGENTS.md",
                   "# Package\n\nRead [repository](../../AGENTS.md) first.\n")
        self.assert_invalid("package-link")

    def test_optional_package_context_can_link_out(self):
        self.write("Assets/CullingSystemDOTS/AGENTS.md",
                   "# Package\n\nOptional development context: [repository](../../AGENTS.md).\n")
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_optional_link_does_not_hide_later_mandatory_link(self):
        self.write("Assets/CullingSystemDOTS/AGENTS.md",
                   "# Package\n\nOptional [context](../../AGENTS.md).\n\n"
                   "Read [required guide](../../AGENTS.md) first.\n")
        self.assert_invalid("package-link")

    def test_non_english_agent_prose(self):
        self.write("Tools/AGENTS.md", "# Tools\n\nRead [parent](../AGENTS.md).\n\nПравила проекта.\n")
        self.assert_invalid("agent-language")

    def test_client_adapters_are_rejected(self):
        for name in ("CLAUDE.md", "GEMINI.md", ".cursor/rules/context.mdc", ".codex/config.toml",
                     ".github/copilot-instructions.md", ".vscode/mcp.json",
                     ".github/instructions/source.instructions.md"):
            with self.subTest(name=name):
                self.write(name, "# Adapter\n")
                self.assert_invalid("client-adapter")
                (self.root / name).unlink()

    def test_generated_private_and_third_party_cache_are_excluded(self):
        for name in ("Library/PackageCache/vendor/check.cs", "Temp/check.py",
                     ".agents/local-context.md", ".agents/migration-work/CLAUDE.md",
                     "node_modules/vendor/CLAUDE.md"):
            self.write(name, "[broken](absent.md)\n")
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
