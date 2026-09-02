"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { execFileSync } = require("child_process");

const root = path.resolve(__dirname, "..");
const tool = path.join(root, "tools", "evaluate", "firestone_xml_to_snapshot.js");
const temp = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-firestone-snapshot-"));
try {
    const xml = `<?xml version="1.0"?>
<GameEntity ts="00:00:00.000000" id="10"><Tag tag="20" value="1" /></GameEntity>
<Player id="11" playerID="4" isMainPlayer="true"><Tag tag="27" value="100" /><Tag tag="26" value="3" /><Tag tag="1377" value="1" /></Player>
<FullEntity ts="00:00:00.100000" id="100" cardID="TB_BaconShop_HERO_50"><Tag tag="49" value="3" /><Tag tag="50" value="4" /><Tag tag="202" value="3" /><Tag tag="45" value="30" /></FullEntity>
<TagChange ts="00:00:01.300000" entity="10" tag="20" value="2" />
<FullEntity ts="00:00:01.400000" id="200" cardID="BG25_001"><Tag tag="49" value="6" /><Tag tag="50" value="4" /><Tag tag="202" value="4" /><Tag tag="47" value="2" /><Tag tag="45" value="1" /><Tag tag="48" value="3" /><Tag tag="263" value="1" /></FullEntity>
<TagChange ts="00:00:02.600000" entity="11" tag="26" value="3" />
<Block ts="00:00:02.700000" entity="200" index="0" effectIndex="0" target="201" type="1" />
<FullEntity ts="00:00:03.000000" id="201" cardID="BG25_002"><Tag tag="49" value="6" /><Tag tag="50" value="4" /><Tag tag="202" value="4" /><Tag tag="47" value="3" /><Tag tag="45" value="2" /><Tag tag="48" value="3" /><Tag tag="263" value="1" /></FullEntity>
<TagChange ts="00:00:04.000000" entity="201" tag="49" value="3" />
<TagChange ts="00:00:05.000000" entity="201" tag="49" value="1" />
<FullEntity ts="00:00:06.000000" id="202" cardID="BG29_503"><Tag tag="49" value="1" /><Tag tag="50" value="4" /><Tag tag="202" value="42" /></FullEntity>
<FullEntity ts="00:00:06.000000" id="300" cardID="BG25_003"><Tag tag="49" value="6" /><Tag tag="50" value="4" /><Tag tag="202" value="4" /></FullEntity>
<FullEntity ts="00:00:06.000000" id="301" cardID="BG25_004"><Tag tag="49" value="6" /><Tag tag="50" value="4" /><Tag tag="202" value="4" /></FullEntity>
<Choices ts="00:00:06.100000" id="0" playerID="11" source="202" taskList="9" type="2"><Choice entity="300" index="0" /><Choice entity="301" index="1" /></Choices>
<Choice ts="00:00:07.000000" entity="300" index="0" />
`;
    const xmlPath = path.join(temp, "review-1.xml");
    const metadataPath = path.join(temp, "metadata.json");
    const outputDir = path.join(temp, "out");
    fs.writeFileSync(xmlPath, xml, "utf8");
    fs.writeFileSync(metadataPath, JSON.stringify({
        games: [{ reviewId: "review-1", playerCardId: "TB_BaconShop_HERO_50", additionalResult: "2", creationDate: "2026-08-11T00:00:00.000Z" }],
    }), "utf8");

    execFileSync(process.execPath, [
        tool, "--xml", xmlPath, "--metadata", metadataPath, "--output-dir", outputDir,
    ], { encoding: "utf8", windowsHide: true });

    const snapshot = JSON.parse(fs.readFileSync(path.join(outputDir, "snapshot_firestone_review-1.json"), "utf8"));
    assert.strictEqual(snapshot.source, "firestone-xml", "source marker is present");
    assert.ok(snapshot.turnCount >= 1, "at least one turn snapshot is generated");
    const turn = snapshot.turns[0];
    assert.strictEqual(turn.turn, 1, "turn number is normalized to game round");
    assert.strictEqual(turn.gold, 3, "player resources are captured");
    assert.strictEqual(turn.tier, 1, "player tech level is captured");
    assert.strictEqual(turn.shop[0].id, "BG25_001", "active shop card is captured");
    assert.strictEqual(turn.board.length, 0, "no minion board in synthetic fixture");
    assert.ok(snapshot.actions.some(action => action.kind === "buy" && action.cardId === "BG25_002"),
        "buy action is extracted");
    assert.ok(snapshot.actions.some(action => action.kind === "play" && action.cardId === "BG25_002"),
        "play action is extracted");
    assert.ok(snapshot.blocks.some(block => block.type === "1" && block.target === "201"),
        "block event is captured");
    assert.strictEqual(snapshot.choices[0].kind, "discover", "choice kind is inferred");
    assert.strictEqual(snapshot.choices[0].selectedCardId, "BG25_003", "selected choice card is resolved");
    console.log("PASS firestone xml to snapshot");
} finally {
    fs.rmSync(temp, { recursive: true, force: true });
}
