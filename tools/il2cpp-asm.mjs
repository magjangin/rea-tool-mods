#!/usr/bin/env node
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const pyScript = join(__dirname, "il2cpp_asm.py");
const args = process.argv.slice(2);

// Check python binary: prefer python3 on unix-like, python on windows
const pythonCmd = process.platform === "win32" ? "python" : "python3";

const child = spawn(pythonCmd, [pyScript, ...args], {
  stdio: "inherit",
  env: process.env,
});

child.on("error", (err) => {
  if (err.code === "ENOENT") {
    // Retry with fallback "python" if "python3" was not found
    if (pythonCmd === "python3") {
      const fallback = spawn("python", [pyScript, ...args], {
        stdio: "inherit",
        env: process.env,
      });
      fallback.on("error", (fbErr) => {
        console.error(`[il2cpp-asm] Error: Python 3 is required to run il2cpp_asm.py (${fbErr.message})`);
        process.exit(1);
      });
      fallback.on("close", (code) => {
        process.exit(code ?? 0);
      });
      return;
    }
    console.error(`[il2cpp-asm] Error: Python is required to run il2cpp_asm.py (${err.message})`);
    process.exit(1);
  } else {
    console.error(`[il2cpp-asm] Process error: ${err.message}`);
    process.exit(1);
  }
});

child.on("close", (code) => {
  process.exit(code ?? 0);
});
