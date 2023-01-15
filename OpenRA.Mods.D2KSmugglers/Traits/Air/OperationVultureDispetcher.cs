using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenRA.Mods.D2KSmugglers.Traits.Air
{
	public enum OperationVultureStage : ushort
	{
		NOT_STARTED = 0,
		SCOUT = 1,
		HARVEST = 2,
		DROP = 3
	}

	public class OperationVultureDispetcher
	{
		private Dictionary<Actor, bool> finishedScoutRun = null;
		bool reachedDropPoint2ndTime = false;
		public OperationVultureDispetcher() { }
		public OperationVultureStage GetStage()
		{
			if (finishedScoutRun == null)
				return OperationVultureStage.NOT_STARTED;

			OperationVultureStage stage = OperationVultureStage.SCOUT;

			if (finishedScoutRun.Values.Min())
				stage = OperationVultureStage.HARVEST;

			if (reachedDropPoint2ndTime)
				stage = OperationVultureStage.DROP;

			return stage;
		}

		public void NotifyFinishScoutRun(Actor actor)
		{
			if (finishedScoutRun.Keys.Contains(actor))
			{
				finishedScoutRun[actor] = true;
			}
		}

		public void NotifyFinishHarvestRun(Actor actor)
		{
			if (finishedScoutRun.Keys.Contains(actor))
			{
				reachedDropPoint2ndTime = true;
			}
		}

		public void NotifyVulturesArrived(IEnumerable<Actor> vultureSquad)
		{
			finishedScoutRun = new Dictionary<Actor, bool>();
			foreach (Actor actor in vultureSquad)
			{
				finishedScoutRun[actor] = false;
			}
		}
	}
}
