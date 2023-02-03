#region Copyright & License Information
/*
 * This code is based on a code from OpenRA
 * Please see it for license information
 */
#endregion

using System.Collections.Generic;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.D2KSmugglers.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.D2KSmugglers.Projectiles
{
	public class SalvageBombInfo : IProjectileInfo
	{
		[Desc("Name of the image containing the projectile sequence.")]
		public readonly string Image = null;

		[SequenceReference(nameof(Image), allowNullImage: true)]
		[Desc("Loop a randomly chosen sequence of Image from this list while this projectile is moving.")]
		public readonly string[] Sequences = { "idle" };

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("Palette used to render the projectile sequence.")]
		public readonly string Palette = "effect";

		[Desc("Palette is a player palette BaseName")]
		public readonly bool IsPlayerPalette = false;

		[Desc("Should the projectile's shadow be rendered?")]
		public readonly bool Shadow = false;

		[Desc("Minimum vertical launch angle (pitch).")]
		public readonly WAngle MinimumLaunchAngle = new WAngle(-64);

		[Desc("Maximum vertical launch angle (pitch).")]
		public readonly WAngle MaximumLaunchAngle = new WAngle(128);

		[Desc("Minimum launch speed in WDist / tick. Defaults to Speed if -1.")]
		public readonly WDist MinimumLaunchSpeed = new WDist(-1);

		[Desc("Maximum launch speed in WDist / tick. Defaults to Speed if -1.")]
		public readonly WDist MaximumLaunchSpeed = new WDist(-1);

		[Desc("Maximum projectile speed in WDist / tick")]
		public readonly WDist Speed = new WDist(384);

		[Desc("Projectile acceleration when propulsion activated.")]
		public readonly WDist Acceleration = new WDist(5);

		[Desc("How many ticks before this missile is armed and can explode.")]
		public readonly int Arm = 0;

		[Desc("Is the missile blocked by actors with BlocksProjectiles: trait.")]
		public readonly bool Blockable = true;

		[Desc("Is the missile aware of terrain height levels. Only needed for mods with real, non-visual height levels.")]
		public readonly bool TerrainHeightAware = false;

		[Desc("Width of projectile (used for finding blocking actors).")]
		public readonly WDist Width = new WDist(1);

		[Desc("The maximum/constant/incremental inaccuracy used in conjunction with the InaccuracyType property.")]
		public readonly WDist Inaccuracy = WDist.Zero;

		[Desc("Controls the way inaccuracy is calculated. Possible values are 'Maximum' - scale from 0 to max with range, 'PerCellIncrement' - scale from 0 with range and 'Absolute' - use set value regardless of range.")]
		public readonly InaccuracyType InaccuracyType = InaccuracyType.Absolute;

		[Desc("Inaccuracy override when sucessfully locked onto Target. Defaults to Inaccuracy if negative.")]
		public readonly WDist LockOnInaccuracy = new WDist(-1);

		[Desc("Probability of locking onto and following Target.")]
		public readonly int LockOnProbability = 100;

		[Desc("Horizontal rate of turn.")]
		public readonly WAngle HorizontalRateOfTurn = new WAngle(20);

		[Desc("Vertical rate of turn.")]
		public readonly WAngle VerticalRateOfTurn = new WAngle(24);

		[Desc("Gravity applied while in free fall.")]
		public readonly int Gravity = 10;

		[Desc("Run out of fuel after covering this distance. Zero for defaulting to weapon range. Negative for unlimited fuel.")]
		public readonly WDist RangeLimit = WDist.Zero;

		[Desc("Explode when running out of fuel.")]
		public readonly bool ExplodeWhenEmpty = true;

		[Desc("Altitude above terrain below which to explode. Zero effectively deactivates airburst.")]
		public readonly WDist AirburstAltitude = WDist.Zero;

		[Desc("Cruise altitude. Zero means no cruise altitude used.")]
		public readonly WDist CruiseAltitude = new WDist(512);

		[Desc("Activate homing mechanism after this many ticks.")]
		public readonly int HomingActivationDelay = 0;

		[Desc("Image that contains the trail animation.")]
		public readonly string TrailImage = null;

		[SequenceReference(nameof(TrailImage), allowNullImage: true)]
		[Desc("Loop a randomly chosen sequence of TrailImage from this list while this projectile is moving.")]
		public readonly string[] TrailSequences = { "idle" };

		[PaletteReference(nameof(TrailUsePlayerPalette))]
		[Desc("Palette used to render the trail sequence.")]
		public readonly string TrailPalette = "effect";

		[Desc("Use the Player Palette to render the trail sequence.")]
		public readonly bool TrailUsePlayerPalette = false;

		[Desc("Interval in ticks between spawning trail animation.")]
		public readonly int TrailInterval = 2;

		[Desc("Should trail animation be spawned when the propulsion is not activated.")]
		public readonly bool TrailWhenDeactivated = false;

		public readonly int ContrailLength = 0;

		public readonly int ContrailZOffset = 2047;

		public readonly WDist ContrailWidth = new WDist(64);

		public readonly Color ContrailColor = Color.White;

		public readonly bool ContrailUsePlayerColor = false;

		public readonly int ContrailDelay = 1;

		[Desc("Should missile targeting be thrown off by nearby actors with JamsMissiles.")]
		public readonly bool Jammable = true;

		[Desc("Range of facings by which jammed missiles can stray from current path.")]
		public readonly int JammedDiversionRange = 20;

		[Desc("Explodes when leaving the following terrain type, e.g., Water for torpedoes.")]
		public readonly string BoundToTerrainType = "";

		[Desc("Allow the missile to snap to the Target, meaning jumping to the Target immediately when",
			"the missile enters the radius of the current speed around the Target.")]
		public readonly bool AllowSnapping = false;

		[Desc("Explodes when inside this proximity radius to Target.",
			"Note: If this value is lower than the missile speed, this check might",
			"not trigger fast enough, causing the missile to fly past the Target.")]
		public readonly WDist CloseEnough = new WDist(298);

		public IProjectile Create(ProjectileArgs args) { return new SalvageBomb(this, args); }
	}

	// TODO: double check square roots!!!
	public class SalvageBomb : IProjectile, ISync
	{
		readonly SalvageBombInfo info;
		readonly ProjectileArgs args;

		int ticks;

		SalvageContrailRenderable contrail;

		[Sync]
		WPos pos;
		WPos lastPos;

		WVec velocity;

		bool firstTick;

		bool arrived;

		public SalvageBomb(SalvageBombInfo info, ProjectileArgs args)
		{
			this.info = info;
			this.args = args;

			firstTick = true;
			arrived = false;

			pos = args.Source;
			lastPos = pos;

			var world = args.SourceActor.World;

			if (info.ContrailLength > 0)
			{
				var color = info.ContrailUsePlayerColor ? SalvageContrailRenderable.ChooseColor(args.SourceActor) : info.ContrailColor;
				contrail = new SalvageContrailRenderable(world, color, info.ContrailWidth, info.ContrailLength, info.ContrailDelay, info.ContrailZOffset);
			}
		}

		static int LoopRadius(int speed, int rot)
		{
			// loopRadius in w-units = speed in w-units per tick / angular speed in radians per tick
			// angular speed in radians per tick = rot in facing units per tick * (pi radians / 128 facing units)
			// pi = 314 / 100
			// ==> loopRadius = (speed * 128 * 100) / (314 * rot)
			return (speed * 6400) / (157 * rot);
		}

		bool JammedBy(TraitPair<JamsMissiles> tp)
		{
			if ((tp.Actor.CenterPosition - pos).HorizontalLengthSquared > tp.Trait.Range.LengthSquared)
				return false;

			if (!tp.Trait.DeflectionStances.HasRelationship(tp.Actor.Owner.RelationshipWith(args.SourceActor.Owner)))
				return false;

			return tp.Actor.World.SharedRandom.Next(100) < tp.Trait.Chance;
		}

		public int MovingAverage(int x1, int x2, int w = 90)
		{
			return (x1 * w + x2 * (100 - w)) / 100;
		}

		public WVec MovingAverage(WVec v1, WVec v2, int w = 90)
		{
			return (v1 * w + v2 * (100 - w)) / 100;
		}

		public void Tick(World world)
		{
			ticks++;

			lastPos = pos;

			if (args.GuidedTarget.Type != TargetType.Actor)
			{
				Explode(world);
				return;
			}

			var goalPos = args.GuidedTarget.CenterPosition;

			/*
			pos = new WPos(
				MovingAverage(pos.X, goalPos.X),
				MovingAverage(pos.Y, goalPos.Y),
				MovingAverage(pos.Z, goalPos.Z));
			*/

			if (!arrived)
			{
				var distance = (goalPos - pos).Length / 2;

				var facing = args.CurrentMuzzleFacing();
				var goalPosFrontal = new WVec(0, -distance, 0)
					.Rotate(new WRot(WAngle.Zero, WAngle.Zero, facing));

				pos = new WPos(
					MovingAverage(pos.X, goalPos.X + goalPosFrontal.X),
					MovingAverage(pos.Y, goalPos.Y + goalPosFrontal.Y),
					MovingAverage(pos.Z, goalPos.Z + goalPosFrontal.Z));

				if (firstTick)
				{
					velocity = pos - lastPos;

					var randomRotationAngle = args.SourceActor.World.SharedRandom.Next() % 500 - 250;
					randomRotationAngle = randomRotationAngle * randomRotationAngle * randomRotationAngle / 250 / 250;
					velocity = velocity.Rotate(new WRot(WAngle.Zero, WAngle.Zero, new WAngle(randomRotationAngle)));

					firstTick = false;
				}
				else
					velocity = MovingAverage(velocity, pos - lastPos, 80);

				pos = new WPos(
					pos.X + velocity.X,
					pos.Y + velocity.Y,
					pos.Z + velocity.Z);
			}

			// Check for walls or other blocking obstacles
			var shouldExplode = false;
			if (info.Blockable && BlocksProjectiles.AnyBlockingActorsBetween(world, args.SourceActor.Owner, lastPos, pos, info.Width, out var blockedPos))
			{
				pos = blockedPos;
				shouldExplode = true;
			}

			if ((pos - goalPos).Length < 500)
			{
				arrived = true;
			}

			if (arrived && contrail.GetTailLength() < 500)
			{
				shouldExplode = true;
				Explode(world);
			}

			contrail.Update(pos);

			if (shouldExplode)
				Explode(world);
		}

		void Explode(World world)
		{
			if (info.ContrailLength > 0)
				world.AddFrameEndTask(w => w.Add(new SalvageContrailFader(pos, contrail)));

			world.AddFrameEndTask(w => w.Remove(this));

			// Don't blow up in our launcher's face!
			if (ticks <= info.Arm)
				return;

			WAngle deg180 = WAngle.ArcCos(0) + WAngle.ArcCos(0);
			var warheadArgs = new WarheadArgs(args)
			{
				ImpactOrientation = new WRot(WAngle.Zero, WAngle.Zero, args.CurrentMuzzleFacing() + deg180),
				ImpactPosition = pos,
			};

			args.Weapon.Impact(Target.FromPos(pos), warheadArgs);
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			if (info.ContrailLength > 0)
				yield return contrail;
		}
	}
}
