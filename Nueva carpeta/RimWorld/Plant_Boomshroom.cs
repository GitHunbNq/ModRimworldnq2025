using Verse;

namespace RimWorld;

public class Plant_Boomshroom : Plant
{
	public override void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null)
	{
		if (!base.Destroyed && HarvestableNow)
		{
			GenExplosion.DoExplosion(base.Position, base.Map, 4.9f, DamageDefOf.Flame, this, -1, -1f, null, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors: false, null, 0f, 1, 0f, damageFalloff: false, null, null, null);
		}
		base.Kill(dinfo, exactCulprit);
	}
}
