using System;
using System.Collections.Generic;
using Actors.Enemies;
using Assets.Scripts._Data.MapsAndStages;

namespace QuestTracker;

/// <summary>Kinds of places in a run that a quest can send the player to.</summary>
internal enum TargetKind
{
    Boombox, MonkeCage, Bush, Bandit, Bananarang, Gift, FrogEgg, Frogs, QuestUnlock, SkeletonStatue,
    Crypt, CryptLoot, CryptExit, ChargeShrine, MagnetShrine, CursedShrine, Microwave,
    ShadyCommon, ShadyRare, ShadyEpic, ShadyLegendary, Chest, Pot, Tumbleweed,
    Boss, Miniboss, Scorpion, Skeletons, Goblins, Spectres, Cactus, SpookySteve, Pumpkin, Coffin, DesertGrave,
}

/// <summary>
/// Where a quest wants the player to go. Kinds are tried in order: the first one with something
/// on the map wins (for example: crypt loot, else the crypt door).
/// </summary>
internal class GuideRule
{
    public TargetKind[] Kinds;
    public EMap Map; // None = any map

    private string _howEn, _howAr;

    public GuideRule(EMap map, params TargetKind[] kinds) { Map = map; Kinds = kinds; }

    /// <summary>Adds a short "how to finish it" line for quests that aren't obvious (hidden things, multi-step quests).</summary>
    public GuideRule How(string en, string ar) { _howEn = en; _howAr = ar; return this; }

    public string HowTo => _howEn == null ? null : L.T(_howEn, _howAr);
}

internal static class GuideTargets
{
    private static readonly Dictionary<string, GuideRule> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a_boombox"] = new GuideRule(EMap.Forest, TargetKind.Boombox).How("Find the 3 boomboxes hidden around the Forest and turn each one on.", "ابحث عن مكبرات الصوت الثلاثة المخفية في الغابة وشغّل كل واحد منها."),
        ["a_monke"] = new GuideRule(EMap.Forest, TargetKind.MonkeCage).How("Find Monke's cage somewhere in the Forest and open it to free him.", "ابحث عن قفص مونكي في مكان ما في الغابة وافتحه لتحريره."),
        ["a_bush"] = new GuideRule(EMap.Forest, TargetKind.Bush).How("Find the Bush hiding in the Forest, talk to it, then beat it in the fight.", "ابحث عن الشجيرة المختبئة في الغابة، تفاعل معها ثم اهزمها في القتال."),
        ["a_bandit"] = new GuideRule(EMap.Desert, TargetKind.Bandit).How("Find the Bandit hiding in the Desert, talk to him, then beat him in the fight.", "ابحث عن اللص المختبئ في الصحراء، تفاعل معه ثم اهزمه في القتال."),
        ["a_bananarang"] = new GuideRule(EMap.None, TargetKind.Bananarang).How("A banana is hidden somewhere on the map. Find it and pick it up.", "توجد موزة مخفية في مكان ما على الخريطة. ابحث عنها والتقطها."),
        ["a_santaHat"] = new GuideRule(EMap.Forest, TargetKind.Gift).How("A present is hidden somewhere in the Forest. Find it and open it.", "توجد هدية مخفية في مكان ما في الغابة. ابحث عنها وافتحها."),
        ["a_hatFrog1"] = new GuideRule(EMap.Forest, TargetKind.Frogs, TargetKind.FrogEgg).How("On Forest stage 1, break the frog egg, then catch the frogs that hop out.", "في المرحلة 1 من الغابة، اكسر بيضة الضفادع ثم امسك الضفادع التي تخرج منها."),
        ["a_hatFrog2"] = new GuideRule(EMap.Forest, TargetKind.Frogs, TargetKind.FrogEgg).How("On Forest stage 2, break the frog egg, then catch the frogs that hop out.", "في المرحلة 2 من الغابة، اكسر بيضة الضفادع ثم امسك الضفادع التي تخرج منها."),
        ["a_hatFrog3"] = new GuideRule(EMap.Forest, TargetKind.Frogs, TargetKind.FrogEgg).How("On Forest stage 3, break the frog egg, then catch the frogs that hop out.", "في المرحلة 3 من الغابة، اكسر بيضة الضفادع ثم امسك الضفادع التي تخرج منها."),
        ["a_oldMask"] = new GuideRule(EMap.Graveyard, TargetKind.QuestUnlock).How("The mask is hidden somewhere on the Graveyard. Find it and pick it up.", "القناع مخفي في مكان ما في المقبرة. ابحث عنه والتقطه."),
        ["a_snek"] = new GuideRule(EMap.Graveyard, TargetKind.QuestUnlock, TargetKind.Crypt).How("Enter the crypt on the Graveyard. The snake is hidden inside.", "ادخل السرداب في المقبرة. الأفعى مخفية بداخله."),
        ["a_graveyard"] = new GuideRule(EMap.Desert, TargetKind.DesertGrave, TargetKind.SkeletonStatue).How("As Calcium on Desert Tier 2: disturb the 4 graves and beat their ghosts, then touch the skeleton statue to summon the undead boss and beat it.", "بشخصية كالسيوم في الصحراء المستوى 2: أزعج القبور الأربعة واهزم أشباحها، ثم المس تمثال الهيكل العظمي لاستدعاء زعيم الموتى الأحياء واهزمه."),
        ["a_potSteel"] = new GuideRule(EMap.Graveyard, TargetKind.CryptLoot, TargetKind.Crypt).How("Enter the first crypt on the Graveyard and break every pot and open every chest inside.", "ادخل السرداب الأول في المقبرة واكسر كل جرة وافتح كل صندوق بداخله."),
        ["a_roberto"] = new GuideRule(EMap.Graveyard, TargetKind.SpookySteve, TargetKind.Coffin, TargetKind.Crypt).How("On the Graveyard, Spooky Steve comes out of the crypt: enter the crypt (guides say open the 4 coffins for crypt keys first, or fail the crypt escape), then kill him.", "في المقبرة، يخرج ستيف المخيف من السرداب: ادخل السرداب (تقول الأدلة افتح التوابيت الأربعة أولا لتحصل على مفاتيح السرداب، أو افشل في الهروب من السرداب)، ثم اقتله."),
        ["a_pumpkin"] = new GuideRule(EMap.Graveyard, TargetKind.Pumpkin).How("As Roberto on the Graveyard, break every pumpkin on the map.", "بشخصية روبرتو في المقبرة، اكسر كل يقطينة على الخريطة."),

