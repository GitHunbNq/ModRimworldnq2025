using System;
using System.Collections.Generic;
using Verse;

namespace RimWorld;

public class RitualOutcomeEffectWorker_GravshipLaunch : RitualOutcomeEffectWorker_FromQuality
{
	public RitualOutcomeEffectWorker_GravshipLaunch()
	{
	}

	public RitualOutcomeEffectWorker_GravshipLaunch(RitualOutcomeEffectDef def)
		: base(def)
	{
	}

	public override void Apply(float progress, Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual)
	{
		if (progress < 1f)
		{
			Messages.Message("GravshipLaunchInterrupted".Translate(), MessageTypeDefOf.NegativeEvent);
			return;
		}
		try
		{
			CompPilotConsole compPilotConsole = jobRitual.selectedTarget.Thing?.TryGetComp<CompPilotConsole>();
			float quality = GetQuality(jobRitual, progress);
			compPilotConsole.engine.launchInfo = new LaunchInfo
			{
				pilot = jobRitual.PawnWithRole("pilot"),
				copilot = jobRitual.PawnWithRole("copilot"),
				quality = quality,
				doNegativeOutcome = Rand.Chance(GravshipUtility.NegativeLandingOutcomeFromQuality(quality))
			};
			if (jobRitual.Map.listerThings.AnyThingWithDef(ThingDefOf.GravAnchor))
			{
				compPilotConsole.StartChoosingDestination();
			}
			else
			{
				GravshipUtility.PreLaunchConfirmation(compPilotConsole.engine, compPilotConsole.StartChoosingDestination);
			}
		}
		catch (Exception ex)
		{
			Log.Error("Error launching gravship: " + ex);
		}
	}
}
