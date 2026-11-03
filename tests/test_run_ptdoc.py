"""Launcher lifecycle checks with no .NET builds, devices, network, or real API.

Run: python3 tests/test_run_ptdoc.py
"""

import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time
import unittest


ROOT = Path(__file__).resolve().parents[1]
FAKE_TOOL = r'''#!/usr/bin/env python3
import json, os, signal, sys, time
from pathlib import Path
root = Path(os.environ["LAUNCHER_TEST_DIR"])
tool = Path(sys.argv[0]).name
if tool == "tput":
    sys.exit(1)
if tool == "adb":
    print("List of devices attached")
    print(os.environ.get("TEST_DEVICES", "emulator-5554\tdevice"))
    sys.exit(int(os.environ.get("TEST_ADB_EXIT", "0")))
if tool == "lsof":
    if os.environ.get("TEST_REUSE"):
        print(os.environ["TEST_REUSE"])
        sys.exit(0)
    sys.exit(1)
if tool == "curl":
    for _ in range(100):
        if os.environ.get("TEST_REUSE") or (root / "api-pid").exists():
            sys.exit(0)
        time.sleep(.01)
    sys.exit(1)
if tool == "dotnet":
    with (root / "calls").open("a") as f:
        f.write(json.dumps(sys.argv[1:]) + "\n")
    if "run" in sys.argv and any("PTDoc.Api.csproj" in a for a in sys.argv):
        def stop(*_):
            (root / "api-stopped").write_text("stopped")
            sys.exit(0)
        signal.signal(signal.SIGTERM, stop)
        (root / "api-pid").write_text(str(os.getpid()))
        while True:
            signal.pause()
    (root / "launched").write_text("launched")
    print("Mock launch completed", flush=True)
    sys.exit(int(os.environ.get("TEST_LAUNCH_EXIT", "0")))
sys.exit(2)
'''


class LauncherTests(unittest.TestCase):
    def launch(self, platform="2", **settings):
        temporary = tempfile.TemporaryDirectory(prefix="ptdoc-launcher-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        script = (ROOT / "run-ptdoc.sh").read_text()
        # Isolate the launcher's log files from any developer session.
        script = script.replace("/tmp/ptdoc-api.log", str(root / "api.log"))
        script = script.replace("/tmp/ptdoc-dev-secrets.log", str(root / "secrets.log"))
        (root / "run-ptdoc.sh").write_text(script)
        for project in ("PTDoc.Web/PTDoc.Web.csproj", "PTDoc.Api/PTDoc.Api.csproj", "PTDoc.Maui/PTDoc.csproj"):
            path = root / "src" / project
            path.parent.mkdir(parents=True, exist_ok=True)
            path.touch()
        binaries = root / "bin"
        binaries.mkdir()
        for name in ("dotnet", "adb", "curl", "lsof", "tput"):
            path = binaries / name
            path.write_text(FAKE_TOOL)
            path.chmod(0o755)
        env = os.environ.copy()
        for key in ("SKIP_API", "PUBLIC_WEB_BASE_URL", "API_URL", "SKIP_SECRET_SETUP"):
            env.pop(key, None)
        env.update(settings, LAUNCHER_TEST_DIR=str(root), PATH=f"{binaries}:{env['PATH']}")
        output = (root / "output").open("w+")
        self.addCleanup(output.close)
        process = subprocess.Popen(
            ["/bin/bash", str(root / "run-ptdoc.sh")], stdin=subprocess.PIPE,
            stdout=output, stderr=subprocess.STDOUT, env=env, start_new_session=True,
        )
        self.addCleanup(self.stop, process)
        process.stdin.write((platform + "\n").encode())
        process.stdin.close()
        return root, process

    @staticmethod
    def stop(process):
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=3)

    def wait_for(self, predicate, root):
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            if predicate():
                return
            time.sleep(.02)
        self.fail((root / "output").read_text())

    def test_native_launch_retains_api_until_interrupt(self):
        for platform, interruption, exit_code in (("2", signal.SIGINT, 130), ("3", signal.SIGTERM, 143), ("4", signal.SIGHUP, 129)):
            with self.subTest(platform=platform):
                root, process = self.launch(platform)
                self.wait_for(lambda: "Keep this terminal open" in (root / "output").read_text(), root)
                self.assertIsNone(process.poll())
                self.assertFalse((root / "api-stopped").exists())
                os.kill(int((root / "api-pid").read_text()), 0)
                if platform == "2":
                    calls = [json.loads(line) for line in (root / "calls").read_text().splitlines()]
                    self.assertIn("-p:AdbTarget=-s emulator-5554", calls[-1])
                process.send_signal(interruption)
                self.assertEqual(process.wait(timeout=5), exit_code)
                self.assertTrue((root / "api-stopped").exists())
                self.assertEqual((root / "output").read_text().count("Stopping API server"), 1)

    def test_bad_device_state_stops_before_api_or_build(self):
        for devices in ("", "emulator-5554\toffline", "phone\tunauthorized", "emulator-5554\tdevice\nemulator-5556\tdevice"):
            with self.subTest(devices=devices):
                root, process = self.launch(TEST_DEVICES=devices)
                self.assertEqual(process.wait(timeout=5), 1)
                self.assertIn("Expected one ready Android device", (root / "output").read_text())
                self.assertFalse((root / "calls").exists())

    def test_adb_failure_stops_before_api_or_build(self):
        root, process = self.launch(TEST_ADB_EXIT="1")
        self.assertEqual(process.wait(timeout=5), 1)
        self.assertIn("Unable to list Android devices", (root / "output").read_text())
        self.assertFalse((root / "calls").exists())

    def test_failed_native_launch_cleans_up_owned_api(self):
        root, process = self.launch(TEST_LAUNCH_EXIT="7")
        self.assertEqual(process.wait(timeout=5), 7)
        self.assertTrue((root / "api-stopped").exists())
        self.assertNotIn("Keep this terminal open", (root / "output").read_text())

    def test_existing_api_is_not_stopped(self):
        existing = subprocess.Popen(["/bin/sleep", "60"])
        self.addCleanup(self.stop_process, existing)
        root, process = self.launch(TEST_REUSE=str(existing.pid))
        self.assertEqual(process.wait(timeout=5), 0)
        self.assertIsNone(existing.poll())
        self.assertFalse((root / "api-pid").exists())
        self.assertNotIn("Stopping API server", (root / "output").read_text())

    @staticmethod
    def stop_process(process):
        if process.poll() is None:
            process.terminate()
        process.wait(timeout=5)

    def test_skip_api_does_not_create_or_wait_for_api(self):
        root, process = self.launch(SKIP_API="1")
        self.assertEqual(process.wait(timeout=5), 0)
        self.assertFalse((root / "api-pid").exists())

    def test_web_exit_still_cleans_up_owned_api(self):
        root, process = self.launch("1")
        self.assertEqual(process.wait(timeout=5), 0)
        self.assertTrue((root / "api-stopped").exists())
        self.assertNotIn("Keep this terminal open", (root / "output").read_text())

    def test_api_exit_ends_session_with_error(self):
        root, process = self.launch()
        self.wait_for(lambda: "Keep this terminal open" in (root / "output").read_text(), root)
        os.kill(int((root / "api-pid").read_text()), signal.SIGTERM)
        self.assertEqual(process.wait(timeout=5), 1)
        self.assertIn("API exited during the native session", (root / "output").read_text())


if __name__ == "__main__":
    unittest.main()
