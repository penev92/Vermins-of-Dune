using System;

using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	public class SalvageTargetWarhead : SpreadDamageWarhead
	{
		[Desc("The percentage of the damage that will be returned as resources.")]
		public readonly int ResourceYield = 20;

		[Desc("The weapon to use for returning resources")]
		public readonly string WeaponYieldInfo = "DecomposeYield";

		const int SalvageResourceMultiplier = 100;
		protected override void InflictDamage(Actor victim, Actor firedBy, HitShape shape, WarheadArgs args)
		{
			var victimMaxHP = victim.Info.TraitInfo<HealthInfo>().HP;
			var victimCost = victim.Info.TraitInfo<ValuedInfo>().Cost;

			var damage = Damage * DamageVersus(victim, shape, args) / 100;

			// damage = Util.ApplyPercentageModifiers(damage, args.DamageModifiers);
			var healthBeforeDamage = victim.Trait<Health>().HP;

			victim.InflictDamage(firedBy, new Damage(damage, DamageTypes));

			var healthAfterDamage = victim.Trait<Health>().HP;

			var salvageGain = (long)SalvageResourceMultiplier * ResourceYield * (healthBeforeDamage - healthAfterDamage) * victimCost / victimMaxHP / 100;

			salvageGain = salvageGain > 0 ? salvageGain : 0;

			Func<WPos> muzzlePosition = () => victim.CenterPosition;

			var firedByMobileTrait = firedBy.Trait<IFacing>();
			Func<WAngle> muzzleFacing = () => firedByMobileTrait.Facing;

			WeaponInfo weaponYield;
			victim.World.Map.Rules.Weapons.TryGetValue(WeaponYieldInfo.ToLower(), out weaponYield);

			var argsReturn = new ProjectileArgs
			{
				Weapon = weaponYield,
				Facing = muzzleFacing(),
				CurrentMuzzleFacing = muzzleFacing,
				DamageModifiers = new int[] { (int)salvageGain },
				InaccuracyModifiers = Array.Empty<int>(),
				RangeModifiers = Array.Empty<int>(),
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
