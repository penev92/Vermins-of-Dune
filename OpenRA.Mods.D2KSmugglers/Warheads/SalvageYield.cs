using System;

using OpenRA.GameRules;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	public class SalvageYieldWarhead : Warhead
	{
		const int SalvageResourceMultiplier = 100;
		static int TryRepair(Actor actor, int resourceAmount)
		{
			var actorHealth = actor.Trait<IHealth>();
			var actorCost = actor.Info.TraitInfo<ValuedInfo>();

			var repairCost = SalvageResourceMultiplier * (actorHealth.MaxHP - actorHealth.HP) * actorCost.Cost / actorHealth.MaxHP;

			var salvageSpentOnRepair = Math.Min(repairCost, resourceAmount);

			var healthRestored = salvageSpentOnRepair * actorHealth.MaxHP / actorCost.Cost / SalvageResourceMultiplier;

			actorHealth.InflictDamage(actor, actor, new Damage(-healthRestored), true);

			return resourceAmount - salvageSpentOnRepair;
		}

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var salvageAmount = args.DamageModifiers[0];
			var actor = args.WeaponTarget.Actor;

			if (actor == null || actor.IsDead)
			{
				return;
			}

			salvageAmount = TryRepair(actor, salvageAmount);
			var playerResources = actor.Owner.PlayerActor.Trait<PlayerResources>();
			var resourcesGain = Math.Min(salvageAmount / SalvageResourceMultiplier,
				playerResources.ResourceCapacity - playerResources.Resources);

			if (resourcesGain > 0)
			{
				actor.Owner.PlayerActor.Trait<PlayerResources>().GiveResources(resourcesGain);

				var resourceGainString = FloatingText.FormatCashTick(resourcesGain);
				actor.World.AddFrameEndTask(w => w.Add(new FloatingText(actor.CenterPosition, actor.Owner.Color, resourceGainString, 30)));

				if (actor.Owner == actor.World.RenderPlayer)
					foreach (var notify in actor.World.ActorsWithTrait<INotifyResourceAccepted>())
					{
						if (notify.Actor.Owner != actor.Owner)
							continue;

						notify.Trait.OnResourceAccepted(notify.Actor, actor, "Spice", 1, salvageAmount);
					}
			}
		}

		public override bool IsValidAgainst(Actor victim, Actor firedBy)
		{
			// Cannot be damaged without a Health trait
			if (!victim.Info.HasTraitInfo<IHealthInfo>())
				return false;

			// Cannot be damaged without a Valued trait
			if (!victim.Info.HasTraitInfo<ValuedInfo>())
				return false;

			return base.IsValidAgainst(victim, firedBy);
		}
	}
}
