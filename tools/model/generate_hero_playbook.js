"use strict";

const fs = require("fs");
const path = require("path");

function readJson(filePath) {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function parseArg(name, fallback) {
    const prefix = `--${name}=`;
    const found = process.argv.find((arg) => arg.startsWith(prefix));
    return found ? found.slice(prefix.length) : fallback;
}

function main() {
    const dataRoot = path.resolve(
        parseArg("data-root", process.env.BOB_COACH_DATA_ROOT || "")
    );
    if (!fs.existsSync(dataRoot)) {
        throw new Error(`data root does not exist: ${dataRoot}`);
    }

    const registryDir = path.join(dataRoot, "generated", "registries");
    const skillPath = path.join(registryDir, "hero_skill_registry.json");
    const outputPath = path.join(registryDir, "hero_playbook_registry.json");
    if (!fs.existsSync(skillPath)) {
        throw new Error(`hero skill registry missing: ${skillPath}`);
    }

    const skillRegistry = readJson(skillPath);
    const meta = skillRegistry._meta;
    if (!meta
        || meta.schemaVersion !== 1
        || meta.season !== "season14"
        || meta.gameBuild !== "36.2.248348"
        || meta.cardPoolIdentity !== "0bcc979ae21df891") {
        throw new Error("hero skill registry metadata is not current season14");
    }

    const officialPatch = "https://hearthstone.blizzard.com/en-us/news/24290432/36-2-patch-notes";
    const cenariusGuide = "https://blizzardwatch.com/2025/05/05/forest-lord-cenarius-hearthstone-battlegrounds/";
    const changedHeroIds = [
        "BG21_HERO_000",
        "BG24_HERO_204",
        "TB_BaconShop_HERO_01",
        "BG20_HERO_102",
        "TB_BaconShop_HERO_11",
        "TB_BaconShop_HERO_75",
        "BG28_HERO_800",
    ];

    const heroes = changedHeroIds.map((heroCardId) => ({
        heroCardId,
        strategyCoverage: "curated",
        sources: [officialPatch],
    }));

    heroes.push({
        heroCardId: "BG32_HERO_001",
        heroPowerCardIds: ["BG32_HERO_001p"],
        usePurpose: "Economy",
        specialRule: "CENARIUS",
        strategyCoverage: "curated",
        phases: [{
            minTurn: 1,
            maxTurn: 5,
            priority: "Critical",
            goldAtLeast: 3,
            healthAtLeast: 8,
            targetRequired: false,
            reason: "early-economy",
        }],
        sources: [officialPatch, cenariusGuide],
    });

    const output = {
        _meta: {
            schemaVersion: 1,
            season: meta.season,
            gameBuild: meta.gameBuild,
            cardPoolIdentity: meta.cardPoolIdentity,
            generatedAt: new Date().toISOString(),
            source: "generate_hero_playbook",
        },
        heroes,
    };

    fs.writeFileSync(
        outputPath,
        JSON.stringify(output, null, 2) + "\n",
        { encoding: "utf8" }
    );
    console.log(`PASS generated hero playbook overlay: ${outputPath}`);
}

try {
    main();
} catch (error) {
    console.error(`FAIL ${error.message}`);
    process.exit(1);
}
