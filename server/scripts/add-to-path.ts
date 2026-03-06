#!/usr/bin/env bun
/**
 * Adds the dist/ folder to the user's PATH so `umcpserver` can be run from anywhere.
 * Works on Windows (registry), macOS/Linux (~/.bashrc, ~/.zshrc, ~/.profile).
 */

import { resolve, dirname } from "path";

const distDir = resolve(dirname(import.meta.dir), "dist");

async function addToPathWindows() {
    const reg = Bun.spawn(
        ["reg", "query", "HKCU\\Environment", "/v", "Path"],
        { stdout: "pipe", stderr: "pipe" }
    );
    const output = await new Response(reg.stdout).text();
    await reg.exited;

    const match = output.match(/Path\s+REG_(?:EXPAND_)?SZ\s+(.*)/i);
    const currentPath = match?.[1]?.trim() ?? "";

    if (currentPath.toLowerCase().includes(distDir.toLowerCase())) {
        console.log(`✓ PATH already contains ${distDir}`);
        return;
    }

    const newPath = currentPath ? `${currentPath};${distDir}` : distDir;
    const setReg = Bun.spawn(
        ["reg", "add", "HKCU\\Environment", "/v", "Path", "/t", "REG_EXPAND_SZ", "/d", newPath, "/f"],
        { stdout: "pipe", stderr: "pipe" }
    );
    await setReg.exited;

    // Broadcast WM_SETTINGCHANGE so new terminals pick up the change
    try {
        Bun.spawn(
            ["powershell", "-Command",
                `Add-Type -Namespace Win32 -Name NativeMethods -MemberDefinition '[DllImport("user32.dll",SetLastError=true,CharSet=CharSet.Auto)]public static extern IntPtr SendMessageTimeout(IntPtr hWnd,uint Msg,UIntPtr wParam,string lParam,uint fuFlags,uint uTimeout,out UIntPtr lpdwResult);'; ` +
                `$r=[UIntPtr]::Zero; [Win32.NativeMethods]::SendMessageTimeout([IntPtr]0xFFFF,0x001A,[UIntPtr]::Zero,'Environment',0x0002,5000,[ref]$r) | Out-Null`
            ],
            { stdout: "pipe", stderr: "pipe" }
        );
    } catch { /* best effort */ }

    console.log(`✓ Added ${distDir} to user PATH`);
    console.log(`  Open a new terminal to use: umcpserver`);
}

async function addToPathUnix() {
    const shell = process.env.SHELL ?? "/bin/bash";
    const home = process.env.HOME ?? "~";

    const rcFiles: string[] = [];
    if (shell.includes("zsh")) rcFiles.push(`${home}/.zshrc`);
    else if (shell.includes("bash")) rcFiles.push(`${home}/.bashrc`);
    rcFiles.push(`${home}/.profile`);

    const exportLine = `export PATH="${distDir}:$PATH"`;

    for (const rc of rcFiles) {
        try {
            const file = Bun.file(rc);
            const exists = await file.exists();
            const content = exists ? await file.text() : "";

            if (content.includes(distDir)) {
                console.log(`✓ ${rc} already contains the path`);
                return;
            }

            await Bun.write(rc, content + `\n# Unity AI Tools MCP Server\n${exportLine}\n`);
            console.log(`✓ Added to ${rc}`);
            console.log(`  Run: source ${rc}  (or open a new terminal)`);
            return;
        } catch { continue; }
    }

    console.log(`⚠ Could not find a shell config file. Add this manually:`);
    console.log(`  ${exportLine}`);
}

if (process.platform === "win32") {
    await addToPathWindows();
} else {
    await addToPathUnix();
}
