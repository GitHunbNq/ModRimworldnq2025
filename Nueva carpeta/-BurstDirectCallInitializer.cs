using RimWorld;
using RimWorld.Planet;
using UnityEngine;

internal static class _0024BurstDirectCallInitializer
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
	private static void Initialize()
	{
		MapGenUtility.ComputeLargestRects_0000B6CA_0024BurstDirectCall.Initialize();
		MapGenUtility.RectsComputeSpaces_0000B6CB_0024BurstDirectCall.Initialize();
		FastTileFinder.Initialize_0024ComputeQueryJob_SphericalDistance_00014E3B_0024BurstDirectCall();
		PlanetLayer.CalculateAverageTileSize_000152FC_0024BurstDirectCall.Initialize();
		PlanetLayer.IntGetTileSize_000152FE_0024BurstDirectCall.Initialize();
		PlanetLayer.IntGetTileCenter_00015301_0024BurstDirectCall.Initialize();
	}
}
