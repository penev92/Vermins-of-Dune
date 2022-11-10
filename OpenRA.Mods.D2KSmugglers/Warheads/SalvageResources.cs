using System;

using OpenRA.Activities;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.D2KSmugglers.Projectiles;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	public class SalvageResourcesWarhead : SpreadDamageWarhead
	{
		[Desc("The percentage of the damage that will be returned as resources.")]
		public readonly int ResourceYield = 75;

		[Desc("The weapon to use for returning resources")]
		public readonly string WeaponYieldInfo = "DecomposeYield";

		protected override void InflictDamage(Actor victim, Actor firedBy, HitShape shape, WarheadArgs args)
		{
			var victimMaxHP = victim.Info.TraitInfo<HealthInfo>().HP;
			var victimCost = victim.Info.TraitInfo<ValuedInfo>().Cost;

			int damage = Damage * DamageVersus(victim, shape, args) / 100;

			// damage = Util.ApplyPercentageModifiers(damage, args.DamageModifiers);
			var healthBeforeDamage = victim.Trait<Health>().HP;

			victim.InflictDamage(firedBy, new Damage(damage, DamageTypes));

			var healthAfterDamage = victim.Trait<Health>().HP;

			var resourceGain = ResourceYield * (healthBeforeDamage - healthAfterDamage) * victimCost / victimMaxHP / 100;

			resourceGain = resourceGain > 0 ? resourceGain : 0;
			resourceGain = ((int)resourceGain / 10) * 10;

			firedBy.Owner.PlayerActor.Trait<PlayerResources>().GiveResources(resourceGain);

			var resourceGainString = FloatingText.FormatCashTick(resourceGain);

			if (firedBy.Owner.IsAlliedWith(firedBy.World.RenderPlayer))
			{
				firedBy.World.AddFrameEndTask(w => w.Add(new FloatingText(firedBy.CenterPosition, firedBy.Owner.Color, resourceGainString, 30)));

				Func<WPos> muzzlePosition = () => victim.CenterPosition;
				Func<WAngle> muzzleFacing = () => (firedBy.CenterPosition - victim.CenterPosition).Yaw;

				WeaponInfo weaponYield;
				victim.World.Map.Rules.Weapons.TryGetValue(WeaponYieldInfo.ToLower(), out weaponYield);

				var argsReturn = new ProjectileArgs
				{
					Weapon = weaponYield,
					Facing = muzzleFacing(),
					CurrentMuzzleFacing = muzzleFacing,
					DamageModifiers = new int[0],
					InaccuracyModifiers = new int[0],
					RangeModifiers = new int[0],
					Source = muzzlePosition(),
					CurrentSource = muzzlePosition,
					SourceActor = victim,
					PassiveTarget = firedBy.CenterPosition,
					GuidedTarget = Target.FromActor(firedBy)
				};
				var projectile = weaponYield.Projectile.Create(argsReturn);
				if (projectile != null)
					firedBy.World.AddFrameEndTask(w => w.Add(projectile));
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
