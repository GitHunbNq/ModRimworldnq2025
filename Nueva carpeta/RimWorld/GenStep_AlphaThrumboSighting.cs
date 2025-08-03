using System.Collections.Generic;
using Verse;

namespace RimWorld;

public class GenStep_AlphaThrumboSighting : GenStep
{
	public IntRange normalThrumboCountRange = new IntRange(3, 5);

	private int MinRoomCells = 225;

	private static readonly FloatRange ExcludeBiologicalAgeRange = new FloatRange(0f, 250f);

	public override int SeedPart => 792525399;

	public override void Generate(Map map, GenStepParams parms)
	{
		TraverseParms traverseParams = TraverseParms.For(TraverseMode.NoPassClosedDoors).WithFenceblocked(forceFenceblocked: true);
		List<CellRect> usedRects = MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects");
		if (RCellFinder.TryFindRandomCellNearTheCenterOfTheMapWith((IntVec3 x) => x.Standable(map) && !x.Fogged(map) && map.reachability.CanReachMapEdge(x, traverseParams) && x.GetRoom(map).CellCount >= MinRoomCells && !usedRects.Any((CellRect ur) => ur.Contains(x)), map, out var result))
		{
			List<Pawn> list = new List<Pawn>();
			PawnGenerationRequest request = new PawnGenerationRequest(PawnKindDefOf.AlphaThrumbo, null, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: false, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: false, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: false, certainlyBeenInCryptosleep: false, forceRedressWorldPawnIfFormerColonist: false, worldPawnFactionDoesntMatter: false, 0f, 0f, null, 1f, null, null, null, null, null, null, null, null, null, null, null, null, forceNoIdeo: false, forceNoBackstory: false, forbidAnyTitle: false, forceDead: false, null, null, null, null, null, 0f, DevelopmentalStage.Adult, null, null, null);
			request.ExcludeBiologicalAgeRange = ExcludeBiologicalAgeRange;
			Pawn item = PawnGenerator.GeneratePawn(request);
			list.Add(item);
			int randomInRange = normalThrumboCountRange.RandomInRange;
			for (int i = 0; i < randomInRange; i++)
			{
				request = new PawnGenerationRequest(PawnKindDefOf.Thrumbo, null, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: false, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: false, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: false, certainlyBeenInCryptosleep: false, forceRedressWorldPawnIfFormerColonist: false, worldPawnFactionDoesntMatter: false, 0f, 0f, null, 1f, null, null, null, null, null, null, null, null, null, null, null, null, forceNoIdeo: false, forceNoBackstory: false, forbidAnyTitle: false, forceDead: false, null, null, null, null, null, 0f, DevelopmentalStage.Adult, null, null, null);
				item = PawnGenerator.GeneratePawn(request);
				list.Add(item);
			}
			for (int j = 0; j < list.Count; j++)
			{
				IntVec3 loc = CellFinder.RandomSpawnCellForPawnNear(result, map, 10);
				GenSpawn.Spawn(list[j], loc, map, Rot4.Random);
			}
		}
	}
}
