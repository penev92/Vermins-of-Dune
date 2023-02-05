using System;

using OpenRA.GameRules;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	public class SalvageYieldWarhead : Warhead
	{
		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var resourceGain = args.DamageModifiers[0];
			Actor actor = args.WeaponTarget.Actor;

			if (actor == null || actor.IsDead)
			{
				return;
			}

			var playerResources = actor.Owner.PlayerActor.Trait<PlayerResources>();

			resourceGain = Math.Min(resourceGain, playerResources.ResourceCapacity - playerResources.Resources);

			if (resourceGain > 0)
			{
				actor.Owner.PlayerActor.Trait<PlayerResources>().GiveResources(resourceGain);
				var resourceGainString = FloatingText.FormatCashTick(resourceGain);

				if (actor.Owner.IsAlliedWith(actor.World.RenderPlayer))
					actor.World.AddFrameEndTask(w => w.Add(new FloatingText(actor.CenterPosition, actor.Owner.Color, resourceGainString, 30)));

				if (actor.Owner == actor.World.RenderPlayer)
					foreach (var notify in actor.World.ActorsWithTrait<INotifyResourceAccepted>())
					{
						if (notify.Actor.Owner != actor.Owner)
							continue;

						notify.Trait.OnResourceAccepted(notify.Actor, actor, "Spice", 1, resourceGain);
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
