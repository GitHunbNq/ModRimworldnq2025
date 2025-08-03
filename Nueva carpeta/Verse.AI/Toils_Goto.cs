using RimWorld;

namespace Verse.AI;

public static class Toils_Goto
{
	public static Toil Goto(TargetIndex ind, PathEndMode peMode)
	{
		return GotoThing(ind, peMode);
	}

	public static Toil GotoThing(TargetIndex ind, PathEndMode peMode, bool canGotoSpawnedParent = false)
	{
		Toil toil = ToilMaker.MakeToil("GotoThing");
		toil.initAction = delegate
		{
			Pawn actor = toil.actor;
			LocalTargetInfo dest = actor.jobs.curJob.GetTarget(ind);
			Thing thing = dest.Thing;
			if (thing != null && canGotoSpawnedParent)
			{
				dest = thing.SpawnedParentOrMe;
			}
			actor.pather.StartPath(dest, peMode);
		};
		toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;
		if (canGotoSpawnedParent)
		{
			toil.FailOnSelfAndParentsDespawnedOrNull(ind);
		}
		else
		{
			toil.FailOnDespawnedOrNull(ind);
		}
		return toil;
	}

	public static Toil GotoThing(TargetIndex ind, IntVec3 exactCell)
	{
		Toil toil = ToilMaker.MakeToil("GotoThing");
		toil.initAction = delegate
		{
			toil.actor.pather.StartPath(exactCell, PathEndMode.OnCell);
		};
		toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;
		toil.FailOnDespawnedOrNull(ind);
		return toil;
	}

	public static Toil GotoCell(TargetIndex ind, PathEndMode peMode)
	{
		Toil toil = ToilMaker.MakeToil("GotoCell");
		toil.initAction = delegate
		{
			Pawn actor = toil.actor;
			LocalTargetInfo target = actor.jobs.curJob.GetTarget(ind);
			if (actor.Position == target.Cell)
			{
				actor.pather.StopDead();
				actor.jobs.curDriver.ReadyForNextToil();
			}
			else
			{
				actor.pather.StartPath(target, peMode);
			}
		};
		toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;
		return toil;
	}

	public static Toil GotoCell(IntVec3 cell, PathEndMode peMode)
	{
		Toil toil = ToilMaker.MakeToil("GotoCell");
		toil.initAction = delegate
		{
			Pawn actor = toil.actor;
			if (actor.Position == cell)
			{
				actor.jobs.curDriver.ReadyForNextToil();
			}
			else
			{
				actor.pather.StartPath(cell, peMode);
			}
		};
		toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;
		return toil;
	}

	public static Toil MoveOffTargetBlueprint(TargetIndex targetInd)
	{
		Toil toil = ToilMaker.MakeToil("MoveOffTargetBlueprint");
		toil.initAction = delegate
		{
			Pawn actor = toil.actor;
			Blueprint blueprint = actor.jobs.curJob.GetTarget(targetInd).Thing as Blueprint;
			IntVec3 result;
			if (blueprint.DestroyedOrNull() || !actor.Position.IsInside(blueprint))
			{
				actor.jobs.curDriver.ReadyForNextToil();
			}
			else if (RCellFinder.TryFindGoodAdjacentSpotToTouch(actor, blueprint, out result))
			{
				actor.pather.StartPath(result, PathEndMode.OnCell);
			}
			else
			{
				actor.jobs.EndCurrentJob(JobCondition.Incompletable);
			}
		};
		toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;
		return toil;
	}

	public static Toil GotoBuild(TargetIndex ind)
	{
		Toil toil = ToilMaker.MakeToil("GotoBuild");
		toil.initAction = delegate
		{
			Pawn actor = toil.actor;
			LocalTargetInfo target = actor.jobs.curJob.GetTarget(ind);
			Thing thing = target.Thing;
			if (RCellFinder.TryFindGoodAdjacentSpotToTouch(actor, thing, out var result))
			{
				actor.pather.StartPath(result, PathEndMode.OnCell);
			}
			else
			{
				actor.pather.StartPath(target, PathEndMode.OnCell);
			}
		};
		toil.tickIntervalAction = delegate
		{
			Pawn actor2 = toil.actor;
			Thing thing2 = actor2.jobs.curJob.GetTarget(ind).Thing;
			if (actor2.CanReachImmediate(thing2, PathEndMode.Touch) && actor2.Map.reservationManager.CanReserve(actor2, actor2.Position))
			{
				actor2.Reserve(actor2.Position, actor2.CurJob);
				actor2.pather.StopDead();
				actor2.jobs.curDriver.ReadyForNextToil();
			}
		};
		toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;
		toil.FailOnDespawnedOrNull(ind);
		return toil;
	}
}
