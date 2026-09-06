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

    // ===== 英雄专属玩法节奏 (round67, 数据: 用户真实对局 powerAction 技能回合 + 机制冷却) =====
    // 原则: phases 全生命周期覆盖(1..99), 用 priority 表达"用/少用/不用"节奏;
    // 落空窗口(未匹配 phase) = ShouldUse=false, 用于表达"此阶段不该用"。
    // 每英雄注: 机制(费用/解锁/冷却) + 用户真实使用回合。

    // 蛇眼·好运当投(1费掷骰得币, 掷出N则N回合冷却): T1 必用(正EV), 冷却结束即用。
    // 用户实测: T1 100%, 后冷却回合 T4/7/8 再用。
    heroes.push({
        heroCardId: "BG28_HERO_400",
        heroPowerCardIds: ["BG28_HERO_400p"],
        usePurpose: "Economy",
        specialRule: "SNAKE_EYES",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 1, priority: "Critical", goldAtLeast: 1, healthAtLeast: 0, targetRequired: false, reason: "snake-t1-open" },
            { minTurn: 2, maxTurn: 99, priority: "High", goldAtLeast: 1, healthAtLeast: 0, targetRequired: false, reason: "snake-reuse-after-cooldown" },
        ],
        sources: [officialPatch, "user-replay-20260905"],
    });

    // 尤朵拉·挖宝(1费挖金, 挖4次出金随从): 前期连挖出金, 中期挖金循环。
    // 用户实测: T1-6 67-100%(每局), 挖到金后 T7-8 续挖。
    heroes.push({
        heroCardId: "TB_BaconShop_HERO_64",
        heroPowerCardIds: ["TB_BaconShop_HP_074"],
        usePurpose: "Resource",
        specialRule: "DIG_GOLDEN",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 6, priority: "Critical", goldAtLeast: 1, healthAtLeast: 0, targetRequired: false, reason: "eudora-dig-early" },
            { minTurn: 7, maxTurn: 99, priority: "High", goldAtLeast: 1, healthAtLeast: 0, targetRequired: false, reason: "eudora-dig-cycle" },
        ],
        sources: [officialPatch, "user-replay-20260905"],
    });

    // 巴罗夫·猜胜者(1费, 猜对得3币): 前期低费猜(期望正), 中后期金币富余时继续。
    // 用户实测: T2-4 80% 高, T5-6 60%, T8/10 80%(后期币多再猜)。
    heroes.push({
        heroCardId: "TB_BaconShop_HERO_72",
        heroPowerCardIds: ["TB_BaconShop_HP_081"],
        usePurpose: "Economy",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 6, priority: "High", goldAtLeast: 1, healthAtLeast: 0, targetRequired: false, reason: "barov-early-bet" },
            { minTurn: 7, maxTurn: 99, priority: "Normal", goldAtLeast: 2, healthAtLeast: 0, targetRequired: false, reason: "barov-late-bet" },
        ],
        sources: [officialPatch, "user-replay-20260905"],
    });

    // 牛头人酋长·发现伙伴(3费, T2解锁): 解锁后尽早发现伙伴(强场面), 持续补伙伴。
    // 用户实测: T4 起 100% 连用至 T9+。
    heroes.push({
        heroCardId: "BG25_HERO_105",
        heroPowerCardIds: ["BG25_HERO_105p"],
        usePurpose: "Resource",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 2, maxTurn: 5, priority: "Critical", goldAtLeast: 3, healthAtLeast: 0, targetRequired: false, reason: "trh-early-buddy" },
            { minTurn: 6, maxTurn: 99, priority: "High", goldAtLeast: 3, healthAtLeast: 0, targetRequired: false, reason: "trh-buddy-cycle" },
        ],
        sources: [officialPatch, "user-replay-20260905"],
    });

    // 雷诺·点金(每局1次, 0费操作, 需友方随从): 前期不用(浪费), 板面成型后点核心。
    // 用户实测: T6-11 区间, 集中 T9-11(板面≥5)。
    heroes.push({
        heroCardId: "TB_BaconShop_HERO_41",
        heroPowerCardIds: ["TB_BaconShop_HP_046"],
        usePurpose: "Buff",
        specialRule: "RENO",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 7, maxTurn: 99, priority: "High", goldAtLeast: 0, healthAtLeast: 0, targetRequired: true, reason: "reno-goldenize" },
        ],
        sources: [officialPatch, "user-replay-20260905"],
    });

    // 克罗米·刷新成法术(0费): 法术流核心, 几乎每回合刷(法术经济/发现)。
    // 用户实测: T1-10 80-100% 每回合用。
    heroes.push({
        heroCardId: "BG34_HERO_001",
        heroPowerCardIds: ["BG34_HERO_001p"],
        usePurpose: "Resource",
        specialRule: "CHROMIE",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 99, priority: "High", goldAtLeast: 0, healthAtLeast: 0, targetRequired: false, reason: "chromie-spell-refresh" },
        ],
        sources: [officialPatch, "user-replay-20260905"],
    });

    // 曲奇·投锅(0费, 集3同族发现): 每回合投(低成本), 集齐即发现。
    // 用户实测: T1-9 100% 每回合用。
    heroes.push({
        heroCardId: "BG21_HERO_020",
        heroPowerCardIds: ["BG21_HERO_020p"],
        usePurpose: "Resource",
        specialRule: "COOKIE",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 99, priority: "High", goldAtLeast: 0, healthAtLeast: 0, targetRequired: true, reason: "cookie-pot" },
        ],
        sources: [officialPatch, "user-replay-20260905"],
    });

    // 玛维·锁牌(1费, 2回合后入手): 商店有好牌即锁(预购), 前期(店牌便宜时)价值高。
    // 用户实测: T1-8 100%(样本少但连续)。
    heroes.push({
        heroCardId: "TB_BaconShop_HERO_62",
        heroPowerCardIds: ["TB_BaconShop_HP_068"],
        usePurpose: "Resource",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 99, priority: "High", goldAtLeast: 1, healthAtLeast: 0, targetRequired: true, reason: "maiev-lock" },
        ],
        sources: [officialPatch, "user-replay-20260905"],
    });

    // ===== 节奏扩展 round68 (数据: decision-data 97局 per-turn powerAction 使用率 + 机制) =====
    // 只选: 主动技能 + 用户实玩(使用率清晰) + 机制可从文本/注册表核验的英雄。

    // 迦拉克隆·渴望进化(1费): 把商店随从替换为更高等级 — 前期追核心/高星, 可用即用。
    // 用户实测: T1-10 每回合 100%(金币≥1 即用)。
    heroes.push({
        heroCardId: "TB_BaconShop_HERO_02",
        heroPowerCardIds: ["TB_BaconShop_HP_011"],
        usePurpose: "Resource",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 99, priority: "High", goldAtLeast: 1, healthAtLeast: 0, targetRequired: false, reason: "galakrond-upgrade" },
        ],
        sources: [officialPatch, "user-replay-20260906"],
    });

    // 斯卡布斯·刀油(2费): 从下一位对手战队发现随从原始复制 — 中前期节奏/针对牌。
    // 用户实测: T2 起可用即用(金币≥2), T4/T6 让位买牌/升本。
    heroes.push({
        heroCardId: "BG21_HERO_010",
        heroPowerCardIds: ["BG21_HERO_010p"],
        usePurpose: "Resource",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 2, maxTurn: 99, priority: "High", goldAtLeast: 2, healthAtLeast: 0, targetRequired: false, reason: "scabbs-discover" },
        ],
        sources: [officialPatch, "user-replay-20260906"],
    });

    // 摩洛克·福尔摩斯(0费): 检视2随从, 猜中下一位对手上一场战斗随从得酒馆币 — 免费EV。
    // T1 无上一场战斗可猜; 用户实测 T2-14 每回合 100%。
    heroes.push({
        heroCardId: "BG23_HERO_303",
        heroPowerCardIds: ["BG23_HERO_303p2"],
        usePurpose: "Economy",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 2, maxTurn: 99, priority: "High", goldAtLeast: 0, healthAtLeast: 0, targetRequired: false, reason: "holmes-guess" },
        ],
        sources: [officialPatch, "user-replay-20260906"],
    });

    // 伟大的阿卡扎曼扎拉克(0费): 选奥秘置入战场 — 免费奥秘, 每回合早用早生效。
    // 用户实测: T1-10 每回合 100%。
    heroes.push({
        heroCardId: "TB_BaconShop_HERO_21",
        heroPowerCardIds: ["TB_BaconShop_HP_020"],
        usePurpose: "Resource",
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 99, priority: "High", goldAtLeast: 0, healthAtLeast: 0, targetRequired: false, reason: "akazamzarak-secret" },
        ],
        sources: [officialPatch, "user-replay-20260906"],
    });

    // 大主教奥萨尔(0费, 第3回合解锁): 下一张酒馆法术-1 — 有法术节奏时前置激活。
    // 用户实测: T7 起每回合激活(前期未买酒馆法术)。
    heroes.push({
        heroCardId: "BG31_HERO_006",
        heroPowerCardIds: ["BG31_HERO_006p"],
        usePurpose: "Economy",
        unlockTurn: 3,
        strategyCoverage: "curated",
        phases: [
            { minTurn: 3, maxTurn: 99, priority: "High", goldAtLeast: 0, healthAtLeast: 0, targetRequired: false, reason: "othaar-spell-discount" },
        ],
        sources: [officialPatch, "user-replay-20260906"],
    });

    // 米尔菲丝·法力风暴(1费, 等级4解锁): 发现磁力机械 — 机械流到4本后持续补磁力。
    // 用户实测: T6(到4本)起, T6-9/T11 100%。
    heroes.push({
        heroCardId: "TB_BaconShop_HERO_17",
        heroPowerCardIds: ["TB_BaconShop_HP_015"],
        usePurpose: "Resource",
        unlockTier: 4,
        strategyCoverage: "curated",
        phases: [
            { minTurn: 1, maxTurn: 99, priority: "High", goldAtLeast: 1, healthAtLeast: 0, targetRequired: false, reason: "millificent-magnetic" },
        ],
        sources: [officialPatch, "user-replay-20260906"],
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
