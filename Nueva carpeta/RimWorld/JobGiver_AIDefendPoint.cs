using Verse;
using Verse.AI;

namespace RimWorld;

public class JobGiver_AIDefendPoint : JobGiver_AIFightEnemy
{
	protected override Job TryGiveJob(Pawn pawn)
	{
		if (pawn.mindState.duty.radius > 0f)
		{
			targetAcquireRadius = pawn.mindState.duty.radius;
			targetKeepRadius = pawn.mindState.duty.radius * 1.5f;
		}
		return base.TryGiveJob(pawn);
	}

	protected override bool TryFindShootingPosition(Pawn pawn, out IntVec3 dest, Verb verbToUse = null)
	{
		Thing enemyTarget = pawn.mindState.enemyTarget;
		Verb verb = verbToUse ?? pawn.TryGetAttackVerb(enemyTarget, !pawn.IsColonist);
		if (verb == null)
		{
			dest = IntVec3.Invalid;
			return false;
		}
		CastPositionRequest newReq = default(CastPositionRequest);
		newReq.caster = pawn;
		newReq.target = enemyTarget;
		newReq.verb = verb;
		newReq.maxRangeFromTarget = 9999f;
		newReq.locus = (IntVec3)pawn.mindState.duty.focus;
		newReq.maxRangeFromLocus = pawn.mindState.duty.radius;
		newReq.wantCoverFromTarget = verb.EffectiveRange > 7f;
		return CastPositionFinder.TryFindCastPosition(newReq, out dest);
	}
}