        ["a_suckyMagnet"] = new(EMap.None, TargetKind.ChargeShrine),
        ["a_chaosTome"] = new(EMap.None, TargetKind.ChargeShrine),
        ["a_tornado"] = new GuideRule(EMap.Desert, TargetKind.ChargeShrine).How("On the Desert, wait for a sandstorm, then charge a charge shrine while it lasts.", "في الصحراء، انتظر العاصفة الرملية ثم اشحن مزار شحن أثناءها."),
        ["a_attractionTome"] = new(EMap.None, TargetKind.MagnetShrine),
        ["a_speedBoi"] = new GuideRule(EMap.None, TargetKind.CursedShrine, TargetKind.Boss).How("Use at least 2 boss curse shrines, then finish the stage.", "استخدم مزاري لعنة الزعيم على الأقل، ثم أكمل المرحلة."),
        ["a_hatHeadset"] = new GuideRule(EMap.Desert, TargetKind.CursedShrine, TargetKind.Boss).How("As Ninja on Desert stage 1, use at least 2 boss curse shrines, then finish the stage.", "بشخصية النينجا في المرحلة 1 من الصحراء، استخدم مزاري لعنة الزعيم على الأقل، ثم أكمل المرحلة."),

        ["a_hatMicrowave"] = new GuideRule(EMap.None, TargetKind.Microwave).How("Keep using microwaves until they blow up.", "استمر في استخدام أجهزة الميكروويف حتى تنفجر."),
        ["a_noelle"] = new GuideRule(EMap.None, TargetKind.Microwave).How("Put an Ice Crystal in a microwave to copy it, 3 times in a row.", "ضع بلورة الجليد في الميكروويف لنسخها 3 مرات متتالية."),
        ["a_poisonGloves"] = new GuideRule(EMap.None, TargetKind.Microwave).How("Put a Moldy Cheese in a microwave to copy it, 3 times in a row.", "ضع الجبنة المتعفنة في الميكروويف لنسخها 3 مرات متتالية."),
        ["a_hatShadyBlack"] = new(EMap.None, TargetKind.ShadyCommon),
        ["a_hatShadyBlue"] = new(EMap.None, TargetKind.ShadyRare),
        ["a_hatShadyPink"] = new(EMap.None, TargetKind.ShadyEpic),
        ["a_hatShadyGold"] = new(EMap.None, TargetKind.ShadyLegendary),
        ["a_key"] = new(EMap.None, TargetKind.Chest),
        ["a_goldenSneakers"] = new(EMap.None, TargetKind.Pot),
        ["a_xpTome"] = new(EMap.None, TargetKind.Pot),
        ["a_katana"] = new GuideRule(EMap.Desert, TargetKind.Tumbleweed).How("On Desert stage 1, break rolling tumbleweeds. Each has a small chance to drop it.", "في المرحلة 1 من الصحراء، اكسر الشجيرات المتدحرجة. لكل واحدة فرصة صغيرة لإسقاطه."),
        ["a_shotgun"] = new GuideRule(EMap.Desert, TargetKind.Tumbleweed).How("On Desert stage 2, break rolling tumbleweeds. Each has a small chance to drop it.", "في المرحلة 2 من الصحراء، اكسر الشجيرات المتدحرجة. لكل واحدة فرصة صغيرة لإسقاطه."),

