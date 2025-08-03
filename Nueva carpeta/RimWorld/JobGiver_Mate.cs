using System;
using System.Linq;
using Verse;
using Verse.AI;

namespace RimWorld;

public class JobGiver_Mate : ThinkNode_JobGiver
{
	protected override Job TryGiveJob(Pawn pawn)
	{
		if (pawn.gender != Gender.Male || pawn.Sterile() || pawn.RaceProps.disableMating)
		{
			return null;
		}
		Predicate<Thing> validator = delegate(Thing t)
		{
			Pawn pawn2 = t as Pawn;
			if (pawn2.Downed)
			{
				return false;
			}
			if (!pawn2.CanCasuallyInteractNow() || pawn2.IsForbidden(pawn))
			{
				return false;
			}
			if (pawn2.Faction != pawn.Faction)
			{
				return false;
			}
			return PawnUtility.FertileMateTarget(pawn, pawn2) ? true : false;
		};
		foreach (ThingDef item in pawn.RaceProps.canCrossBreedWith.OrElseEmptyEnumerable().Prepend(pawn.def))
		{
			Pawn pawn3 = (Pawn)GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(item), PathEndMode.Touch, TraverseParms.For(pawn), 30f, validator);
			if (pawn3 != null)
			{
				return JobMaker.MakeJob(JobDefOf.Mate, pawn3);
			}
		}
		return null;
	}
}
