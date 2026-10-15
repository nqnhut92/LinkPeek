"""Exercise release failures without building an app or accessing Keychain."""

import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


MOCK = r'''#!/usr/bin/env python3
import os
from pathlib import Path
import plistlib
import sys
import zipfile

tool = Path(sys.argv[0]).name
args = sys.argv[1:]
mode = os.environ.get("RELEASE_TEST_MODE", "success")
with open(os.environ["RELEASE_TEST_LOG"], "a") as log:
    log.write(tool + " " + " ".join(args) + "\n")
if tool == "security":
    if mode != "missing_identity":
        print('1) FAKE "LinkPeek Release"')
elif tool == "xcodebuild":
    archive = Path(args[args.index("-archivePath") + 1])
    app = archive / "Products/Applications/LinkPeek.app/Contents"
    app.mkdir(parents=True)
    (app / "Info.plist").write_bytes(plistlib.dumps({
        "CFBundleShortVersionString": "0.1.0",
        "CFBundleVersion": "20261015.120000",
    }))
    (app / "MacOS").mkdir()
    (app / "MacOS/LinkPeek").write_text(
        str(Path.home()) + "/private-source" if mode == "path_leak" else "fake executable"
    )
elif tool == "codesign":
    if "--verify" in args and mode == "bad_signature":
        sys.exit(1)
    if "-dvv" in args:
        print("Authority=" + (
            "person@example.invalid" if mode == "email" else "LinkPeek Release"
        ), file=sys.stderr)
elif tool == "ditto":
    app = Path(args[-2])
    with zipfile.ZipFile(args[-1], "w") as output:
        for item in app.rglob("*"):
            if item.is_file():
                output.write(item, item.relative_to(app.parent))
'''


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="linkpeek-release-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        scripts = self.root / "scripts"
        scripts.mkdir()
        shutil.copyfile(Path(__file__).resolve().parents[1] / "release.sh", scripts / "release.sh")
        self.bin = self.root / "bin"
        self.bin.mkdir()
        for name in ("security", "xcodebuild", "codesign", "ditto"):
            command = self.bin / name
            command.write_text(MOCK)
            command.chmod(0o755)
        self.log = self.root / "calls.log"

    def run_release(self, mode="success", identity="LinkPeek Release"):
        env = dict(os.environ)
        env.update(
            PATH=str(self.bin) + os.pathsep + env["PATH"],
            RELEASE_TEST_LOG=str(self.log),
            RELEASE_TEST_MODE=mode,
            LINKPEEK_SIGNING_IDENTITY=identity,
        )
        return subprocess.run(
            ["sh", str(self.root / "scripts/release.sh")],
            env=env, capture_output=True, text=True,
        )

    def test_packages_app_and_matching_checksum(self):
        result = self.run_release()
        self.assertEqual(result.returncode, 0, result.stderr)
        output = self.root / "build/release"
        archive = output / "LinkPeek-0.1.0.zip"
        expected = hashlib.sha256(archive.read_bytes()).hexdigest()
        self.assertEqual(
            (output / "LinkPeek-0.1.0.zip.sha256").read_text(),
            expected + "  LinkPeek-0.1.0.zip\n",
        )
        calls = self.log.read_text()
        self.assertIn("CODE_SIGNING_ALLOWED=NO", calls)
        self.assertIn("--options runtime --sign LinkPeek Release", calls)
        self.assertLess(calls.index("--verify"), calls.index("ditto"))

    def test_missing_identity_stops_before_archive(self):
        result = self.run_release("missing_identity")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("no code-signing identity", result.stderr)
        self.assertNotIn("xcodebuild", self.log.read_text())

    def test_failed_checks_never_replace_existing_release(self):
        output = self.root / "build/release"
        output.mkdir(parents=True)
        existing = output / "LinkPeek-0.1.0.zip"
        existing.write_bytes(b"previous release")
        for mode in ("bad_signature", "path_leak", "email"):
            with self.subTest(mode=mode):
                result = self.run_release(mode)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(existing.read_bytes(), b"previous release")
                self.assertFalse((output / "LinkPeek-0.1.0.zip.sha256").exists())
        self.assertNotIn("ditto", self.log.read_text())

    def test_adhoc_build_has_distinct_filename_and_skips_keychain(self):
        result = self.run_release(identity="-")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((self.root / "build/release/LinkPeek-0.1.0-adhoc.zip").exists())
        self.assertNotIn("security", self.log.read_text())


if __name__ == "__main__":
    unittest.main()
