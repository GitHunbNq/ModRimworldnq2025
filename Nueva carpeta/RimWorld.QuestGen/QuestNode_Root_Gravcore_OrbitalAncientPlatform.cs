using RimWorld.Planet;
using Verse;

namespace RimWorld.QuestGen;

public class QuestNode_Root_Gravcore_OrbitalAncientPlatform : QuestNode_Root_Gravcore
{
	protected override void RunInt()
	{
		Slate slate = QuestGen.slate;
		Quest quest = QuestGen.quest;
		if (!TryFindSiteTile(out var tile))
		{
			Log.Error("Could not find valid site tile for orbital ancient platform quest.");
			return;
		}
		string text = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated");
		string inSignal = QuestGenUtility.HardcodedSignalWithQuestID("site.MapRemoved");
		string inSignal2 = QuestGenUtility.HardcodedSignalWithQuestID("site.MapSettled");
		Site site = QuestGen_Sites.GenerateSite(new SitePartDefWithParams[1]
		{
			new SitePartDefWithParams(SitePartDefOf.OrbitalAncientPlatform, new SitePartParams
			{
				points = slate.Get("points", 0f),
				threatPoints = slate.Get("points", 0f)
			})
		}, tile, Faction.OfAncientsHostile, hiddenSitePartsPossible: false, null, WorldObjectDefOf.ClaimableSpaceSite);
		slate.Set("site", site);
		quest.SpawnWorldObject(site);
		QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
		choice.rewards.Add(new Reward_DefinedThingDef(ThingDefOf.Gravcore));
		choice.rewards.Add(new Reward_DefinedThingDef(ThingDefOf.GravlitePanel));
		quest.RewardChoice().choices.Add(choice);
		quest.Letter(LetterDefOf.NeutralEvent, text, null, null, null, useColonistsFromCaravanArg: false, QuestPart.SignalListenMode.OngoingOnly, label: "OrbitalAncientPlatformLetterArrived".Translate(), text: "OrbitalAncientPlatformLetterArrivedText".Translate(), lookTargets: Gen.YieldSingle(site.Map));
		quest.End(QuestEndOutcome.Success, 0, null, inSignal);
		quest.End(QuestEndOutcome.Unknown, 0, null, inSignal2);
	}
}
