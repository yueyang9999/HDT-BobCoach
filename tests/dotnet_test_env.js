"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { execFileSync } = require("child_process");

function firstExisting(candidates) {
    for (const candidate of candidates) {
        if (candidate && fs.existsSync(candidate)) return path.resolve(candidate);
    }
    return null;
}

function findVsWhere() {
    return firstExisting([
        process.env.VSWHERE,
        path.join(process.env["ProgramFiles(x86)"] || "", "Microsoft Visual Studio", "Installer", "vswhere.exe"),
        path.join(process.env.ProgramFiles || "", "Microsoft Visual Studio", "Installer", "vswhere.exe"),
    ]);
}

function findRoslynCompiler() {
    const direct = firstExisting([
        process.env.BOBCOACH_CSC,
        process.env.VSINSTALLDIR
            ? path.join(process.env.VSINSTALLDIR, "MSBuild", "Current", "Bin", "Roslyn", "csc.exe")
            : null,
    ]);
    if (direct) return direct;

    const vswhere = findVsWhere();
    if (!vswhere) return null;
    try {
        const output = execFileSync(vswhere, [
            "-latest", "-products", "*", "-requires", "Microsoft.Component.MSBuild",
            "-find", "MSBuild\\**\\Bin\\Roslyn\\csc.exe",
        ], { encoding: "utf8", windowsHide: true });
        return firstExisting(output.split(/\r?\n/).map(value => value.trim()).filter(Boolean));
    } catch (_) {
        return null;
    }
}

function getHdtCandidatesFromConfig() {
    const appData = process.env.APPDATA;
    if (!appData) return [];
    const configPath = path.join(appData, "HearthstoneDeckTracker", "config.xml");
    if (!fs.existsSync(configPath)) return [];
    try {
        const xml = fs.readFileSync(configPath, "utf8");
        const match = xml.match(/<HearthstoneDirectory>([^<]+)<\/HearthstoneDirectory>/i);
        if (!match) return [];
        const parent = path.dirname(match[1].trim());
        return fs.readdirSync(parent, { withFileTypes: true })
            .filter(entry => entry.isDirectory() && /^HDT/i.test(entry.name))
            .flatMap(entry => {
                const root = path.join(parent, entry.name);
                return [root, path.join(root, "HDT")];
            });
    } catch (_) {
        return [];
    }
}

function getNuGetNewtonsoftCandidates() {
    const packageRoot = path.join(os.homedir(), ".nuget", "packages", "newtonsoft.json");
    if (!fs.existsSync(packageRoot)) return [];
    try {
        return fs.readdirSync(packageRoot, { withFileTypes: true })
            .filter(entry => entry.isDirectory())
            .sort((left, right) => right.name.localeCompare(left.name, undefined, { numeric: true }))
            .flatMap(entry => [
                path.join(packageRoot, entry.name, "lib", "net45", "Newtonsoft.Json.dll"),
                path.join(packageRoot, entry.name, "lib", "netstandard2.0", "Newtonsoft.Json.dll"),
            ]);
    } catch (_) {
        return [];
    }
}

function findNewtonsoftJson() {
    const hdtRoots = [
        process.env.BOBCOACH_HDT_DIR,
        path.join(process.env.LOCALAPPDATA || "", "HearthstoneDeckTracker"),
        path.join(process.env.ProgramFiles || "", "Hearthstone Deck Tracker"),
        path.join(process.env["ProgramFiles(x86)"] || "", "Hearthstone Deck Tracker"),
        ...getHdtCandidatesFromConfig(),
    ].filter(Boolean);

    return firstExisting([
        process.env.BOBCOACH_NEWTONSOFT,
        ...hdtRoots.flatMap(root => [
            path.join(root, "Newtonsoft.Json.dll"),
            path.join(root, "HDT", "Newtonsoft.Json.dll"),
        ]),
        ...getNuGetNewtonsoftCandidates(),
    ]);
}

function findFrameworkAssembly(fileName) {
    const windowsDir = process.env.WINDIR || "C:\\Windows";
    return firstExisting([
        path.join(windowsDir, "Microsoft.NET", "Framework64", "v4.0.30319", fileName),
        path.join(windowsDir, "Microsoft.NET", "Framework", "v4.0.30319", fileName),
    ]);
}

function skipOrFail(message) {
    if (process.env.CI) {
        console.error(`FAIL ${message}`);
        process.exit(1);
    }
    console.log(`SKIP ${message}`);
    process.exit(0);
}

module.exports = {
    findFrameworkAssembly,
    findNewtonsoftJson,
    findRoslynCompiler,
    skipOrFail,
};