        ["a_poisonFlask"] = new GuideRule(EMap.Desert, TargetKind.Scorpion).How("The Scorpion miniboss spawns on the Desert. Kill it 3 times (across runs).", "يظهر الزعيم المصغر العقربوسي في الصحراء. اقتله 3 مرات (عبر عدة جولات)."),
        ["a_tacticalGlasses"] = new(EMap.None, TargetKind.Miniboss),
        ["a_hatSheriff"] = new(EMap.None, TargetKind.Miniboss, TargetKind.Boss),
        ["a_calcium"] = new(EMap.None, TargetKind.Skeletons),
        ["a_ogre"] = new(EMap.None, TargetKind.Goblins),
        ["a_dragonsBreath"] = new(EMap.Desert, TargetKind.Spectres),
        ["a_cactus"] = new(EMap.Desert, TargetKind.Cactus),

        ["a_scythe"] = new GuideRule(EMap.Graveyard, TargetKind.Boss, TargetKind.Coffin).How("On the Graveyard, open the 4 coffins and beat each ghost for a crypt key, then fight Big Bob and kill him.", "في المقبرة، افتح التوابيت الأربعة واهزم كل شبح لتحصل على مفتاح سرداب، ثم قاتل بوب الكبير واقتله."),
        ["a_bobsLantern"] = new GuideRule(EMap.Graveyard, TargetKind.Boss, TargetKind.Coffin).How("Reach Big Bob on the Graveyard (open the 4 coffins for crypt keys) and land the killing blow with a lamp.", "صل إلى بوب الكبير في المقبرة (افتح التوابيت الأربعة لتحصل على مفاتيح السرداب) واجعل الضربة القاضية بمصباح."),
        ["a_hatSunglasses"] = new GuideRule(EMap.Graveyard, TargetKind.Boss, TargetKind.Coffin).How("As Calcium, reach Big Bob on the Graveyard (open the 4 coffins first) and win the fight in under 60 seconds.", "بشخصية كالسيوم، صل إلى بوب الكبير في المقبرة (افتح التوابيت الأربعة أولا) واربح القتال في أقل من 60 ثانية."),
        ["a_desert"] = new(EMap.Forest, TargetKind.Boss),
        ["a_chadwell"] = new(EMap.Forest, TargetKind.Boss),
        ["a_clank"] = new(EMap.Forest, TargetKind.Boss),
        ["a_robinette"] = new(EMap.Forest, TargetKind.Boss),
        ["a_ninja"] = new(EMap.Desert, TargetKind.Boss),
        ["a_vlad"] = new(EMap.Desert, TargetKind.Boss),
        ["a_spaceNoodle"] = new(EMap.Desert, TargetKind.Boss),
        ["a_skin_megachadKevin"] = new(EMap.Forest, TargetKind.Boss),
        ["a_hatMedieval"] = new(EMap.Forest, TargetKind.Boss),
        ["a_hatCheese"] = new(EMap.None, TargetKind.Boss),
        ["a_energyCore"] = new(EMap.None, TargetKind.Boss),
        ["a_bossBuster"] = new(EMap.None, TargetKind.Boss),
        ["a_cursedTome"] = new(EMap.None, TargetKind.Boss),
        ["a_heroSword"] = new(EMap.None, TargetKind.Boss),
        ["a_holyBook"] = new(EMap.None, TargetKind.Boss),
        ["a_gamerGoggles"] = new(EMap.None, TargetKind.Boss),
        ["a_quinsMask"] = new(EMap.None, TargetKind.Boss),
    };

    private const string CryptEn = "On the Graveyard, enter the first crypt and reach its exit before the timer runs out.";
    private const string CryptAr = "في المقبرة، ادخل السرداب الأول واصل إلى مخرجه قبل انتهاء الوقت.";

    /// <summary>The guide rule for a quest, or null when the quest has no place to go.</summary>
    public static GuideRule For(TrackedQuest q)
    {
        if (q?.Id == null) return null;
        if (ById.TryGetValue(q.Id, out var rule)) return rule;

        string id = q.Id;
        // Skin quests share a naming pattern for every character.
        if (id.StartsWith("a_skin_", StringComparison.OrdinalIgnoreCase))
        {
            if (id.EndsWith("Crypt", StringComparison.OrdinalIgnoreCase)) return new GuideRule(EMap.Graveyard, TargetKind.CryptExit, TargetKind.Crypt).How(CryptEn, CryptAr);
            if (id.EndsWith("Speedrun", StringComparison.OrdinalIgnoreCase) || id.EndsWith("FinalBoss", StringComparison.OrdinalIgnoreCase))
                return new(EMap.None, TargetKind.Boss);
            return null;
        }

        // Challenges: escape the crypt, or beat the stage (boss, then portal).
        if (id.StartsWith("a_graveyard1_crypt", StringComparison.OrdinalIgnoreCase)) return new GuideRule(EMap.Graveyard, TargetKind.CryptExit, TargetKind.Crypt).How(CryptEn, CryptAr);
        if (id.StartsWith("a_forest", StringComparison.OrdinalIgnoreCase)) return new(EMap.Forest, TargetKind.Boss);
        if (id.StartsWith("a_desert", StringComparison.OrdinalIgnoreCase)) return new(EMap.Desert, TargetKind.Boss);
        if (id.StartsWith("a_graveyard", StringComparison.OrdinalIgnoreCase)) return new(EMap.Graveyard, TargetKind.Boss);

        return FromText(q.Description);
    }

    /// <summary>Fallback for quests we don't know (new game versions): look for a place in the quest text.</summary>
    private static GuideRule FromText(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string t = text.ToLowerInvariant();
        bool Has(params string[] words) { foreach (var w in words) if (t.Contains(w)) return true; return false; }

        if (Has("charge shrine", "مزار الشحن")) return new(EMap.None, TargetKind.ChargeShrine);
        if (Has("crypt", "السرداب")) return new(EMap.Graveyard, TargetKind.CryptExit, TargetKind.Crypt);
        if (Has("microwave", "ميكروويف")) return new(EMap.None, TargetKind.Microwave);
        if (Has("miniboss", "mini boss", "زعيم مصغر")) return new(EMap.None, TargetKind.Miniboss);
        if (Has("boss", "زعيم")) return new(EMap.None, TargetKind.Boss);
        return null;
    }


    /// <summary>The one enemy a target kind stands for, so its spawn map and stage can be looked up.</summary>
    public static EEnemy? EnemyFor(TargetKind kind) => kind switch
    {
        TargetKind.SpookySteve => EEnemy.GhostInvincible,
        TargetKind.Scorpion => EEnemy.MinibossScorpion,
        TargetKind.Cactus => EEnemy.CactusShooter,
        TargetKind.Spectres => EEnemy.Wisp,
        _ => null,
    };

    public static bool Matches(EEnemy e, TargetKind kind) => kind switch
    {
        TargetKind.Frogs => e is EEnemy.FrogGreen or EEnemy.FrogBlue,
        TargetKind.Scorpion => e == EEnemy.MinibossScorpion,
        TargetKind.Skeletons => e is EEnemy.Skeleton or EEnemy.GoldenSkeleton or EEnemy.XpSkeleton or EEnemy.ArmoredSkeleton
            or EEnemy.SkeletonDusty or EEnemy.ArmoredSkeletonDusty or EEnemy.SkeletonMage or EEnemy.Tumblebone,
        TargetKind.Goblins => e is EEnemy.Goblin or EEnemy.GoblinStrong or EEnemy.GoblinTank,
        TargetKind.Spectres => e == EEnemy.Wisp,
        TargetKind.Cactus => e == EEnemy.CactusShooter,
        TargetKind.SpookySteve => e == EEnemy.GhostInvincible, // "Spooky Steve" in the game's enemy names
        _ => false,
    };
}
