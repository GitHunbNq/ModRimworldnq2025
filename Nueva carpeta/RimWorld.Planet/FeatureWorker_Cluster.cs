using System.Collections.Generic;
using Verse;

namespace RimWorld.Planet;

public abstract class FeatureWorker_Cluster : FeatureWorker
{
	private readonly List<PlanetTile> roots = new List<PlanetTile>();

	private readonly HashSet<PlanetTile> rootsSet = new HashSet<PlanetTile>();

	private readonly List<PlanetTile> rootsWithAreaInBetween = new List<PlanetTile>();

	private readonly HashSet<PlanetTile> rootsWithAreaInBetweenSet = new HashSet<PlanetTile>();

	private readonly List<PlanetTile> currentGroup = new List<PlanetTile>();

	private readonly List<PlanetTile> currentGroupMembers = new List<PlanetTile>();

	private readonly HashSet<int> visitedValidGroupIDs = new HashSet<int>();

	private static readonly List<int> tmpGroup = new List<int>();

	protected virtual int MinRootGroupsInCluster => def.minRootGroupsInCluster;

	protected virtual int MinRootGroupSize => def.minRootGroupSize;

	protected virtual int MaxRootGroupSize => def.maxRootGroupSize;

	protected virtual int MinOverallSize => def.minSize;

	protected virtual int MaxOverallSize => def.maxSize;

	protected virtual int MaxSpaceBetweenRootGroups => def.maxSpaceBetweenRootGroups;

	protected abstract bool IsRoot(PlanetTile tile);

	protected virtual bool CanTraverse(PlanetTile tile, out bool ifRootThenRootGroupSizeMustMatch)
	{
		ifRootThenRootGroupSizeMustMatch = false;
		return true;
	}

	protected virtual bool IsMember(PlanetTile tile, out bool ifRootThenRootGroupSizeMustMatch)
	{
		ifRootThenRootGroupSizeMustMatch = false;
		return Find.WorldGrid[tile].feature == null;
	}

	public override void GenerateWhereAppropriate(PlanetLayer layer)
	{
		CalculateRootTiles(layer);
		CalculateRootsWithAreaInBetween(layer);
		CalculateContiguousGroups(layer);
	}

	private void CalculateRootTiles(PlanetLayer layer)
	{
		roots.Clear();
		_ = layer.TilesCount;
		for (int i = 0; i < layer.TilesCount; i++)
		{
			PlanetTile planetTile = new PlanetTile(i, layer);
			if (IsRoot(planetTile))
			{
				roots.Add(planetTile);
			}
		}
		rootsSet.Clear();
		rootsSet.AddRange(roots);
	}

	private void CalculateRootsWithAreaInBetween(PlanetLayer layer)
	{
		rootsWithAreaInBetween.Clear();
		rootsWithAreaInBetween.AddRange(roots);
		GenPlanetMorphology.Close(layer, rootsWithAreaInBetween, MaxSpaceBetweenRootGroups);
		rootsWithAreaInBetweenSet.Clear();
		rootsWithAreaInBetweenSet.AddRange(rootsWithAreaInBetween);
	}

	private void CalculateContiguousGroups(PlanetLayer layer)
	{
		WorldFloodFiller filler = layer.Filler;
		WorldGrid worldGrid = Find.WorldGrid;
		int minRootGroupSize = MinRootGroupSize;
		int maxRootGroupSize = MaxRootGroupSize;
		int minOverallSize = MinOverallSize;
		int maxOverallSize = MaxOverallSize;
		int minRootGroupsInCluster = MinRootGroupsInCluster;
		FeatureWorker.ClearVisited(layer);
		FeatureWorker.ClearGroupSizes(layer);
		FeatureWorker.ClearGroupIDs(layer);
		for (int i = 0; i < roots.Count; i++)
		{
			PlanetTile rootTile = roots[i];
			if (FeatureWorker.visited[rootTile.tileId])
			{
				continue;
			}
			bool anyMember = false;
			tmpGroup.Clear();
			filler.FloodFill(rootTile, (PlanetTile x) => rootsSet.Contains(x), delegate(PlanetTile x)
			{
				FeatureWorker.visited[x.tileId] = true;
				tmpGroup.Add(x.tileId);
				if (!anyMember && IsMember(x, out var _))
				{
					anyMember = true;
				}
			});
			for (int j = 0; j < tmpGroup.Count; j++)
			{
				FeatureWorker.groupSize[tmpGroup[j]] = tmpGroup.Count;
				if (anyMember)
				{
					FeatureWorker.groupID[tmpGroup[j]] = i + 1;
				}
			}
		}
		FeatureWorker.ClearVisited(layer);
		for (int k = 0; k < roots.Count; k++)
		{
			PlanetTile rootTile2 = roots[k];
			if (FeatureWorker.visited[rootTile2.tileId] || FeatureWorker.groupSize[rootTile2.tileId] < minRootGroupSize || FeatureWorker.groupSize[rootTile2.tileId] > maxRootGroupSize || FeatureWorker.groupSize[rootTile2.tileId] > maxOverallSize)
			{
				continue;
			}
			currentGroup.Clear();
			visitedValidGroupIDs.Clear();
			filler.FloodFill(rootTile2, delegate(PlanetTile x)
			{
				if (!rootsWithAreaInBetweenSet.Contains(x))
				{
					return false;
				}
				if (!CanTraverse(x, out var ifRootThenRootGroupSizeMustMatch2))
				{
					return false;
				}
				return (!ifRootThenRootGroupSizeMustMatch2 || !rootsSet.Contains(x) || (FeatureWorker.groupSize[x.tileId] >= minRootGroupSize && FeatureWorker.groupSize[x.tileId] <= maxRootGroupSize)) ? true : false;
			}, delegate(PlanetTile x)
			{
				FeatureWorker.visited[x.tileId] = true;
				currentGroup.Add(x);
				if (FeatureWorker.groupID[x.tileId] != 0 && FeatureWorker.groupSize[x.tileId] >= minRootGroupSize && FeatureWorker.groupSize[x.tileId] <= maxRootGroupSize)
				{
					visitedValidGroupIDs.Add(FeatureWorker.groupID[x.tileId]);
				}
			});
			if (currentGroup.Count < minOverallSize || currentGroup.Count > maxOverallSize || visitedValidGroupIDs.Count < minRootGroupsInCluster || (!def.canTouchWorldEdge && currentGroup.Any((PlanetTile x) => worldGrid.IsOnEdge(x))))
			{
				continue;
			}
			currentGroupMembers.Clear();
			for (int l = 0; l < currentGroup.Count; l++)
			{
				PlanetTile planetTile = currentGroup[l];
				if (IsMember(planetTile, out var ifRootThenRootGroupSizeMustMatch3) && (!ifRootThenRootGroupSizeMustMatch3 || !rootsSet.Contains(planetTile) || (FeatureWorker.groupSize[planetTile.tileId] >= minRootGroupSize && FeatureWorker.groupSize[planetTile.tileId] <= maxRootGroupSize)))
				{
					currentGroupMembers.Add(currentGroup[l]);
				}
			}
			if (currentGroupMembers.Count < minOverallSize)
			{
				continue;
			}
			if (currentGroup.Any((PlanetTile x) => worldGrid[x].feature == null))
			{
				currentGroup.RemoveAll((PlanetTile x) => worldGrid[x].feature != null);
			}
			AddFeature(layer, currentGroupMembers, currentGroup);
		}
	}
}
